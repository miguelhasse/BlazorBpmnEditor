using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;

namespace Hasseware.AspNetCore.Components.Bpmn.Editing;

/// <summary>
/// One edit the user asked for.
/// </summary>
/// <remarks>
/// <para>
/// A command describes intent, not a mutation. It is applied by
/// <see cref="BpmnDocumentEditor"/>, which returns a new document rather than changing the old
/// one, so a rejected command leaves the caller holding exactly what it had — the atomic rollback
/// the specification requires falls out of immutability instead of needing rollback code.
/// </para>
/// <para>
/// Commands carry no inverse. Undo restores a previous document snapshot, which is the only way
/// to honour "undo restores exact attachment coordinates, routing constraints and containment
/// exactly": a hand-written inverse is a second implementation of every edit, and the one that
/// silently drifts.
/// </para>
/// </remarks>
public abstract record BpmnEditCommand
{
    /// <summary>A short description, suitable for an undo menu.</summary>
    public abstract string Label { get; }

    /// <summary>Who asked for the edit; the user unless set.</summary>
    /// <remarks>Recorded in history with the edit; see <see cref="BpmnEditHistory.UndoOrigin"/>.</remarks>
    public BpmnEditOrigin Origin { get; init; }
}

/// <summary>One shape's new geometry.</summary>
/// <param name="ShapeId">The DI shape being moved, not the semantic element.</param>
/// <param name="Bounds">Its new bounds, in diagram coordinates.</param>
public sealed record ShapeGeometry(string ShapeId, Bounds Bounds);

/// <summary>
/// Moves one or more shapes to new bounds.
/// </summary>
/// <remarks>
/// Every affected shape is listed, including the descendants of a moved container. The canvas
/// reports the whole set because it is what actually resolved the drag — recomputing which
/// children moved would be a second implementation of containment, and the plan requires deep
/// container movement to change each descendant exactly once.
/// </remarks>
/// <summary>
/// One connector's route after the shapes at its ends moved.
/// </summary>
/// <remarks>
/// Computed by the canvas, which owns routing, and carried on the move rather than applied as a
/// second command: a drag is one gesture and has to be one undo. Nothing here is invented — these
/// are connectors whose ends the user just moved, so the rule (sections 10.6 and 11) about leaving imported
/// geometry alone does not reach them.
/// </remarks>
/// <param name="ElementId">The connector.</param>
/// <param name="Waypoints">Its whole route, ends included.</param>
public sealed record ConnectorRoute(string ElementId, IReadOnlyList<DiPoint> Waypoints);

/// <param name="Moves">The shapes and their new bounds.</param>
/// <param name="Routes">
/// The connectors attached to what moved, re-squared against their new ends. A move changes where a
/// connector starts and ends but not the bends between, so without these the first and last
/// segments come out at whatever angle the new position implies.
/// </param>
public sealed record MoveShapesCommand(
    IReadOnlyList<ShapeGeometry> Moves,
    IReadOnlyList<ConnectorRoute>? Routes = null) : BpmnEditCommand
{
    /// <summary>The diagram whose geometry to edit, or all diagrams when omitted.</summary>
    public string? DiagramId { get; init; }

    /// <inheritdoc />
    public override string Label => Moves.Count == 1 ? "Move" : $"Move {Moves.Count} elements";
}

/// <summary>
/// Deletes elements, with everything that cannot outlive them.
/// </summary>
/// <remarks>
/// The cascade is specified, not incidental: Sequence Flows lose an endpoint, Boundary Events lose
/// their host, an embedded Sub-Process takes its nested graph with it (section 2.7), and every DI
/// entry whose <c>bpmnElement</c> would dangle goes too (section 10.1).
/// </remarks>
/// <param name="ElementIds">The semantic elements to delete.</param>
public sealed record DeleteElementsCommand(IReadOnlyList<string> ElementIds) : BpmnEditCommand
{
    /// <inheritdoc />
    public override string Label => ElementIds.Count == 1 ? "Delete" : $"Delete {ElementIds.Count} elements";
}

