using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Editing;

/// <summary>
/// Moving an element into a different container.
/// </summary>
/// <remarks>
/// CT-008 names a partially-applied reparent as an error in its own right, so what is asserted
/// here is mostly that the pieces move together: semantic ownership, Lane membership, Pool scope
/// and geometry. The other half is the refusals, because the two BPMN rules a reparent can break —
/// a Sequence Flow crossing a container (SF-007) and a container swallowing itself — are both
/// silent corruptions if they get through.
/// </remarks>
public sealed class ReparentTests
{
    [Fact]
    public void MovesANodeIntoASubProcess()
    {
        var result = BpmnDocumentEditor.Apply(
            Loose(),
            new ReparentElementCommand("Task_1", "Sub_1", null, new Bounds(160, 160, 100, 80)));

        Assert.True(result.IsApplied, string.Join(", ", result.Rejections.Select(r => r.Code)));
        Assert.Equal("Sub_1", result.Document.FlowNodes.Single(node => node.Id == "Task_1").ContainerId);
        Assert.Equal(new Bounds(160, 160, 100, 80), ShapeOf(result.Document, "Task_1").Bounds);
    }

    [Fact]
    public void RefusesAMoveThatWouldStrandASequenceFlow()
    {
        // SF-007: a Sequence Flow lives inside one container. Refused rather than deleting the
        // flow, because a reparent is not a delete and the flow is the user's.
        var result = BpmnDocumentEditor.Apply(
            Connected(),
            new ReparentElementCommand("Task_1", "Sub_1", null, new Bounds(160, 160, 100, 80)));

        Assert.False(result.IsApplied);
        Assert.Equal("SF-007", Assert.Single(result.Rejections).Code);
    }

    [Fact]
    public void CarriesAWholeSubProcessAndItsContent()
    {
        var document = Nested();

        var result = BpmnDocumentEditor.Apply(
            document,
            new ReparentElementCommand("Inner_Sub", "Process_2", null, new Bounds(700, 100, 200, 140)));

        Assert.True(result.IsApplied, string.Join(", ", result.Rejections.Select(r => r.Code)));

        // Only the moved node's own containerId changes; its content still points at it, which is
        // what keeps the nested graph intact.
        Assert.Equal("Process_2", result.Document.FlowNodes.Single(node => node.Id == "Inner_Sub").ContainerId);
        Assert.Equal("Inner_Sub", result.Document.FlowNodes.Single(node => node.Id == "Deep_Task").ContainerId);
    }

    [Fact]
    public void RefusesMovingAContainerIntoItself()
    {
        var result = BpmnDocumentEditor.Apply(
            Nested(),
            new ReparentElementCommand("Inner_Sub", "Inner_Sub", null, new Bounds(0, 0, 200, 140)));

        Assert.False(result.IsApplied);
        Assert.Equal("CT-008", Assert.Single(result.Rejections).Code);
    }

    [Fact]
    public void RefusesMovingAContainerIntoItsOwnContent()
    {
        // A cycle: the Sub-Process would end up owned by something it owns.
        var document = Nested() with { };

        var withInnerContainer = document with
        {
            FlowNodes =
            [
                .. document.FlowNodes.Select(node =>
                    node.Id == "Deep_Task" ? node with { FlowElementsOwned = true } : node),
            ],
        };

        var result = BpmnDocumentEditor.Apply(
            withInnerContainer,
            new ReparentElementCommand("Inner_Sub", "Deep_Task", null, new Bounds(0, 0, 200, 140)));

        Assert.False(result.IsApplied);
        Assert.Equal("CT-008", Assert.Single(result.Rejections).Code);
    }

    [Fact]
    public void RefusesMovingABoundaryEvent()
    {
        // Section 2.5 makes attachedToRef the authority and DI-010 pins it to the host's outline,
        // so moving one is re-attachment, not a change of container.
        var result = BpmnDocumentEditor.Apply(
            WithBoundary(),
            new ReparentElementCommand("Boundary_1", "Sub_1", null, new Bounds(160, 160, 36, 36)));

        Assert.False(result.IsApplied);
        Assert.Equal("CT-005", Assert.Single(result.Rejections).Code);
    }

