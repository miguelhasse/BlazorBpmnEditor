using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Editing;

/// <summary>
/// Resizing, and the geometry other shapes share with what was resized.
/// </summary>
/// <remarks>
/// Section 1.5.3 makes a Pool's bounds and its Lanes' bounds one fact expressed twice: Lanes
/// subdivide the Pool and span its width minus the header band. So the interesting assertions here
/// are not about the shape that was grabbed but about everything that had to change with it — a
/// Pool resized without re-tiling its Lanes produces a file that no other tool will draw the way
/// this one does.
/// </remarks>
public sealed class ResizeTests
{
    [Fact]
    public void ResizesAPlainShape()
    {
        var result = BpmnDocumentEditor.Apply(
            Linear(),
            new ResizeShapeCommand("Task_1_di", new Bounds(220, 100, 160, 120)));

        Assert.True(result.IsApplied);
        Assert.Equal(new Bounds(220, 100, 160, 120), ShapeOf(result.Document, "Task_1").Bounds);
    }

    [Fact]
    public void RefusesAShapeThatDoesNotExist()
    {
        var result = BpmnDocumentEditor.Apply(
            Linear(),
            new ResizeShapeCommand("Nowhere_di", new Bounds(0, 0, 100, 100)));

        Assert.False(result.IsApplied);
        Assert.Equal("EDIT-011", Assert.Single(result.Rejections).Code);
    }

    [Theory]
    [InlineData(0, 80)]
    [InlineData(100, 0)]
    [InlineData(-10, 80)]
    public void RefusesBoundsWithoutPositiveArea(double width, double height)
    {
        var result = BpmnDocumentEditor.Apply(
            Linear(),
            new ResizeShapeCommand("Task_1_di", new Bounds(220, 100, width, height)));

        Assert.False(result.IsApplied);
        Assert.Equal("DI-002", Assert.Single(result.Rejections).Code);
    }

    [Fact]
    public void RefusesBoundsBelowTheMinimumSize()
    {
        // Positive area but unusable: a shape a few units across cannot be grabbed again.
        var result = BpmnDocumentEditor.Apply(
            Linear(),
            new ResizeShapeCommand("Task_1_di", new Bounds(220, 100, 8, 8)));

        Assert.False(result.IsApplied);
        Assert.Equal("EDIT-050", Assert.Single(result.Rejections).Code);
    }

    [Fact]
    public void RefusesAResizeThatWouldCollideWithAPeer()
    {
        // Section 1.5.3's acceptance list is explicit that resize enforces DI-011 like every other
        // geometry change, with the same atomic rollback.
        var result = BpmnDocumentEditor.Apply(
            Linear(),
            new ResizeShapeCommand("Task_1_di", new Bounds(100, 100, 400, 80)));

        Assert.False(result.IsApplied);
        Assert.Equal("DI-011", Assert.Single(result.Rejections).Code);
    }

    [Fact]
    public void RetilesLanesWhenThePoolGrows()
    {
        var document = TwoLanes();

        var result = BpmnDocumentEditor.Apply(
            document,
            new ResizeShapeCommand("Participant_1_di", new Bounds(0, 0, 600, 400)));

        Assert.True(result.IsApplied);

        var first = ShapeOf(result.Document, "Lane_1").Bounds;
        var second = ShapeOf(result.Document, "Lane_2").Bounds;

        // The two Lanes still tile the Pool exactly, and still span its width past the header.
        Assert.Equal(400, first.Height + second.Height);
        Assert.Equal(570, first.Width);
        Assert.Equal(570, second.Width);
        Assert.Equal(first.Y + first.Height, second.Y);
    }

    [Fact]
    public void KeepsUnevenLaneProportionsWhenThePoolGrows()
    {
        // A Pool whose Lanes were deliberately uneven should stay uneven when it is made taller.
        var document = UnevenLanes();

        var result = BpmnDocumentEditor.Apply(
            document,
            new ResizeShapeCommand("Participant_1_di", new Bounds(0, 0, 600, 400)));

        var first = ShapeOf(result.Document, "Lane_1").Bounds;
        var second = ShapeOf(result.Document, "Lane_2").Bounds;

        // Started at 3:1, so it stays at 3:1.
        Assert.Equal(300, first.Height, 1);
        Assert.Equal(100, second.Height, 1);
    }