/// <summary>
/// Several commands applied as one, succeeding or failing together.
/// </summary>
/// <remarks>
/// One history entry and one rejection. A compound that applied half of itself would leave the
/// document in a state the user never asked for and cannot undo in one step.
/// </remarks>
/// <param name="Commands">The commands, in application order.</param>
/// <param name="Label">What the whole operation is called.</param>
public sealed record CompoundCommand(IReadOnlyList<BpmnEditCommand> Commands, string Label) : BpmnEditCommand
{
    /// <inheritdoc />
    public override string Label { get; } = Label;
}

/// <summary>
/// Creates one flow node and its DI shape.
/// </summary>
/// <remarks>
/// The id is supplied rather than generated inside the command, which keeps
/// <see cref="BpmnDocumentEditor.Apply(Model.BpmnDocument, BpmnEditCommand)"/> pure: the same
/// command applied twice gives the same document, and a test can name the element it expects.
/// <see cref="BpmnId"/> is what callers use to mint one.
/// </remarks>
/// <param name="Id">The new element's id, unique within the document.</param>
/// <param name="Type">Which flow node to create.</param>
/// <param name="ContainerId">The Process or embedded Sub-Process that will own it.</param>
/// <param name="Bounds">Where to draw it.</param>
/// <param name="Name">Its label, when it has one.</param>
/// <param name="EventDefinition">The trigger, for Events.</param>
/// <param name="AttachedToRef">The host Activity, which a Boundary Event must have.</param>
/// <param name="PoolId">The Participant whose Pool draws it, in a collaboration.</param>
/// <param name="ShapeId">The DI shape id; derived from the element id when omitted.</param>
/// <param name="CalledElementRef">
/// The reusable global Process or Global Task a Call Activity invokes. Required for one, and
/// meaningless on anything else (section 9.1).
/// </param>
public sealed record CreateElementCommand(
    string Id,
    BpmnFlowNodeType Type,
    string ContainerId,
    Bounds Bounds,
    string? Name = null,
    BpmnEventDefinitionType EventDefinition = BpmnEventDefinitionType.None,
    string? AttachedToRef = null,
    string? PoolId = null,
    string? ShapeId = null,
    string? CalledElementRef = null) : BpmnEditCommand
{
    /// <summary>Whether a new Boundary/Event Sub-Process Start Event interrupts.</summary>
    public bool IsInterrupting { get; init; } = true;

    /// <summary>The destination diagram; omitted, the first diagram is used.</summary>
    public string? DiagramId { get; init; }

    /// <inheritdoc />
    public override string Label => $"Create {Type}";
}

/// <summary>
/// Moves an element into a different container.
/// </summary>
/// <remarks>
/// <para>
/// One command, because section 2.8.1 requires semantic ownership, Lane membership, BPMNDI and the
/// visual hierarchy to be updated together or not at all — CT-008 names a partial update as an
/// error in its own right. Splitting it into a move and an ownership change would make the partial
/// state reachable by undoing half of it.
/// </para>
/// <para>
/// Lane membership is not ownership. A Flow Node inside a Lane is owned by the Lane's Process and
/// merely <em>listed</em> in <c>Lane.flowNodeRefs</c>, so a move between Lanes of one Pool changes
/// the membership and leaves <c>containerId</c> alone.
/// </para>
/// </remarks>
/// <param name="ElementId">The element being moved.</param>
/// <param name="ContainerId">The Process or embedded Sub-Process that will own it.</param>
/// <param name="LaneId">The Lane it joins, or <see langword="null"/> for no Lane membership.</param>
/// <param name="Bounds">Where it lands. Its DI shape moves with it.</param>
public sealed record ReparentElementCommand(
    string ElementId,
    string ContainerId,
    string? LaneId,
    Bounds Bounds) : BpmnEditCommand
{
    /// <inheritdoc />
    public override string Label => "Move into container";
}

/// <summary>Which artifact to create.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<BpmnArtifactKind>))]
public enum BpmnArtifactKind
{
    /// <summary>A Text Annotation: free text tied to the diagram by an Association.</summary>
    [System.Text.Json.Serialization.JsonStringEnumMemberName("textAnnotation")] TextAnnotation,

