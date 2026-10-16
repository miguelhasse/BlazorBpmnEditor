using System.Text.Json;

using Hasseware.AspNetCore.Components.Bpmn.Model;

namespace Hasseware.AspNetCore.Components.Bpmn.Editing;

/// <summary>What kind of container a stencil is being computed for (specification section 9.1).</summary>
public enum BpmnStencilContext
{
    /// <summary>No root Process or Collaboration exists yet.</summary>
    EmptyCanvas,

    /// <summary>A Collaboration plane: Pools and artifacts, never Flow Nodes directly.</summary>
    CollaborationPlane,

    /// <summary>A Process root with no Participant.</summary>
    StandaloneProcess,

    /// <summary>A Pool that references a Process.</summary>
    PoolWithProcess,

    /// <summary>A Pool with no Process assigned — a Message Flow endpoint and nothing else.</summary>
    BlackBoxPool,

    /// <summary>An expanded embedded Sub-Process, Transaction or Ad-Hoc Sub-Process.</summary>
    EmbeddedSubProcess,

    /// <summary>An Event Sub-Process, whose content is constrained further.</summary>
    EventSubProcess,
}

/// <summary>
/// One entry in the stencil.
/// </summary>
/// <remarks>
/// A disabled entry carries the reason code rather than disappearing. Section 9.3 asks for exactly
/// that: a tool the user cannot use should say which rule stopped it, because a silently missing
/// tool is indistinguishable from a bug in the editor.
/// </remarks>
/// <summary>What kind of thing a stencil entry makes.</summary>
/// <remarks>
/// A stencil is not a list of Flow Nodes. An artifact is not a Flow Node, a data reference points
/// at a definition that is never drawn, and a Call Activity cannot be created until the user has
/// said what it calls — three different commands behind one palette, so the entry has to say which.
/// </remarks>
public enum BpmnStencilKind
{
    /// <summary>A Flow Node: an Event, Activity or Gateway.</summary>
    FlowNode,

    /// <summary>A Text Annotation or a Group.</summary>
    Artifact,

    /// <summary>A Data Object or Data Store reference.</summary>
    Data,

    /// <summary>A Call Activity, which needs a definition picked before it can be created.</summary>
    CallActivity,

    /// <summary>
    /// A Boundary Event, which is dropped onto an Activity rather than at a point.
    /// </summary>
    /// <remarks>
    /// Section 9.3 requires it to have a host the moment it exists, so the drop resolves the
    /// Activity under the pointer and refuses when there is none.
    /// </remarks>
    BoundaryEvent,

    /// <summary>An Intermediate Event or Boundary Event, selected by the drop target.</summary>
    IntermediateBoundaryEvent,

    /// <summary>A Pool, which creates a Participant and — through a second command — its Process.</summary>
    Participant,

    /// <summary>A Lane, which subdivides a stack rather than taking a rectangle of its own.</summary>
    Lane,

    /// <summary>A Pool or Lane, selected by the drop target.</summary>
    PoolOrLane,
}

/// <param name="Type">The flow node this entry creates, for a <see cref="BpmnStencilKind.FlowNode"/>.</param>
/// <param name="Label">What the entry is called.</param>
/// <param name="EventDefinition">The trigger, for the typed Event entries.</param>
/// <param name="IsEnabled">Whether it can be used in the current context.</param>
/// <param name="DisabledReason">The rule code that disabled it, when it is disabled.</param>
/// <param name="Kind">Which command the entry runs.</param>
/// <param name="Artifact">Which artifact, for an <see cref="BpmnStencilKind.Artifact"/> entry.</param>
/// <param name="Data">Which data reference, for a <see cref="BpmnStencilKind.Data"/> entry.</param>
public sealed record BpmnStencilEntry(
    BpmnFlowNodeType Type,
    string Label,
    BpmnEventDefinitionType EventDefinition = BpmnEventDefinitionType.None,
    bool IsEnabled = true,
    string? DisabledReason = null,
    BpmnStencilKind Kind = BpmnStencilKind.FlowNode,
    BpmnArtifactKind Artifact = BpmnArtifactKind.TextAnnotation,
    BpmnDataKind Data = BpmnDataKind.DataObject)
{
    internal bool HasCustomLabel { get; init; }

    /// <summary>Whether using this entry needs something chosen first.</summary>
    /// <remarks>
    /// Only the Call Activity, which cannot be created until the user names the definition it
    /// calls (CT-010). Everything else is a drop.
    /// </remarks>
    public bool NeedsChoice => Kind == BpmnStencilKind.CallActivity;

    /// <summary>Element template applied atomically when this tool creates an element.</summary>
    public Model.Extensions.Templates.BpmnElementTemplate? Template { get; init; }

    /// <summary>A tooltip, when a host has given one.</summary>
    public string? Description { get; init; }

    /// <summary>A glyph other than the one the entry's type implies, named as the manifest names it.</summary>
    public string? IconId { get; init; }

    /// <summary>
    /// What this entry is, independently of what it is called.
    /// </summary>
    /// <remarks>
    /// The key a host curates by. It deliberately excludes the label: renaming a tool is one of the
    /// things curation is for, and a key containing the label would stop matching the moment it was
    /// used. Built from what the entry creates, which is the thing that does not change.
    /// </remarks>
    public string Key => Kind switch
    {
        BpmnStencilKind.Artifact => $"artifact/{Wire(Artifact)}",
        BpmnStencilKind.Data => $"data/{Wire(Data)}",
        BpmnStencilKind.Participant => "swimlane/participant",
        BpmnStencilKind.Lane => "swimlane/lane",
        BpmnStencilKind.PoolOrLane => "swimlane/poolOrLane",
        BpmnStencilKind.IntermediateBoundaryEvent => "event/intermediateOrBoundary",
        BpmnStencilKind.CallActivity => "flowNode/callActivity",
        BpmnStencilKind.BoundaryEvent => $"boundaryEvent/{Wire(EventDefinition)}",
        _ => EventDefinition == BpmnEventDefinitionType.None
            ? $"flowNode/{Wire(Type)}"
            : $"flowNode/{Wire(Type)}/{Wire(EventDefinition)}",
    };

    private static string Wire<T>(T value) =>
        JsonSerializer.Serialize(value).Trim('"');
}