    [Fact]
    public void RefusesResizingALaneOnItsOwn()
    {
        // Lanes fill their Pool, so a Lane has no size of its own to change.
        var document = TwoLanes();

        var result = BpmnDocumentEditor.Apply(
            document,
            new ResizeShapeCommand("Lane_1_di", new Bounds(30, 0, 570, 160)));

        Assert.False(result.IsApplied);
        Assert.Equal("EDIT-051", Assert.Single(result.Rejections).Code);
        Assert.Same(document, result.Document);
    }

    [Fact]
    public void MovesTheSplitterBetweenTwoLanes()
    {
        var document = TwoLanes();

        var result = BpmnDocumentEditor.Apply(document, new MoveLaneSplitterCommand("Lane_1", 160));

        Assert.True(result.IsApplied);

        var first = ShapeOf(result.Document, "Lane_1").Bounds;
        var second = ShapeOf(result.Document, "Lane_2").Bounds;

        // The Lanes still tile the Pool: one grew by exactly what the other lost.
        Assert.Equal(160, first.Height);
        Assert.Equal(90, second.Height);
        Assert.Equal(first.Y + first.Height, second.Y);
        Assert.Equal(ShapeOf(document, "Participant_1").Bounds, ShapeOf(result.Document, "Participant_1").Bounds);
    }

    [Fact]
    public void RefusesASplitterMoveThatLeavesALaneTooSmall()
    {
        var document = TwoLanes();

        var result = BpmnDocumentEditor.Apply(document, new MoveLaneSplitterCommand("Lane_1", 240));

        // Absorbing this would leave the second Lane 10 units tall, which is not a Lane.
        Assert.False(result.IsApplied);
        Assert.Equal("EDIT-051", Assert.Single(result.Rejections).Code);
        Assert.Same(document, result.Document);
    }

    [Fact]
    public void RefusesASplitterAfterTheLastLane()
    {
        var result = BpmnDocumentEditor.Apply(TwoLanes(), new MoveLaneSplitterCommand("Lane_2", 200));

        Assert.False(result.IsApplied);
        Assert.Equal("EDIT-051", Assert.Single(result.Rejections).Code);
    }

    [Fact]
    public void RefusesASplitterForSomethingThatIsNotALane()
    {
        var result = BpmnDocumentEditor.Apply(TwoLanes(), new MoveLaneSplitterCommand("Nowhere", 100));

        Assert.False(result.IsApplied);
        Assert.Equal("EDIT-053", Assert.Single(result.Rejections).Code);
    }

    [Fact]
    public void LetsTheRemainingLaneFillThePoolWhenAnotherIsDeleted()
    {
        var result = BpmnDocumentEditor.Apply(TwoLanes(), new DeleteElementsCommand(["Lane_2"]));

        Assert.True(result.IsApplied);
        Assert.Equal(new Bounds(30, 0, 570, 250), ShapeOf(result.Document, "Lane_1").Bounds);
    }

    [Fact]
    public void RefusesMovingALaneWithoutItsPool()
    {
        var document = TwoLanes();

        var result = BpmnDocumentEditor.Apply(
            document,
            new MoveShapesCommand([new ShapeGeometry("Lane_1_di", new Bounds(30, 40, 570, 125))]));

        Assert.False(result.IsApplied);
        Assert.Equal("EDIT-054", Assert.Single(result.Rejections).Code);
        Assert.Same(document, result.Document);
    }

    [Fact]
    public void MovesLanesWithTheirPool()
    {
        var result = BpmnDocumentEditor.Apply(
            TwoLanes(),
            new MoveShapesCommand(
            [
                new ShapeGeometry("Participant_1_di", new Bounds(0, 100, 600, 250)),
                new ShapeGeometry("Lane_1_di", new Bounds(30, 100, 570, 125)),
                new ShapeGeometry("Lane_2_di", new Bounds(30, 225, 570, 125)),
            ]));

        Assert.True(result.IsApplied);
        Assert.Equal(new Bounds(30, 225, 570, 125), ShapeOf(result.Document, "Lane_2").Bounds);
    }