    /// <summary>A Group: visual categorisation that owns nothing (CT-003).</summary>
    [System.Text.Json.Serialization.JsonStringEnumMemberName("group")] Group,
}

/// <summary>
/// Creates a Text Annotation or a Group and its DI shape.
/// </summary>
/// <remarks>
/// Separate from <see cref="CreateElementCommand"/> because neither is a Flow Node: they carry no
/// event definition, take part in no Sequence Flow, and a Group is explicitly never a containment
/// parent. Sharing one command would mean a type parameter that is meaningless for two thirds of
/// its values.
/// </remarks>
/// <param name="Id">The new artifact's id.</param>
/// <param name="Kind">Which artifact to create.</param>
/// <param name="ContainerId">The Process, Sub-Process or Collaboration that owns it.</param>
/// <param name="Bounds">Where to draw it.</param>
/// <param name="Text">The annotation body. Ignored for a Group.</param>
/// <param name="Name">The Group's label. Ignored for a Text Annotation.</param>
/// <param name="ShapeId">The DI shape id; derived from the element id when omitted.</param>
public sealed record CreateArtifactCommand(
    string Id,
    BpmnArtifactKind Kind,
    string ContainerId,
    Bounds Bounds,
    string? Text = null,
    string? Name = null,
    string? ShapeId = null) : BpmnEditCommand
{
    /// <inheritdoc />
    public override string Label => Kind == BpmnArtifactKind.TextAnnotation ? "Create Text Annotation" : "Create Group";

    /// <summary>The destination diagram; omitted, the first diagram is used.</summary>
    public string? DiagramId { get; init; }
}

/// <summary>Which kind of data a reference draws.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<BpmnDataKind>))]
public enum BpmnDataKind
{
    /// <summary>A Data Object, whose definition is scoped to a container.</summary>
    [System.Text.Json.Serialization.JsonStringEnumMemberName("dataObject")] DataObject,

    /// <summary>A Data Store, whose definition is document-scoped (section 1.0).</summary>
    [System.Text.Json.Serialization.JsonStringEnumMemberName("dataStore")] DataStore,
}

/// <summary>
/// Creates a Data Object or Data Store reference, and its definition when asked to.
/// </summary>
/// <remarks>
/// <para>
/// What is drawn is a <em>reference</em>; what it points at is a definition that is never drawn.
/// Section 9.1 requires the editor to offer "new definition" against "reference an existing one"
/// and forbids duplicating definitions by accident, so the choice is in the command rather than
/// inferred: exactly one of <paramref name="DefinitionRef"/> and <paramref name="NewDefinitionId"/>
/// must be given, and supplying both or neither is refused rather than guessed at.
/// </para>
/// <para>
/// A Data Object definition belongs to a container; a Data Store definition belongs to the
/// document's global catalog. That difference is the reason the two kinds are one command with a
/// discriminator rather than one command each — everything except where the definition lands is
/// identical.
/// </para>
/// </remarks>
/// <param name="Id">The new reference's id.</param>
/// <param name="Kind">Whether this draws a Data Object or a Data Store.</param>
/// <param name="ContainerId">The Process or Sub-Process that owns the reference.</param>
/// <param name="Bounds">Where to draw it.</param>
/// <param name="Name">The reference's label.</param>
/// <param name="DefinitionRef">An existing definition to point at.</param>
/// <param name="NewDefinitionId">The id for a definition to create alongside the reference.</param>
/// <param name="ShapeId">The DI shape id; derived from the element id when omitted.</param>
public sealed record CreateDataReferenceCommand(
    string Id,
    BpmnDataKind Kind,
    string ContainerId,
    Bounds Bounds,
    string? Name = null,
    string? DefinitionRef = null,
    string? NewDefinitionId = null,
    string? ShapeId = null) : BpmnEditCommand
{
    /// <inheritdoc />
    public override string Label => Kind == BpmnDataKind.DataObject ? "Create Data Object" : "Create Data Store";

    /// <summary>The destination diagram; omitted, the first diagram is used.</summary>
    public string? DiagramId { get; init; }
}

