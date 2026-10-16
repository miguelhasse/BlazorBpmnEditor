using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;
using Hasseware.AspNetCore.Components.Bpmn.Validation;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Editing;

/// <summary>
/// <c>extensionElements</c> through the editing commands (specification section 6.1).
/// </summary>
/// <remarks>
/// <para>
/// Reading and writing extensions is covered by the serialization tests. What is covered here is
/// the part in between, which is where they are easiest to lose: every command that changes an
/// element rebuilds the record that holds them, and one written with <c>new BpmnFlowNode(...)</c>
/// instead of <c>with</c> would drop a vendor's entire configuration without a word.
/// </para>
/// <para>
/// Section 6.1 makes that the editor's problem rather than the vendor's: extension content is
/// preserved even when the renderer ignores it, and a round trip through an editing session must
/// not be the thing that loses it. So each command gets a test, and they assert the extension is
/// still <em>there</em> rather than that it was not looked at.
/// </para>
/// </remarks>
public sealed class ExtensionPreservationTests
{
    // --- editing a property ----------------------------------------------------------

    [Fact]
    public void RenamingKeepsTheExtension()
    {
        var result = Apply(Extended(), new UpdatePropertiesCommand("Task_1", "name", "Renamed"));

        Assert.True(result.IsApplied, Codes(result));
        Assert.Equal("Renamed", result.Document.FindFlowNode("Task_1")!.Name);
        AssertTaskExtension(result.Document);
    }

    [Fact]
    public void EditingAConnectorKeepsItsExtension()
    {
        var result = Apply(Extended(), new UpdatePropertiesCommand("Flow_1", "name", "approved"));

        Assert.True(result.IsApplied, Codes(result));
        Assert.Equal(
            "taskDefinition",
            Assert.Single(result.Document.SequenceFlows.Single().Extras.Extensions).LocalName);
    }

    [Fact]
    public void ClearingAPropertyKeepsTheExtension()
    {
        // The riskiest shape of edit: setting a value to null rebuilds the record for a field that
        // is nothing to do with the extension.
        var result = Apply(Extended(), new UpdatePropertiesCommand("Task_1", "name", null));

        Assert.Null(result.Document.FindFlowNode("Task_1")!.Name);
        AssertTaskExtension(result.Document);
    }

    // --- changing what an element is ----------------------------------------------------

    [Fact]
    public void SwitchingTypeKeepsTheExtension()
    {
        /*
         * Section 7 promises a switch preserves references and DI; a vendor's extension is neither,
         * and is exactly the kind of thing a conversion loses. It is kept because it belongs to the
         * element, and the element survives the switch — that is what makes it a switch rather than
         * a replacement.
         */
        var result = Apply(Extended(), new SwitchElementTypeCommand("Task_1", BpmnFlowNodeType.UserTask));

        Assert.True(result.IsApplied, Codes(result));
        Assert.Equal(BpmnFlowNodeType.UserTask, result.Document.FindFlowNode("Task_1")!.Type);
        AssertTaskExtension(result.Document);
    }

    [Fact]
    public void RetriggeringAnEventKeepsTheExtension()
    {
        var document = Extended();
        var withTrigger = document with
        {
            FlowNodes = [.. document.FlowNodes.Select(node => node.Id == "End_1"
                ? node with { Extras = TaskExtras }
                : node)],
        };

        var result = Apply(withTrigger, new UpdatePropertiesCommand("End_1", "eventDefinition", "terminate"));

        Assert.True(result.IsApplied, Codes(result));
        Assert.NotEmpty(result.Document.FindFlowNode("End_1")!.Extras.Extensions);
    }

    // --- geometry ------------------------------------------------------------------------