    [Fact]
    public void RefusesAContainerThatCannotHoldAnything()
    {
        var result = BpmnDocumentEditor.Apply(
            Loose(),
            new ReparentElementCommand("Task_1", "Event_1", null, new Bounds(0, 0, 100, 80)));

        Assert.False(result.IsApplied);
        Assert.Equal("CT-008", Assert.Single(result.Rejections).Code);
    }

    // --- lanes ----------------------------------------------------------------

    [Fact]
    public void MovesBetweenLanesWithoutChangingOwnership()
    {
        /*
         * Section 2.8.1: Lane membership is not ownership. A Flow Node inside a Lane is owned by
         * the Lane's Process and merely listed in flowNodeRefs, so a move between Lanes leaves
         * containerId alone.
         */
        var document = TwoLanes();

        var result = BpmnDocumentEditor.Apply(
            document,
            new ReparentElementCommand("Task_1", "Process_1", "Lane_2", new Bounds(220, 160, 100, 80)));

        Assert.True(result.IsApplied, string.Join(", ", result.Rejections.Select(r => r.Code)));

        var node = result.Document.FlowNodes.Single(candidate => candidate.Id == "Task_1");
        Assert.Equal("Process_1", node.ContainerId);
        Assert.Equal("Lane_2", node.LaneId);

        Assert.DoesNotContain("Task_1", result.Document.Lanes.Single(lane => lane.Id == "Lane_1").FlowNodeRefs);
        Assert.Contains("Task_1", result.Document.Lanes.Single(lane => lane.Id == "Lane_2").FlowNodeRefs);
    }

    [Fact]
    public void DropsLaneMembershipWhenNoLaneIsNamed()
    {
        var result = BpmnDocumentEditor.Apply(
            TwoLanes(),
            new ReparentElementCommand("Task_1", "Process_1", null, new Bounds(220, 20, 100, 80)));

        Assert.True(result.IsApplied);
        Assert.All(result.Document.Lanes, lane => Assert.DoesNotContain("Task_1", lane.FlowNodeRefs));
        Assert.Null(result.Document.FlowNodes.Single(node => node.Id == "Task_1").LaneId);
    }

    [Fact]
    public void RefusesALaneThatBelongsToADifferentContainer()
    {
        var document = TwoLanes();

        var result = BpmnDocumentEditor.Apply(
            document,
            new ReparentElementCommand("Task_1", "Sub_1", "Lane_1", new Bounds(160, 160, 100, 80)));

        Assert.False(result.IsApplied);
        Assert.Equal("CT-008", Assert.Single(result.Rejections).Code);
    }

    [Fact]
    public void FollowsThePoolScopeOfTheNewContainer()
    {
        // poolId decides Message Flow legality (section 2.8), so it has to follow the move or the
        // rules would judge the node against the Pool it used to be in.
        var result = BpmnDocumentEditor.Apply(
            TwoPools(),
            new ReparentElementCommand("A_Task", "Process_B", null, new Bounds(100, 400, 100, 80)));

        Assert.True(result.IsApplied, string.Join(", ", result.Rejections.Select(r => r.Code)));
        Assert.Equal("Participant_B", result.Document.FlowNodes.Single(node => node.Id == "A_Task").PoolId);
    }

    [Fact]
    public void ARoundTripSurvivesAReparent()
    {
        var result = BpmnDocumentEditor.Apply(
            Loose(),
            new ReparentElementCommand("Task_1", "Sub_1", null, new Bounds(160, 160, 100, 80)));

        var xml = Hasseware.AspNetCore.Components.Bpmn.Serialization.BpmnXmlWriter.Write(result.Document).Xml;
        var reread = Hasseware.AspNetCore.Components.Bpmn.Serialization.BpmnXmlReader.Read(xml).Document;

        Assert.Equal("Sub_1", reread.FlowNodes.Single(node => node.Id == "Task_1").ContainerId);
    }