/// <summary>
/// Moves one end of an existing connector to a different element.
/// </summary>
/// <remarks>
/// <para>
/// Separate from deleting and recreating, which is how it would otherwise have to be done: the
/// connector keeps its id, its name, its condition, its extensions and its place in the document,
/// and every reference to it — a Gateway's <c>default</c>, most obviously — stays valid. Recreating
/// would silently break all of that.
/// </para>
/// <para>
/// The live connection matrix is evaluated on the canvas before the gesture is reported, exactly
/// as it is for creation, so this checks what a document can check: that the connector exists, the
/// new endpoints exist, and the result is not a connector to itself.
/// </para>
/// </remarks>
/// <param name="Id">The connector to reconnect.</param>
/// <param name="SourceRef">Its new source, or <see langword="null"/> to leave it alone.</param>
/// <param name="TargetRef">Its new target, or <see langword="null"/> to leave it alone.</param>
/// <param name="Waypoints">The route, including both endpoints. At least two (DI-001).</param>
public sealed record ReconnectConnectorCommand(
    string Id,
    string? SourceRef,
    string? TargetRef,
    IReadOnlyList<DiPoint> Waypoints) : BpmnEditCommand
{
    /// <summary>The diagram whose route to edit; semantic endpoints change in every view.</summary>
    public string? DiagramId { get; init; }

    /// <inheritdoc />
    public override string Label => "Reconnect";
}

/// <summary>
/// Replaces a connector's route.
/// </summary>
/// <remarks>
/// Geometry only: dragging a bend, adding one, or removing one changes where the line goes and
/// nothing about what it connects. The bends are taken verbatim rather than being re-derived from the
/// endpoints (sections 10.6 and 11); only the two ends are put on their shapes' outlines (section 4.6).
/// </remarks>
/// <param name="Id">The connector whose route changed.</param>
/// <param name="Waypoints">The new route, including both endpoints. At least two (DI-001).</param>
public sealed record UpdateWaypointsCommand(string Id, IReadOnlyList<DiPoint> Waypoints) : BpmnEditCommand
{
    /// <summary>The diagram whose route to edit, or all diagrams when omitted.</summary>
    public string? DiagramId { get; init; }

    /// <inheritdoc />
    public override string Label => "Change route";
}

/// <summary>
/// Resizes one shape, and whatever its geometry is shared with.
/// </summary>
/// <remarks>
/// <para>
/// Separate from <see cref="MoveShapesCommand"/> even though both end in new bounds, because the
/// rules differ: a resize has a minimum size, may not shrink a container below its contents, and
/// has to keep a Pool and its Lanes describing the same rectangle (section 1.5.3). A move has none
/// of those concerns and adding them to it would make every drag pay for them.
/// </para>
/// <para>
/// One shape in, many out. Resizing a Pool re-tiles its Lanes and a Lane cannot be resized
/// directly (see <see cref="MoveLaneSplitterCommand"/>); the caller names the shape the user actually grabbed and the
/// editor works out the rest, because that relationship is a property of the document rather than
/// of the gesture.
/// </para>
/// </remarks>
/// <param name="ShapeId">The DI shape the user resized.</param>
/// <param name="Bounds">Its requested bounds.</param>
/// <param name="Routes">
/// The Sequence and Message Flows attached to the shape, routed by the canvas against its new size.
/// Their ends already follow the shape, so the document does not move them a second time.
/// </param>
public sealed record ResizeShapeCommand(
    string ShapeId,
    Bounds Bounds,
    IReadOnlyList<ConnectorRoute>? Routes = null) : BpmnEditCommand
{
    /// <summary>The diagram whose geometry to edit, or all diagrams when omitted.</summary>
    public string? DiagramId { get; init; }

    /// <inheritdoc />
    public override string Label => "Resize";
}

