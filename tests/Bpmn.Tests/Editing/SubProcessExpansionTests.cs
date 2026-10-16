using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;
using Hasseware.AspNetCore.Components.Bpmn.Validation;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Editing;

/// <summary>
/// Expanding and collapsing a Sub-Process (specification section 1.2.1).
/// </summary>
/// <remarks>
/// <para>
/// Both commands move DI between planes and change nothing semantic. A collapsed Sub-Process keeps
/// its children on a plane of its own, which is where every exporter puts them and what section
/// 2.8.1 requires; an expanded one has them on the plane it is on. So the thing to check is not
/// really the flag — it is that the content survives the trip intact, in both directions, and that
/// nothing about the model moves with it.
/// </para>
/// <para>
/// The fixture mirrors the shipped sample: a Sub-Process on the root plane with its content on a
/// diagram of its own, and a Task beside it so there is something the move must not disturb.
/// </para>
/// </remarks>
public sealed class SubProcessExpansionTests
{
    private const double Tolerance = 1e-9;

    // --- expanding ---------------------------------------------------------------------

    [Fact]
    public void ExpandingDrawsTheContentOnThePlaneTheSubProcessIsOn()
    {
        var result = Apply(Collapsed(), new ExpandSubProcessCommand("Sub_1"));

        Assert.True(result.IsApplied);
        Assert.Contains("Inner_task", Drawn(result.Document, "Diagram_1"));
        Assert.Contains("Inner_start", Drawn(result.Document, "Diagram_1"));
    }

    [Fact]
    public void AndSaysSoOnTheShape()
    {
        var result = Apply(Collapsed(), new ExpandSubProcessCommand("Sub_1"));

        Assert.True(ShapeOf(result.Document, "Sub_1").IsExpanded);
    }

    [Fact]
    public void AndTakesTheContentsOwnPlaneAwaySoNothingIsDrawnTwice()
    {
        // Leaving it would be CT-009: the same elements drawn in two places, with no answer to
        // which one a drill-down should show.
        var result = Apply(Collapsed(), new ExpandSubProcessCommand("Sub_1"));

        Assert.DoesNotContain(result.Document.Diagrams, diagram => diagram.Plane.BpmnElement == "Sub_1");
    }

    [Fact]
    public void AndTheSubProcessStaysWhereItWas()
    {
        /*
         * The alternative — keep the child coordinates and draw the container round them — would
         * move this Sub-Process from (345, 80) to wherever its content plane happens to start.
         * Centring keeps the diagram legible.
         */
        var before = ShapeOf(Collapsed(), "Sub_1").Bounds;
        var after = ShapeOf(Apply(Collapsed(), new ExpandSubProcessCommand("Sub_1")).Document, "Sub_1").Bounds;

        Assert.Equal(before.X, after.X, Tolerance);
        Assert.Equal(before.Y, after.Y, Tolerance);
        Assert.True(after.Width > before.Width, "it did not grow");
    }

    [Fact]
    public void AndTheContentKeepsItsShape()
    {
        // What "never changes child BPMNDI coordinates" protects: the block moves rigidly, so every
        // distance inside it survives the trip between planes.
        var source = Collapsed().Diagrams.Single(diagram => diagram.Plane.Id == "Sub_plane").Plane;
        var expected = Offset(source, "Inner_start", "Inner_task");

        var expanded = Apply(Collapsed(), new ExpandSubProcessCommand("Sub_1")).Document;

        Assert.Equal(expected, Offset(PlaneOf(expanded, "Diagram_1"), "Inner_start", "Inner_task"));
    }

    [Fact]
    public void AndTheContentLandsInsideTheContainer()
    {
        var expanded = Apply(Collapsed(), new ExpandSubProcessCommand("Sub_1")).Document;
        var container = ShapeOf(expanded, "Sub_1").Bounds;

        foreach (var id in new[] { "Inner_start", "Inner_task" })
        {
            var box = ShapeOf(expanded, id).Bounds;

            Assert.True(box.X >= container.X, $"{id} starts left of its container");
            Assert.True(box.Y >= container.Y, $"{id} starts above its container");
            Assert.True(box.X + box.Width <= container.X + container.Width + Tolerance, $"{id} reaches past it");
            Assert.True(box.Y + box.Height <= container.Y + container.Height + Tolerance, $"{id} reaches below it");
        }
    }