    // --- fixtures -------------------------------------------------------------

    private static BpmnShape ShapeOf(BpmnDocument document, string elementId) =>
        document.Diagrams.SelectMany(diagram => diagram.Plane.Shapes)
            .Single(shape => shape.BpmnElement == elementId);

    private static BpmnDocument Loose() => new BpmnDocumentBuilder("Process_1")
        .Node("Event_1", BpmnFlowNodeType.StartEvent, new Bounds(40, 40, 36, 36), "Start")
        .Node("Sub_1", BpmnFlowNodeType.SubProcess, new Bounds(120, 120, 350, 200), "Container")
        .Node("Task_1", BpmnFlowNodeType.UserTask, new Bounds(600, 40, 100, 80), "Loose")
        .Build();

    private static BpmnDocument Connected() => new BpmnDocumentBuilder("Process_1")
        .Node("Event_1", BpmnFlowNodeType.StartEvent, new Bounds(40, 40, 36, 36), "Start")
        .Node("Sub_1", BpmnFlowNodeType.SubProcess, new Bounds(120, 120, 350, 200), "Container")
        .Node("Task_1", BpmnFlowNodeType.UserTask, new Bounds(600, 40, 100, 80), "Connected")
        .Flow("Flow_1", "Event_1", "Task_1", [new DiPoint(76, 58), new DiPoint(600, 80)])
        .Build();

    private static BpmnDocument Nested()
    {
        var document = new BpmnDocumentBuilder("Process_1")
            .Node("Inner_Sub", BpmnFlowNodeType.SubProcess, new Bounds(120, 120, 300, 180), "Inner")
            .Node("Deep_Task", BpmnFlowNodeType.UserTask, new Bounds(160, 160, 100, 80), "Deep", containerId: "Inner_Sub")
            .Build();

        return document with
        {
            Processes = [.. document.Processes, new BpmnProcess("Process_2", "Second")],
        };
    }

    private static BpmnDocument WithBoundary() => new BpmnDocumentBuilder("Process_1")
        .Node("Sub_1", BpmnFlowNodeType.SubProcess, new Bounds(120, 120, 350, 200), "Container")
        .Node("Task_1", BpmnFlowNodeType.UserTask, new Bounds(600, 40, 100, 80), "Host")
        .Node(
            "Boundary_1",
            BpmnFlowNodeType.BoundaryEvent,
            new Bounds(682, 102, 36, 36),
            "Timeout",
            BpmnEventDefinitionType.Timer,
            attachedToRef: "Task_1")
        .Build();

    private static BpmnDocument TwoLanes() => new BpmnDocumentBuilder("Process_1")
        .Pool("Participant_1", "Customer", new Bounds(0, 0, 600, 250))
        .Lane("Lane_1", "Sales", new Bounds(30, 0, 570, 125), ["Task_1"])
        .Lane("Lane_2", "Support", new Bounds(30, 125, 570, 125), [])
        .Node("Task_1", BpmnFlowNodeType.UserTask, new Bounds(220, 20, 100, 80), "Work")
        .Node("Sub_1", BpmnFlowNodeType.SubProcess, new Bounds(340, 130, 200, 110), "Container")
        .Build();

    /// <summary>Two Pools whose second Participant references a Process that really exists.</summary>
    private static BpmnDocument TwoPools()
    {
        var document = new BpmnDocumentBuilder("Process_A")
            .Pool("Participant_A", "A", new Bounds(0, 0, 600, 250))
            .Pool("Participant_B", "B", new Bounds(0, 300, 600, 250), processRef: "Process_B")
            .Node("A_Task", BpmnFlowNodeType.UserTask, new Bounds(220, 60, 100, 80), "A work")
            .Build();

        return document with
        {
            Processes = [.. document.Processes, new BpmnProcess("Process_B", "B")],
        };
    }
}