/// <summary>
/// Moves the splitter between a Lane and the Lane after it.
/// </summary>
/// <remarks>
/// The only way Lanes are sized against one another: they always tile their whole Pool, so the
/// shared edge moves and the Pool does not. A <see cref="ResizeShapeCommand"/> aimed at a Lane is
/// refused.
/// </remarks>
/// <param name="UpperLaneId">The Lane before the splitter (above it, or to its left in a vertical Pool).</param>
/// <param name="Position">The splitter's new coordinate: Y in a horizontal Pool, X in a vertical one.</param>
public sealed record MoveLaneSplitterCommand(string UpperLaneId, double Position) : BpmnEditCommand
{
    /// <inheritdoc />
    public override string Label => "Resize Lanes";
}

/// <summary>
/// Draws a collapsed Sub-Process's content in place (specification section 1.2.1).
/// </summary>
/// <remarks>
/// <para>
/// Expanding is a DI command: it changes <c>BPMNShape.isExpanded</c>, the container's bounds and
/// which plane its children are drawn on, and it changes nothing semantic — no <c>flowElements</c>,
/// no ids, no ownership, no Sequence Flows, no Boundary Events. The content was always there; this
/// is only about where it is drawn.
/// </para>
/// <para>
/// A pair with <see cref="CollapseSubProcessCommand"/> rather than one command that toggles.
/// Applying the same command twice has to give the same document — that is what makes a history of
/// snapshots replayable and what stops two quick presses on the marker cancelling each other out.
/// </para>
/// </remarks>
/// <param name="ElementId">
/// The Sub-Process, not its shape. The canvas reports elements, and a Sub-Process may be drawn on
/// more than one plane, so the handler resolves the shape itself.
/// </param>
/// <param name="Bounds">
/// The size to restore, when the caller remembers one from an earlier collapse. Section 1.2.1 asks
/// for "the last expanded bounds in the current session when available"; whether a remembered size
/// is still the right one is a question about what the user has done since, which the component can
/// answer and the editor cannot. Absent, the bounds are computed from the content.
/// </param>
/// <param name="HostDiagramId">Which plane to expand it on, when more than one draws it.</param>
public sealed record ExpandSubProcessCommand(
    string ElementId,
    Bounds? Bounds = null,
    string? HostDiagramId = null) : BpmnEditCommand
{
    /// <inheritdoc />
    public override string Label => "Expand Sub-Process";
}

/// <summary>
/// Draws a Sub-Process as an opaque leaf again, keeping its content for re-expansion.
/// </summary>
/// <remarks>
/// <para>
/// The content moves to a <c>BPMNPlane</c> of its own, which is where every exporter puts a
/// collapsed container's children and what section 2.8.1 requires — a collapsed Sub-Process "must
/// not have visible embedded process-content cells" on the active plane. A plane is created for it
/// when it has none.
/// </para>
/// <para>
/// What moves is decided by containment, never by geometry. A Boundary Event attached to the
/// Sub-Process itself belongs to the <em>parent</em> process and stays where it is, repositioned
/// onto the smaller outline; only events attached to things inside travel. The same is true of a
/// Sequence Flow that merely ends at the Sub-Process.
/// </para>
/// </remarks>
/// <param name="ElementId">The Sub-Process to close.</param>
/// <param name="Bounds">The collapsed size, when the caller wants a particular one.</param>
/// <param name="HostDiagramId">Which plane to collapse it on, when more than one draws it.</param>
/// <param name="ContentDiagramId">
/// The id to give the diagram the content moves to. Naming it lets an expand-then-collapse cycle
/// put the original file's diagram back under the id it had.
/// </param>
/// <param name="ContentPlaneId">The id to give that diagram's plane.</param>
public sealed record CollapseSubProcessCommand(
    string ElementId,
    Bounds? Bounds = null,
    string? HostDiagramId = null,
    string? ContentDiagramId = null,
    string? ContentPlaneId = null) : BpmnEditCommand
{
    /// <inheritdoc />
    public override string Label => "Collapse Sub-Process";
}