/// <summary>A titled run of stencil entries.</summary>
/// <param name="Title">The group heading.</param>
/// <param name="Entries">Its entries, in display order.</param>
public sealed record BpmnStencilGroup(string Title, IReadOnlyList<BpmnStencilEntry> Entries);

/// <summary>
/// Works out what a stencil may offer for a given container.
/// </summary>
/// <remarks>
/// <para>
/// Section 9.1 tabulates this by container, and the interesting part is what each row
/// <em>excludes</em>: a Collaboration plane offers Pools but not Flow Nodes, a black-box Pool
/// offers nothing at all until it has a Process, and an Event Sub-Process may not have a None
/// Start Event. Computing it from the container rather than showing one fixed palette is what
/// stops the editor offering a creation it would then have to refuse.
/// </para>
/// <para>
/// Recomputed from the active semantic owner on demand rather than cached, because the answer
/// changes as soon as a Process is assigned to a Participant.
/// </para>
/// </remarks>
public static class BpmnStencil
{
    /// <summary>
    /// Classifies a container so the right section 9.1 row applies.
    /// </summary>
    /// <param name="document">The document being edited.</param>
    /// <param name="containerId">
    /// The Process, Sub-Process or Participant the user is creating into, or
    /// <see langword="null"/> for the plane itself.
    /// </param>
    /// <param name="diagramId">
    /// The plane being drawn, which decides whether a Sub-Process on it counts as open.
    /// </param>
    /// <returns>Which row of the section 9.1 table governs.</returns>
    public static BpmnStencilContext ContextFor(BpmnDocument document, string? containerId, string? diagramId = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.Processes.Count == 0 && document.Collaboration is null) return BpmnStencilContext.EmptyCanvas;

        if (containerId is null)
        {
            return document.Collaboration is not null
                ? BpmnStencilContext.CollaborationPlane
                : BpmnStencilContext.StandaloneProcess;
        }

        if (document.Find(containerId) is BpmnParticipant participant)
        {
            // A Participant with no processRef is black-box: a valid Message Flow endpoint with no
            // internal content at all until a Process is assigned.
            return participant.ProcessRef is null
                ? BpmnStencilContext.BlackBoxPool
                : BpmnStencilContext.PoolWithProcess;
        }

        if (document.FindFlowNode(containerId) is { } node)
        {
            // A collapsed Sub-Process, or a node that holds nothing at all, is not somewhere the
            // user is working *inside*. Whatever holds it is.
            if (!IsOpenContainer(document, node, diagramId)) return ContextFor(document, node.ContainerId, diagramId);

            return node.Type switch
            {
                BpmnFlowNodeType.EventSubProcess => BpmnStencilContext.EventSubProcess,
                _ => BpmnStencilContext.EmbeddedSubProcess,
            };
        }

        // A Process that a Participant references is a Pool row, not a standalone one: the
        // available stencil follows the Pool, not the Process's own identity.
        var referenced = document.Collaboration?.Participants
            .Any(candidate => candidate.ProcessRef == containerId) == true;