    [Fact]
    public void MovingKeepsBothTheElementsAndTheShapesExtensions()
    {
        // Section 10.5 gives DI its own extension content, and a move rebuilds the DI shape.
        var result = Apply(
            Extended(),
            new MoveShapesCommand([new ShapeGeometry("Task_1_di", new Bounds(400, 300, 100, 80))]));

        Assert.True(result.IsApplied, Codes(result));
        AssertTaskExtension(result.Document);
        Assert.NotEmpty(ShapeOf(result.Document, "Task_1").Extras.Extensions);
    }

    [Fact]
    public void ResizingKeepsTheShapesExtension()
    {
        var result = Apply(Extended(), new ResizeShapeCommand("Task_1_di", new Bounds(200, 120, 160, 120)));

        Assert.True(result.IsApplied, Codes(result));
        Assert.NotEmpty(ShapeOf(result.Document, "Task_1").Extras.Extensions);
    }

    [Fact]
    public void ReroutingKeepsTheConnectorsExtension()
    {
        var result = Apply(
            Extended(),
            new UpdateWaypointsCommand("Flow_1", [new DiPoint(300, 160), new DiPoint(360, 200), new DiPoint(420, 160)]));

        Assert.True(result.IsApplied, Codes(result));
        Assert.NotEmpty(result.Document.SequenceFlows.Single().Extras.Extensions);
    }

    // --- containment ----------------------------------------------------------------------

    [Fact]
    public void ReparentingKeepsTheExtension()
    {
        var document = Extended();
        var withSub = document with
        {
            FlowNodes = [.. document.FlowNodes, new BpmnFlowNode(
                "Sub_1",
                BpmnFlowNodeType.SubProcess,
                "Process_1",
                FlowElementsOwned: true)],
            Diagrams = [.. document.Diagrams.Select(diagram => diagram with
            {
                Plane = diagram.Plane with
                {
                    Shapes = [.. diagram.Plane.Shapes, new BpmnShape("Sub_1_di", "Sub_1", new Bounds(600, 100, 300, 240))],
                },
            })],
        };

        // SF-007 refuses a move that would leave a flow crossing the new boundary, so the flow goes
        // first. Reparenting then rewrites ownership, Lane membership, Pool scope and geometry at
        // once (CT-008), which is four chances to rebuild the record and drop what is on it.
        var loose = withSub with { SequenceFlows = [] };

        var result = Apply(loose, new ReparentElementCommand("Task_1", "Sub_1", null, new Bounds(640, 140, 100, 80)));

        Assert.True(result.IsApplied, Codes(result));
        Assert.Equal("Sub_1", result.Document.FindFlowNode("Task_1")!.ContainerId);
        AssertTaskExtension(result.Document);
    }

    // --- the clipboard -----------------------------------------------------------------------

    [Fact]
    public void CopyingAndPastingCarriesTheExtension()
    {
        /*
         * A paste rewrites every id in the fragment, so it rebuilds each record by definition. That
         * makes it the one command where losing an extension would be an easy accident — and a
         * pasted copy of a configured Service Task that quietly forgot its configuration is worse
         * than one that failed to paste.
         */
        var document = Extended();
        var payload = BpmnClipboard.Copy(document, ["Task_1"]);

        Assert.False(payload.IsEmpty);

        var result = Apply(document, new PasteCommand(payload, "Process_1", new DiPoint(40, 40)));

        Assert.True(result.IsApplied, Codes(result));

        var pasted = result.Document.FlowNodes.Single(
            node => node.Id != "Task_1" && node.Type == BpmnFlowNodeType.ServiceTask);
        Assert.Equal("taskDefinition", Assert.Single(pasted.Extras.Extensions).LocalName);
        Assert.Equal("worker", Assert.Single(pasted.Extras.Extensions).Attributes["type"]);
    }

    // --- undo -------------------------------------------------------------------------------

    [Fact]
    public void UndoRestoresTheExtensionExactly()
    {
        var history = new BpmnEditHistory(Extended());

        history.Apply(new SwitchElementTypeCommand("Task_1", BpmnFlowNodeType.UserTask));
        var restored = history.Undo();

        Assert.Equal(BpmnFlowNodeType.ServiceTask, restored.FindFlowNode("Task_1")!.Type);
        AssertTaskExtension(restored);
    }

