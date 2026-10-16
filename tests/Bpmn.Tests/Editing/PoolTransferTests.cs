using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Editing;

/// <summary>
/// Dragging content out of one Pool and dropping it in another moves it there.
/// </summary>
/// <remarks>
/// The alternative — growing the source Pool to reach the drop point — draws the element in a Pool
/// whose Process does not own it, overlapping the Pool it was actually dropped on.
/// </remarks>
public sealed class PoolTransferTests
{
    [Fact]
    public void ATaskDroppedInAnotherPoolIsOwnedByThatPoolsProcess()
    {
        var document = TwoPools();
        var sourceBefore = ShapeOf(document, "Participant_A").Bounds;
        var targetBefore = ShapeOf(document, "Participant_B").Bounds;

        var result = Move(document, "A_Task", new Bounds(220, 360, 100, 80));

        Assert.True(result.IsApplied, string.Join(", ", result.Rejections.Select(r => r.Code)));

        var task = result.Document.FindFlowNode("A_Task")!;
        Assert.Equal("Process_B", task.ContainerId);
        Assert.Equal("Participant_B", task.PoolId);

        // Neither Pool grew: the task is inside the one it was dropped in.
        Assert.Equal(sourceBefore, ShapeOf(result.Document, "Participant_A").Bounds);
        Assert.Equal(targetBefore, ShapeOf(result.Document, "Participant_B").Bounds);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "EDIT-056");
    }

    [Fact]
    public void ATaskDroppedInALaneOfAnotherPoolJoinsThatLane()
    {
        var document = TwoPoolsWithLanes();

        var result = Move(document, "A_Task", new Bounds(220, 450, 100, 60));

        Assert.True(result.IsApplied, string.Join(", ", result.Rejections.Select(r => r.Code)));

        var task = result.Document.FindFlowNode("A_Task")!;
        Assert.Equal("Process_B", task.ContainerId);
        Assert.Equal("B_Lane_2", task.LaneId);

        Assert.Contains("A_Task", Lane(result.Document, "B_Lane_2").FlowNodeRefs);
        Assert.DoesNotContain("A_Task", Lane(result.Document, "A_Lane_1").FlowNodeRefs);
    }

    [Fact]
    public void ATaskDroppedPastItsOwnPoolButNotInAnotherStillGrowsItsPool()
    {
        var document = TwoPools();

        var result = Move(document, "A_Task", new Bounds(650, 60, 100, 80));

        Assert.True(result.IsApplied);
        Assert.Equal("Process_A", result.Document.FindFlowNode("A_Task")!.ContainerId);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "EDIT-053");
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "EDIT-056");
    }

    [Fact]
    public void ASequenceFlowLeftCrossingThePoolsBecomesAMessageFlow()
    {
        var document = TwoPools();

        var result = Move(document, "A_Next", new Bounds(400, 360, 100, 80));

        Assert.True(result.IsApplied, string.Join(", ", result.Rejections.Select(r => r.Code)));
        Assert.DoesNotContain(result.Document.SequenceFlows, flow => flow.Id == "Flow_1");

        var message = Assert.Single(result.Document.MessageFlows, flow => flow.Id == "Flow_1");
        Assert.Equal(("A_Task", "A_Next"), (message.SourceRef, message.TargetRef));

        // Same id, so the edge and its waypoints carry over.
        Assert.Contains(Edges(result.Document), edge => edge.BpmnElement == "Flow_1");
    }

    [Fact]
    public void ASequenceFlowFromAGatewayIsDeletedRatherThanCrossing()
    {
        var document = TwoPools();

        var result = Move(document, "A_Next", new Bounds(400, 360, 100, 80), from: "A_Gateway");
        Assert.True(result.IsApplied, string.Join(", ", result.Rejections.Select(r => r.Code)));

        Assert.DoesNotContain(result.Document.SequenceFlows, flow => flow.Id == "Flow_G");
        Assert.DoesNotContain(result.Document.MessageFlows, flow => flow.Id == "Flow_G");
        Assert.DoesNotContain(Edges(result.Document), edge => edge.BpmnElement == "Flow_G");
        Assert.Null(result.Document.FindFlowNode("A_Gateway")!.DefaultFlowRef);
    }

    [Fact]
    public void AMessageFlowWhoseEndsNowShareAPoolBecomesASequenceFlow()
    {
        var document = TwoPools();

        var result = Move(document, "A_Task", new Bounds(220, 360, 100, 80));

        Assert.True(result.IsApplied, string.Join(", ", result.Rejections.Select(r => r.Code)));
        Assert.DoesNotContain(result.Document.MessageFlows, flow => flow.Id == "Message_1");

        var sequence = Assert.Single(result.Document.SequenceFlows, flow => flow.Id == "Message_1");
        Assert.Equal("Process_B", sequence.ContainerId);
    }

    [Fact]
    public void ASequenceFlowBetweenTwoElementsMovedTogetherFollowsThem()
    {
        var document = TwoPools();

        var result = BpmnDocumentEditor.Apply(
            document,
            new MoveShapesCommand(
            [
                new ShapeGeometry("A_Task_di", new Bounds(220, 360, 100, 80)),
                new ShapeGeometry("A_Next_di", new Bounds(400, 360, 100, 80)),
            ]));

        Assert.True(result.IsApplied, string.Join(", ", result.Rejections.Select(r => r.Code)));

        var flow = Assert.Single(result.Document.SequenceFlows, flow => flow.Id == "Flow_1");
        Assert.Equal("Process_B", flow.ContainerId);
        Assert.All(["A_Task", "A_Next"], id => Assert.Equal("Process_B", result.Document.FindFlowNode(id)!.ContainerId));
    }

    [Fact]
    public void AnAssociationWhoseEndsMoveTogetherIsReownedWithThem()
    {
        var document = TwoPools();

        var result = BpmnDocumentEditor.Apply(
            document,
            new MoveShapesCommand(
            [
                new ShapeGeometry("A_Note_di", new Bounds(450, 450, 100, 40)),
                new ShapeGeometry("A_Next_di", new Bounds(300, 360, 100, 80)),
            ]));

        Assert.True(result.IsApplied, string.Join(", ", result.Rejections.Select(r => r.Code)));
        Assert.Equal("Process_B", Assert.Single(result.Document.Associations, a => a.Id == "Assoc_1").ContainerId);
    }

    [Fact]
    public void ADropOnABlackBoxPoolIsRefused()
    {
        var document = new BpmnDocumentBuilder("Process_A")
            .Pool("Participant_A", "A", new Bounds(0, 0, 600, 250))
            .BlackBoxPool("Participant_B", "B", new Bounds(0, 300, 600, 100))
            .Node("A_Task", BpmnFlowNodeType.UserTask, new Bounds(220, 60, 100, 80), "A work")
            .Build();

        var result = Move(document, "A_Task", new Bounds(220, 310, 100, 80));

        Assert.False(result.IsApplied);
        Assert.Contains(result.Rejections, diagnostic => diagnostic.Code == "EDIT-057");
        Assert.Same(document, result.Document);
    }

    [Fact]
    public void AGroupDroppedInAnotherPoolMovesThere()
    {
        var document = TwoPools();

        var result = Move(document, "A_Group", new Bounds(30, 330, 150, 100));

        Assert.True(result.IsApplied, string.Join(", ", result.Rejections.Select(r => r.Code)));
        Assert.Equal("Process_B", result.Document.Groups.Single(group => group.Id == "A_Group").ContainerId);
    }

    [Fact]
    public void ATextAnnotationAndItsCrossingAssociationAreReconciled()
    {
        var document = TwoPools();

        var result = Move(document, "A_Note", new Bounds(450, 450, 100, 40));

        Assert.True(result.IsApplied, string.Join(", ", result.Rejections.Select(r => r.Code)));
        Assert.Equal("Process_B", result.Document.TextAnnotations.Single(note => note.Id == "A_Note").ContainerId);
        Assert.DoesNotContain(result.Document.Associations, association => association.Id == "Assoc_1");
        Assert.DoesNotContain(Edges(result.Document), edge => edge.BpmnElement == "Assoc_1");
    }

    [Fact]
    public void ADataObjectReferenceTakesAnUnsharedDefinitionWithIt()
    {
        var document = WithData(TwoPools(), shared: false);

        var result = Move(document, "A_Data", new Bounds(500, 450, 36, 50));

        Assert.True(result.IsApplied, string.Join(", ", result.Rejections.Select(r => r.Code)));
        Assert.Equal("Process_B", result.Document.DataObjectReferences.Single(item => item.Id == "A_Data").ContainerId);
        Assert.Equal("Process_B", result.Document.DataObjects.Single(item => item.Id == "DataObject_1").ContainerId);

        // The Data Association into A_Task would leave its Process.
        Assert.DoesNotContain(result.Document.DataAssociations, association => association.Id == "DataIn_1");
    }

    [Fact]
    public void ADataObjectReferenceCopiesASharedDefinition()
    {
        var document = WithData(TwoPools(), shared: true);

        var result = Move(document, "A_Data", new Bounds(500, 450, 36, 50));

        Assert.True(result.IsApplied, string.Join(", ", result.Rejections.Select(r => r.Code)));

        var moved = result.Document.DataObjectReferences.Single(item => item.Id == "A_Data");
        Assert.NotEqual("DataObject_1", moved.DataObjectRef);
        Assert.Equal("Process_B", result.Document.DataObjects.Single(item => item.Id == moved.DataObjectRef).ContainerId);
        Assert.Equal("Process_A", result.Document.DataObjects.Single(item => item.Id == "DataObject_1").ContainerId);
    }

    [Fact]
    public void ATaskDroppedInsideAnExpandedSubProcessOfAnotherPoolIsOwnedByIt()
    {
        var document = new BpmnDocumentBuilder("Process_A")
            .Process("Process_B")
            .Pool("Participant_A", "A", new Bounds(0, 0, 600, 250))
            .Pool("Participant_B", "B", new Bounds(0, 300, 600, 250), processRef: "Process_B")
            .Node("A_Task", BpmnFlowNodeType.UserTask, new Bounds(220, 60, 100, 80), "A work")
            .Node("B_Sub", BpmnFlowNodeType.SubProcess, new Bounds(200, 330, 350, 200), "Sub", isExpanded: true, containerId: "Process_B")
            .Build();

        var result = Move(document, "A_Task", new Bounds(300, 380, 100, 80));

        Assert.True(result.IsApplied, string.Join(", ", result.Rejections.Select(r => r.Code)));
        Assert.Equal("B_Sub", result.Document.FindFlowNode("A_Task")!.ContainerId);
    }

    // --- helpers --------------------------------------------------------------

    private static BpmnEditResult Move(BpmnDocument document, string elementId, Bounds bounds, string? from = null)
    {
        if (from is not null)
        {
            // Re-point Flow_1 at a Gateway source for the case that cannot become a Message Flow.
            document = document with
            {
                SequenceFlows = [.. document.SequenceFlows.Select(flow => flow.Id == "Flow_1" ? flow with { Id = "Flow_G", SourceRef = from } : flow)],
                FlowNodes = [.. document.FlowNodes.Select(node => node.Id == from ? node with { DefaultFlowRef = "Flow_G" } : node)],
                Diagrams = [.. document.Diagrams.Select(diagram => diagram with
                {
                    Plane = diagram.Plane with
                    {
                        Edges = [.. diagram.Plane.Edges.Select(edge => edge.BpmnElement == "Flow_1" ? edge with { BpmnElement = "Flow_G" } : edge)],
                    },
                })],
            };
        }

        return BpmnDocumentEditor.Apply(
            document,
            new MoveShapesCommand([new ShapeGeometry($"{elementId}_di", bounds)]));
    }

    private static BpmnShape ShapeOf(BpmnDocument document, string elementId) =>
        document.Diagrams.SelectMany(diagram => diagram.Plane.Shapes).Single(shape => shape.BpmnElement == elementId);

    private static IEnumerable<BpmnEdge> Edges(BpmnDocument document) =>
        document.Diagrams.SelectMany(diagram => diagram.Plane.Edges);

    private static BpmnLane Lane(BpmnDocument document, string id) => document.Lanes.Single(lane => lane.Id == id);

    private static BpmnDocument TwoPools() => new BpmnDocumentBuilder("Process_A")
        .Process("Process_B")
        .Pool("Participant_A", "A", new Bounds(0, 0, 600, 250))
        .Pool("Participant_B", "B", new Bounds(0, 300, 600, 250), processRef: "Process_B")
        .Node("A_Task", BpmnFlowNodeType.UserTask, new Bounds(220, 60, 100, 80), "A work")
        .Node("A_Next", BpmnFlowNodeType.UserTask, new Bounds(400, 60, 100, 80), "Next")
        .Node("A_Gateway", BpmnFlowNodeType.ExclusiveGateway, new Bounds(100, 75, 50, 50))
        .Node("B_Task", BpmnFlowNodeType.UserTask, new Bounds(60, 450, 100, 80), "B work", containerId: "Process_B")
        .Flow("Flow_1", "A_Task", "A_Next", [new(320, 100), new(400, 100)])
        .MessageFlow("Message_1", "A_Task", "B_Task", [new(270, 140), new(270, 300), new(110, 300), new(110, 450)])
        .TextAnnotation("A_Note", "Why", new Bounds(450, 170, 100, 40))
        .Association("Assoc_1", "A_Note", "A_Next", [new(500, 170), new(450, 140)])
        .Group("A_Group", "Phase", new Bounds(30, 20, 150, 100))
        .Build();

    private static BpmnDocument TwoPoolsWithLanes() => new BpmnDocumentBuilder("Process_A")
        .Process("Process_B")
        .Pool("Participant_A", "A", new Bounds(0, 0, 600, 250))
        .Lane("A_Lane_1", "A one", new Bounds(30, 0, 570, 250), ["A_Task"], laneSetId: "LaneSet_A")
        .Pool("Participant_B", "B", new Bounds(0, 300, 600, 250), processRef: "Process_B")
        .Lane("B_Lane_1", "B one", new Bounds(30, 300, 570, 125), [], laneSetId: "LaneSet_B", containerId: "Process_B")
        .Lane("B_Lane_2", "B two", new Bounds(30, 425, 570, 125), [], laneSetId: "LaneSet_B", containerId: "Process_B")
        .Node("A_Task", BpmnFlowNodeType.UserTask, new Bounds(220, 60, 100, 60), "A work")
        .Build();

    private static BpmnDocument WithData(BpmnDocument document, bool shared)
    {
        BpmnDataObjectReference[] references = shared
            ?
            [
                new("A_Data", "Process_A", "Order", "DataObject_1"),
                new("A_Data_2", "Process_A", "Order", "DataObject_1"),
            ]
            : [new("A_Data", "Process_A", "Order", "DataObject_1")];

        BpmnShape[] shapes = shared
            ? [new("A_Data_di", "A_Data", new Bounds(500, 170, 36, 50)), new("A_Data_2_di", "A_Data_2", new Bounds(30, 170, 36, 50))]
            : [new("A_Data_di", "A_Data", new Bounds(500, 170, 36, 50))];

        return document with
        {
            DataObjects = [new BpmnDataObject("DataObject_1", "Process_A", "Order")],
            DataObjectReferences = references,
            DataAssociations =
            [
                new BpmnDataAssociation("DataIn_1", BpmnDataAssociationKind.Input, ["A_Data"], "A_Task", "Process_A") { OwnerId = "A_Task" },
            ],
            Diagrams = [.. document.Diagrams.Select(diagram => diagram with
            {
                Plane = diagram.Plane with { Shapes = [.. diagram.Plane.Shapes, .. shapes] },
            })],
        };
    }
}