    [Fact]
    public void AndTheRouteInsideComesWithIt()
    {
        var expanded = Apply(Collapsed(), new ExpandSubProcessCommand("Sub_1")).Document;

        var route = PlaneOf(expanded, "Diagram_1").Edges.Single(edge => edge.BpmnElement == "Inner_flow");

        Assert.Equal(2, route.Waypoints.Count);
        Assert.NotEqual(new DiPoint(0, 0), route.Waypoints[0]);
    }

    [Fact]
    public void AndTheLabelStylesItsContentReferencesComeTooWithIt()
    {
        // The one that would otherwise fail silently: a child label referencing a style the host
        // diagram does not declare is D022, and the style lives on the diagram being removed.
        var expanded = Apply(WithStyledLabel(), new ExpandSubProcessCommand("Sub_1")).Document;

        var host = expanded.Diagrams.Single(diagram => diagram.Id == "Diagram_1");

        Assert.Contains(host.LabelStyles, style => style.Id == "Style_inner");
        Assert.DoesNotContain(BpmnValidator.Validate(expanded).Diagnostics, d => d.RuleId == "D022");
    }

    [Fact]
    public void AndNothingSemanticMoves()
    {
        // Section 1.2.1: the command "never changes the Sub-Process's semantic flowElements, IDs,
        // ownership, Sequence Flows, Boundary Events".
        var before = Collapsed();
        var after = Apply(before, new ExpandSubProcessCommand("Sub_1")).Document;

        Assert.Equal(
            before.FlowNodes.Select(node => (node.Id, node.Type, node.ContainerId)),
            after.FlowNodes.Select(node => (node.Id, node.Type, node.ContainerId)));

        Assert.Equal(
            before.SequenceFlows.Select(flow => (flow.Id, flow.SourceRef, flow.TargetRef, flow.ContainerId)),
            after.SequenceFlows.Select(flow => (flow.Id, flow.SourceRef, flow.TargetRef, flow.ContainerId)));
    }

    [Fact]
    public void ExpandingSomethingWithNoContentGivesAnEmptyContainerOfTheMinimumSize()
    {
        var result = Apply(Bare(), new ExpandSubProcessCommand("Sub_1"));
        var options = new EditingOptions(true, 5d, 50);

        Assert.True(result.IsApplied);
        Assert.Equal(options.MinimumExpandedWidth, ShapeOf(result.Document, "Sub_1").Bounds.Width, Tolerance);
    }

    [Fact]
    public void AndSaysWhenThatContentExistsButIsNotDrawn()
    {
        // The elements are real and still owned by the Sub-Process; what is missing is any statement
        // of where they go, and sections 10.6 and 11 say the editor keeps what the file says.
        var result = Apply(UndrawnContent(), new ExpandSubProcessCommand("Sub_1"));

        Assert.True(result.IsApplied);
        Assert.Contains(result.Diagnostics, d => d.Code == "EDIT-094");
    }

    [Fact]
    public void ExpandingRestoresASizeTheCallerRemembers()
    {
        var remembered = new Bounds(300, 60, 420, 260);

        var result = Apply(Collapsed(), new ExpandSubProcessCommand("Sub_1", remembered));

        Assert.Equal(remembered, ShapeOf(result.Document, "Sub_1").Bounds);
    }

    // --- collapsing --------------------------------------------------------------------

    [Fact]
    public void CollapsingPutsTheContentOnAPlaneOfItsOwn()
    {
        var expanded = Apply(Collapsed(), new ExpandSubProcessCommand("Sub_1")).Document;

        var result = Apply(expanded, new CollapseSubProcessCommand("Sub_1"));

        Assert.True(result.IsApplied);
        Assert.DoesNotContain("Inner_task", Drawn(result.Document, "Diagram_1"));

        var own = result.Document.Diagrams.Single(diagram => diagram.Plane.BpmnElement == "Sub_1");
        Assert.Contains(own.Plane.Shapes, shape => shape.BpmnElement == "Inner_task");
    }

    [Fact]
    public void AndCreatesThatPlaneWhenTheSubProcessHasNone()
    {
        // The shipped sample's expanded Sub-Process is exactly this: drawn in place, with no
        // diagram of its own to go back to.
        var result = Apply(ExpandedInPlace(), new CollapseSubProcessCommand("Sub_1"));

        Assert.True(result.IsApplied);
        Assert.Single(result.Document.Diagrams, diagram => diagram.Plane.BpmnElement == "Sub_1");
    }

