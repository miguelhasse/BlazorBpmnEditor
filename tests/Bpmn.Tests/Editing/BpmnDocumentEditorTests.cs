using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Editing;

/// <summary>
/// Applying edits to a document.
/// </summary>
/// <remarks>
/// Two properties matter more than any individual rule: a refused command changes nothing at all,
/// and an applied one leaves no dangling reference behind. Both are asserted on every path here,
/// because a half-applied edit is the failure mode that is hardest to notice and worst to live
/// with.
/// </remarks>
public sealed class BpmnDocumentEditorTests
{
    // --- move -----------------------------------------------------------------

    [Fact]
    public void MovesAShape()
    {
        var document = Diagram();

        var result = BpmnDocumentEditor.Apply(
            document,
            new MoveShapesCommand([new ShapeGeometry("Task_1_di", new Bounds(500, 400, 100, 80))]));

        Assert.True(result.IsApplied);
        Assert.Equal(new Bounds(500, 400, 100, 80), ShapeOf(result.Document, "Task_1_di").Bounds);
    }

    [Fact]
    public void MovesEveryShapeInOneStep()
    {
        var document = Diagram();

        var result = BpmnDocumentEditor.Apply(
            document,
            new MoveShapesCommand(
            [
                new ShapeGeometry("Event_1_di", new Bounds(700, 700, 36, 36)),
                new ShapeGeometry("Task_1_di", new Bounds(800, 800, 100, 80)),
            ]));

        Assert.True(result.IsApplied);
        Assert.Equal(700d, ShapeOf(result.Document, "Event_1_di").Bounds.X);
        Assert.Equal(800d, ShapeOf(result.Document, "Task_1_di").Bounds.X);
    }

    [Fact]
    public void LeavesTheOriginalDocumentUntouched()
    {
        // The whole atomicity argument rests on this: the caller's reference is still the old
        // document, so nothing has to be rolled back.
        var document = Diagram();
        var before = ShapeOf(document, "Task_1_di").Bounds;

        BpmnDocumentEditor.Apply(
            document,
            new MoveShapesCommand([new ShapeGeometry("Task_1_di", new Bounds(999, 999, 100, 80))]));

        Assert.Equal(before, ShapeOf(document, "Task_1_di").Bounds);
    }

    [Fact]
    public void RejectsAMoveOfAShapeThatDoesNotExist()
    {
        var document = Diagram();

        var result = BpmnDocumentEditor.Apply(
            document,
            new MoveShapesCommand([new ShapeGeometry("Nope_di", new Bounds(0, 0, 10, 10))]));

        Assert.False(result.IsApplied);
        Assert.Same(document, result.Document);
        Assert.Equal("EDIT-011", Assert.Single(result.Rejections).Code);
    }

    [Theory]
    [InlineData(double.NaN, 0, 100, 80, "DI-003")]
    [InlineData(0, double.PositiveInfinity, 100, 80, "DI-003")]
    [InlineData(0, 0, 0, 80, "DI-002")]
    [InlineData(0, 0, 100, -5, "DI-002")]
    public void RejectsUnusableBounds(double x, double y, double width, double height, string code)
    {
        var document = Diagram();

        var result = BpmnDocumentEditor.Apply(
            document,
            new MoveShapesCommand([new ShapeGeometry("Task_1_di", new Bounds(x, y, width, height))]));

        Assert.False(result.IsApplied);
        Assert.Equal(code, Assert.Single(result.Rejections).Code);
    }

    [Fact]
    public void RejectsAMoveThatWouldOverlapAPeer()
    {
        // DI-011. Task_1 sits at 220,100 (100x80); moving the Event on top of it must be refused.
        var document = Diagram();

        var result = BpmnDocumentEditor.Apply(
            document,
            new MoveShapesCommand([new ShapeGeometry("Event_1_di", new Bounds(250, 120, 36, 36))]));

        Assert.False(result.IsApplied);
        Assert.Same(document, result.Document);

        var rejection = Assert.Single(result.Rejections);
        Assert.Equal("DI-011", rejection.Code);
        Assert.Equal("Event_1", rejection.ElementId);
    }

