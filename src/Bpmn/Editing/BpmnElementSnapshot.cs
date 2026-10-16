using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;

namespace Hasseware.AspNetCore.Components.Bpmn.Editing;

/// <summary>
/// The broad family an element belongs to (specification section 7, "element ID/type/category").
/// </summary>
/// <remarks>
/// Coarser than <see cref="BpmnFlowNodeType"/> on purpose: this is what a menu groups by and what
/// section 7's "same element category" rule is stated in terms of. It is not, on its own, the
/// switching rule — see <see cref="BpmnSwitchGroup"/>, which is narrower.
/// </remarks>
public enum BpmnElementCategory
{
    /// <summary>Start, End, Intermediate or Boundary Event.</summary>
    Event,

    /// <summary>Task, Sub-Process kind or Call Activity.</summary>
    Activity,

    /// <summary>Any Gateway.</summary>
    Gateway,

    /// <summary>Sequence Flow, Message Flow, Association or Data Association.</summary>
    Connector,

    /// <summary>Text Annotation or Group.</summary>
    Artifact,

    /// <summary>A Data Object or Data Store Reference.</summary>
    Data,

    /// <summary>A Participant, drawn as a Pool.</summary>
    Pool,

    /// <summary>A Lane.</summary>
    Lane,

    /// <summary>A Process root element — the plane itself.</summary>
    Process,
}

/// <summary>
/// The set of types one element may be converted between (specification section 7).
/// </summary>
/// <remarks>
/// <para>
/// Deliberately narrower than <see cref="BpmnElementCategory"/>. "Activity" holds Tasks,
/// Sub-Processes and Call Activities, but a Task has no interior and a Call Activity's whole
/// identity is the reference it carries — converting between them is not a type switch, it is a
/// different element with the same id. So the switchable sets are the ones where everything
/// section 7 promises to preserve (geometry, label, connections, containment, references, DI)
/// actually survives.
/// </para>
/// <para>
/// Events switch their trigger rather than their type: section 7's own example is None Start Event
/// ↔ Message Start Event, and it forbids Start ↔ End. Catch ↔ throw is excluded for the same
/// reason a Task is not a Gateway — the valid trigger set differs and the direction of the element
/// changes.
/// </para>
/// </remarks>
public enum BpmnSwitchGroup
{
    /// <summary>Not switchable.</summary>
    None,

    /// <summary>The eight Task kinds.</summary>
    Task,

    /// <summary>Exclusive, Inclusive, Parallel, Complex and Event-Based Gateways.</summary>
    Gateway,

    /// <summary>Embedded Sub-Process, Transaction and Ad-Hoc Sub-Process.</summary>
    EmbeddedSubProcess,

    /// <summary>An Event: the type is fixed and the trigger varies.</summary>
    EventTrigger,
}

/// <summary>How a standard property is edited.</summary>
public enum BpmnPropertyKind
{
    /// <summary>A single line of text.</summary>
    Text,

    /// <summary>Text that may contain line breaks.</summary>
    MultilineText,

    /// <summary>A checkbox.</summary>
    Boolean,

    /// <summary>One of <see cref="BpmnPropertyDescriptor.Choices"/>.</summary>
    Choice,
}

/// <summary>One option of a <see cref="BpmnPropertyKind.Choice"/> property.</summary>
/// <param name="Value">The value submitted in an <see cref="UpdatePropertiesCommand"/>.</param>
/// <param name="Label">What to show for it.</param>
public sealed record BpmnPropertyChoice(string Value, string Label)
{
    /// <summary>The resource identifying a built-in caption; absent for document and host labels.</summary>
    public string? LabelResourceKey { get; init; }
}

/// <summary>
/// One standard BPMN property of an element: what it is, what it currently holds, and how to edit it.
/// </summary>
/// <remarks>
/// Section 7 requires the snapshot to carry "the list of editable standard properties" and to be
/// "sufficient for <c>BpmnContextMenu</c> to display and bind standard element properties without
/// re-querying or inferring them". A descriptor carries its own kind and choices so the surface can
/// render a property it has never heard of, and the same descriptor is what
/// <see cref="BpmnDocumentEditor"/> validates a submitted value against — the type of a property is
/// declared in exactly one place.
/// </remarks>
/// <param name="Name">
/// The BPMN attribute name, which is also the key in an <see cref="UpdatePropertiesCommand"/>.
/// </param>
/// <param name="Label">A display name for the property.</param>
/// <param name="Kind">How it is edited.</param>
/// <param name="Value">
/// The current value as text: <c>"true"</c>/<c>"false"</c> for a boolean, the choice value for a
/// choice, and <see langword="null"/> when the attribute is absent — which is not the same as an
/// empty string, and the distinction survives an edit.
/// </param>
/// <param name="IsReadOnly">
/// True for a property that is shown but not bound: the element id, and everything in Readonly mode.
/// </param>
/// <param name="Choices">The options, for a choice property; empty otherwise.</param>
public sealed record BpmnPropertyDescriptor(
    string Name,
    string Label,
    BpmnPropertyKind Kind,
    string? Value,
    bool IsReadOnly = false,
    IReadOnlyList<BpmnPropertyChoice>? Choices = null)
{
    /// <summary>The owning BPMN concept; unclassified descriptors remain in General.</summary>
    public BpmnPropertyGroupKind Group { get; init; } = BpmnPropertyGroupKind.General;

    /// <summary>The options, for a choice property.</summary>
    public IReadOnlyList<BpmnPropertyChoice> Choices { get; init; } = Choices ?? [];
}