    [Fact]
    public void AndShrinksToATaskFootprint()
    {
        var expanded = Apply(Collapsed(), new ExpandSubProcessCommand("Sub_1")).Document;

        var collapsed = ShapeOf(Apply(expanded, new CollapseSubProcessCommand("Sub_1")).Document, "Sub_1").Bounds;

        Assert.Equal(100d, collapsed.Width, Tolerance);
        Assert.Equal(80d, collapsed.Height, Tolerance);
    }

    [Fact]
    public void AndLeavesBehindWhatWasNeverInside()
    {
        /*
         * Decided by containment, not geometry. A Boundary Event attached to the Sub-Process
         * belongs to the parent process and stays; a Sequence Flow that merely ends at it is the
         * parent's too. Both sit within the expanded rectangle, so a geometric rule would take them.
         */
        var expanded = Apply(WithBoundaryAndFlow(), new ExpandSubProcessCommand("Sub_1")).Document;

        var collapsed = Apply(expanded, new CollapseSubProcessCommand("Sub_1")).Document;

        Assert.Contains("Boundary_1", Drawn(collapsed, "Diagram_1"));
        Assert.Contains("Flow_in", Drawn(collapsed, "Diagram_1"));
    }

    [Fact]
    public void AndTheBoundaryEventEndsUpOnTheSmallerOutline()
    {
        var expanded = Apply(WithBoundaryAndFlow(), new ExpandSubProcessCommand("Sub_1")).Document;
        var collapsed = Apply(expanded, new CollapseSubProcessCommand("Sub_1")).Document;

        var host = ShapeOf(collapsed, "Sub_1").Bounds;
        var boundary = Centre(ShapeOf(collapsed, "Boundary_1").Bounds);

        var off = Math.Min(
            Math.Min(Math.Abs(boundary.X - host.X), Math.Abs(boundary.X - (host.X + host.Width))),
            Math.Min(Math.Abs(boundary.Y - host.Y), Math.Abs(boundary.Y - (host.Y + host.Height))));

        Assert.True(off < 0.001, $"the Boundary Event sat {off:0.000} off its host's outline");
    }

    // --- the cycle ---------------------------------------------------------------------

    [Fact]
    public void ExpandingAndCollapsingAgainPutsTheSubProcessBackExactly()
    {
        var before = Collapsed();

        var expanded = Apply(before, new ExpandSubProcessCommand("Sub_1")).Document;
        var after = Apply(expanded, new CollapseSubProcessCommand("Sub_1")).Document;

        Assert.Equal(ShapeOf(before, "Sub_1").Bounds, ShapeOf(after, "Sub_1").Bounds);
        Assert.False(ShapeOf(after, "Sub_1").IsExpanded);
    }

    [Fact]
    public void AndTheContentKeepsItsGeometryThroughTheCycle()
    {
        /*
         * Relative, not absolute. Collapse leaves the content at the coordinates it had while it
         * was drawn in place, because a plane's origin is arbitrary and re-anchoring it would be
         * work with no observable result. What has to survive is the shape of the content, which
         * is what the next expansion lays out again.
         */
        var before = Collapsed();
        var expected = Offset(before.Diagrams.Single(d => d.Plane.BpmnElement == "Sub_1").Plane, "Inner_start", "Inner_task");

        var expanded = Apply(before, new ExpandSubProcessCommand("Sub_1")).Document;
        var after = Apply(expanded, new CollapseSubProcessCommand("Sub_1")).Document;

        Assert.Equal(expected, Offset(after.Diagrams.Single(d => d.Plane.BpmnElement == "Sub_1").Plane, "Inner_start", "Inner_task"));
    }

    [Fact]
    public void AndExpandingASecondTimeDrawsItIdentically()
    {
        // The property "restores the same drawing" actually means: what the user sees after a
        // collapse and a re-expansion is what they saw before the collapse, to the pixel.
        var once = Apply(Collapsed(), new ExpandSubProcessCommand("Sub_1")).Document;

        var collapsed = Apply(once, new CollapseSubProcessCommand("Sub_1")).Document;
        var twice = Apply(collapsed, new ExpandSubProcessCommand("Sub_1")).Document;

        Assert.Equal(
            PlaneOf(once, "Diagram_1").Shapes.Select(s => (s.BpmnElement, s.Bounds)).OrderBy(s => s.BpmnElement, StringComparer.Ordinal),
            PlaneOf(twice, "Diagram_1").Shapes.Select(s => (s.BpmnElement, s.Bounds)).OrderBy(s => s.BpmnElement, StringComparer.Ordinal));
    }