    [Fact]
    public void AllowsEdgeToEdgeTouchingAtZeroClearance()
    {
        // Legal BPMN, and a default that forbade it would refuse layouts other tools produce.
        var document = Diagram();

        var result = BpmnDocumentEditor.Apply(
            document,
            new MoveShapesCommand([new ShapeGeometry("Event_1_di", new Bounds(184, 100, 36, 36))]));

        Assert.True(result.IsApplied);
    }

    [Fact]
    public void AppliesTheConfiguredClearance()
    {
        var document = Diagram();
        var touching = new MoveShapesCommand([new ShapeGeometry("Event_1_di", new Bounds(184, 100, 36, 36))]);

        var options = EditingOptions.Default with { CollisionClearance = 20d };

        Assert.False(BpmnDocumentEditor.Apply(document, touching, options).IsApplied);
    }

    [Fact]
    public void DoesNotEnforceCollisionsWhenTurnedOff()
    {
        var document = Diagram();
        var overlapping = new MoveShapesCommand([new ShapeGeometry("Event_1_di", new Bounds(250, 120, 36, 36))]);

        var options = EditingOptions.Default with { PreventCollisions = false };

        Assert.True(BpmnDocumentEditor.Apply(document, overlapping, options).IsApplied);
    }

    [Fact]
    public void IgnoresCollisionsAcrossContainers()
    {
        // Two nodes in different Pools are not laid out against each other, so overlapping bounds
        // are meaningless rather than invalid.
        var document = TwoPools();

        var result = BpmnDocumentEditor.Apply(
            document,
            new MoveShapesCommand([new ShapeGeometry("A_Task_di", new Bounds(100, 100, 100, 80))]));

        Assert.True(result.IsApplied);
    }

    [Fact]
    public void IgnoresBoundaryEventOverlapWithItsHost()
    {
        // DI-010 requires a Boundary Event to sit on its host's outline, which is an overlap.
        var document = WithBoundaryEvent();

        var result = BpmnDocumentEditor.Apply(
            document,
            new MoveShapesCommand([new ShapeGeometry("Boundary_1_di", new Bounds(300, 162, 36, 36))]));

        Assert.True(result.IsApplied);
    }

    // --- delete ---------------------------------------------------------------

    [Fact]
    public void DeletesANodeWithTheFlowsThatTouchedIt()
    {
        var document = Diagram();

        var result = BpmnDocumentEditor.Apply(document, new DeleteElementsCommand(["Task_1"]));

        Assert.True(result.IsApplied);
        Assert.DoesNotContain("Task_1", result.Document.FlowNodes.Select(n => n.Id));
        Assert.Empty(result.Document.SequenceFlows);
    }

    [Fact]
    public void LeavesNoDanglingDiBehind()
    {
        // Section 10.1: a shape or edge whose bpmnElement no longer resolves must not survive.
        // This is precisely the defect that made the previous interchange package unusable.
        var result = BpmnDocumentEditor.Apply(Diagram(), new DeleteElementsCommand(["Task_1"]));
        var document = result.Document;

        foreach (var shape in document.Diagrams.SelectMany(d => d.Plane.Shapes))
        {
            Assert.NotNull(document.Find(shape.BpmnElement));
        }

        foreach (var edge in document.Diagrams.SelectMany(d => d.Plane.Edges))
        {
            Assert.NotNull(document.Find(edge.BpmnElement));
        }
    }

    [Fact]
    public void LeavesNoDanglingReferenceBehind()
    {
        var result = BpmnDocumentEditor.Apply(Diagram(), new DeleteElementsCommand(["Task_1"]));
        var document = result.Document;

        foreach (var flow in document.SequenceFlows)
        {
            Assert.NotNull(document.Find(flow.SourceRef));
            Assert.NotNull(document.Find(flow.TargetRef));
        }

        foreach (var node in document.FlowNodes.Where(n => n.AttachedToRef is not null))
        {
            Assert.NotNull(document.Find(node.AttachedToRef!));
        }
    }

