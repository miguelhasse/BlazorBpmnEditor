using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Editing;

public sealed class PoolBootstrapTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyDocumentCanStartWithPoolOrLane(bool lane)
    {
        var before = new BpmnDocument { Definitions = new BpmnDefinitions("Definitions", "http://example.org/bpmn") };
        var history = new BpmnEditHistory(before);
        var command = lane
            ? (BpmnEditCommand)new CreateLaneCommand("Lane", "Process")
            : new CreatePoolCommand("Pool", "Process", new Bounds(100, 200, 600, 250));
        var result = history.Apply(command);
        Assert.True(result.IsApplied, string.Join(", ", result.Diagnostics));
        Assert.Equal("Process", Assert.Single(result.Document.Processes).Id);
        Assert.Equal("Process", Assert.Single(result.Document.Collaboration!.Participants).ProcessRef);
        Assert.Equal(result.Document.Collaboration.Id, Assert.Single(result.Document.Diagrams).Plane.BpmnElement);
        Assert.Equal(lane ? 1 : 0, result.Document.Lanes.Count);
        Assert.Same(before, history.Undo());
        Assert.Same(result.Document, history.Redo());
        var reload = BpmnXmlReader.Read(BpmnXmlWriter.Write(result.Document).Xml).Document;
        Assert.Single(reload.Collaboration!.Participants);
        Assert.Equal(result.Document.Lanes.Count, reload.Lanes.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WrapsActiveProcessWithoutMovingAnyExistingGeometry(bool vertical)
    {
        var document = Populated();
        var oldPlane = document.Diagrams[0].Plane;
        var result = BpmnDocumentEditor.Apply(document,
            new CreatePoolCommand("Pool", "Process_1", new Bounds(900, 900, 600, 250), IsHorizontal: !vertical)
            { DiagramId = "Diagram_1", ContentBounds = new Bounds(-200, -180, 1000, 600) });
        Assert.True(result.IsApplied, string.Join(", ", result.Diagnostics));
        var after = result.Document;
        Assert.Equal(2, after.Processes.Count);
        Assert.Same(document.Diagrams[1], after.Diagrams[1]);
        Assert.Equal(oldPlane.Shapes, after.Diagrams[0].Plane.Shapes.Where(shape => shape.BpmnElement != "Pool"));
        Assert.Equal(oldPlane.Edges, after.Diagrams[0].Plane.Edges);
        Assert.Equal(document.FlowNodes.Select(node => (node.Id, node.ContainerId)), after.FlowNodes.Select(node => (node.Id, node.ContainerId)));
        Assert.All(after.FlowNodes.Where(node => node.Id != "Other"), node => Assert.Equal("Pool", node.PoolId));
        Assert.Null(after.FindFlowNode("Other")!.PoolId);
        var pool = after.Diagrams[0].Plane.Shapes.Single(shape => shape.BpmnElement == "Pool").Bounds;
        Assert.True(pool.X < -200 && pool.Y < -180 && pool.X + pool.Width > 800 && pool.Y + pool.Height > 420);
        var reload = BpmnXmlReader.Read(BpmnXmlWriter.Write(after).Xml).Document;
        Assert.Equal(after.Diagrams[0].Plane.Shapes, reload.Diagrams[0].Plane.Shapes);
    }

    [Fact]
    public void FirstLaneClassifiesOnlyDirectFlowNodesAndPreservesAllContent()
    {
        var document = Populated();
        var result = BpmnDocumentEditor.Apply(document, new CreateLaneCommand("Lane", "Process_1")
        { DiagramId = "Diagram_1" });
        Assert.True(result.IsApplied);
        var lane = Assert.Single(result.Document.Lanes);
        Assert.Equal(["A", "Sub"], lane.FlowNodeRefs);
        Assert.Equal("Lane", result.Document.FindFlowNode("A")!.LaneId);
        Assert.Null(result.Document.FindFlowNode("Inner")!.LaneId);
        Assert.Equal("Sub", result.Document.FindFlowNode("Inner")!.ContainerId);
        Assert.Equal(document.Diagrams[0].Plane.Shapes,
            result.Document.Diagrams[0].Plane.Shapes.Where(shape => document.Diagrams[0].Plane.Shapes.Any(old => old.Id == shape.Id)));
        Assert.Equal(document.Diagrams[0].Plane.Edges, result.Document.Diagrams[0].Plane.Edges);
        Assert.Single(result.Document.TextAnnotations);
        Assert.Single(result.Document.DataObjectReferences);
    }

    [Fact]
    public void FailedLaneBootstrapDoesNotLeaveStructuralRootsBehind()
    {
        var document = Populated();
        var result = BpmnDocumentEditor.Apply(document,
            new CreateLaneCommand("Lane", "Process_1", FlowNodeRefs: ["Other"]) { DiagramId = "Diagram_1" });
        Assert.False(result.IsApplied);
        Assert.Same(document, result.Document);
        Assert.Equal("CT-004", Assert.Single(result.Rejections).Code);
    }

    [Fact]
    public void MissingDiagramAndDuplicateProcessPoolAreRejectedAtomically()
    {
        var document = Populated();
        var invalid = BpmnDocumentEditor.Apply(document,
            new CreatePoolCommand("Pool", "Process_1", new Bounds(0, 0, 600, 250)) { DiagramId = "Missing" });
        Assert.False(invalid.IsApplied);
        Assert.Same(document, invalid.Document);
        var created = BpmnDocumentEditor.Apply(document,
            new CreatePoolCommand("Pool", "Process_1", new Bounds(0, 0, 600, 250)));
        var duplicate = BpmnDocumentEditor.Apply(created.Document,
            new CreatePoolCommand("Second", "Process_1", new Bounds(0, 500, 600, 250)));
        Assert.False(duplicate.IsApplied);
        Assert.Same(created.Document, duplicate.Document);
    }

    [Theory]
    [InlineData("Pool", "Pool")]
    [InlineData("Pool", "Pool_di")]
    [InlineData("Pool", "Pool_Collaboration")]
    [InlineData("A_di", "Process_1")]
    [InlineData("Definitions_1", "Process_1")]
    public void PoolBootstrapRejectsCollidingSemanticAndDiagramIds(string id, string processId)
    {
        var document = Populated();
        if (processId != "Process_1") document = document with { Diagrams = [] };
        var result = BpmnDocumentEditor.Apply(document,
            new CreatePoolCommand(id, processId, new Bounds(0, 0, 600, 250)));
        Assert.False(result.IsApplied);
        Assert.Same(document, result.Document);
        Assert.Equal("EDIT-030", Assert.Single(result.Rejections).Code);
    }

    [Fact]
    public void ExistingPoolLaneRejectsInvalidMeasuredBoundsWithoutChanges()
    {
        var pooled = BpmnDocumentEditor.Apply(Populated(),
            new CreatePoolCommand("Pool", "Process_1", new Bounds(0, 0, 600, 250))).Document;
        var result = BpmnDocumentEditor.Apply(pooled, new CreateLaneCommand("Lane", "Process_1")
        { ContentBounds = new Bounds(0, 0, double.NaN, 10) });
        Assert.False(result.IsApplied);
        Assert.Same(pooled, result.Document);
        Assert.Equal("DI-003", Assert.Single(result.Rejections).Code);
    }

    [Theory]
    [InlineData("Lane", null)]
    [InlineData("Lane_di", null)]
    [InlineData("Process", "Lane_Pool")]
    [InlineData("Process", "Lane_Pool_Collaboration")]
    public void LaneBootstrapIdCollisionRollsBackPoolAndProcess(string processId, string? laneSetId)
    {
        var document = new BpmnDocument { Definitions = new BpmnDefinitions("Definitions", "http://example.org/bpmn") };
        var result = BpmnDocumentEditor.Apply(document,
            new CreateLaneCommand("Lane", processId, LaneSetId: laneSetId));
        Assert.False(result.IsApplied);
        Assert.Same(document, result.Document);
        Assert.Equal("EDIT-030", Assert.Single(result.Rejections).Code);
    }

    [Fact]
    public void FirstLaneExpansionCannotOverlapAnotherPool()
    {
        var document = new BpmnDocumentBuilder()
            .Pool("Pool", "Pool", new Bounds(0, 0, 200, 200))
            .Node("A", BpmnFlowNodeType.Task, new Bounds(40, 40, 100, 80))
            .Process("Other")
            .Pool("OtherPool", "Other Pool", new Bounds(-100, 0, 70, 200), processRef: "Other")
            .Build();
        var result = BpmnDocumentEditor.Apply(document, new CreateLaneCommand("Lane", "Process_1"));
        Assert.False(result.IsApplied);
        Assert.Same(document, result.Document);
        Assert.Equal("DI-011", Assert.Single(result.Rejections).Code);
    }

    [Fact]
    public void FirstLaneExpansionKeepsMessageFlowAttachedToPoolWithoutReroutingProcessContent()
    {
        var document = new BpmnDocumentBuilder()
            .Pool("Pool", "Pool", new Bounds(100, 100, 600, 250))
            .Node("A", BpmnFlowNodeType.Task, new Bounds(140, 150, 100, 80))
            .Node("B", BpmnFlowNodeType.Task, new Bounds(350, 150, 100, 80))
            .Flow("Sequence", "A", "B", [new DiPoint(240, 190), new DiPoint(350, 190)])
            .Process("Other")
            .Pool("OtherPool", "Other Pool", new Bounds(-800, 100, 600, 250), processRef: "Other")
            .MessageFlow("Message", "Pool", "OtherPool", [new DiPoint(100, 200), new DiPoint(-200, 200)])
            .Build();
        var result = BpmnDocumentEditor.Apply(document, new CreateLaneCommand("Lane", "Process_1")
        { DiagramId = "Diagram_1" });
        Assert.True(result.IsApplied);
        var shapes = result.Document.Diagrams[0].Plane.Shapes;
        var pool = shapes.Single(shape => shape.BpmnElement == "Pool").Bounds;
        Assert.True(pool.X < 100);
        var route = result.Document.Diagrams[0].Plane.Edges.Single(edge => edge.BpmnElement == "Message").Waypoints;
        Assert.Equal(new DiPoint(pool.X, pool.Y + (100d / 250 * pool.Height)), route[0]);
        Assert.Equal(new DiPoint(-200, 200), route[^1]);
        Assert.Same(document.Diagrams[0].Plane.Edges.Single(edge => edge.BpmnElement == "Sequence"),
            result.Document.Diagrams[0].Plane.Edges.Single(edge => edge.BpmnElement == "Sequence"));
        Assert.All(document.Diagrams[0].Plane.Shapes.Where(shape => shape.BpmnElement != "Pool"),
            original => Assert.Same(original, shapes.Single(shape => shape.Id == original.Id)));
    }

    [Fact]
    public void EmptyCollaborationCanStartWithDirectLaneCreation()
    {
        var document = new BpmnDocument
        {
            Definitions = new BpmnDefinitions("Definitions", "http://example.org/bpmn"),
            Collaboration = new BpmnCollaboration("Collaboration", null, []),
            Diagrams = [new BpmnDiagram("Diagram", new BpmnPlane("Plane", "Collaboration", [], []))],
        };
        var result = BpmnDocumentEditor.Apply(document, new CreateLaneCommand("Lane", "Process") { DiagramId = "Diagram" });
        Assert.True(result.IsApplied);
        Assert.Equal("Collaboration", result.Document.Collaboration!.Id);
        Assert.Equal("Process", Assert.Single(result.Document.Processes).Id);
        Assert.Equal("Process", Assert.Single(result.Document.Collaboration.Participants).ProcessRef);
        Assert.Single(result.Document.Lanes);
        Assert.Empty(Assert.Single(result.Document.Lanes).FlowNodeRefs);
    }

    [Fact]
    public void BootstrapCannotReuseAnotherProcessesLaneSetId()
    {
        var document = Populated() with
        {
            LaneSets = [new BpmnLaneSet("OtherLaneSet", "Process_2", [])],
        };
        var result = BpmnDocumentEditor.Apply(document,
            new CreateLaneCommand("Lane", "Process_1", LaneSetId: "OtherLaneSet") { DiagramId = "Diagram_1" });
        Assert.False(result.IsApplied);
        Assert.Same(document, result.Document);
        Assert.Equal("EDIT-030", Assert.Single(result.Rejections).Code);
    }

    private static BpmnDocument Populated()
    {
        var document = new BpmnDocumentBuilder()
            .Node("A", BpmnFlowNodeType.Task, new Bounds(-100, -50, 100, 80))
            .Node("Sub", BpmnFlowNodeType.SubProcess, new Bounds(200, -50, 350, 200))
            .Node("Inner", BpmnFlowNodeType.Task, new Bounds(250, 0, 100, 80), containerId: "Sub")
            .TextAnnotation("Note", "Note", new Bounds(100, 220, 100, 50))
            .DataObject("Data", "Data", new Bounds(200, 220, 36, 50))
            .Flow("Flow", "A", "Sub", [new DiPoint(0, -10), new DiPoint(100, -10), new DiPoint(200, -10)])
            .Build();
        var diagram = document.Diagrams[0];
        return document with
        {
            Processes = [.. document.Processes, new BpmnProcess("Process_2")],
            FlowNodes = [.. document.FlowNodes, new BpmnFlowNode("Other", BpmnFlowNodeType.Task, "Process_2")],
            Diagrams = [diagram, new BpmnDiagram("OtherDiagram", new BpmnPlane("OtherPlane", "Process_2", [], []))],
        };
    }
}