    [Fact]
    public void MovesAVerticalSplitterAlongTheXAxis()
    {
        // Two lanes side by side in a 600x300 pool, under a 30-unit header band.
        IReadOnlyList<Bounds> lanes = [new Bounds(0, 30, 285, 270), new Bounds(285, 30, 285, 270)];

        var moved = BpmnLaneLayout.MoveSplitter(lanes, 0, 200, isHorizontal: false);

        Assert.NotNull(moved);
        Assert.Equal(new Bounds(0, 30, 200, 270), moved[0]);
        Assert.Equal(new Bounds(200, 30, 370, 270), moved[1]);
        Assert.Null(BpmnLaneLayout.MoveSplitter(lanes, 0, 20, isHorizontal: false));
    }

    [Fact]
    public void RefusesShrinkingAContainerOffItsOwnContents()
    {
        // Section 11 has geometry diagnosed rather than silently rewritten, so the alternative — dragging the
        // children inwards to fit — would discard positions the user chose.
        var document = SubProcessWithContent();

        var result = BpmnDocumentEditor.Apply(
            document,
            new ResizeShapeCommand("Sub_1_di", new Bounds(100, 100, 60, 60)));

        Assert.False(result.IsApplied);
        Assert.Equal("EDIT-052", Assert.Single(result.Rejections).Code);
    }

    [Fact]
    public void AllowsGrowingAContainerAroundItsContents()
    {
        var document = SubProcessWithContent();

        var result = BpmnDocumentEditor.Apply(
            document,
            new ResizeShapeCommand("Sub_1_di", new Bounds(100, 100, 500, 300)));

        Assert.True(result.IsApplied);
        Assert.Equal(new Bounds(100, 100, 500, 300), ShapeOf(result.Document, "Sub_1").Bounds);
    }

    [Fact]
    public void DoesNotTreatABoundaryEventAsStrandableContent()
    {
        // DI-010 puts a Boundary Event half outside whatever its host is flush against, so it can
        // never satisfy a containment check and must not be asked to.
        var document = SubProcessWithBoundary();

        var result = BpmnDocumentEditor.Apply(
            document,
            new ResizeShapeCommand("Sub_1_di", new Bounds(100, 100, 360, 210)));

        Assert.True(result.IsApplied);
    }

    // --- auto-expansion -------------------------------------------------------