/// <summary>
/// Adds a clipboard fragment to the document.
/// </summary>
/// <remarks>
/// <para>
/// The fragment still carries the ids it was copied with; this command regenerates them, which is
/// what makes the same payload pastable repeatedly (section 2.7: each occurrence is a distinct,
/// unrelated set of elements even when visually identical).
/// </para>
/// <para>
/// One transaction, like every other creation. Either every element and its DI arrives, or none
/// does — a paste that dropped half a Sub-Process's content would produce a document that will not
/// round-trip.
/// </para>
/// </remarks>
/// <param name="Payload">What was copied.</param>
/// <param name="ContainerId">The Process or embedded Sub-Process the roots are pasted into.</param>
/// <param name="Offset">How far from the copied position to place it.</param>
/// <param name="PoolId">The Participant whose Pool draws the roots, in a collaboration.</param>
public sealed record PasteCommand(
    BpmnClipboardPayload Payload,
    string ContainerId,
    Model.Di.DiPoint Offset,
    string? PoolId = null) : BpmnEditCommand
{
    /// <inheritdoc />
    public override string Label => Payload.Count == 1 ? "Paste" : $"Paste {Payload.Count} elements";
}

/// <summary>
/// Changes an element's id, and every reference to it.
/// </summary>
/// <remarks>
/// <para>
/// Separate from <see cref="UpdatePropertiesCommand"/> because an id is not a property of the
/// element so much as the name other elements were written against. Changing it in place produces
/// a document that still serialises and no longer loads: flows pointing at nothing, shapes drawing
/// elements that are not there. Every reference moves with it or the command is refused.
/// </para>
/// <para>
/// This is not the label. The label is <c>name</c>, which is an ordinary editable property.
/// </para>
/// </remarks>
/// <param name="ElementId">The element to rename.</param>
/// <param name="NewId">The id it should have, which must be an XML name and must be free.</param>
public sealed record RenameElementCommand(string ElementId, string NewId) : BpmnEditCommand
{
    /// <inheritdoc />
    public override string Label => $"Rename {ElementId} to {NewId}";
}

/// <summary>Which connector to create.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<BpmnConnectorKind>))]
public enum BpmnConnectorKind
{
    /// <summary>A Sequence Flow, within one container.</summary>
    [System.Text.Json.Serialization.JsonStringEnumMemberName("sequenceFlow")] SequenceFlow,

    /// <summary>A Message Flow, which always crosses a Pool boundary.</summary>
    [System.Text.Json.Serialization.JsonStringEnumMemberName("messageFlow")] MessageFlow,

    /// <summary>An Association, joining an artifact to a flow element.</summary>
    [System.Text.Json.Serialization.JsonStringEnumMemberName("association")] Association,

    /// <summary>A Data Association, into or out of an Activity.</summary>
    [System.Text.Json.Serialization.JsonStringEnumMemberName("dataAssociation")] DataAssociation,
}

/// <summary>
/// Creates one connector and its DI edge.
/// </summary>
/// <remarks>
/// <para>
/// The live connection matrix (section 5.1) is evaluated on the canvas, against the shared rule
/// table, before the gesture is ever reported — a drag has to be judged at pointer speed and the
/// canvas is where the endpoints are known. This command therefore checks what it can check
/// without duplicating those predicates: that both endpoints exist, that they are not the same
/// element, and that the geometry is usable.
/// </para>
/// <para>
/// Re-implementing the matrix here in C# is exactly what the shared table exists to avoid, and
/// document-scoped rules — the ones that count sibling edges or walk reachability — are the C#
/// side's to run over the whole model rather than per gesture.
/// </para>
/// </remarks>
/// <param name="Id">The new connector's id.</param>
/// <param name="Kind">Which connector to create.</param>
/// <param name="SourceRef">The originating element.</param>
/// <param name="TargetRef">The receiving element.</param>
/// <param name="Waypoints">The route, including both endpoints. At least two (DI-001).</param>
/// <param name="ContainerId">The owning container, for the kinds that are scoped to one.</param>
/// <param name="EdgeId">The DI edge id; derived from the connector id when omitted.</param>
public sealed record CreateConnectorCommand(
    string Id,
    BpmnConnectorKind Kind,
    string SourceRef,
    string TargetRef,
    IReadOnlyList<DiPoint> Waypoints,
    string? ContainerId = null,
    string? EdgeId = null) : BpmnEditCommand
{
    /// <summary>The destination diagram, or the first declared diagram when omitted.</summary>
    public string? DiagramId { get; init; }

    /// <inheritdoc />
    public override string Label => $"Create {Kind}";
}

