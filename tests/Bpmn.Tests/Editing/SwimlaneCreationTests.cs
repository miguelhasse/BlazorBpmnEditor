using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Editing;

/// <summary>
/// Creating Pools, Lanes and Boundary Events from the stencil (specification sections 9.1 and 9.3).
/// </summary>
/// <remarks>
/// The three entries section 9.1 lists that the palette had been leaving out. Each is a different
/// kind of not-a-Flow-Node: a Pool belongs to a Collaboration rather than to a flow-elements
/// container, a Lane has no rectangle of its own because it subdivides one, and a Boundary Event is
/// dropped onto something rather than at a point.
/// </remarks>
public sealed class SwimlaneCreationTests
{
    // --- Pools ---------------------------------------------------------------------

    [Fact]
    public void CreatesAPoolAndItsShape()
    {
        var result = Apply(Collaborated(), new CreateParticipantCommand(
            "Participant_2",
            new Bounds(0, 400, 600, 250),
            "Supplier",
            IsHorizontal: true));

        Assert.True(result.IsApplied, Codes(result));

        var pool = result.Document.Collaboration!.Participants.Single(p => p.Id == "Participant_2");
        Assert.Equal("Supplier", pool.Name);

        // Black box by default: section 9.3 keeps Process assignment a separate command, and a
        // Participant with no processRef is a legal Message Flow endpoint.
        Assert.Null(pool.ProcessRef);
        Assert.True(ShapeOf(result.Document, "Participant_2").IsHorizontal);
    }

    [Fact]
    public void CreatesAPoolAndItsProcessAsOneStep()
    {
        // What the stencil drop composes: two commands, one undo.
        var result = Apply(Collaborated(), new CompoundCommand(
            [
                new CreateProcessCommand("Process_2"),
                new CreateParticipantCommand("Participant_2", new Bounds(0, 400, 600, 250), "Supplier", "Process_2"),
            ],
            "Create Pool"));

        Assert.True(result.IsApplied, Codes(result));
        Assert.Contains(result.Document.Processes, process => process.Id == "Process_2");
        Assert.Equal(
            "Process_2",
            result.Document.Collaboration!.Participants.Single(p => p.Id == "Participant_2").ProcessRef);
    }

    [Fact]
    public void RefusesAPoolWhereThereIsNoCollaborationToHoldIt()
    {
        var result = Apply(Bare(), new CreateParticipantCommand("Participant_1", new Bounds(0, 0, 600, 250)));

        Assert.False(result.IsApplied);
        Assert.Equal("CT-001", Assert.Single(result.Rejections).Code);
    }

    [Fact]
    public void RefusesTwoPoolsDrawingOneProcess()
    {
        // Containment would be ambiguous: a node in that Process would be in both Pools.
        var result = Apply(Collaborated(), new CreateParticipantCommand(
            "Participant_2",
            new Bounds(0, 400, 600, 250),
            "Supplier",
            "Process_1"));

        Assert.False(result.IsApplied);
        Assert.Equal("CT-002", Assert.Single(result.Rejections).Code);
    }

    [Fact]
    public void RefusesAPoolThatOverlapsAnother()
    {
        var result = Apply(Collaborated(), new CreateParticipantCommand(
            "Participant_2",
            new Bounds(100, 100, 600, 250)));

        Assert.False(result.IsApplied);
        Assert.Equal("DI-011", Assert.Single(result.Rejections).Code);
    }

    // --- Lanes ----------------------------------------------------------------------

    [Fact]
    public void FirstLaneFillsThePoolBesideItsHeader()
    {
        var result = Apply(Collaborated(), new CreateLaneCommand("Lane_1", "Process_1", "Sales"));

        Assert.True(result.IsApplied, Codes(result));

        var pool = ShapeOf(result.Document, "Participant_1").Bounds;
        var lane = ShapeOf(result.Document, "Lane_1").Bounds;

        // Section 1.5.3: the Lanes tile the Pool, leaving its header band.
        Assert.Equal(pool.Y, lane.Y);
        Assert.Equal(pool.Height, lane.Height);
        Assert.Equal(pool.X + BpmnLaneLayout.HeaderSize, lane.X);
        Assert.Equal(pool.Width - BpmnLaneLayout.HeaderSize, lane.Width);
    }