    [Fact]
    public void AndTheDocumentIsValidAtEveryStep()
    {
        var expanded = Apply(Collapsed(), new ExpandSubProcessCommand("Sub_1")).Document;
        var collapsed = Apply(expanded, new CollapseSubProcessCommand("Sub_1")).Document;

        foreach (var (name, document) in new[] { ("expanded", expanded), ("collapsed", collapsed) })
        {
            var errors = BpmnValidator.Validate(document).Diagnostics
                .Where(d => d.Severity == BpmnSeverity.Error)
                .Select(d => $"{d.RuleId}: {d.Message}")
                .ToList();

            Assert.True(errors.Count == 0, $"{name}: {string.Join(" | ", errors)}");
        }
    }

    [Fact]
    public void AndItSurvivesARoundTripThroughXml()
    {
        // isExpanded and the relocated DI both have to persist, or the next session sees something
        // different from what the user left.
        var expanded = Apply(Collapsed(), new ExpandSubProcessCommand("Sub_1")).Document;

        var reread = BpmnXmlReader.Read(BpmnXmlWriter.Write(expanded).Xml).Document;

        Assert.True(ShapeOf(reread, "Sub_1").IsExpanded);
        Assert.Contains("Inner_task", Drawn(reread, reread.Diagrams[0].Id));
    }

    [Fact]
    public void ExpandingIsOneUndoStep()
    {
        var history = new BpmnEditHistory(Collapsed(), new EditingOptions(true, 5d, 50));

        history.Apply(new ExpandSubProcessCommand("Sub_1"));

        Assert.True(history.CanUndo);
        history.Undo();

        // Including the diagram the expansion removed, which a snapshot history restores for free.
        Assert.False(ShapeOf(history.Document, "Sub_1").IsExpanded);
        Assert.Single(history.Document.Diagrams, diagram => diagram.Plane.BpmnElement == "Sub_1");
    }

    // --- making room -------------------------------------------------------------------

    [Fact]
    public void ExpandingMovesTheNeighbourOutOfTheWay()
    {
        // Task_after sits entirely right of the collapsed Sub-Process, which is the side a
        // container grows into.
        var before = ShapeOf(Neighboured(), "Task_after").Bounds;

        var expanded = Apply(Neighboured(), new ExpandSubProcessCommand("Sub_1")).Document;

        Assert.True(ShapeOf(expanded, "Task_after").Bounds.X > before.X, "the neighbour did not move");
    }

    [Fact]
    public void AndByExactlyHowFarTheContainerGrewOnThatSide()
    {
        var document = Neighboured();
        var before = ShapeOf(document, "Sub_1").Bounds;
        var neighbour = ShapeOf(document, "Task_after").Bounds;

        var expanded = Apply(document, new ExpandSubProcessCommand("Sub_1")).Document;
        var grown = ShapeOf(expanded, "Sub_1").Bounds;

        var grewRight = grown.X + grown.Width - (before.X + before.Width);

        Assert.Equal(neighbour.X + grewRight, ShapeOf(expanded, "Task_after").Bounds.X, Tolerance);
    }

    [Fact]
    public void AndSomethingToTheLeftIsNeverDisturbed()
    {
        // The reason growth is one-directional: nothing is ever pushed back out of the leading edge
        // of whatever holds it.
        var before = ShapeOf(Neighboured(), "Task_outer").Bounds;

        var expanded = Apply(Neighboured(), new ExpandSubProcessCommand("Sub_1")).Document;

        Assert.Equal(before, ShapeOf(expanded, "Task_outer").Bounds);
    }

    [Fact]
    public void AndCollapsingPutsItBackExactly()
    {
        /*
         * The property the user asked for, and the reason this is arithmetic rather than a
         * remembered list: collapsing runs the same rule with the boxes the other way round, so
         * every distance is the negative of the one expanding handed out.
         */
        var document = Neighboured();
        var before = ShapeOf(document, "Task_after").Bounds;

        var expanded = Apply(document, new ExpandSubProcessCommand("Sub_1")).Document;
        var after = Apply(expanded, new CollapseSubProcessCommand("Sub_1")).Document;

        Assert.Equal(before, ShapeOf(after, "Task_after").Bounds);
    }