/// <summary>One standard property to set, by the name its descriptor declares.</summary>
/// <param name="Name">The BPMN attribute name, matching a <see cref="BpmnPropertyDescriptor.Name"/>.</param>
/// <param name="Value">
/// The new value as text, or <see langword="null"/> to remove the attribute. An empty string is
/// distinct from <see langword="null"/> for text, and clears a reference for a choice.
/// </param>
public sealed record BpmnPropertyValue(string Name, string? Value);

/// <summary>
/// Sets standard BPMN properties on one element.
/// </summary>
/// <remarks>
/// <para>
/// Section 7 requires property edits to be "typed, validated, and submitted as commands ... with an
/// atomic result". Typed here means schema-driven rather than one optional field per attribute: the
/// editable set differs per element type — a condition expression on a Sequence Flow, an
/// <c>isExecutable</c> on a Process, a trigger on an Event — and a command with every attribute of
/// every type on it would accept nonsense the descriptors already rule out.
/// </para>
/// <para>
/// So the descriptors produced by <see cref="BpmnElementInspector"/> are the schema. A name that is
/// not editable on this element is refused (EDIT-070), and a value that does not fit its declared
/// kind or is not among its choices is refused (EDIT-071) — before anything is written, so a batch
/// that is wrong in one property changes nothing at all.
/// </para>
/// </remarks>
/// <param name="ElementId">The element to edit.</param>
/// <param name="Values">The properties to set.</param>
public sealed record UpdatePropertiesCommand(string ElementId, IReadOnlyList<BpmnPropertyValue> Values)
    : BpmnEditCommand
{
    /// <summary>Sets a single property.</summary>
    /// <param name="elementId">The element to edit.</param>
    /// <param name="name">The property name.</param>
    /// <param name="value">The new value, or <see langword="null"/> to remove it.</param>
    public UpdatePropertiesCommand(string elementId, string name, string? value)
        : this(elementId, [new BpmnPropertyValue(name, value)])
    {
    }

    /// <inheritdoc />
    public override string Label => Values.Count == 1 ? $"Set {Values[0].Name}" : "Edit properties";
}

/// <summary>
/// Converts one element to another type in the same category (specification section 7).
/// </summary>
/// <remarks>
/// <para>
/// A switch keeps the element: its id, geometry, label, connections, containment, references and DI
/// all survive, which is the whole point of offering it instead of "delete and draw a new one". The
/// eligible sets are narrow for that reason — see <see cref="BpmnSwitchGroup"/> — and anything the
/// target type cannot carry is cleared explicitly and reported (EDIT-082) rather than dropped
/// quietly.
/// </para>
/// <para>
/// Rejected atomically: if the converted element or any connection attached to it would break a
/// rule, nothing changes. That is why the command names a target rather than describing a mutation
/// — the editor has to be able to build the result and judge it before keeping it.
/// </para>
/// </remarks>
/// <param name="ElementId">The element to convert.</param>
/// <param name="Type">The target type. For an Event this is its present type, unchanged.</param>
/// <param name="EventDefinition">The target trigger, for an Event.</param>
public sealed record SwitchElementTypeCommand(
    string ElementId,
    BpmnFlowNodeType Type,
    BpmnEventDefinitionType EventDefinition = BpmnEventDefinitionType.None) : BpmnEditCommand
{
    /// <inheritdoc />
    public override string Label => "Switch type";
}