    [Fact]
    public void ASecondLaneSharesThePoolWithTheFirst()
    {
        var one = Apply(Collaborated(), new CreateLaneCommand("Lane_1", "Process_1", "Sales"));
        var two = Apply(one.Document, new CreateLaneCommand("Lane_2", "Process_1", "Support"));

        Assert.True(two.IsApplied, Codes(two));

        var pool = ShapeOf(two.Document, "Participant_1").Bounds;
        var first = ShapeOf(two.Document, "Lane_1").Bounds;
        var second = ShapeOf(two.Document, "Lane_2").Bounds;

        // They stack, they do not overlap, and together they are exactly the Pool.
        Assert.Equal(first.Y + first.Height, second.Y);
        Assert.Equal(pool.Height, first.Height + second.Height);
        Assert.Equal(pool.Y, first.Y);
    }

    [Fact]
    public void CreatesTheLaneSetOnceAndAddsToIt()
    {
        var one = Apply(Collaborated(), new CreateLaneCommand("Lane_1", "Process_1"));
        var two = Apply(one.Document, new CreateLaneCommand("Lane_2", "Process_1"));

        // Section 9.3: "creates/uses a LaneSet". One set, two Lanes in it.
        var set = Assert.Single(two.Document.LaneSets);
        Assert.Equal("Process_1", set.ContainerId);
        Assert.Equal(["Lane_1", "Lane_2"], set.LaneIds);
        Assert.All(two.Document.Lanes, lane => Assert.Equal(set.Id, lane.LaneSetId));
    }

    [Fact]
    public void ALaneChangesNothingAboutOwnership()
    {
        // Section 9.3: "without changing Process/Sub-Process scope". Lane membership is not
        // ownership (section 2.8), so the nodes it now covers still belong where they did.
        var before = Collaborated();
        var result = Apply(before, new CreateLaneCommand("Lane_1", "Process_1"));

        Assert.True(result.IsApplied, Codes(result));
        Assert.Equal(
            before.FlowNodes.Select(node => (node.Id, node.ContainerId)),
            result.Document.FlowNodes.Select(node => (node.Id, node.ContainerId)));

        Assert.Equal(["Task_1"], result.Document.Lanes.Single().FlowNodeRefs);
        Assert.Equal("Lane_1", result.Document.FlowNodes.Single().LaneId);
    }

    [Fact]
    public void ALaneMayClaimNodesTheContainerOwns()
    {
        var result = Apply(Collaborated(), new CreateLaneCommand("Lane_1", "Process_1", "Sales", ["Task_1"]));

        Assert.True(result.IsApplied, Codes(result));
        Assert.Equal(["Task_1"], result.Document.Lanes.Single().FlowNodeRefs);
    }

    [Fact]
    public void RefusesALaneClaimingANodeFromAnotherContainer()
    {
        // CT-004: membership and embedding must agree.
        var document = Collaborated();
        var nested = document with
        {
            FlowNodes = [.. document.FlowNodes, new BpmnFlowNode("Outsider", BpmnFlowNodeType.Task, "Process_Other")],
        };

        var result = Apply(nested, new CreateLaneCommand("Lane_1", "Process_1", "Sales", ["Outsider"]));

        Assert.False(result.IsApplied);
        Assert.Equal("CT-004", Assert.Single(result.Rejections).Code);
    }

    [Fact]
    public void RefusesALaneInSomethingThatIsNotAContainer()
    {
        var result = Apply(Collaborated(), new CreateLaneCommand("Lane_1", "Task_1"));

        Assert.False(result.IsApplied);
        Assert.Equal("CT-001", Assert.Single(result.Rejections).Code);
    }

    [Fact]
    public void CreatesAPoolForTheFirstLaneOnAStandaloneProcess()
    {
        var result = Apply(Bare(), new CreateLaneCommand("Lane_1", "Process_1", "Sales"));

        Assert.True(result.IsApplied, Codes(result));
        Assert.Equal("Process_1", Assert.Single(result.Document.Collaboration!.Participants).ProcessRef);
        Assert.Equal(["Task_1"], Assert.Single(result.Document.Lanes).FlowNodeRefs);
        Assert.Equal(ShapeOf(Bare(), "Task_1"), ShapeOf(result.Document, "Task_1"));
    }