    [Fact]
    public void EvenAfterTheMemoryOfTheExpansionIsGone()
    {
        // Nothing carries state between the two commands, so a collapse works the same in a fresh
        // session as it does immediately after the expansion that caused the displacement.
        var expanded = Apply(Neighboured(), new ExpandSubProcessCommand("Sub_1")).Document;

        var reread = BpmnXmlReader.Read(BpmnXmlWriter.Write(expanded).Xml).Document;
        var after = Apply(reread, new CollapseSubProcessCommand("Sub_1")).Document;

        Assert.Equal(ShapeOf(Neighboured(), "Task_after").Bounds, ShapeOf(after, "Task_after").Bounds);
    }

    [Fact]
    public void SomethingStraddlingTheEdgeIsLeftAlone()
    {
        // It already overlapped the collapsed container, so there is no side to move it to that is
        // not equally wrong. Leaving it is at least predictable.
        var document = WithAStraddler();
        var before = ShapeOf(document, "Straddler").Bounds;

        var expanded = Apply(document, new ExpandSubProcessCommand("Sub_1")).Document;

        Assert.Equal(before, ShapeOf(expanded, "Straddler").Bounds);
    }

    [Fact]
    public void ThePoolAroundItGrowsRatherThanMoving()
    {
        // A container that wraps the Sub-Process cannot be pushed aside — that would take its whole
        // contents with it — so it grows to keep holding what it holds.
        var document = Pooled();
        var before = ShapeOf(document, "Participant_1").Bounds;

        var expanded = Apply(document, new ExpandSubProcessCommand("Sub_1")).Document;
        var after = ShapeOf(expanded, "Participant_1").Bounds;

        Assert.Equal(before.X, after.X, Tolerance);
        Assert.True(after.Width > before.Width, "the Pool did not grow");
    }

    [Fact]
    public void AndTheExpandedSubProcessEndsUpInsideIt()
    {
        var expanded = Apply(Pooled(), new ExpandSubProcessCommand("Sub_1")).Document;

        var pool = ShapeOf(expanded, "Participant_1").Bounds;
        var sub = ShapeOf(expanded, "Sub_1").Bounds;

        Assert.True(sub.X >= pool.X, "it starts left of its Pool");
        Assert.True(sub.X + sub.Width <= pool.X + pool.Width + Tolerance, "it reaches past its Pool");
    }

    [Fact]
    public void ARouteWhollyOnOneSideKeepsItsShape()
    {
        // It moves with everything else on that side, rigidly, so the bends its author drew are
        // exactly the bends it still has.
        var document = Neighboured();
        var before = RouteOf(document, "Flow_after");

        var expanded = Apply(document, new ExpandSubProcessCommand("Sub_1")).Document;
        var after = RouteOf(expanded, "Flow_after");

        var shift = (X: after[0].X - before[0].X, Y: after[0].Y - before[0].Y);

        Assert.NotEqual((0d, 0d), shift);
        Assert.Equal([.. before.Select(point => BpmnSubProcessLayout.Shift(point, shift))], after);
    }

    [Fact]
    public void ARouteSpanningTheOpeningIsSquaredRatherThanStretched()
    {
        /*
         * Its two ends move by different amounts, so the bends between them no longer describe
         * anything — they were drawn for a distance that has changed. Shifting each point by its
         * own side leaves a diagonal through the gap, which is the one thing this editor's
         * connectors are never supposed to be.
         */
        var expanded = Apply(Neighboured(), new ExpandSubProcessCommand("Sub_1")).Document;

        var route = RouteOf(expanded, "Flow_across");

        for (var i = 1; i < route.Count; i++)
        {
            var square = Math.Abs(route[i].X - route[i - 1].X) < Tolerance
                || Math.Abs(route[i].Y - route[i - 1].Y) < Tolerance;

            Assert.True(square, $"segment {i} runs from {route[i - 1]} to {route[i]}");
        }
    }

    // --- refusals ----------------------------------------------------------------------