/// <summary>
/// A reference the element carries, resolved to a display name where the target exists.
/// </summary>
/// <remarks>
/// Section 7 asks for "relevant references" in the snapshot. Resolution happens here, once, because
/// a menu that resolves <c>errorRef</c> by walking the document is a menu that has to be given the
/// document — which is exactly what the snapshot exists to avoid.
/// </remarks>
/// <param name="Name">The BPMN attribute, for example <c>sourceRef</c> or <c>calledElementRef</c>.</param>
/// <param name="TargetId">The referenced id.</param>
/// <param name="TargetName">The target's name, or <see langword="null"/> if it is unnamed.</param>
/// <param name="IsResolved">False when nothing in the document has that id (a dangling reference).</param>
public sealed record BpmnElementReference(string Name, string TargetId, string? TargetName, bool IsResolved);

/// <summary>
/// One type an element may be switched to (specification section 7, "Switch type").
/// </summary>
/// <param name="Type">The target flow node type.</param>
/// <param name="EventDefinition">The target trigger, for an Event.</param>
/// <param name="Label">What to show in the menu.</param>
/// <param name="IsCurrent">True for the element's present type, which the menu marks rather than hides.</param>
public sealed record BpmnTypeOption(
    BpmnFlowNodeType Type,
    BpmnEventDefinitionType EventDefinition,
    string Label,
    bool IsCurrent);

/// <summary>
/// What may be done to the element (specification section 7, "capability flags").
/// </summary>
/// <remarks>
/// Every flag is already false in Readonly mode, so a surface does not have to consult the editor
/// configuration as well as the snapshot — and cannot get the two out of step.
/// </remarks>
/// <param name="CanEditLabel">Whether the label may be edited in place (section 4.5).</param>
/// <param name="CanEditProperties">Whether property binding is offered at all.</param>
/// <param name="CanSwitchType">Whether <see cref="SwitchElementTypeCommand"/> applies here.</param>
/// <param name="CanDelete">Whether the element may be deleted.</param>
/// <param name="CanResize">Whether the shape has resize handles.</param>
/// <param name="CanReparent">Whether dragging it elsewhere changes its owner.</param>
/// <param name="CanConnect">Whether a connector may start at it.</param>
public sealed record BpmnElementCapabilities(
    bool CanEditLabel,
    bool CanEditProperties,
    bool CanSwitchType,
    bool CanDelete,
    bool CanResize,
    bool CanReparent,
    bool CanConnect)
{
    /// <summary>Nothing is permitted: the Readonly answer, and the answer for an unknown element.</summary>
    public static readonly BpmnElementCapabilities None =
        new(false, false, false, false, false, false, false);
}

/// <summary>
/// One piece of preserved extension content, identified but not read.
/// </summary>
/// <param name="Namespace">The namespace URI that gives it its identity (section 6.1.2).</param>
/// <param name="Name">Its local name.</param>
public sealed record BpmnExtensionSummary(string Namespace, string Name);

/// <summary>
/// Everything a context menu, property surface or inline editor needs about one element
/// (specification section 7).
/// </summary>
/// <remarks>
/// <para>
/// Versioned, typed, and complete: section 7 requires it to be "sufficient for
/// <c>BpmnContextMenu</c> to display and bind standard element properties without re-querying or
/// inferring them from SVG/JointJS cells". Anything the menu would otherwise have to go and look up
/// — the owner, the Pool and Lane scope, the DI bounds, the resolved references, the diagnostics —
/// is resolved here against the document, which is the only place that can answer correctly.
/// </para>
/// <para>
/// Extension values never appear as standard fields, also per section 7. They are reachable through
/// the element's own <c>Extras</c>, and a surface that wants to show them must say that is what it
/// is showing.
/// </para>
/// </remarks>
public sealed record BpmnElementSnapshot
{
    /// <summary>
    /// The snapshot's schema version.
    /// </summary>
    /// <remarks>
    /// Separate from the interop contract version: the contract governs the shape of the
    /// canvas↔Blazor calls, while this governs the shape of one payload inside them. A surface that
    /// binds properties can refuse a version it does not understand without the whole canvas having
    /// to be renegotiated.
    /// </remarks>
    public const int CurrentVersion = 2;

