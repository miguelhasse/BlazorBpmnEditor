using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Serialization;

public sealed class DocumentBuilderTests
{
    [Fact]
    public void DistinctProcessesKeepTheirPoolsLanesAndNestedNodes()
    {
        var document = new BpmnDocumentBuilder("Process_A")
            .Process("Process_B")
            .Pool("Pool_A", "A", new Bounds(0, 0, 600, 200))
            .Pool("Pool_B", "B", new Bounds(0, 240, 600, 200), processRef: "Process_B")
            .Lane("Lane_A", "A", new Bounds(30, 0, 570, 200), ["Task_A"])
            .Lane("Lane_B", "B", new Bounds(30, 240, 570, 200), ["Task_B"],
                laneSetId: "LaneSet_B", containerId: "Process_B")
            .Node("Task_A", BpmnFlowNodeType.Task, new Bounds(100, 60, 100, 80))
            .Node("Task_B", BpmnFlowNodeType.Task, new Bounds(100, 300, 100, 80), containerId: "Process_B")
            .Node("Sub_B", BpmnFlowNodeType.SubProcess, new Bounds(250, 260, 300, 160),
                containerId: "Process_B", isExpanded: true)
            .Node("Inner_B", BpmnFlowNodeType.Task, new Bounds(300, 300, 100, 80), containerId: "Sub_B")
            .Build();

        Assert.Equal(["Process_A", "Process_B"], document.Processes.Select(process => process.Id));
        Assert.Equal("Process_B", document.LaneSets.Single(set => set.Id == "LaneSet_B").ContainerId);
        Assert.Equal("Pool_A", document.FindFlowNode("Task_A")!.PoolId);
        Assert.Equal("Lane_A", document.FindFlowNode("Task_A")!.LaneId);
        Assert.Equal("Pool_B", document.FindFlowNode("Task_B")!.PoolId);
        Assert.Equal("Lane_B", document.FindFlowNode("Task_B")!.LaneId);
        Assert.Equal("Pool_B", document.FindFlowNode("Inner_B")!.PoolId);

        var read = BpmnXmlReader.Read(BpmnXmlWriter.Write(document).Xml).Document;
        Assert.Equal("Process_B", read.LaneSets.Single(set => set.Id == "LaneSet_B").ContainerId);
        Assert.Equal("Pool_B", read.FindFlowNode("Inner_B")!.PoolId);
    }

    [Fact]
    public void BlackBoxPoolHasNoProcessWhileDefaultPoolKeepsDefaultOwner()
    {
        var document = new BpmnDocumentBuilder()
            .Pool("White", "White", new Bounds(0, 0, 400, 160))
            .BlackBoxPool("Black", "Black", new Bounds(0, 200, 400, 90), isHorizontal: false)
            .Build();

        var participants = document.Collaboration!.Participants;
        Assert.Equal("Process_1", participants.Single(pool => pool.Id == "White").ProcessRef);
        Assert.Null(participants.Single(pool => pool.Id == "Black").ProcessRef);
        Assert.False(document.Diagrams[0].Plane.Shapes.Single(shape => shape.BpmnElement == "Black").IsHorizontal);

        var read = BpmnXmlReader.Read(BpmnXmlWriter.Write(document).Xml).Document;
        Assert.Null(read.Collaboration!.Participants.Single(pool => pool.Id == "Black").ProcessRef);
    }

    [Fact]
    public void LaneSetCannotBeReassignedToAnotherContainer()
    {
        var builder = new BpmnDocumentBuilder("Process_A")
            .Process("Process_B")
            .Lane("Lane_A", "A", new Bounds(0, 0, 400, 100), []);

        Assert.Throws<ArgumentException>(() => builder.Lane(
            "Lane_B", "B", new Bounds(0, 100, 400, 100), [], containerId: "Process_B"));
    }
}