    [Fact]
    public void ExpandingSomethingAlreadyExpandedIsRefused()
    {
        var expanded = Apply(Collapsed(), new ExpandSubProcessCommand("Sub_1")).Document;

        var result = Apply(expanded, new ExpandSubProcessCommand("Sub_1"));

        Assert.False(result.IsApplied);
        Assert.Contains(result.Rejections, d => d.Code == "EDIT-092");
    }

    [Fact]
    public void CollapsingSomethingAlreadyCollapsedIsRefused()
    {
        var result = Apply(Collapsed(), new CollapseSubProcessCommand("Sub_1"));

        Assert.False(result.IsApplied);
        Assert.Contains(result.Rejections, d => d.Code == "EDIT-092");
    }

    [Fact]
    public void ExpandingATaskIsRefused()
    {
        var result = Apply(Collapsed(), new ExpandSubProcessCommand("Task_outer"));

        Assert.False(result.IsApplied);
        Assert.Contains(result.Rejections, d => d.Code == "EDIT-091");
    }

    [Fact]
    public void ExpandingACallActivitySaysWhyItCannotBe()
    {
        // It looks like a container and behaves like one everywhere else, so the message has to be
        // about what a Call Activity is rather than a flat refusal.
        var result = Apply(WithCallActivity(), new ExpandSubProcessCommand("Call_1"));

        Assert.False(result.IsApplied);
        var refusal = Assert.Single(result.Rejections, d => d.Code == "EDIT-091");
        Assert.Contains("Call Activity", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpandingSomethingThatIsNotThereIsRefusedRatherThanThrowing()
    {
        var result = Apply(Collapsed(), new ExpandSubProcessCommand("Nothing_1"));

        Assert.False(result.IsApplied);
        Assert.Contains(result.Rejections, d => d.Code == "EDIT-090");
    }

    [Fact]
    public void ARefusalChangesNothing()
    {
        var before = Collapsed();

        var result = Apply(before, new ExpandSubProcessCommand("Task_outer"));

        Assert.Same(before, result.Document);
    }

    // --- fixtures ----------------------------------------------------------------------

    private static BpmnEditResult Apply(BpmnDocument document, BpmnEditCommand command) =>
        BpmnDocumentEditor.Apply(document, command, new EditingOptions(true, 5d, 50));

    private static BpmnPlane PlaneOf(BpmnDocument document, string diagramId) =>
        document.Diagrams.Single(diagram => diagram.Id == diagramId).Plane;

    private static IReadOnlyList<string> Drawn(BpmnDocument document, string diagramId)
    {
        var plane = PlaneOf(document, diagramId);

        return [.. plane.Shapes.Select(s => s.BpmnElement), .. plane.Edges.Select(e => e.BpmnElement)];
    }

    private static BpmnShape ShapeOf(BpmnDocument document, string elementId) => document.Diagrams
        .SelectMany(diagram => diagram.Plane.Shapes)
        .First(shape => shape.BpmnElement == elementId);

    private static IReadOnlyList<DiPoint> RouteOf(BpmnDocument document, string elementId) => document.Diagrams
        .SelectMany(diagram => diagram.Plane.Edges)
        .First(edge => edge.BpmnElement == elementId)
        .Waypoints;

    private static (double X, double Y) Centre(Bounds bounds) =>
        (bounds.X + (bounds.Width / 2d), bounds.Y + (bounds.Height / 2d));

    private static (double X, double Y) Offset(BpmnPlane plane, string from, string to)
    {
        var a = plane.Shapes.Single(s => s.BpmnElement == from).Bounds;
        var b = plane.Shapes.Single(s => s.BpmnElement == to).Bounds;

        return (b.X - a.X, b.Y - a.Y);
    }

    /// <summary>A collapsed Sub-Process with its content on a diagram of its own, plus a neighbour.</summary>
    private static BpmnDocument Collapsed()
    {
        var document = new BpmnDocumentBuilder("Process_1")
            .Node("Task_outer", BpmnFlowNodeType.UserTask, new Bounds(120, 100, 100, 80), "Outside")
            .Node("Sub_1", BpmnFlowNodeType.SubProcess, new Bounds(345, 80, 100, 80), "Holder", isExpanded: false)
            .Build();

        return document with
        {
            FlowNodes =
            [
                .. document.FlowNodes,
                new BpmnFlowNode("Inner_start", BpmnFlowNodeType.StartEvent, "Sub_1", "Begin"),
                new BpmnFlowNode("Inner_task", BpmnFlowNodeType.UserTask, "Sub_1", "Do it"),
            ],
            SequenceFlows = [new BpmnSequenceFlow("Inner_flow", "Inner_start", "Inner_task", "Sub_1")],
            Diagrams =
            [
                document.Diagrams[0],
                new BpmnDiagram(
                    "Sub_diagram",
                    new BpmnPlane(
                        "Sub_plane",
                        "Sub_1",
                        [
                            new BpmnShape("Inner_start_di", "Inner_start", new Bounds(1000, 2000, 36, 36)),
                            new BpmnShape("Inner_task_di", "Inner_task", new Bounds(1100, 1978, 100, 80)),
                        ],
                        [new BpmnEdge("Inner_flow_di", "Inner_flow", [new DiPoint(1036, 2018), new DiPoint(1100, 2018)])])),
            ],
        };
    }

    /// <summary>The same, with a child label pointing at a style the content plane declares.</summary>
    private static BpmnDocument WithStyledLabel()
    {
        var document = Collapsed();
        var sub = document.Diagrams.Single(diagram => diagram.Id == "Sub_diagram");

        return document with
        {
            Diagrams =
            [
                document.Diagrams[0],
                sub with
                {
                    LabelStyles = [new BpmnLabelStyle("Style_inner", "Arial", 11d)],
                    Plane = sub.Plane with
                    {
                        Shapes =
                        [
                            .. sub.Plane.Shapes.Select(shape => shape.BpmnElement != "Inner_task"
                                ? shape
                                : shape with { Label = new BpmnLabel("Inner_task_label", new Bounds(1100, 2060, 60, 14), "Style_inner") }),
                        ],
                    },
                },
            ],
        };
    }

    /// <summary>The same document with a Task on the side a container grows into.</summary>
    private static BpmnDocument Neighboured()
    {
        var document = Collapsed();
        var plane = document.Diagrams[0].Plane;

        return document with
        {
            FlowNodes =
            [
                .. document.FlowNodes,
                new BpmnFlowNode("Task_after", BpmnFlowNodeType.UserTask, "Process_1", "Later"),
                new BpmnFlowNode("Task_far", BpmnFlowNodeType.UserTask, "Process_1", "Further"),
            ],
            SequenceFlows =
            [
                .. document.SequenceFlows,
                // Both ends right of the container, so the opening moves the whole route.
                new BpmnSequenceFlow("Flow_after", "Task_after", "Task_far", "Process_1"),
                // One end each side of it, which is the route the opening pulls apart.
                new BpmnSequenceFlow("Flow_across", "Task_outer", "Task_after", "Process_1"),
            ],
            Diagrams =
            [
                document.Diagrams[0] with
                {
                    Plane = plane with
                    {
                        Shapes =
                        [
                            .. plane.Shapes,
                            new BpmnShape("Task_after_di", "Task_after", new Bounds(600, 80, 100, 80)),
                            new BpmnShape("Task_far_di", "Task_far", new Bounds(800, 80, 100, 80)),
                        ],
                        Edges =
                        [
                            .. plane.Edges,
                            new BpmnEdge("Flow_after_di", "Flow_after", [new DiPoint(700, 120), new DiPoint(750, 120), new DiPoint(750, 120), new DiPoint(800, 120)]),
                            new BpmnEdge("Flow_across_di", "Flow_across", [new DiPoint(220, 140), new DiPoint(400, 140), new DiPoint(400, 120), new DiPoint(600, 120)]),
                        ],
                    },
                },
                document.Diagrams[1],
            ],
        };
    }

    /// <summary>A neighbour that already overlaps the collapsed container's column.</summary>
    private static BpmnDocument WithAStraddler()
    {
        var document = Collapsed();
        var plane = document.Diagrams[0].Plane;

        return document with
        {
            FlowNodes = [.. document.FlowNodes, new BpmnFlowNode("Straddler", BpmnFlowNodeType.UserTask, "Process_1", "Across")],
            Diagrams =
            [
                document.Diagrams[0] with
                {
                    Plane = plane with
                    {
                        // Overlaps the Sub-Process on both axes, so neither side is the side it is on.
                        Shapes = [.. plane.Shapes, new BpmnShape("Straddler_di", "Straddler", new Bounds(300, 100, 100, 80))],
                    },
                },
                document.Diagrams[1],
            ],
        };
    }

    /// <summary>The same collapsed Sub-Process, inside a Pool that has to grow around it.</summary>
    private static BpmnDocument Pooled()
    {
        var document = Collapsed();
        var plane = document.Diagrams[0].Plane;

        return document with
        {
            Collaboration = new BpmnCollaboration(
                "Collaboration_1",
                null,
                [new BpmnParticipant("Participant_1", "Everyone", "Process_1")]),
            Diagrams =
            [
                document.Diagrams[0] with
                {
                    Plane = plane with
                    {
                        Shapes =
                        [
                            new BpmnShape("Participant_1_di", "Participant_1", new Bounds(60, 40, 500, 220), IsHorizontal: true),
                            .. plane.Shapes,
                        ],
                    },
                },
                document.Diagrams[1],
            ],
        };
    }

    /// <summary>A Sub-Process with no content at all.</summary>
    private static BpmnDocument Bare() => new BpmnDocumentBuilder("Process_1")
        .Node("Sub_1", BpmnFlowNodeType.SubProcess, new Bounds(345, 80, 100, 80), "Empty", isExpanded: false)
        .Build();

    /// <summary>A Sub-Process that owns content no diagram draws.</summary>
    private static BpmnDocument UndrawnContent()
    {
        var document = Bare();

        return document with
        {
            FlowNodes = [.. document.FlowNodes, new BpmnFlowNode("Ghost_1", BpmnFlowNodeType.UserTask, "Sub_1", "Unseen")],
        };
    }

    /// <summary>A Sub-Process drawn expanded in place, with no diagram of its own.</summary>
    private static BpmnDocument ExpandedInPlace()
    {
        var document = new BpmnDocumentBuilder("Process_1")
            .Node("Sub_1", BpmnFlowNodeType.SubProcess, new Bounds(200, 100, 350, 200), "Holder", isExpanded: true)
            .Build();

        var plane = document.Diagrams[0].Plane;

        return document with
        {
            FlowNodes = [.. document.FlowNodes, new BpmnFlowNode("Inner_task", BpmnFlowNodeType.UserTask, "Sub_1", "Do it")],
            Diagrams =
            [
                document.Diagrams[0] with
                {
                    Plane = plane with
                    {
                        Shapes = [.. plane.Shapes, new BpmnShape("Inner_task_di", "Inner_task", new Bounds(240, 140, 100, 80))],
                    },
                },
            ],
        };
    }

    /// <summary>A collapsed Sub-Process with a Boundary Event on it and a flow into it.</summary>
    private static BpmnDocument WithBoundaryAndFlow()
    {
        var document = Collapsed();
        var plane = document.Diagrams[0].Plane;

        return document with
        {
            FlowNodes =
            [
                .. document.FlowNodes,
                new BpmnFlowNode("Boundary_1", BpmnFlowNodeType.BoundaryEvent, "Process_1", "Timeout",
                    BpmnEventDefinitionType.Timer, AttachedToRef: "Sub_1"),
            ],
            SequenceFlows =
            [
                .. document.SequenceFlows,
                new BpmnSequenceFlow("Flow_in", "Task_outer", "Sub_1", "Process_1"),
            ],
            Diagrams =
            [
                document.Diagrams[0] with
                {
                    Plane = plane with
                    {
                        Shapes = [.. plane.Shapes, new BpmnShape("Boundary_1_di", "Boundary_1", new Bounds(377, 142, 36, 36))],
                        Edges = [.. plane.Edges, new BpmnEdge("Flow_in_di", "Flow_in", [new DiPoint(220, 140), new DiPoint(345, 120)])],
                    },
                },
                document.Diagrams[1],
            ],
        };
    }

    private static BpmnDocument WithCallActivity()
    {
        var document = new BpmnDocumentBuilder("Process_1")
            .Node("Call_1", BpmnFlowNodeType.CallActivity, new Bounds(200, 100, 100, 80), "Calls out")
            .Build();

        return document with
        {
            Processes = [.. document.Processes, new BpmnProcess("Reusable_1", "Reusable")],
            FlowNodes = [.. document.FlowNodes.Select(node => node with { CalledElementRef = "Reusable_1" })],
        };
    }
}