    [Fact]
    public void DeletesABoundaryEventWithItsHost()
    {
        var document = WithBoundaryEvent();

        var result = BpmnDocumentEditor.Apply(document, new DeleteElementsCommand(["Task_1"]));

        Assert.True(result.IsApplied);
        Assert.DoesNotContain("Boundary_1", result.Document.FlowNodes.Select(n => n.Id));
    }

    [Fact]
    public void DeletesAnEmbeddedSubProcessWithItsNestedGraph()
    {
        // Section 2.7: nothing else could reference it, so the nested graph goes permanently.
        var document = WithSubProcess();

        var result = BpmnDocumentEditor.Apply(document, new DeleteElementsCommand(["Sub_1"]));

        Assert.True(result.IsApplied);
        Assert.DoesNotContain(result.Document.FlowNodes, n => n.Id is "Sub_1" or "Inner_Start" or "Inner_End");
        Assert.Empty(result.Document.SequenceFlows);
    }

    [Fact]
    public void KeepsAPoolAndItsNodesWhenALaneIsDeleted()
    {
        // Section 2.8, and the easy mistake: a Lane looks like a container but deleting it must
        // not re-parent or remove anything it classified.
        var document = WithLane();

        var result = BpmnDocumentEditor.Apply(document, new DeleteElementsCommand(["Lane_1"]));

        Assert.True(result.IsApplied);
        Assert.Empty(result.Document.Lanes);
        Assert.Contains("Task_1", result.Document.FlowNodes.Select(n => n.Id));
        Assert.NotNull(result.Document.Collaboration);
        Assert.Single(result.Document.Processes);

        // Its former membership goes with it, on both sides of the projection.
        Assert.All(result.Document.FlowNodes, node => Assert.Null(node.LaneId));
        Assert.Empty(result.Document.LaneSets.SelectMany(set => set.LaneIds));
    }

