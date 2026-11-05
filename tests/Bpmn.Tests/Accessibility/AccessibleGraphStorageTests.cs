using Hasseware.AspNetCore.Components.Bpmn.Accessibility;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Accessibility;

public sealed class AccessibleGraphStorageTests
{
    [Fact]
    public void ConnectedGraphAvoidsIntermediateFlowGroupAllocations()
    {
        var builder = new BpmnDocumentBuilder();
        for (var index = 0; index < 600; index++)
        {
            builder.Node($"Node_{index}", BpmnFlowNodeType.Task, new Bounds(index * 200, 0, 100, 80));
            if (index > 0) builder.Flow($"Flow_{index}", $"Node_{index - 1}", $"Node_{index}", []);
        }
        var document = builder.Build();
        for (var warm = 0; warm < 10; warm++) _ = BpmnAccessibleGraph.For(document, null);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var graph = BpmnAccessibleGraph.For(document, null);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(600, graph.Count);
        Assert.InRange(allocated, 0, 500_000);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AdjacencyPreservesDocumentOrderDuplicatesAndSelfReferences(bool reverse)
    {
        var document = Document();
        if (reverse) document = document with { SequenceFlows = [.. document.SequenceFlows.Reverse()] };

        var graph = BpmnAccessibleGraph.For(document, null);
        var drawn = document.Diagrams[0].Plane.Shapes.Select(shape => shape.BpmnElement).ToHashSet(StringComparer.Ordinal);

        foreach (var node in graph)
        {
            Assert.Equal(document.SequenceFlows.Where(flow => flow.SourceRef == node.ElementId && drawn.Contains(flow.TargetRef))
                .Select(flow => flow.TargetRef), node.Outgoing);
            Assert.Equal(document.SequenceFlows.Where(flow => flow.TargetRef == node.ElementId && drawn.Contains(flow.SourceRef))
                .Select(flow => flow.SourceRef), node.Incoming);
        }
        Assert.Equal(["First", "Second", "Note"], graph.Select(node => node.ElementId));
        Assert.Equal(["First label"], graph[2].Attached);
    }

    [Fact]
    public void GraphCallsDoNotShareMutableAdjacencyStorage()
    {
        var document = Document();
        var first = BpmnAccessibleGraph.For(document, null);
        var second = BpmnAccessibleGraph.For(document, null);

        Assert.NotSame(first[0].Outgoing, second[0].Outgoing);
        Assert.NotSame(first[0].Outgoing, first[0].Incoming);
        Assert.True(BpmnGraphNode.SameContent(first[0], second[0]));
    }

    [Fact]
    public void FreshGraphObservesHostFlowArrayChangesWithoutMutatingPriorResults()
    {
        var document = Document();
        var flows = document.SequenceFlows.ToArray();
        document = document with { SequenceFlows = flows };
        var before = BpmnAccessibleGraph.For(document, null);
        flows[0] = flows[0] with { TargetRef = "First" };

        var after = BpmnAccessibleGraph.For(document, null);

        Assert.Equal(["Second", "First", "Second"], before[0].Outgoing);
        Assert.Equal(["First", "First", "Second"], after[0].Outgoing);
    }

    [Fact]
    public void NullFlowEndpointsRemainExcludedWhenNotDrawn()
    {
        var document = Document();
        document = document with
        {
            SequenceFlows = [.. document.SequenceFlows,
                document.SequenceFlows[0] with { Id = "NullSource", SourceRef = null! },
                document.SequenceFlows[0] with { Id = "NullTarget", TargetRef = null! }],
        };

        var graph = BpmnAccessibleGraph.For(document, null);

        Assert.Equal(["Second", "First", "Second"], graph[0].Outgoing);
        Assert.Equal(["First"], graph[0].Incoming);
    }

    private static BpmnDocument Document() => new BpmnDocumentBuilder()
        .Node("First", BpmnFlowNodeType.Task, new Bounds(0, 0, 100, 80), "First label")
        .Node("Second", BpmnFlowNodeType.Task, new Bounds(200, 0, 100, 80))
        .Flow("FirstFlow", "First", "Second", [])
        .Flow("Self", "First", "First", [])
        .Flow("Duplicate", "First", "Second", [])
        .Flow("HiddenTarget", "First", "Hidden", [])
        .Flow("HiddenSource", "Hidden", "First", [])
        .TextAnnotation("Note", "  Note\n text ", new Bounds(0, 100, 100, 50))
        .Association("Attach", "Note", "First", [])
        .Build();
}