    [Fact]
    public void GrowsAPoolWhenContentIsDraggedPastItsEdge()
    {
        var document = TwoLanes();

        var result = BpmnDocumentEditor.Apply(
            document,
            new MoveShapesCommand([new ShapeGeometry("Task_1_di", new Bounds(220, 400, 100, 80))]));

        Assert.True(result.IsApplied);

        var pool = ShapeOf(result.Document, "Participant_1").Bounds;

        // Grown to cover the node plus a margin, rather than leaving it outside the Pool that owns it.
        Assert.True(pool.Y + pool.Height >= 500, $"the pool stopped at {pool.Y + pool.Height}");

        /*
         * And it says so. The canvas dragged the element itself and knows nothing about the Pool
         * that grew around it, so a caller that treats the result as already-rendered would leave
         * the Pool at its old size on screen — which is exactly what happened.
         */
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "EDIT-053");
    }

    [Fact]
    public void RetilesLanesWhenAPoolAutoExpands()
    {
        var document = TwoLanes();

        var result = BpmnDocumentEditor.Apply(
            document,
            new MoveShapesCommand([new ShapeGeometry("Task_1_di", new Bounds(220, 400, 100, 80))]));

        var pool = ShapeOf(result.Document, "Participant_1").Bounds;
        var first = ShapeOf(result.Document, "Lane_1").Bounds;
        var second = ShapeOf(result.Document, "Lane_2").Bounds;

        // Otherwise the Pool ends with a strip no Lane covers.
        Assert.Equal(pool.Height, first.Height + second.Height, 1);
    }

    [Fact]
    public void GrowsTheEdgeLaneAndPoolWhenAFlowNodeIsDraggedPastThePool()
    {
        var result = BpmnDocumentEditor.Apply(
            TwoLanes(),
            new MoveShapesCommand([new ShapeGeometry("Task_1_di", new Bounds(220, 400, 100, 80))]));

        Assert.True(result.IsApplied);

        var pool = ShapeOf(result.Document, "Participant_1").Bounds;
        var first = ShapeOf(result.Document, "Lane_1").Bounds;
        var second = ShapeOf(result.Document, "Lane_2").Bounds;
        var task = ShapeOf(result.Document, "Task_1").Bounds;

        // The splitter between the Lanes did not move; the bottom Lane took all of the growth.
        Assert.Equal(new Bounds(30, 0, 570, 125), first);
        Assert.Equal(125, second.Y, 1);
        Assert.Equal(pool.Y + pool.Height, second.Y + second.Height, 1);
        Assert.True(second.Y + second.Height >= task.Y + task.Height);

        // And the node now belongs to the Lane it is drawn in.
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "EDIT-055");
        Assert.Contains("Task_1", result.Document.Lanes.Single(lane => lane.Id == "Lane_2").FlowNodeRefs);
    }

    [Fact]
    public void GrowsThePoolOnlyOnceWhenALaneGrowsUpward()
    {
        var result = BpmnDocumentEditor.Apply(
            TwoLanes(),
            new MoveShapesCommand([new ShapeGeometry("Task_1_di", new Bounds(220, -100, 100, 80))]));

        Assert.True(result.IsApplied);

        var pool = ShapeOf(result.Document, "Participant_1").Bounds;
        var first = ShapeOf(result.Document, "Lane_1").Bounds;
        var second = ShapeOf(result.Document, "Lane_2").Bounds;

        Assert.True(pool.Y <= -100);
        Assert.Equal(250, pool.Y + pool.Height, 1);
        Assert.Equal(pool.Y, first.Y, 1);
        Assert.Equal(125, second.Height, 1);
        Assert.Equal(pool.Y + pool.Height, second.Y + second.Height, 1);
    }

    [Fact]
    public void AMovedNodeJoinsTheLaneItIsDroppedIn()
    {
        var result = BpmnDocumentEditor.Apply(
            TwoLanes(),
            new MoveShapesCommand([new ShapeGeometry("Task_1_di", new Bounds(220, 150, 100, 80))]));

        Assert.True(result.IsApplied);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "EDIT-055");
        Assert.DoesNotContain("Task_1", result.Document.Lanes.Single(lane => lane.Id == "Lane_1").FlowNodeRefs);
        Assert.Contains("Task_1", result.Document.Lanes.Single(lane => lane.Id == "Lane_2").FlowNodeRefs);
        Assert.Equal("Lane_2", result.Document.FindFlowNode("Task_1")!.LaneId);
    }

    [Fact]
    public void AMoveWithinItsOwnLaneKeepsMembershipAndIsNotReported()
    {
        var result = BpmnDocumentEditor.Apply(
            TwoLanes(),
            new MoveShapesCommand([new ShapeGeometry("Task_1_di", new Bounds(300, 20, 100, 80))]));

        Assert.True(result.IsApplied);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "EDIT-055");
        Assert.Contains("Task_1", result.Document.Lanes.Single(lane => lane.Id == "Lane_1").FlowNodeRefs);
    }

    [Fact]
    public void AMiddleLaneNodeDroppedPastThePoolTopJoinsTheTopLaneAndMovesNothingElse()
    {
        var original = ThreeLanes();
        var result = BpmnDocumentEditor.Apply(
            original,
            new MoveShapesCommand([new ShapeGeometry("Task_B_di", new Bounds(300, -150, 100, 60))]));

        Assert.True(result.IsApplied);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "EDIT-055");
        Assert.Contains("Task_B", result.Document.Lanes.Single(lane => lane.Id == "Lane_1").FlowNodeRefs);
        Assert.Contains("Task_A", result.Document.Lanes.Single(lane => lane.Id == "Lane_1").FlowNodeRefs);
        Assert.Contains("Task_C", result.Document.Lanes.Single(lane => lane.Id == "Lane_3").FlowNodeRefs);

        var pool = ShapeOf(result.Document, "Participant_1").Bounds;
        var first = ShapeOf(result.Document, "Lane_1").Bounds;

        Assert.True(pool.Y <= -150);
        Assert.Equal(pool.Y, first.Y, 1);
        Assert.Equal(100, first.Y + first.Height, 1);
        Assert.Equal(new Bounds(30, 100, 570, 100), ShapeOf(result.Document, "Lane_2").Bounds);
        Assert.Equal(new Bounds(30, 200, 570, 100), ShapeOf(result.Document, "Lane_3").Bounds);
        AssertUnmoved(original, result.Document, "Task_A", "Task_C", "Boundary_1", "Store_1");
    }

    [Fact]
    public void AnUntouchedShapeNearThePoolEdgeDoesNotGrowItWhenAnotherShapeIsDragged()
    {
        var original = ThreeLanes();
        original = original with
        {
            Diagrams = [.. original.Diagrams.Select(diagram => diagram with
            {
                Plane = diagram.Plane with
                {
                    Shapes = [.. diagram.Plane.Shapes.Select(shape => shape.BpmnElement == "Task_C"
                        ? shape with { Bounds = new Bounds(300, 236, 100, 60) }
                        : shape)],
                },
            })],
        };

        var result = BpmnDocumentEditor.Apply(
            original,
            new MoveShapesCommand([new ShapeGeometry("Task_B_di", new Bounds(300, -150, 100, 60))]));

        Assert.True(result.IsApplied);
        var pool = ShapeOf(result.Document, "Participant_1").Bounds;
        Assert.Equal(300, pool.Y + pool.Height, 1);
        Assert.Equal(new Bounds(30, 200, 570, 100), ShapeOf(result.Document, "Lane_3").Bounds);
        AssertUnmoved(original, result.Document, "Task_A", "Task_C", "Boundary_1", "Store_1");
    }

    [Fact]
    public void AMiddleLaneNodeDroppedPastThePoolBottomJoinsTheBottomLaneAndMovesNothingElse()
    {
        var original = ThreeLanes();
        var result = BpmnDocumentEditor.Apply(
            original,
            new MoveShapesCommand([new ShapeGeometry("Task_B_di", new Bounds(300, 400, 100, 60))]));

        Assert.True(result.IsApplied);
        Assert.Contains("Task_B", result.Document.Lanes.Single(lane => lane.Id == "Lane_3").FlowNodeRefs);

        var pool = ShapeOf(result.Document, "Participant_1").Bounds;
        var third = ShapeOf(result.Document, "Lane_3").Bounds;

        Assert.Equal(new Bounds(30, 0, 570, 100), ShapeOf(result.Document, "Lane_1").Bounds);
        Assert.Equal(new Bounds(30, 100, 570, 100), ShapeOf(result.Document, "Lane_2").Bounds);
        Assert.Equal(200, third.Y, 1);
        Assert.Equal(pool.Y + pool.Height, third.Y + third.Height, 1);
        Assert.True(pool.Y + pool.Height >= 460);
        AssertUnmoved(original, result.Document, "Task_A", "Task_C", "Boundary_1", "Store_1");
    }

    [Fact]
    public void ADataReferenceDraggedPastThePoolGrowsOnlyTheEdgeLane()
    {
        var original = ThreeLanes();
        var result = BpmnDocumentEditor.Apply(
            original,
            new MoveShapesCommand([new ShapeGeometry("Store_1_di", new Bounds(100, 400, 50, 50))]));

        Assert.True(result.IsApplied);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "EDIT-053");
        Assert.Equal(new Bounds(30, 0, 570, 100), ShapeOf(result.Document, "Lane_1").Bounds);
        Assert.Equal(new Bounds(30, 100, 570, 100), ShapeOf(result.Document, "Lane_2").Bounds);
        Assert.Equal(200, ShapeOf(result.Document, "Lane_3").Bounds.Y, 1);
        AssertUnmoved(original, result.Document, "Task_A", "Task_B", "Task_C", "Boundary_1");
    }

    [Fact]
    public void ANodeNearAnInnerSplitterResizesNoLane()
    {
        var original = ThreeLanes();
        var result = BpmnDocumentEditor.Apply(
            original,
            new MoveShapesCommand([new ShapeGeometry("Task_A_di", new Bounds(200, 35, 100, 60))]));

        Assert.True(result.IsApplied);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code is "EDIT-053" or "EDIT-055");
        string[] containers = ["Lane_1", "Lane_2", "Lane_3", "Participant_1"];
        foreach (var lane in containers)
        {
            Assert.Equal(ShapeOf(original, lane).Bounds, ShapeOf(result.Document, lane).Bounds);
        }
    }

    private static void AssertUnmoved(BpmnDocument before, BpmnDocument after, params string[] elementIds)
    {
        foreach (var id in elementIds)
        {
            Assert.Equal(ShapeOf(before, id), ShapeOf(after, id));
        }

        Assert.Equal(
            before.Diagrams.SelectMany(diagram => diagram.Plane.Edges).Single(edge => edge.BpmnElement == "Flow_AB").Waypoints[0],
            after.Diagrams.SelectMany(diagram => diagram.Plane.Edges).Single(edge => edge.BpmnElement == "Flow_AB").Waypoints[0]);
    }

    [Fact]
    public void AWholePoolDragKeepsItsSizeLanesAndMembership()
    {
        var original = ThreeLanes();
        var moves = original.Diagrams.SelectMany(diagram => diagram.Plane.Shapes)
            .Select(shape => new ShapeGeometry(shape.Id, shape.Bounds with { Y = shape.Bounds.Y - 100 }))
            .ToList();

        var result = BpmnDocumentEditor.Apply(original, new MoveShapesCommand(moves));

        Assert.True(result.IsApplied);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code is "EDIT-053" or "EDIT-055");
        foreach (var move in moves)
        {
            Assert.Equal(move.Bounds, result.Document.Diagrams.SelectMany(diagram => diagram.Plane.Shapes)
                .Single(shape => shape.Id == move.ShapeId).Bounds);
        }
    }

    [Fact]
    public void APoolDraggedWithoutItsLanesDoesNotMoveItsNodesIntoOtherLanes()
    {
        var result = BpmnDocumentEditor.Apply(
            ThreeLanes(),
            new MoveShapesCommand(
            [
                new ShapeGeometry("Participant_1_di", new Bounds(0, 100, 600, 300)),
                new ShapeGeometry("Task_A_di", new Bounds(200, 120, 100, 60)),
            ]));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "EDIT-055");
        Assert.Contains("Task_A", result.Document.Lanes.Single(lane => lane.Id == "Lane_1").FlowNodeRefs);
    }

    [Fact]
    public void GrowsAPoolAroundMovedArtifactsAndDataReferences()
    {
        var document = new BpmnDocumentBuilder("Process_1")
            .Pool("Participant_1", "Customer", new Bounds(0, 0, 600, 250))
            .Lane("Lane_1", "Sales", new Bounds(30, 0, 570, 250), ["Task_1"])
            .Node("Task_1", BpmnFlowNodeType.UserTask, new Bounds(100, 40, 100, 80), "Work")
            .DataObject("Data_1", "Order", new Bounds(240, 40, 36, 50))
            .DataStore("Store_1", "Orders", new Bounds(320, 40, 50, 60))
            .TextAnnotation("Note_1", "Review", new Bounds(400, 40, 100, 40))
            .Group("Group_1", "Related", new Bounds(40, 130, 180, 80))
            .Build();

        var result = BpmnDocumentEditor.Apply(
            document,
            new MoveShapesCommand(
            [
                new ShapeGeometry("Data_1_di", new Bounds(240, 400, 36, 50)),
                new ShapeGeometry("Store_1_di", new Bounds(320, 430, 50, 60)),
                new ShapeGeometry("Note_1_di", new Bounds(400, 460, 100, 40)),
                new ShapeGeometry("Group_1_di", new Bounds(620, 500, 180, 80)),
            ]));

        Assert.True(result.IsApplied);

        var pool = ShapeOf(result.Document, "Participant_1").Bounds;
        Assert.True(pool.X + pool.Width >= 800);
        Assert.True(pool.Y + pool.Height >= 600);
        Assert.Equal(pool.Height, ShapeOf(result.Document, "Lane_1").Bounds.Height, 1);
    }

    [Fact]
    public void NeverShrinksAContainerAroundItsContent()
    {
        // Deliberate white space is a layout decision; tightening on every drag would make it
        // impossible to keep.
        var document = TwoLanes();
        var before = ShapeOf(document, "Participant_1").Bounds;

        var result = BpmnDocumentEditor.Apply(
            document,
            new MoveShapesCommand([new ShapeGeometry("Task_1_di", new Bounds(100, 20, 100, 80))]));

        Assert.True(result.IsApplied);
        Assert.Equal(before, ShapeOf(result.Document, "Participant_1").Bounds);

        // Nothing grew, so nothing is reported and the canvas keeps what it already drew.
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "EDIT-053");
    }

    [Fact]
    public void LeavesUnrelatedContainersAlone()
    {
        var document = TwoPools();
        var before = ShapeOf(document, "Participant_B").Bounds;

        var result = BpmnDocumentEditor.Apply(
            document,
            new MoveShapesCommand([new ShapeGeometry("A_Task_di", new Bounds(650, 60, 100, 80))]));

        // Recomputing every Pool on every drag would grow one the user never touched.
        Assert.Equal(before, ShapeOf(result.Document, "Participant_B").Bounds);
    }

    [Fact]
    public void CanBeTurnedOff()
    {
        var document = TwoLanes();
        var before = ShapeOf(document, "Participant_1").Bounds;

        var result = BpmnDocumentEditor.Apply(
            document,
            new MoveShapesCommand([new ShapeGeometry("Task_1_di", new Bounds(220, 400, 100, 80))]),
            EditingOptions.Default with { AutoExpandContainers = false });

        Assert.True(result.IsApplied);
        Assert.Equal(before, ShapeOf(result.Document, "Participant_1").Bounds);
    }

    [Fact]
    public void GrowsAPoolInARealCollaborationFixture()
    {
        // The synthetic fixtures above are built by the builder; this is the file the browser
        // loads, with two Participants and separate Processes.
        var document = Serialization.InterchangeFixtures.Read("collaboration.bpmn").Document;

        var task = document.FlowNodes.First(node => node.Id.StartsWith("Task_", StringComparison.Ordinal));
        var shape = document.Diagrams.SelectMany(diagram => diagram.Plane.Shapes)
            .First(candidate => candidate.BpmnElement == task.Id);
        var pool = document.Collaboration!.Participants.First(p => p.ProcessRef == task.ContainerId);
        var poolShape = document.Diagrams.SelectMany(diagram => diagram.Plane.Shapes)
            .First(candidate => candidate.BpmnElement == pool.Id);

        var moved = shape.Bounds with { X = poolShape.Bounds.X + poolShape.Bounds.Width + 100 };
        var result = BpmnDocumentEditor.Apply(document, new MoveShapesCommand([new ShapeGeometry(shape.Id, moved)]));

        Assert.True(result.IsApplied, string.Join(", ", result.Rejections.Select(r => r.Code)));

        var grown = ShapeOf(result.Document, pool.Id).Bounds;
        Assert.True(
            grown.X + grown.Width >= moved.X + moved.Width,
            $"the pool stopped at {grown.X + grown.Width}, the task reaches {moved.X + moved.Width}");
    }

    // --- fixtures -------------------------------------------------------------

    private static BpmnShape ShapeOf(BpmnDocument document, string elementId) =>
        document.Diagrams.SelectMany(diagram => diagram.Plane.Shapes)
            .Single(shape => shape.BpmnElement == elementId);

    private static BpmnDocument Linear() => new BpmnDocumentBuilder("Process_1")
        .Node("Event_1", BpmnFlowNodeType.StartEvent, new Bounds(80, 120, 36, 36), "Start")
        .Node("Task_1", BpmnFlowNodeType.UserTask, new Bounds(220, 100, 100, 80), "Work")
        .Build();

    private static BpmnDocument TwoLanes() => new BpmnDocumentBuilder("Process_1")
        .Pool("Participant_1", "Customer", new Bounds(0, 0, 600, 250))
        .Lane("Lane_1", "Sales", new Bounds(30, 0, 570, 125), ["Task_1"])
        .Lane("Lane_2", "Support", new Bounds(30, 125, 570, 125), [])
        .Node("Task_1", BpmnFlowNodeType.UserTask, new Bounds(220, 20, 100, 80), "Work")
        .Build();

    private static BpmnDocument ThreeLanes()
    {
        var document = new BpmnDocumentBuilder("Process_1")
            .Pool("Participant_1", "Customer", new Bounds(0, 0, 600, 300))
            .Lane("Lane_1", "Sales", new Bounds(30, 0, 570, 100), ["Task_A"])
            .Lane("Lane_2", "Support", new Bounds(30, 100, 570, 100), ["Task_B"])
            .Lane("Lane_3", "Billing", new Bounds(30, 200, 570, 100), ["Task_C"])
            .Node("Task_A", BpmnFlowNodeType.UserTask, new Bounds(200, 20, 100, 60), "A")
            .Node("Boundary_1", BpmnFlowNodeType.BoundaryEvent, new Bounds(285, 25, 30, 30), attachedToRef: "Task_A")
            .Node("Task_B", BpmnFlowNodeType.UserTask, new Bounds(300, 120, 100, 60), "B")
            .Node("Task_C", BpmnFlowNodeType.UserTask, new Bounds(300, 220, 100, 60), "C")
            .DataStore("Store_1", "Orders", new Bounds(100, 25, 50, 50))
            .Flow("Flow_AB", "Task_A", "Task_B", [new(250, 80), new(250, 100), new(350, 100), new(350, 120)])
            .Build();

        return document with
        {
            Diagrams = [.. document.Diagrams.Select(diagram => diagram with
            {
                Plane = diagram.Plane with
                {
                    Shapes = [.. diagram.Plane.Shapes.Select(shape => shape.BpmnElement == "Task_A"
                        ? shape with { Label = new BpmnLabel(Bounds: new Bounds(200, 85, 100, 14)) }
                        : shape)],
                },
            })],
        };
    }

    private static BpmnDocument UnevenLanes() => new BpmnDocumentBuilder("Process_1")
        .Pool("Participant_1", "Customer", new Bounds(0, 0, 600, 200))
        .Lane("Lane_1", "Sales", new Bounds(30, 0, 570, 150), [])
        .Lane("Lane_2", "Support", new Bounds(30, 150, 570, 50), [])
        .Build();

    private static BpmnDocument TwoPools() => new BpmnDocumentBuilder("Process_A")
        .Pool("Participant_A", "A", new Bounds(0, 0, 600, 250))
        .Pool("Participant_B", "B", new Bounds(0, 300, 600, 250), processRef: "Process_B")
        .Node("A_Task", BpmnFlowNodeType.UserTask, new Bounds(220, 60, 100, 80), "A work")
        .Build();

    private static BpmnDocument SubProcessWithContent() => new BpmnDocumentBuilder("Process_1")
        .Node("Sub_1", BpmnFlowNodeType.SubProcess, new Bounds(100, 100, 350, 200), "Outer")
        .Node("Inner_Task", BpmnFlowNodeType.UserTask, new Bounds(140, 150, 100, 80), "Inner", containerId: "Sub_1")
        .Build();

    private static BpmnDocument SubProcessWithBoundary() => new BpmnDocumentBuilder("Process_1")
        .Node("Sub_1", BpmnFlowNodeType.SubProcess, new Bounds(100, 100, 350, 200), "Outer")
        .Node("Inner_Task", BpmnFlowNodeType.UserTask, new Bounds(140, 150, 100, 80), "Inner", containerId: "Sub_1")
        .Node(
            "Boundary_1",
            BpmnFlowNodeType.BoundaryEvent,
            new Bounds(222, 212, 36, 36),
            "Timeout",
            BpmnEventDefinitionType.Timer,
            attachedToRef: "Inner_Task",
            containerId: "Sub_1")
        .Build();
}
