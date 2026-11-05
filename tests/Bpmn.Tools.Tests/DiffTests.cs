using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;

using static Hasseware.AspNetCore.Components.Bpmn.Tools.Tests.Documents;

namespace Hasseware.AspNetCore.Components.Bpmn.Tools.Tests;

/// <summary>The minimal id-based diff (design doc, "Open questions": bpmn_diff).</summary>
public sealed class DiffTests
{
    [Fact]
    public void FindsNothingBetweenADocumentAndItself()
    {
        var document = Simple();

        var diff = BpmnDocumentDiff.Compare(document, document);

        Assert.Empty(diff.Added);
        Assert.Empty(diff.Removed);
        Assert.Empty(diff.Changed);
    }

    [Fact]
    public void ReportsAddedAndRemovedElements()
    {
        var before = Simple();
        var after = BpmnDocumentEditor.Apply(before, new DeleteElementsCommand(["end"])).Document;
        after = BpmnDocumentEditor.Apply(after, new CreateElementCommand("extra", Model.BpmnFlowNodeType.Task, "Process_1", new Bounds(700, 110, 100, 80), "Extra")).Document;

        var diff = BpmnDocumentDiff.Compare(before, after);

        Assert.Equal("end", Assert.Single(diff.Removed).Id);
        var added = Assert.Single(diff.Added);
        Assert.Equal(("extra", "task", "Extra"), (added.Id, added.Type, added.Name?.UntrustedText));
    }

    [Fact]
    public void ReportsAChangedProperty()
    {
        var before = Simple();
        var after = BpmnDocumentEditor.Apply(before, new UpdatePropertiesCommand("review", "name", "Approve")).Document;

        var changed = Assert.Single(BpmnDocumentDiff.Compare(before, after).Changed);

        Assert.Equal("review", changed.Id);
        var property = Assert.Single(changed.Properties);
        Assert.Equal(("name", "Review", "Approve"), (property.Name, property.Before?.UntrustedText, property.After?.UntrustedText));
        Assert.False(changed.GeometryChanged);
    }

    [Fact]
    public void ReportsAMoveAsGeometryOnly()
    {
        var before = Simple();
        var after = BpmnDocumentEditor.Apply(before, new MoveShapesCommand([new ShapeGeometry("end_di", new Bounds(520, 140, 36, 36))])).Document;

        var changed = Assert.Single(BpmnDocumentDiff.Compare(before, after).Changed, element => element.Id == "end");

        Assert.True(changed.GeometryChanged);
        Assert.Empty(changed.Properties);
    }

    [Fact]
    public void ReportsAReconnectedFlow()
    {
        var before = Simple();
        var after = BpmnDocumentEditor.Apply(before, new ReconnectConnectorCommand("f1", SourceRef: null, TargetRef: "end", [new DiPoint(136, 150), new DiPoint(500, 150)])).Document;

        var changed = Assert.Single(BpmnDocumentDiff.Compare(before, after).Changed, element => element.Id == "f1");

        Assert.Equal(("review", "end"), (changed.TargetBefore, changed.TargetAfter));
        Assert.Null(changed.SourceBefore);
    }

    [Fact]
    public void StopsAtTheLimitAndSaysSo()
    {
        var before = Simple();
        var after = BpmnDocumentEditor.Apply(before, new DeleteElementsCommand(["start", "review", "end"])).Document;

        var diff = BpmnDocumentDiff.Compare(before, after, new BpmnToolLimits(MaxElements: 2));

        Assert.Equal(2, diff.Removed.Count + diff.Added.Count + diff.Changed.Count);
        Assert.True(diff.Truncated);
    }

    [Fact]
    public async Task DiffsTheHostsDocumentAgainstABaseline()
    {
        var host = Host(Simple());
        var baseline = host.Document;
        await BpmnTools.SetPropertyAsync(host, new BpmnSetPropertyRequest("review", "name", "Approve"), Token);

        var result = await BpmnTools.DiffAsync(host, baseline, Token);

        Assert.Equal("review", Assert.Single(result.Data!.Changed).Id);
    }

    public static TheoryData<BpmnFlowNodeType, BpmnFlowNodeType> TypePairs
    {
        get
        {
            var data = new TheoryData<BpmnFlowNodeType, BpmnFlowNodeType>();
            foreach (var before in Enum.GetValues<BpmnFlowNodeType>())
            {
                foreach (var after in Enum.GetValues<BpmnFlowNodeType>()) data.Add(before, after);
            }
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(TypePairs))]
    public void TypeChangesPreservePropertyPresenceOrderAndNullSemantics(BpmnFlowNodeType beforeType, BpmnFlowNodeType afterType)
    {
        var before = new BpmnDocumentBuilder()
            .Node("node", beforeType, new Bounds(0, 0, 100, 80), "Before")
            .Build();
        before = before with
        {
            FlowNodes = [before.FlowNodes[0] with { ScriptFormat = "", Script = "before script" }],
        };
        var after = before with
        {
            FlowNodes = [before.FlowNodes[0] with
            {
                Type = afterType, Name = "After", ScriptFormat = "javascript", Script = null,
            }],
        };
        var old = BpmnElementInspector.PropertiesOf(before, "node").ToDictionary(item => item.Name, item => item.Value, StringComparer.Ordinal);
        var now = BpmnElementInspector.PropertiesOf(after, "node").ToDictionary(item => item.Name, item => item.Value, StringComparer.Ordinal);
        var expected = new List<BpmnPropertyChange>();
        if (beforeType != afterType)
        {
            expected.Add(new("type",
                BpmnUntrustedText.From(BpmnElementInspector.WireNameOf(beforeType), 500),
                BpmnUntrustedText.From(BpmnElementInspector.WireNameOf(afterType), 500)));
        }
        foreach (var name in old.Keys.Concat(now.Keys).Distinct(StringComparer.Ordinal))
        {
            var previous = old.GetValueOrDefault(name);
            var current = now.GetValueOrDefault(name);
            if (previous != current)
            {
                expected.Add(new(name, BpmnUntrustedText.From(previous, 500), BpmnUntrustedText.From(current, 500)));
            }
        }

        var changed = Assert.Single(BpmnDocumentDiff.Compare(before, after).Changed);

        Assert.Equal(expected, changed.Properties);
        Assert.False(changed.GeometryChanged);
    }