    [Fact]
    public void CreatesAPoolForALaneInASubProcessOfAStandaloneProcess()
    {
        // The rule follows containment outwards: an embedded Sub-Process is held by whatever holds
        // the Process it is in, so a Sub-Process on a bare Process has no Pool either.
        var document = Bare() with
        {
            FlowNodes =
            [
                .. Bare().FlowNodes,
                new BpmnFlowNode("Sub_1", BpmnFlowNodeType.SubProcess, "Process_1", "Handle") { FlowElementsOwned = true },
            ],
        };

        var result = Apply(document, new CreateLaneCommand("Lane_1", "Sub_1", "Sales"));

        Assert.True(result.IsApplied, Codes(result));
        Assert.Equal("Process_1", Assert.Single(result.Document.Collaboration!.Participants).ProcessRef);
        Assert.Equal("Sub_1", Assert.Single(result.Document.LaneSets).ContainerId);
    }

    [Fact]
    public void TheStencilSaysSoBeforeTheCommandHasTo()
    {
        /*
         * Section 9.3: a tool that cannot be used here is shown disabled with its reason rather
         * than hidden, so the reader is told a Lane needs a Pool instead of wondering where the
         * tool went.
         */
        var tool = BpmnStencil.GroupsFor(Bare(), "Process_1")
            .SelectMany(group => group.Entries)
            .Single(entry => entry.Kind == BpmnStencilKind.PoolOrLane);

        Assert.True(tool.IsEnabled);
        Assert.Null(tool.DisabledReason);
    }

    [Fact]
    public void AndOffersItWhereAPoolHoldsTheProcess()
    {
        var tool = BpmnStencil.GroupsFor(Collaborated(), "Process_1")
            .SelectMany(group => group.Entries)
            .Single(entry => entry.Kind == BpmnStencilKind.PoolOrLane);

        Assert.True(tool.IsEnabled);
    }

    // --- Boundary Events --------------------------------------------------------------

    [Fact]
    public void ResolvesTheActivityUnderTheDropAndSnapsToItsOutline()
    {
        var document = Collaborated();

        // Dropped on the host's right border, roughly.
        var placement = BpmnBoundaryPlacement.Resolve(document, new Bounds(302, 160, 36, 36));

        Assert.NotNull(placement);
        Assert.Equal("Task_1", placement.Value.HostId);

        // DI-010: the centre sits exactly on the host's outline.
        var host = ShapeOf(document, "Task_1").Bounds;
        var centreX = placement.Value.Bounds.X + (placement.Value.Bounds.Width / 2);
        Assert.Equal(host.X + host.Width, centreX);
    }

    [Fact]
    public void FindsNoHostForADropInOpenSpace()
    {
        Assert.Null(BpmnBoundaryPlacement.Resolve(Collaborated(), new Bounds(900, 900, 36, 36)));
    }

    [Fact]
    public void AttachesToTheContainerItWasDroppedInsideOf()
    {
        /*
         * Anywhere on the Activity counts, including the middle of a large one. Requiring the drop
         * to land near the outline left a dead area in the centre that grew with the shape — aiming
         * at the middle of a Sub-Process, which is where a user aims when they mean that
         * Sub-Process, did nothing at all. Where on the border it ends up is this code's job.
         *
         * Only a Boundary Event resolves through here, so this does not make an ordinary drop into
         * a Sub-Process attach to it rather than land in it.
         */
        var document = new BpmnDocumentBuilder("Process_1")
            .Node("Sub_1", BpmnFlowNodeType.SubProcess, new Bounds(100, 100, 400, 300))
            .Build();

        var placement = BpmnBoundaryPlacement.Resolve(document, new Bounds(280, 230, 36, 36));

        Assert.NotNull(placement);
        Assert.Equal("Sub_1", placement.Value.HostId);

        // And pinned to the border, not left where it was dropped (DI-010). The drop at (298, 248)
        // is 148 from the top edge and further from the other three, so it snaps to the top.
        var centreY = placement.Value.Bounds.Y + (placement.Value.Bounds.Height / 2d);
        Assert.Equal(100d, centreY, 3);
    }