        return referenced ? BpmnStencilContext.PoolWithProcess : BpmnStencilContext.StandaloneProcess;
    }

    /// <summary>
    /// Whether a Flow Node is a container the user can create into.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Owning content is not enough: a <em>collapsed</em> Sub-Process renders as an opaque leaf and
    /// does not materialise its children, so there is nowhere on the canvas for a new element to
    /// land. Offering its palette invites a creation whose shape would be invisible — and the drop
    /// resolver refuses it anyway, because the collapsed outline is smaller than the thing being
    /// dropped.
    /// </para>
    /// <para>
    /// A shape with no explicit <c>isExpanded</c> is <em>collapsed</em>. The specification gives no
    /// default for the attribute — section 10.4 is about <c>isHorizontal</c> and says nothing about
    /// expansion — so this is the editor's choice, and it is the safe one. A file that omits the
    /// flag and draws no children renders as a Task-sized box with a "+" the user can press; read
    /// the other way it would be a large empty rectangle with nothing to say its content is
    /// elsewhere. It is also what the renderer, the embedding and the marker layer have always
    /// done, so this is the one reading of four that had to move.
    /// </para>
    /// <para>
    /// Judged on one plane, because the answer differs between them: the same Sub-Process can be
    /// collapsed where the user is looking and expanded on a diagram they are not. Passing no
    /// diagram asks about any plane that draws it, which is what a caller with no active plane —
    /// a test, a headless validation — actually means.
    /// </para>
    /// </remarks>
    /// <param name="document">The document being edited.</param>
    /// <param name="node">The node to judge.</param>
    /// <param name="diagramId">The plane the question is about, or <see langword="null"/> for any.</param>
    /// <returns><see langword="true"/> if new elements may be created inside it.</returns>
    public static bool IsOpenContainer(BpmnDocument document, BpmnFlowNode node, string? diagramId = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(node);

        if (!node.FlowElementsOwned) return false;

        var shape = document.Diagrams
            .Where(diagram => diagramId is null || diagram.Id == diagramId)
            .SelectMany(diagram => diagram.Plane.Shapes)
            .FirstOrDefault(candidate => candidate.BpmnElement == node.Id);

        return shape?.IsExpanded == true;
    }

    /// <summary>
    /// The groups a stencil should show for a container.
    /// </summary>
    /// <param name="document">The document being edited.</param>
    /// <param name="containerId">The container being created into, or <see langword="null"/> for the plane.</param>
    /// <param name="diagramId">The plane being drawn, which decides whether a Sub-Process on it is open.</param>
    /// <returns>Groups in display order; may be empty for a black-box Pool.</returns>
    public static IReadOnlyList<BpmnStencilGroup> GroupsFor(
        BpmnDocument document,
        string? containerId,
        string? diagramId = null) =>
        WithSwimlaneRule(document, containerId, GroupsFor(ContextFor(document, containerId, diagramId)));

    /// <summary>
    /// Disables the Pool/Lane tool where neither action can be performed.
    /// </summary>
    /// <remarks>
    /// The drop chooses a Lane inside an existing Pool and a Pool otherwise. Without a
    /// Collaboration, neither operation has a valid home.
    /// </remarks>
    private static IReadOnlyList<BpmnStencilGroup> WithSwimlaneRule(
        BpmnDocument document,
        string? containerId,
        IReadOnlyList<BpmnStencilGroup> groups)
    {
        if (document.Collaboration is not null || PoolOwning(document, containerId) is not null) return groups;

        return
        [
            .. groups.Select(group => group with
            {
                Entries =
                [
                    .. group.Entries.Select(entry => entry.Kind == BpmnStencilKind.PoolOrLane
                        ? entry with { IsEnabled = false, DisabledReason = "CT-001" }
                        : entry),
                ],
            }),
        ];
    }

    /// <summary>
    /// The Participant whose Process holds this container, following Sub-Processes outwards.
    /// </summary>
    /// <param name="document">The document to look in.</param>
    /// <param name="containerId">The active container, or null for the document's first Process.</param>
    /// <returns>The Pool, or <see langword="null"/> when no Participant references the Process.</returns>
    public static BpmnParticipant? PoolOwning(BpmnDocument document, string? containerId)
    {
        ArgumentNullException.ThrowIfNull(document);

        var participants = document.Collaboration?.Participants ?? [];
        if (participants.Count == 0) return null;

        // An embedded Sub-Process is owned by whatever owns it, however deeply nested.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var current = containerId ?? (document.Processes.Count > 0 ? document.Processes[0].Id : null);

        while (current is not null && seen.Add(current))
        {
            if (participants.FirstOrDefault(participant => participant.Id == current) is { } pool) return pool;

            if (participants.FirstOrDefault(participant => participant.ProcessRef == current) is { } owner) return owner;

            current = document.FlowNodes.FirstOrDefault(node => node.Id == current)?.ContainerId;
        }

        return null;
    }

    /// <summary>The groups for a context.</summary>
    /// <param name="context">Which section 9.1 row applies.</param>
    /// <returns>Groups in display order.</returns>
    public static IReadOnlyList<BpmnStencilGroup> GroupsFor(BpmnStencilContext context) => context switch
    {
        // Nothing can be drawn before a plane exists; creating the root is a separate action, not
        // a Flow Node dropped on the Definitions root.
        BpmnStencilContext.EmptyCanvas => [],

        // Pool/Lane resolves against the pointer; the Group is the only other Collaboration artifact.
        BpmnStencilContext.CollaborationPlane => [Swimlanes(), Artifacts()],

        // A Message Flow endpoint and nothing else until a Process is assigned.
        BpmnStencilContext.BlackBoxPool => [],

        BpmnStencilContext.EventSubProcess =>
        [
            // Its Start Event must be typed: a None Start would make the Event Sub-Process
            // untriggerable, so the untyped entry is shown disabled rather than hidden.
            new BpmnStencilGroup("Start", [
                new(BpmnFlowNodeType.StartEvent, "Start Event", IsEnabled: false, DisabledReason: "EV-004"),
            ]),
            Events(includeStart: false),
            Activities(),
            Gateways(),
            Data(),
            Artifacts(),
        ],

        _ =>
        [
            Events(includeStart: true),
            Activities(),
            Gateways(),
            Data(),

            Swimlanes(),
            Artifacts(),
        ],
    };

    private static BpmnStencilGroup Swimlanes() => new(
        "Swimlanes",
        [
            new(BpmnFlowNodeType.Task, "Pool / Lane", Kind: BpmnStencilKind.PoolOrLane),
        ]);

    private static BpmnStencilGroup Events(bool includeStart) => new(
        "Events",
        [
            .. includeStart
                ? (BpmnStencilEntry[])
                [
                    new(BpmnFlowNodeType.StartEvent, "Start Event"),
                ]
                : [],
            new(
                BpmnFlowNodeType.IntermediateThrowEvent,
                "Intermediate / Boundary Event",
                Kind: BpmnStencilKind.IntermediateBoundaryEvent),
            new(BpmnFlowNodeType.EndEvent, "End Event"),
        ]);

    private static BpmnStencilGroup Activities() => new(
        "Activities",
        [
            new(BpmnFlowNodeType.Task, "Task"),
            new(BpmnFlowNodeType.SubProcess, "Sub-Process"),
            new(BpmnFlowNodeType.CallActivity, "Call Activity", Kind: BpmnStencilKind.CallActivity),
        ]);

    private static BpmnStencilGroup Gateways() => new(
        "Gateways",
        [
            new(BpmnFlowNodeType.ExclusiveGateway, "Gateway"),
        ]);

    /// <summary>
    /// Data references.
    /// </summary>
    /// <remarks>
    /// What is dropped is a <em>reference</em>; what it points at is a definition that is never
    /// drawn. A drop makes both, with a fresh definition — section 9.1's other option, referencing
    /// an existing definition, is a choice the property panel will offer rather than the stencil,
    /// because a palette entry cannot ask a question.
    /// </remarks>
    private static BpmnStencilGroup Data() => new(
        "Data",
        [
            new(BpmnFlowNodeType.Task, "Data Object Reference", Kind: BpmnStencilKind.Data, Data: BpmnDataKind.DataObject),
            new(BpmnFlowNodeType.Task, "Data Store Reference", Kind: BpmnStencilKind.Data, Data: BpmnDataKind.DataStore),
        ]);

    /// <summary>
    /// Artifacts: documentation rather than notation.
    /// </summary>
    /// <remarks>
    /// Neither is a Flow Node, so neither takes part in Sequence Flow and neither is subject to
    /// DI-011 — an annotation is meant to sit beside what it annotates and a Group is meant to
    /// enclose things.
    /// </remarks>
    private static BpmnStencilGroup Artifacts() => new(
        "Artifacts",
        [
            new(
                BpmnFlowNodeType.Task,
                "Text Annotation",
                Kind: BpmnStencilKind.Artifact,
                Artifact: BpmnArtifactKind.TextAnnotation),
            new(
                BpmnFlowNodeType.Task,
                "Group",
                Kind: BpmnStencilKind.Artifact,
                Artifact: BpmnArtifactKind.Group),
        ]);
}