    [Fact]
    public void GeometryAccumulatesAcrossDiagramsInDocumentOrder()
    {
        var before = Simple();
        var diagram = before.Diagrams[0];
        var additional = diagram with { Id = "second", Plane = diagram.Plane with { Id = "second_plane" } };
        before = before with { Diagrams = [diagram, additional] };
        var route = new List<DiPoint>(additional.Plane.Edges[0].Waypoints);
        var changedDiagram = additional with
        {
            Plane = additional.Plane with { Edges = [additional.Plane.Edges[0] with { Waypoints = route }] },
        };
        var after = before with { Diagrams = [diagram, changedDiagram] };
        Assert.Empty(BpmnDocumentDiff.Compare(before, after).Changed);
        route[^1] = route[^1] with { Y = route[^1].Y + 1 };

        Assert.True(Assert.Single(BpmnDocumentDiff.Compare(before, after).Changed).GeometryChanged);

        route[^1] = additional.Plane.Edges[0].Waypoints[^1];
        Assert.Empty(BpmnDocumentDiff.Compare(before, after).Changed);
        var reordered = before with { Diagrams = [changedDiagram, diagram] };
        route[0] = route[0] with { X = route[0].X + 1 };
        var changed = Assert.Single(BpmnDocumentDiff.Compare(after, reordered).Changed);
        Assert.Equal("f1", changed.Id);
        Assert.True(changed.GeometryChanged);
    }

    [Fact]
    public void EmptyAndMissingRoutesCompareEquallyButMissingShapesDoNot()
    {
        var before = Simple();
        var diagram = before.Diagrams[0];
        before = before with
        {
            Diagrams = [diagram with { Plane = diagram.Plane with { Edges = [diagram.Plane.Edges[0] with { Waypoints = [] }] } }],
        };
        var after = before with { Diagrams = [diagram with { Plane = diagram.Plane with { Edges = [] } }] };
        Assert.Empty(BpmnDocumentDiff.Compare(before, after).Changed);
        after = after with { Diagrams = [] };

        Assert.Equal(["start", "review", "end"], BpmnDocumentDiff.Compare(before, after).Changed.Select(item => item.Id));
    }

    [Fact]
    public void GeometryPreservesNaNAndSignedZeroEquality()
    {
        var before = Simple();
        var diagram = before.Diagrams[0];
        var edge = diagram.Plane.Edges[0] with { Waypoints = [new(double.NaN, -0d), new(double.PositiveInfinity, 1)] };
        before = before with { Diagrams = [diagram with { Plane = diagram.Plane with { Edges = [edge] } }] };
        var after = before with
        {
            Diagrams = [diagram with { Plane = diagram.Plane with
            {
                Edges = [edge with { Waypoints = [new(double.NaN, 0d), new(double.PositiveInfinity, 1)] }],
            } }],
        };

        Assert.Empty(BpmnDocumentDiff.Compare(before, after).Changed);
    }

    [Fact]
    public void RoutedDocumentDiffStaysWithinAllocationBudget()
    {
        var builder = new BpmnDocumentBuilder();
        for (var index = 0; index < 100; index++)
        {
            builder.Node($"node_{index}", BpmnFlowNodeType.Task, new Bounds(index * 100, 0, 100, 80));
            if (index > 0)
            {
                builder.Flow($"flow_{index}", $"node_{index - 1}", $"node_{index}",
                    [.. Enumerable.Range(0, 32).Select(point => new DiPoint(point, point))]);
            }
        }
        var document = builder.Build();
        for (var warm = 0; warm < 3; warm++) BpmnDocumentDiff.Compare(document, document);
        var before = GC.GetAllocatedBytesForCurrentThread();

        var result = BpmnDocumentDiff.Compare(document, document);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Empty(result.Changed);
        Assert.InRange(allocated, 0, 900_000);
    }

    [Fact]
    public void RepeatedDiffsObserveMutableOutgoingFlowLists()
    {
        var builder = new BpmnDocumentBuilder();
        for (var index = 0; index < 9; index++)
        {
            builder.Node($"node_{index}", BpmnFlowNodeType.Task, new Bounds(index * 100, 0, 100, 80));
        }
        var document = builder.Build();
        var flows = new List<BpmnSequenceFlow> { new("first", "node_0", "node_1", "Process_1") };
        var before = document with { SequenceFlows = flows };
        var after = document with
        {
            FlowNodes = [document.FlowNodes[0] with { DefaultFlowRef = "first" }, .. document.FlowNodes.Skip(1)],
            SequenceFlows = flows,
        };
        Assert.Empty(BpmnDocumentDiff.Compare(before, after).Changed);
        flows.Add(new("second", "node_0", "node_2", "Process_1"));

        var changed = Assert.Single(BpmnDocumentDiff.Compare(before, after).Changed);

        Assert.Equal("node_0", changed.Id);
        Assert.Equal("defaultFlowRef", Assert.Single(changed.Properties).Name);
        flows.RemoveAt(1);
        Assert.Empty(BpmnDocumentDiff.Compare(before, after).Changed);
    }
}
