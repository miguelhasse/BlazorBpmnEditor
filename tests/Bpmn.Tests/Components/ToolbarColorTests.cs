using System.Collections;
using System.Collections.Immutable;
using Bunit;
using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions;
using Microsoft.AspNetCore.Components;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Components;

public sealed class ToolbarColorTests : BunitContext
{
    [Fact]
    public void ReadoutsMatchOrderedFullScanAcrossSelectionsAndPlanes()
    {
        var document = Document();
        var first = document.Diagrams[0];
        var second = first with
        {
            Id = "Second",
            Plane = first.Plane with
            {
                Shapes = first.Plane.Shapes.Select(shape => shape with
                {
                    Extras = Color("background-color", "#123456"),
                    Label = null,
                }).ToArray(),
                Edges = [],
            },
        };
        document = document with { Diagrams = [first, second] };
        var root = RenderProbe();
        var content = root.Instance.ChildContent;
        var probe = root.FindComponent<ColorProbe>().Instance;
        string[][] selections =
        [
            [], ["Task_0"], ["task_0"], ["missing"], ["task_0", "missing"],
            ["Task_0", "Task_1"], ["Task_0", "Task_0", "missing"],
            ["Flow_1"], ["Flow_1", "Flow_2"], ["Task_0", "Flow_1"],
            [.. document.FlowNodes.Select(node => node.Id)],
            [.. document.FlowNodes.Select(node => node.Id), "Flow_1", "missing"],
        ];

        var replacement = document with { Diagrams = [second with { Id = first.Id }, first with { Id = second.Id }] };
        foreach (var currentDocument in new[] { document, replacement, document })
            foreach (var diagramId in new string?[] { null, first.Id, second.Id, "missing" })
                foreach (var readOnly in new[] { false, true })
                    foreach (var selection in selections)
                    {
                        var state = Assert.IsType<BpmnEditorState>(root.Instance.Value) with
                        {
                            Document = currentDocument,
                            ActiveDiagramId = diagramId,
                            Selection = [.. selection],
                            IsReadOnly = readOnly,
                        };
                        root.Render(p => p.Add(c => c.Value, state).Add(c => c.ChildContent, content));
                        var plane = diagramId is null ? currentDocument.Diagrams[0].Plane
                            : currentDocument.Diagrams.FirstOrDefault(diagram => diagram.Id == diagramId)?.Plane;
                        foreach (var channel in Enum.GetValues<BpmnColorChannel>())
                        {
                            var values = FullScan(plane, state.Selection, channel).Distinct(StringComparer.OrdinalIgnoreCase).Take(2).ToArray();
                            var value = values.Length == 1 ? values[0] : null;
                            var (enabled, actualValue, status) = probe.Read(channel);
                            Assert.True((!readOnly && values.Length > 0) == enabled,
                                $"diagram={diagramId}; readOnly={readOnly}; selection={string.Join(",", selection)}; channel={channel}; expectedCount={values.Length}; enabled={enabled}");
                            Assert.Equal(value, actualValue);
                            Assert.Equal(values.Length > 1 ? "Mixed" : value ?? "Theme default", status);
                        }
                    }
    }

    [Fact]
    public void EmptySelectionDoesNotEnumerateDiForReadouts()
    {
        var document = Document();
        var first = document.Diagrams[0];
        var shapes = new CountingItems<BpmnShape>(first.Plane.Shapes);
        var edges = new CountingItems<BpmnEdge>(first.Plane.Edges);
        document = document with { Diagrams = [first with { Plane = first.Plane with { Shapes = shapes, Edges = edges } }] };
        var root = RenderProbe();
        var content = root.Instance.ChildContent;
        root.Render(p => p.Add(c => c.Value, Assert.IsType<BpmnEditorState>(root.Instance.Value) with
        {
            Document = document,
            Selection = [],
        }).Add(c => c.ChildContent, content));

        foreach (var channel in Enum.GetValues<BpmnColorChannel>())
            Assert.Equal((false, null, "Theme default"), root.FindComponent<ColorProbe>().Instance.Read(channel));

        Assert.Equal(0, shapes.Enumerations);
        Assert.Equal(0, edges.Enumerations);
    }

    private static IEnumerable<string?> FullScan(BpmnPlane? plane, ImmutableArray<string> selection, BpmnColorChannel channel)
    {
        if (plane is null) yield break;
        foreach (var shape in plane.Shapes.Where(shape => selection.Contains(shape.BpmnElement)))
            yield return channel switch
            {
                BpmnColorChannel.Background => shape.BackgroundColor,
                BpmnColorChannel.Border => shape.BorderColor,
                _ => shape.Label?.Color,
            };
        if (channel == BpmnColorChannel.Background) yield break;
        foreach (var edge in plane.Edges.Where(edge => selection.Contains(edge.BpmnElement)))
            yield return channel == BpmnColorChannel.Border ? edge.BorderColor : edge.Label?.Color;
    }

    private static BpmnDocument Document()
    {
        var builder = new BpmnDocumentBuilder();
        for (var index = 0; index < 602; index++)
        {
            builder.Node($"Task_{index}", BpmnFlowNodeType.Task, new Bounds(index * 150, 0, 100, 80));
            if (index > 0)
                builder.Flow($"Flow_{index}", $"Task_{index - 1}", $"Task_{index}",
                    [new DiPoint(((index - 1) * 150) + 100, 40), new DiPoint(index * 150, 40)]);
        }
        var document = builder.Build();
        var first = document.Diagrams[0];
        return document with
        {
            Diagrams = [first with
            {
                Plane = first.Plane with
                {
                    Shapes = first.Plane.Shapes.Select((shape, index) => shape with
                    {
                        Extras = index == 0 ? Color("background-color", "#abcdef") : shape.Extras,
                        Label = index == 1 ? new BpmnLabel { Extras = Color("color", "#654321") } : null,
                    }).ToArray(),
                    Edges = first.Plane.Edges.Select((edge, index) => edge with
                    {
                        Extras = Color("border-color", index % 2 == 0 ? "#ABCDEF" : "#abcdef"),
                        Label = new BpmnLabel { Extras = Color("color", "#654321") },
                    }).ToArray(),
                },
            }],
        };
    }

    private static BpmnElementExtras Color(string channel, string value) => new()
    {
        ForeignAttributes = new Dictionary<string, string> { [$"{{http://www.omg.org/spec/BPMN/non-normative/color/1.0}}{channel}"] = value },
    };

    private IRenderedComponent<CascadingValue<BpmnEditorState>> RenderProbe()
    {
        Services.AddBpmnComponents();
        var session = new BpmnEditorSession(new BpmnOptions(), Services);
        return Render<CascadingValue<BpmnEditorState>>(p => p.Add(c => c.Value, session.State)
            .AddChildContent<CascadingValue<BpmnEditorSession>>(p => p.Add(c => c.Value, session)
                .AddChildContent<ColorProbe>()));
    }

    public sealed class ColorProbe : BpmnToolbarBase
    {
        public (bool Enabled, string? Value, string Status) Read(BpmnColorChannel channel) =>
            (CanColor(channel), ColorValue(channel), ColorStatus(channel));
    }

    private sealed class CountingItems<T>(IReadOnlyList<T> items) : IReadOnlyList<T>
    {
        public int Enumerations { get; private set; }
        public int Count => items.Count;
        public T this[int index] => items[index];
        public IEnumerator<T> GetEnumerator()
        {
            Enumerations++;
            return items.GetEnumerator();
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