    /// <summary>The schema version of this snapshot.</summary>
    public int Version { get; init; } = CurrentVersion;

    /// <summary>The element's id.</summary>
    public required string ElementId { get; init; }

    /// <summary>The BPMN element name, as the wire contract spells it — <c>serviceTask</c>, <c>sequenceFlow</c>.</summary>
    public required string ElementType { get; init; }

    /// <summary>The family it belongs to.</summary>
    public required BpmnElementCategory Category { get; init; }

    /// <summary>The semantic owner: the Process, Sub-Process or Collaboration that contains it.</summary>
    public string? ContainerId { get; init; }

    /// <summary>The Participant whose Pool draws it, in a collaboration.</summary>
    public string? PoolId { get; init; }

    /// <summary>
    /// The Lane it belongs to.
    /// </summary>
    /// <remarks>
    /// Read from the Lane's <c>flowNodeRefs</c>, which is authoritative, and not from the node's own
    /// convenience projection — membership is the Lane's statement about the node (section 2.8).
    /// </remarks>
    public string? LaneId { get; init; }

    /// <summary>The text drawn as the element's label, when it has one.</summary>
    public string? Label { get; init; }

    /// <summary>The standard properties, in display order.</summary>
    public IReadOnlyList<BpmnPropertyDescriptor> Properties { get; init; } = [];

    /// <summary>
    /// The Event's trigger definitions with their references, in document order.
    /// </summary>
    /// <remarks>
    /// Section 7 lists "event definitions" separately from standard attributes because they are
    /// separate: the trigger enum decides the glyph, while these carry the <c>messageRef</c>,
    /// timer expression or link name that makes the Event mean something.
    /// </remarks>
    public IReadOnlyList<BpmnEventDefinition> EventDefinitions { get; init; } = [];

    /// <summary>The references it carries, resolved.</summary>
    public IReadOnlyList<BpmnElementReference> References { get; init; } = [];

    /// <summary>
    /// What vendor extension content the element carries, named but not interpreted.
    /// </summary>
    /// <remarks>
    /// Names only, deliberately. Section 7 forbids presenting extension content as a standard
    /// field and section 6.1 keeps it opaque until an adapter claims it — but a surface that shows
    /// nothing at all leaves no way to tell a configured element from a bare one, and no warning
    /// that editing it carries something the editor does not understand.
    /// </remarks>
    public IReadOnlyList<BpmnExtensionSummary> Extensions { get; init; } = [];

    /// <summary>
    /// Every value inside that extension content, addressed by path and editable as text.
    /// </summary>
    /// <remarks>
    /// A separate list from <see cref="Properties"/>, and that separation is the point. Section 7
    /// forbids extension content appearing as a standard field; two lists make that structural
    /// rather than a convention a surface has to remember. Every name carries an
    /// <c>extension:</c> prefix as well, so the two can never collide.
    /// </remarks>
    public IReadOnlyList<BpmnPropertyDescriptor> ExtensionProperties { get; init; } = [];

    /// <summary>Persisted applied-template id, including unresolved templates.</summary>
    public string? TemplateId { get; init; }
    /// <summary>Persisted applied-template version.</summary>
    public int? TemplateVersion { get; init; }

    /// <summary>Schema-interpreted vendor property groups, computed before the snapshot reaches a surface.</summary>
    public IReadOnlyList<BpmnPropertyGroup> ExtensionGroups { get; init; } = [];

    /// <summary>The current DI bounds, for a shape.</summary>
    public Bounds? Bounds { get; init; }

    /// <summary>The route, for a connector.</summary>
    public IReadOnlyList<DiPoint> Waypoints { get; init; } = [];

    /// <summary>Explicit label bounds, when the document supplies them.</summary>
    public Bounds? LabelBounds { get; init; }

    /// <summary>What may be done to it.</summary>
    public BpmnElementCapabilities Capabilities { get; init; } = BpmnElementCapabilities.None;

    /// <summary>The validation findings that name this element.</summary>
    public IReadOnlyList<BpmnEditDiagnostic> Diagnostics { get; init; } = [];

    /// <summary>The types it may be switched to, including its own; empty when it may not be switched.</summary>
    public IReadOnlyList<BpmnTypeOption> SwitchableTypes { get; init; } = [];
}