    [Fact]
    public void ReportsWhatItCascadedTo()
    {
        var result = BpmnDocumentEditor.Apply(Diagram(), new DeleteElementsCommand(["Task_1"]));

        var info = Assert.Single(result.Diagnostics, d => d.Code == "EDIT-023");
        Assert.Contains("could not outlive", info.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SaysNothingAboutCascadeWhenThereWasNone()
    {
        var document = Diagram();

        var result = BpmnDocumentEditor.Apply(document, new DeleteElementsCommand(["Flow_1"]));

        Assert.True(result.IsApplied);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "EDIT-023");
    }

    [Fact]
    public void RejectsDeletingAnElementThatDoesNotExist()
    {
        var document = Diagram();

        var result = BpmnDocumentEditor.Apply(document, new DeleteElementsCommand(["Ghost"]));

        Assert.False(result.IsApplied);
        Assert.Same(document, result.Document);
        Assert.Equal("EDIT-021", Assert.Single(result.Rejections).Code);
    }

    [Fact]
    public void RefusesToDeleteAPoolRatherThanGuess()
    {
        // The specification does not say whether a Pool takes its Process and that Process's nodes
        // with it. Refusing is better than guessing at something irreversible.
        var document = WithLane();

        var result = BpmnDocumentEditor.Apply(document, new DeleteElementsCommand(["Participant_1"]));

        Assert.False(result.IsApplied);
        Assert.Equal("EDIT-022", Assert.Single(result.Rejections).Code);
    }

    [Fact]
    public void RejectsAnEmptyCommand()
    {
        Assert.Equal("EDIT-020", Assert.Single(BpmnDocumentEditor.Apply(Diagram(), new DeleteElementsCommand([])).Rejections).Code);
        Assert.Equal("EDIT-010", Assert.Single(BpmnDocumentEditor.Apply(Diagram(), new MoveShapesCommand([])).Rejections).Code);
    }

    // --- compound -------------------------------------------------------------

    [Fact]
    public void AppliesACompoundAsOneStep()
    {
        var document = Diagram();

        var result = BpmnDocumentEditor.Apply(
            document,
            new CompoundCommand(
            [
                new MoveShapesCommand([new ShapeGeometry("Task_1_di", new Bounds(600, 600, 100, 80))]),
                new DeleteElementsCommand(["Event_1"]),
            ],
            "Move and delete"));

        Assert.True(result.IsApplied);
        Assert.Equal(600d, ShapeOf(result.Document, "Task_1_di").Bounds.X);
        Assert.DoesNotContain("Event_1", result.Document.FlowNodes.Select(n => n.Id));
    }

    [Fact]
    public void AbandonsAWholeCompoundOnTheFirstRefusal()
    {
        var document = Diagram();

        var result = BpmnDocumentEditor.Apply(
            document,
            new CompoundCommand(
            [
                new MoveShapesCommand([new ShapeGeometry("Task_1_di", new Bounds(600, 600, 100, 80))]),
                new DeleteElementsCommand(["Ghost"]),
            ],
            "Move then fail"));

        Assert.False(result.IsApplied);

        // The successful first step must not survive: the user asked for one operation.
        Assert.Same(document, result.Document);
        Assert.Equal(220d, ShapeOf(result.Document, "Task_1_di").Bounds.X);
    }

    // --- fixtures -------------------------------------------------------------

    private static BpmnShape ShapeOf(BpmnDocument document, string shapeId) =>
        document.Diagrams.SelectMany(d => d.Plane.Shapes).Single(shape => shape.Id == shapeId);

    private static BpmnDocument Diagram() => new BpmnDocumentBuilder("Process_1")
        .Node("Event_1", BpmnFlowNodeType.StartEvent, new Bounds(80, 120, 36, 36), "Start")
        .Node("Task_1", BpmnFlowNodeType.UserTask, new Bounds(220, 100, 100, 80), "Work")
        .Flow("Flow_1", "Event_1", "Task_1", [new DiPoint(116, 138), new DiPoint(220, 138)])
        .Build();

    private static BpmnDocument WithBoundaryEvent() => new BpmnDocumentBuilder("Process_1")
        .Node("Task_1", BpmnFlowNodeType.UserTask, new Bounds(220, 100, 100, 80), "Work")
        .Node(
            "Boundary_1",
            BpmnFlowNodeType.BoundaryEvent,
            new Bounds(280, 162, 36, 36),
            "Timeout",
            BpmnEventDefinitionType.Timer,
            attachedToRef: "Task_1")
        .Build();

    private static BpmnDocument WithSubProcess() => new BpmnDocumentBuilder("Process_1")
        .Node("Sub_1", BpmnFlowNodeType.SubProcess, new Bounds(200, 100, 350, 200), "Handle")
        .Node("Inner_Start", BpmnFlowNodeType.StartEvent, new Bounds(230, 170, 36, 36), "Go", containerId: "Sub_1")
        .Node("Inner_End", BpmnFlowNodeType.EndEvent, new Bounds(460, 170, 36, 36), "Done", containerId: "Sub_1")
        .Flow(
            "Inner_Flow",
            "Inner_Start",
            "Inner_End",
            [new DiPoint(266, 188), new DiPoint(460, 188)],
            containerId: "Sub_1")
        .Build();

    private static BpmnDocument WithLane() => new BpmnDocumentBuilder("Process_1")
        .Pool("Participant_1", "Customer", new Bounds(0, 0, 600, 250))
        .Lane("Lane_1", "Sales", new Bounds(30, 0, 570, 250), ["Task_1"])
        .Node("Task_1", BpmnFlowNodeType.UserTask, new Bounds(220, 100, 100, 80), "Work")
        .Build();

    private static BpmnDocument TwoPools() => new BpmnDocumentBuilder("Process_A")
        .Pool("Participant_A", "A", new Bounds(0, 0, 600, 250))
        .Node("A_Task", BpmnFlowNodeType.UserTask, new Bounds(220, 100, 100, 80), "A work")
        .Node(
            "B_Task",
            BpmnFlowNodeType.UserTask,
            new Bounds(100, 100, 100, 80),
            "B work",
            containerId: "Process_B")
        .Build();
}