    // --- what the property surface may show ---------------------------------------------------

    [Fact]
    public void TheSnapshotNeverOffersAnExtensionAsAStandardProperty()
    {
        /*
         * Section 7: the snapshot "must not include stale or extension-only values as standard
         * fields". A vendor's worker type is not a BPMN attribute, and a property form that bound it
         * as one would write it back through a command that knows nothing about it.
         */
        var snapshot = BpmnElementInspector.Describe(Extended(), "Task_1");

        Assert.NotNull(snapshot);
        Assert.DoesNotContain(snapshot.Properties, property => property.Name is "taskDefinition" or "type");
        Assert.All(snapshot.Properties, property => Assert.DoesNotContain("worker", property.Value ?? string.Empty, StringComparison.Ordinal));
    }

    [Fact]
    public void RefusingAnEditLeavesTheExtensionUntouched()
    {
        // A rejection returns the document that went in, so this is really a statement about
        // immutability — but it is the statement a vendor cares about after a failed edit.
        var document = Extended();
        var result = Apply(document, new UpdatePropertiesCommand("Task_1", "conditionExpression", "x"));

        Assert.False(result.IsApplied);
        Assert.Same(document, result.Document);
        AssertTaskExtension(result.Document);
    }

    // --- a real file ---------------------------------------------------------------------------

    [Fact]
    public void EditingARealZeebeDocumentKeepsItsExtensions()
    {
        var document = ReadSample("zeebe-process.bpmn");

        var extended = document.FlowNodes.First(node => node.Extras.Extensions.Count > 0);
        var before = extended.Extras.Extensions;

        var result = Apply(document, new UpdatePropertiesCommand(extended.Id, "name", "Edited by the editor"));

        Assert.True(result.IsApplied, Codes(result));
        Assert.Equal(before, result.Document.FindFlowNode(extended.Id)!.Extras.Extensions);
    }

    [Fact]
    public void AnEditedDocumentStillWritesItsExtensions()
    {
        // The whole point: an extension that survives the edit but not the export has not survived.
        var document = ReadSample("zeebe-process.bpmn");
        var extended = document.FlowNodes.First(node => node.Extras.Extensions.Count > 0);

        var edited = Apply(document, new UpdatePropertiesCommand(extended.Id, "name", "Edited")).Document;
        var written = BpmnXmlReader.Read(BpmnXmlWriter.Write(edited).Xml).Document;

        Assert.Equal(
            extended.Extras.Extensions,
            written.FindFlowNode(extended.Id)!.Extras.Extensions);
    }

    // --- the two places extensions were not supported ------------------------------------------

