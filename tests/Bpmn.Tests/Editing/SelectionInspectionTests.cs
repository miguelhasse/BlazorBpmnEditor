using System.Collections;
using System.Globalization;
using System.Text.Json;
using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions.Schema;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions.Templates;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;
using Hasseware.AspNetCore.Components.Bpmn.Validation;
using Microsoft.Extensions.DependencyInjection;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Editing;

public sealed class SelectionInspectionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompleteSnapshotsMatchIndependentInspectionAcrossShippedDiagrams(bool readOnly)
    {
        using var services = new ServiceCollection().AddBpmnComponents().BuildServiceProvider();
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "playground"))) root = root.Parent;
        var samples = Path.Combine(root?.FullName ?? throw new InvalidOperationException("Repository root not found."),
            "playground", "Bpmn.Demo", "wwwroot", "samples");
        var files = Directory.GetFiles(samples, "*.bpmn");
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            var document = BpmnXmlReader.Read(File.ReadAllText(file)).Document;
            var ids = BpmnValidator.Extended(document).Select(item => item.Id).Distinct(StringComparer.Ordinal).Take(40)
                .Concat(["Missing"]).ToArray();
            foreach (var diagram in document.Diagrams)
            {
                var batch = Session(document, services, diagram.Id, readOnly);
                var independent = Session(document, services, diagram.Id, readOnly);
                await batch.SetSelectionAsync(ids);

                var expected = ids.Select(independent.Describe).OfType<BpmnElementSnapshot>().ToArray();
                var actual = batch.SelectionSnapshots;

                Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
                Assert.Equal(actual, batch.SelectionSnapshots);
            }
        }
    }

    [Fact]
    public async Task ColdSelectionChecksAllDiagramInvariantsOnceAndWarmSelectionDoesNotCheckAgain()
    {
        using var services = new ServiceCollection().AddBpmnComponents().BuildServiceProvider();
        var document = Document();
        var diagram = document.Diagrams[0];
        var shapes = new CountingItems<BpmnShape>(diagram.Plane.Shapes);
        document = document with { Diagrams = [diagram with { Plane = diagram.Plane with { Shapes = shapes } }] };
        var session = Session(document, services);
        await session.SetSelectionAsync(document.FlowNodes.Select(node => node.Id).ToArray());
        shapes.CompletedEnumerations = 0;
        shapes.VisitedItems = 0;

        var snapshots = session.SelectionSnapshots;

        Assert.Equal(12, snapshots.Count);
        // One global geometry index and one invariant pass; no per-element full scan.
        Assert.Equal(2, shapes.CompletedEnumerations);
        Assert.Equal(24, shapes.VisitedItems);
        _ = session.SelectionSnapshots;
        Assert.Equal(2, shapes.CompletedEnumerations);
        Assert.Equal(24, shapes.VisitedItems);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SelectionGeometryUsesLinearColdWorkAndNoWarmWork(bool activeDiagram)
    {
        using var services = new ServiceCollection().AddBpmnComponents().BuildServiceProvider();
        var source = Document();
        var diagram = source.Diagrams[0];
        var shapes = new CountingItems<BpmnShape>(diagram.Plane.Shapes);
        var edges = new CountingItems<BpmnEdge>(source.FlowNodes.Select((node, index) =>
            new BpmnEdge($"Edge_{index}", node.Id, [new(index, 0), new(index, 80)])).ToArray());
        var document = source with { Diagrams = [diagram with { Plane = diagram.Plane with { Shapes = shapes, Edges = edges } }] };
        var session = Session(document, services, activeDiagram ? diagram.Id : null);
        await session.SetSelectionAsync(document.FlowNodes.Select(node => node.Id).ToArray());
        shapes.VisitedItems = edges.VisitedItems = 0;

        var snapshots = session.SelectionSnapshots;

        Assert.Equal(12, snapshots.Count);
        var expectedVisits = activeDiagram ? 36 : 24; // Global index, invariants, and optional active index.
        Assert.Equal(expectedVisits, shapes.VisitedItems);
        Assert.Equal(expectedVisits, edges.VisitedItems);
        _ = session.SelectionSnapshots;
        Assert.Equal(expectedVisits, shapes.VisitedItems);
        Assert.Equal(expectedVisits, edges.VisitedItems);
        AssertMatches(session, document, services, activeDiagram ? diagram.Id : null);
    }

    [Fact]
    public async Task SingleSelectionRetainsFirstMatchScansWithoutBuildingGeometryIndexes()
    {
        using var services = new ServiceCollection().AddBpmnComponents().BuildServiceProvider();
        var source = Document();
        var diagram = source.Diagrams[0];
        var shapes = new CountingItems<BpmnShape>(diagram.Plane.Shapes);
        var edges = new CountingItems<BpmnEdge>(source.FlowNodes.Select((node, index) =>
            new BpmnEdge($"Edge_{index}", node.Id, [new(index, 0), new(index, 80)])).ToArray());
        var document = source with { Diagrams = [diagram with { Plane = diagram.Plane with { Shapes = shapes, Edges = edges } }] };
        var session = Session(document, services, diagram.Id);
        await session.SetSelectionAsync(["Task_0"]);
        session.IsReadOnly = true; // Single selection is eagerly described while publishing state.
        shapes.VisitedItems = edges.VisitedItems = 0;

        _ = session.SelectionSnapshots;

        Assert.Equal(14, shapes.VisitedItems); // Two first-item matches and the invariant pass.
        Assert.Equal(14, edges.VisitedItems);
        _ = session.SelectionSnapshots;
        Assert.Equal(14, shapes.VisitedItems);
        Assert.Equal(14, edges.VisitedItems);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("MissingDiagram", false)]
    [InlineData("OtherDiagram", false)]
    [InlineData("OtherDiagram", true)]
    public async Task BatchGeometryPreservesFirstMatchesAndActiveShapeEdgeAndLabelPrecedence(string? diagramId, bool readOnly)
    {
        using var services = new ServiceCollection().AddBpmnComponents().BuildServiceProvider();
        var source = Document();
        var diagram = source.Diagrams[0];
        var first = diagram.Plane.Shapes[0];
        var globalEdge = new BpmnEdge("GlobalEdge", "Task_0", [new(1, 2), new(3, 4)],
            new(Bounds: new Bounds(10, 20, 30, 40)));
        var activeEdge = new BpmnEdge("ActiveEdge", "Task_1", [new(5, 6), new(7, 8)],
            new(Bounds: new Bounds(50, 60, 70, 80)));
        var activeShape = first with { Bounds = new Bounds(100, 200, 300, 400), Label = null };
        var other = diagram with
        {
            Id = "OtherDiagram",
            Plane = diagram.Plane with
            {
                Shapes = [first with { BpmnElement = null! }, activeShape, activeShape with { Bounds = new Bounds(900, 900, 900, 900) }],
                Edges = [globalEdge with { BpmnElement = null! }, globalEdge with { Id = "OtherEdge" }, activeEdge, activeEdge with { Waypoints = [] }],
            },
        };
        var document = source with
        {
            Diagrams =
            [
                diagram with { Plane = diagram.Plane with { Shapes = [.. diagram.Plane.Shapes, first with { Label = new(Bounds: first.Bounds) }], Edges = [globalEdge] } },
                other,
                other with { Plane = other.Plane with { Shapes = [], Edges = [] } },
            ],
        };
        // Malformed DI isolates inspection from graph and document-validation key rejection.
        var batch = Session(document, services, diagramId, readOnly, inspectionOnly: true);
        var independent = Session(document, services, diagramId, readOnly, inspectionOnly: true);
        await batch.SetSelectionAsync(["Task_1", "Task_0", "Missing", "Task_2", "Task_0"]);

        var expected = batch.State.Selection.Select(independent.Describe).OfType<BpmnElementSnapshot>().ToArray();
        var actual = batch.SelectionSnapshots;

        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
        Assert.Equal(["Task_1", "Task_0", "Task_2", "Task_0"], actual.Select(item => item.ElementId));
        if (diagramId == "OtherDiagram")
        {
            Assert.Null(actual[0].Bounds);
            Assert.Equal(activeEdge.Waypoints, actual[0].Waypoints);
            Assert.Equal(activeEdge.Label!.Bounds, actual[0].LabelBounds);
            Assert.Equal(activeShape.Bounds, actual[1].Bounds);
            Assert.Null(actual[1].LabelBounds);
            Assert.Empty(actual[1].Waypoints);
        }
        else
        {
            Assert.Equal(first.Bounds, actual[1].Bounds);
            Assert.Equal(globalEdge.Label!.Bounds, actual[1].LabelBounds);
            Assert.Equal(globalEdge.Waypoints, actual[1].Waypoints);
        }
        Assert.Equal(diagram.Plane.Shapes[2].Bounds, actual[2].Bounds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BuilderDocumentOrDiagramChangesRefreshBatchGeometry(bool replaceDocument)
    {
        using var services = new ServiceCollection().AddBpmnComponents().BuildServiceProvider();
        var source = Document();
        var diagram = source.Diagrams[0];
        var other = diagram with
        {
            Id = "OtherDiagram",
            Plane = diagram.Plane with
            {
                Shapes = diagram.Plane.Shapes.Select(shape => shape with { Bounds = new Bounds(200, 300, 400, 500) }).ToArray(),
            },
        };
        var document = source with
        {
            FlowNodes = source.FlowNodes.Select(node => node with { Extras = new BpmnElementExtras() }).ToArray(),
            Diagrams = [diagram, other],
        };
        var replacement = document with { Diagrams = [diagram with { Plane = other.Plane }] };
        var batch = CreateSession();
        var independent = CreateSession();
        await batch.SetSelectionAsync(["Task_0", "Task_1", "Task_2"]);

        var expected = batch.State.Selection.Select(independent.Describe).OfType<BpmnElementSnapshot>().ToArray();
        var actual = batch.SelectionSnapshots;

        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
        Assert.All(actual, snapshot => Assert.Equal(new Bounds(200, 300, 400, 500), snapshot.Bounds));

        BpmnEditorSession CreateSession()
        {
            var options = new BpmnOptions();
            options.Extensions.AddCamunda7();
            var session = Session(document, services, diagram.Id, options: options);
            var changed = false;
            options.Extensions.GroupBuilders.Add(new TrackingBuilder
            {
                OnBuild = _ =>
                {
                    if (changed) return;
                    changed = true;
                    if (replaceDocument)
                    {
                        session.Document = replacement;
                        session.UpdateParameters(EditorConfig.CreateDefault());
                    }
                    else session.DiagramId = other.Id;
                },
            });
            return session;
        }
    }

    [Fact]
    public async Task BatchKeepsOrderedInvalidFindingsAndRebuildsAfterRemoteChanges()
    {
        using var services = new ServiceCollection().AddBpmnComponents().BuildServiceProvider();
        var document = Document();
        var diagram = document.Diagrams[0];
        document = document with
        {
            FlowNodes = document.FlowNodes.Select((node, index) => index == 0
                ? node with { Type = BpmnFlowNodeType.CallActivity, FlowElementsOwned = true, CalledElementRef = "Missing" }
                : index == 1 ? node with { Type = BpmnFlowNodeType.BoundaryEvent, AttachedToRef = "Missing" } : node).ToArray(),
            Diagrams =
            [
                diagram,
                diagram with
                {
                    Id = "OtherDiagram",
                    Plane = diagram.Plane with
                    {
                        Shapes = [diagram.Plane.Shapes[0] with { Bounds = new Bounds(0, 0, 0, 0), IsHorizontal = true }],
                    },
                },
            ],
        };
        var session = Session(document, services, "OtherDiagram");
        await session.SetSelectionAsync(["Task_0", "Task_1", "Missing", "Task_0"]);
        AssertMatches(session, document, services, "OtherDiagram");
        Assert.Equal(["CT-001", "CT-001", "DI-001", "DI-007"], session.SelectionSnapshots[0].Diagnostics.Select(item => item.Code));
        Assert.Equal("CT-005", Assert.Single(session.SelectionSnapshots[1].Diagnostics).Code);
        Assert.Equal(new Bounds(0, 0, 0, 0), session.SelectionSnapshots[0].Bounds);

        var changed = document with { FlowNodes = document.FlowNodes.Select(node => node with { FlowElementsOwned = false }).ToArray() };
        session.Document = changed;
        session.UpdateParameters(EditorConfig.CreateDefault());
        await session.SetSelectionAsync(["Task_0", "Task_1"]);
        AssertMatches(session, changed, services, "OtherDiagram");
        Assert.DoesNotContain(session.SelectionSnapshots[0].Diagnostics, item => item.Code == "CT-001");

        session.Document = document;
        session.UpdateParameters(EditorConfig.CreateDefault());
        await session.SetSelectionAsync(["Task_0", "Task_1"]);
        AssertMatches(session, document, services, "OtherDiagram");
    }

    [Fact]
    public async Task UnseenSelectionAndEditUndoRedoUseFreshCompleteSnapshots()
    {
        using var services = new ServiceCollection().AddBpmnComponents().BuildServiceProvider();
        var document = Document();
        var session = Session(document, services);
        await session.SetSelectionAsync(["Task_0", "Task_1"]);
        _ = session.SelectionSnapshots;
        await session.SetSelectionAsync(["Task_0", "Task_2", "Task_3"]);
        AssertMatches(session, document, services);

        await session.ApplyAsync(new UpdatePropertiesCommand("Task_2", "name", "Changed"));
        Assert.Equal("Changed", session.SelectionSnapshots[1].Label);
        AssertMatches(session, session.EditedDocument!, services);
        await session.UndoAsync();
        Assert.Equal("Task", session.SelectionSnapshots[1].Label);
        await session.RedoAsync();
        Assert.Equal("Changed", session.SelectionSnapshots[1].Label);
        await session.ApplyAsync(new RenameElementCommand("Task_2", "Renamed"));
        Assert.DoesNotContain(session.SelectionSnapshots, item => item.ElementId == "Task_2");
        await session.ApplyAsync(new DeleteElementsCommand(["Task_3"]));
        Assert.DoesNotContain(session.SelectionSnapshots, item => item.ElementId == "Task_3");
    }

    [Fact]
    public async Task CustomDescribeKeepsCacheInvocationOrderAndExceptions()
    {
        using var services = new ServiceCollection().AddBpmnComponents().BuildServiceProvider();
        var document = Document();
        var session = Session(document, services);
        var calls = new List<string>();
        session.DescribeOverride = id =>
        {
            calls.Add(id);
            return BpmnElementInspector.Describe(document, id);
        };
        await session.SetSelectionAsync(["Task_2", "Missing", "Task_1", "Task_2"]);

        Assert.Equal(["Task_2", "Task_1", "Task_2"], session.SelectionSnapshots.Select(item => item.ElementId));
        Assert.Equal(["Task_2", "Missing", "Task_1"], calls);
        _ = session.SelectionSnapshots;
        Assert.Equal(3, calls.Count);

        session.DescribeOverride = _ => throw new InvalidOperationException("Host inspection failed.");
        var error = Assert.Throws<InvalidOperationException>(() => session.SelectionSnapshots);
        Assert.Equal("Host inspection failed.", error.Message);
    }

    [Fact]
    public async Task TemplateSchemaAndCultureChangesKeepCompleteSnapshotsAndBuilderCalls()
    {
        using var services = new ServiceCollection().AddBpmnComponents().BuildServiceProvider();
        var options = new BpmnOptions();
        options.Extensions.AddCamunda7();
        var builder = new TrackingBuilder();
        options.Extensions.GroupBuilders.Add(builder);
        var extras = new BpmnElementExtras
        {
            ForeignAttributes = new Dictionary<string, string>
            {
                ["{" + BpmnTemplateMarkers.CamundaNamespaceUri + "}modelerTemplate"] = "inspection",
                ["{" + BpmnTemplateMarkers.CamundaNamespaceUri + "}modelerTemplateVersion"] = "1",
                ["{" + BpmnTemplateMarkers.CamundaNamespaceUri + "}taskPriority"] = "5",
            },
        };
        var source = Document();
        var document = source with { FlowNodes = source.FlowNodes.Select(node => node with { Extras = extras }).ToArray() };
        var session = Session(document, services, options: options);
        await session.SetSelectionAsync(["Task_2", "Task_1", "Task_0"]);
        _ = session.SelectionSnapshots;
        Assert.Equal(["Task_2", "Task_1", "Task_0"], builder.Calls);
        _ = session.SelectionSnapshots;
        Assert.Equal(3, builder.Calls.Count);
        AssertMatches(session, document, services, options: options);

        options.Templates.Registry.Add(new BpmnElementTemplate
        {
            Id = "inspection",
            Name = "Inspection",
            Version = 1,
            Flavor = BpmnTemplateFlavor.Camunda7,
            AppliesTo = ["bpmn:Task"],
            Properties = [new() { Id = "name", Label = "Template name", Binding = new("property",
                new Dictionary<string, string> { ["name"] = "name" }) }],
        });
        Assert.DoesNotContain(session.SelectionSnapshots[0].ExtensionGroups, group => group.Id == "template:missing");
        AssertMatches(session, document, services, options: options);
        options.Templates.HonorEntriesVisible = false;
        AssertMatches(session, document, services, options: options);
        options.Extensions.AlwaysOfferRegisteredSchemas = false;
        AssertMatches(session, document, services, options: options);

        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");
            AssertMatches(session, document, services, options: options);
        }
        finally { CultureInfo.CurrentUICulture = previous; }

        options.Extensions.GroupBuilders.Add(new TrackingBuilder { OnBuild = _ => throw new InvalidOperationException("Host builder failed.") });
        Assert.Equal("Host builder failed.", Assert.Throws<InvalidOperationException>(() => session.SelectionSnapshots).Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BuilderCultureChangesDoNotReuseEarlierInvariantPresentation(bool formattingCulture)
    {
        using var services = new ServiceCollection().AddBpmnComponents().BuildServiceProvider();
        var document = Document();
        var diagram = document.Diagrams[0];
        var shapes = new CountingItems<BpmnShape>(diagram.Plane.Shapes);
        document = document with
        {
            FlowNodes = document.FlowNodes.Select(node => node with { Extras = new BpmnElementExtras() }).ToArray(),
            Diagrams = [diagram with { Plane = diagram.Plane with { Shapes = shapes } }],
        };
        var options = new BpmnOptions();
        options.Extensions.AddCamunda7();
        options.Extensions.GroupBuilders.Add(new TrackingBuilder
        {
            OnBuild = _ => SetCulture(CultureInfo.GetCultureInfo("fr-FR")),
        });
        var previous = formattingCulture ? CultureInfo.CurrentCulture : CultureInfo.CurrentUICulture;
        try
        {
            SetCulture(CultureInfo.GetCultureInfo("en-US"));
            var session = Session(document, services, options: options);
            await session.SetSelectionAsync(["Task_0", "Task_1", "Task_2"]);
            shapes.CompletedEnumerations = 0;
            _ = session.SelectionSnapshots;
            Assert.Equal(3, shapes.CompletedEnumerations); // One geometry index and two culture-sensitive invariant passes.
        }
        finally { SetCulture(previous); }

        void SetCulture(CultureInfo culture)
        {
            if (formattingCulture) CultureInfo.CurrentCulture = culture;
            else CultureInfo.CurrentUICulture = culture;
        }
    }

    private static void AssertMatches(BpmnEditorSession session, BpmnDocument document, IServiceProvider services,
        string? diagramId = null, BpmnOptions? options = null)
    {
        var independent = Session(document, services, diagramId, options: options);
        var expected = session.State.Selection.Select(independent.Describe).OfType<BpmnElementSnapshot>().ToArray();
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(session.SelectionSnapshots));
    }

    private static BpmnEditorSession Session(BpmnDocument document, IServiceProvider services, string? diagramId = null,
        bool readOnly = false, BpmnOptions? options = null, bool inspectionOnly = false)
    {
        var session = new BpmnEditorSession(options ?? new BpmnOptions(), services)
        {
            Document = document,
            DiagramId = diagramId,
            IsReadOnly = readOnly,
            AccessibleGraphOverride = inspectionOnly ? [] : null,
            ValidateOnChange = !inspectionOnly,
        };
        session.UpdateParameters(EditorConfig.CreateDefault());
        return session;
    }

    private static BpmnDocument Document()
    {
        var builder = new BpmnDocumentBuilder();
        for (var index = 0; index < 12; index++)
            builder.Node($"Task_{index}", BpmnFlowNodeType.Task, new Bounds(index * 150, 0, 100, 80), "Task");
        return builder.Build();
    }

    private sealed class CountingItems<T>(IReadOnlyList<T> items) : IReadOnlyList<T>
    {
        public int CompletedEnumerations { get; set; }
        public int VisitedItems { get; set; }
        public int Count => items.Count;
        public T this[int index] => items[index];
        public IEnumerator<T> GetEnumerator()
        {
            foreach (var item in items)
            {
                VisitedItems++;
                yield return item;
            }
            CompletedEnumerations++;
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class TrackingBuilder : IBpmnExtensionGroupBuilder
    {
        public List<string> Calls { get; } = [];
        public Action<BpmnElementSnapshot>? OnBuild { get; init; }
        public BpmnExtensionGroupBuildResult Build(BpmnElementSnapshot snapshot, object element, BpmnElementExtras extras,
            BpmnExtensionRegistry registry, IBpmnPropertyLabelProvider labels)
        {
            Calls.Add(snapshot.ElementId);
            OnBuild?.Invoke(snapshot);
            return new([], []);
        }
    }
}