    [Fact]
    public void PrefersTheTaskOverTheSubProcessItSitsIn()
    {
        // Both contain the drop, so the smaller one wins: the pointer was on the Task.
        var document = new BpmnDocumentBuilder("Process_1")
            .Node("Sub_1", BpmnFlowNodeType.SubProcess, new Bounds(100, 100, 400, 300))
            .Node("Task_1", BpmnFlowNodeType.Task, new Bounds(200, 200, 100, 80))
            .Build();

        var placement = BpmnBoundaryPlacement.Resolve(document, new Bounds(232, 222, 36, 36));

        Assert.NotNull(placement);
        Assert.Equal("Task_1", placement.Value.HostId);
    }

    [Fact]
    public void PrefersTheSmallerShapeWhenTwoOutlinesAreEquallyNear()
    {
        // A Task on the edge of a Sub-Process: the pointer was on the Task.
        var document = new BpmnDocumentBuilder("Process_1")
            .Node("Sub_1", BpmnFlowNodeType.SubProcess, new Bounds(100, 100, 400, 300))
            .Node("Task_Inner", BpmnFlowNodeType.Task, new Bounds(100, 100, 100, 80), containerId: "Sub_1")
            .Build();

        var placement = BpmnBoundaryPlacement.Resolve(document, new Bounds(82, 130, 36, 36));

        Assert.Equal("Task_Inner", placement?.HostId);
    }

    [Fact]
    public void CreatesABoundaryEventAttachedToItsHost()
    {
        var placement = BpmnBoundaryPlacement.Resolve(Collaborated(), new Bounds(302, 160, 36, 36))!.Value;

        var result = Apply(Collaborated(), new CreateElementCommand(
            "Boundary_1",
            BpmnFlowNodeType.BoundaryEvent,
            "Process_1",
            placement.Bounds,
            "Timeout",
            BpmnEventDefinitionType.Timer,
            AttachedToRef: placement.HostId));

        Assert.True(result.IsApplied, Codes(result));

        var created = result.Document.FindFlowNode("Boundary_1")!;
        Assert.Equal("Task_1", created.AttachedToRef);
        Assert.Equal(BpmnEventDefinitionType.Timer, created.EventDefinition);
    }

    [Fact]
    public void ABoundaryEventMayOverlapItsHost()
    {
        // DI-011 governs non-boundary Events, Activities and Gateways; a Boundary Event is meant
        // to sit on its host's border and would be refused by its own host otherwise.
        var placement = BpmnBoundaryPlacement.Resolve(Collaborated(), new Bounds(302, 160, 36, 36))!.Value;

        var result = Apply(Collaborated(), new CreateElementCommand(
            "Boundary_1",
            BpmnFlowNodeType.BoundaryEvent,
            "Process_1",
            placement.Bounds,
            EventDefinition: BpmnEventDefinitionType.Error,
            AttachedToRef: placement.HostId));

        Assert.True(result.IsApplied, Codes(result));
    }

    // --- helpers -------------------------------------------------------------------------

    private static BpmnEditResult Apply(BpmnDocument document, BpmnEditCommand command) =>
        BpmnDocumentEditor.Apply(document, command);

    private static string Codes(BpmnEditResult result) =>
        string.Join(", ", result.Rejections.Select(rejection => $"{rejection.Code}: {rejection.Message}"));

    private static BpmnShape ShapeOf(BpmnDocument document, string id) => document.Diagrams
        .SelectMany(diagram => diagram.Plane.Shapes)
        .Single(shape => shape.BpmnElement == id);

    private static BpmnDocument Collaborated() => new BpmnDocumentBuilder("Process_1")
        .Pool("Participant_1", "Customer", new Bounds(0, 0, 600, 250))
        .Node("Task_1", BpmnFlowNodeType.UserTask, new Bounds(202, 120, 100, 80), "Work", poolId: "Participant_1")
        .Build();

    private static BpmnDocument Bare() => new BpmnDocumentBuilder("Process_1")
        .Node("Task_1", BpmnFlowNodeType.UserTask, new Bounds(200, 120, 100, 80), "Work")
        .Build();
}