    [Fact]
    public void ADataObjectsExtensionsSurviveARoundTrip()
    {
        /*
         * Every other element that can carry extensionElements had somewhere to put them; the
         * dataObject definition did not, so a vendor's payload configuration was read past and
         * dropped silently. Its own Reference next door kept them, which is what made the hole easy
         * to miss.
         */
        const string Xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
                              id="Definitions_1" targetNamespace="http://example.org">
              <bpmn:process id="Process_1">
                <bpmn:dataObject id="Data_1" name="Order">
                  <bpmn:extensionElements>
                    <zeebe:properties><zeebe:property name="schema" value="order.v2" /></zeebe:properties>
                  </bpmn:extensionElements>
                </bpmn:dataObject>
              </bpmn:process>
            </bpmn:definitions>
            """;

        var document = BpmnXmlReader.Read(Xml).Document;
        var read = Assert.Single(document.DataObjects).Extras.Extensions;

        Assert.Equal("properties", Assert.Single(read).LocalName);
        Assert.Equal("order.v2", Assert.Single(Assert.Single(read).Children).Attributes["value"]);

        var written = BpmnXmlReader.Read(BpmnXmlWriter.Write(document).Xml).Document;

        Assert.Equal(read, Assert.Single(written.DataObjects).Extras.Extensions);
    }

    [Fact]
    public void ExtensionContentWithNoNamespaceIsReported()
    {
        /*
         * Section 6.1.2 gives extension content its identity as an expanded name, so content in no
         * namespace cannot be claimed by an adapter or told apart from another vendor's element of
         * the same local name. It still round-trips — hence a warning, and hence still exportable.
         */
        var document = Extended();
        var anonymous = document with
        {
            FlowNodes = [.. document.FlowNodes.Select(node => node.Id == "Task_1"
                ? node with
                {
                    Extras = new BpmnElementExtras
                    {
                        Extensions = [new BpmnExtensionElement(string.Empty, "config", NoAttributes, [])],
                    },
                }
                : node)],
        };

        var report = BpmnValidator.Validate(anonymous);
        var found = Assert.Single(report.Diagnostics, diagnostic => diagnostic.RuleId == "EXT-001");

        Assert.Equal("Task_1", found.ElementId);
        Assert.Equal(BpmnSeverity.Warning, found.Severity);
        Assert.Contains("<config>", found.Message, StringComparison.Ordinal);
        Assert.True(report.IsExportable);
    }

    [Fact]
    public void ANamespacedExtensionIsNotReported()
    {
        // The fixtures are full of these; a rule that fired on them would be noise, not validation.
        var report = BpmnValidator.Validate(Extended());

        Assert.DoesNotContain(report.Diagnostics, diagnostic => diagnostic.RuleId == "EXT-001");
    }

    [Fact]
    public void AnUnnamespacedChildOfANamespacedExtensionIsReported()
    {
        // The realistic shape of the mistake: the prefix is declared on the outer element and then
        // forgotten on the inner ones.
        var document = Extended();
        var nested = document with
        {
            FlowNodes = [.. document.FlowNodes.Select(node => node.Id == "Task_1"
                ? node with
                {
                    Extras = new BpmnElementExtras
                    {
                        Extensions =
                        [
                            new BpmnExtensionElement(Zeebe, "taskDefinition", NoAttributes,
                                [new BpmnExtensionElement(string.Empty, "retry", NoAttributes, [])]),
                        ],
                    },
                }
                : node)],
        };

        var found = Assert.Single(
            BpmnValidator.Validate(nested).Diagnostics,
            diagnostic => diagnostic.RuleId == "EXT-001");

        Assert.Contains("<retry>", found.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryRealFixtureKeepsItsExtensionsIdentifiable()
    {
        // The rule has to be quiet on all twelve, or it is a rule nobody will keep switched on.
        foreach (var path in Directory.EnumerateFiles(
            Path.Combine(FindRepositoryRoot(), "playground", "Bpmn.Demo", "wwwroot", "samples"),
            "*.bpmn"))
        {
            var report = BpmnValidator.Validate(BpmnXmlReader.Read(File.ReadAllText(path)).Document);

            Assert.DoesNotContain(report.Diagnostics, diagnostic => diagnostic.RuleId == "EXT-001");
        }
    }

    // --- documentation ---------------------------------------------------------------------

    [Fact]
    public void DocumentationIsEditableAndSurvivesARoundTrip()
    {
        // The one field every element has. It lives on the same carrier as the extensions, so an
        // editor that could not write it was an editor that could only ever read half of that
        // carrier.
        var edited = Apply(Extended(), new UpdatePropertiesCommand("Task_1", "documentation", "Charges the card."));

        Assert.True(edited.IsApplied, Codes(edited));
        Assert.Equal("Charges the card.", edited.Document.FindFlowNode("Task_1")!.Extras.Documentation);

        // And it must not have cost the extensions, which share the carrier it rewrote.
        AssertTaskExtension(edited.Document);

        var written = BpmnXmlReader.Read(BpmnXmlWriter.Write(edited.Document).Xml).Document;

        Assert.Equal("Charges the card.", written.FindFlowNode("Task_1")!.Extras.Documentation);
        AssertTaskExtension(written);
    }

    [Fact]
    public void ClearingDocumentationRemovesItRatherThanEmptyingIt()
    {
        // An empty <documentation/> in the XML says something was written and then deleted, which
        // is not what clearing a text box means.
        var document = Apply(Extended(), new UpdatePropertiesCommand("Task_1", "documentation", "Notes")).Document;
        var cleared = Apply(document, new UpdatePropertiesCommand("Task_1", "documentation", "   ")).Document;

        Assert.Null(cleared.FindFlowNode("Task_1")!.Extras.Documentation);
    }

    [Fact]
    public void DocumentationIsOfferedOnEveryElementThatCanCarryIt()
    {
        var document = Extended();

        foreach (var id in new[] { "Task_1", "End_1", "Flow_1" })
        {
            var snapshot = BpmnElementInspector.Describe(document, id);

            Assert.NotNull(snapshot);
            Assert.Contains(snapshot.Properties, property => property.Name == "documentation");
        }
    }

    // --- helpers -----------------------------------------------------------------------------

    private const string Zeebe = "http://camunda.org/schema/zeebe/1.0";

    private static readonly Dictionary<string, string> NoAttributes = new(StringComparer.Ordinal);

    private static readonly BpmnElementExtras TaskExtras = new()
    {
        Extensions =
        [
            new BpmnExtensionElement(
                Zeebe,
                "taskDefinition",
                new Dictionary<string, string>(StringComparer.Ordinal) { ["type"] = "worker" },
                []),
        ],
    };

    private static void AssertTaskExtension(BpmnDocument document)
    {
        var extension = Assert.Single(document.FindFlowNode("Task_1")!.Extras.Extensions);

        Assert.Equal(Zeebe, extension.Namespace);
        Assert.Equal("taskDefinition", extension.LocalName);
        Assert.Equal("worker", extension.Attributes["type"]);
    }

    private static BpmnEditResult Apply(BpmnDocument document, BpmnEditCommand command) =>
        BpmnDocumentEditor.Apply(document, command);

    private static string Codes(BpmnEditResult result) =>
        string.Join(", ", result.Rejections.Select(rejection => $"{rejection.Code}: {rejection.Message}"));

    private static BpmnShape ShapeOf(BpmnDocument document, string elementId) => document.Diagrams
        .SelectMany(diagram => diagram.Plane.Shapes)
        .Single(shape => shape.BpmnElement == elementId);

    /// <summary>A Task, a flow and a shape, each carrying vendor content.</summary>
    private static BpmnDocument Extended()
    {
        var document = new BpmnDocumentBuilder()
            .Node("Task_1", BpmnFlowNodeType.ServiceTask, new Bounds(200, 120, 100, 80), "Work")
            .Node("End_1", BpmnFlowNodeType.EndEvent, new Bounds(420, 142, 36, 36), "Done")
            .Flow("Flow_1", "Task_1", "End_1", [new DiPoint(300, 160), new DiPoint(420, 160)])
            .Build();

        return document with
        {
            FlowNodes = [.. document.FlowNodes.Select(node => node.Id == "Task_1"
                ? node with { Extras = TaskExtras }
                : node)],
            SequenceFlows = [.. document.SequenceFlows.Select(flow => flow with { Extras = TaskExtras })],
            Diagrams = [.. document.Diagrams.Select(diagram => diagram with
            {
                Plane = diagram.Plane with
                {
                    Shapes = [.. diagram.Plane.Shapes.Select(shape => shape.BpmnElement == "Task_1"
                        ? shape with { Extras = TaskExtras }
                        : shape)],
                },
            })],
        };
    }

    private static BpmnDocument ReadSample(string fileName)
    {
        var path = Path.Combine(FindRepositoryRoot(), "playground", "Bpmn.Demo", "wwwroot", "samples", fileName);

        return BpmnXmlReader.Read(File.ReadAllText(path)).Document;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "playground")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("the repository root was not found");
    }
}