/// <summary>
/// Creates a Process root element.
/// </summary>
/// <remarks>
/// Its own command because section 9.3 says so: "Pool creation creates a Participant and Pool
/// shape; Process assignment is a separate command." A Pool dropped from the stencil composes the
/// two, which keeps the composition visible instead of hiding a second creation inside the first.
/// A Process has no geometry — the Pool that references it is what gets drawn.
/// </remarks>
/// <param name="Id">The new Process's id.</param>
/// <param name="Name">Its name, when it has one.</param>
/// <param name="IsExecutable">Whether it is marked executable.</param>
public sealed record CreateProcessCommand(string Id, string? Name = null, bool? IsExecutable = null)
    : BpmnEditCommand
{
    /// <inheritdoc />
    public override string Label => "Create Process";
}

/// <summary>
/// Creates a Participant and its Pool shape (specification section 9.1).
/// </summary>
/// <remarks>
/// <para>
/// A Collaboration is required: a Participant is a Collaboration's child, and creating one with no
/// Collaboration to hold it would be creating a Pool that belongs to nothing. A document that is a
/// bare Process has no Collaboration, so the stencil does not offer the tool there — section 9.1
/// puts Pool creation on the Collaboration plane row only.
/// </para>
/// <para>
/// <paramref name="ProcessRef"/> is optional because a black-box Participant is legal: it is a
/// valid Message Flow endpoint with no internal content. When it is supplied it must resolve to a
/// Process that no other Participant already references, since two Pools drawing one Process would
/// make containment ambiguous.
/// </para>
/// </remarks>
/// <param name="Id">The new Participant's id.</param>
/// <param name="Bounds">Where the Pool is drawn.</param>
/// <param name="Name">The Pool label.</param>
/// <param name="ProcessRef">The Process it draws, or <see langword="null"/> for a black-box Pool.</param>
/// <param name="IsHorizontal">The Pool's orientation, which its Lanes must share (DI-006).</param>
/// <param name="ShapeId">The DI shape id; derived from the Participant id when omitted.</param>
public sealed record CreateParticipantCommand(
    string Id,
    Bounds Bounds,
    string? Name = null,
    string? ProcessRef = null,
    bool IsHorizontal = true,
    string? ShapeId = null) : BpmnEditCommand
{
    /// <inheritdoc />
    public override string Label => "Create Pool";

    /// <summary>The destination diagram; omitted, the first diagram is used.</summary>
    public string? DiagramId { get; init; }
}

/// <summary>
/// Creates a Lane inside a Process or Sub-Process (specification section 9.1).
/// </summary>
/// <remarks>
/// <para>
/// Section 9.3: "Lane creation creates/uses a LaneSet and updates membership without changing
/// Process/Sub-Process scope." Both halves matter. The lane set is found or made, because a Process
/// may legitimately have none and one must never be synthesised speculatively; and the new Lane
/// changes nothing about who owns the nodes it now covers, because Lane membership is not
/// ownership (section 2.8).
/// </para>
/// <para>
/// It has no bounds of its own. A Lane is a subdivision, so its geometry is whatever is left once
/// the stack it joins has been re-tiled — asking the caller for a rectangle would invite one that
/// does not tile, and section 1.5.3 requires that the Lanes always add up to exactly their Pool.
/// </para>
/// </remarks>
/// <param name="Id">The new Lane's id.</param>
/// <param name="ContainerId">The Process or Sub-Process whose lane set it joins.</param>
/// <param name="Name">The Lane label.</param>
/// <param name="FlowNodeRefs">
/// Nodes to claim as members. Empty by default: a Lane added to an existing stack claims nothing
/// until something is moved into it, and guessing from geometry would reassign work silently.
/// </param>
/// <param name="LaneSetId">The lane set to join; the container's own, or a new one, when omitted.</param>
/// <param name="ShapeId">The DI shape id; derived from the Lane id when omitted.</param>
public sealed record CreateLaneCommand(
    string Id,
    string ContainerId,
    string? Name = null,
    IReadOnlyList<string>? FlowNodeRefs = null,
    string? LaneSetId = null,
    string? ShapeId = null) : BpmnEditCommand
{
    /// <inheritdoc />
    public override string Label => "Create Lane";

    /// <summary>The destination diagram; omitted, the first diagram is used.</summary>
    public string? DiagramId { get; init; }
}
