using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions;

namespace Hasseware.AspNetCore.Components.Bpmn.Model;

/// <summary>Document-level metadata (specification section 6 <c>definitions</c>).</summary>
/// <param name="Id">Document id.</param>
/// <param name="TargetNamespace">Required BPMN target namespace.</param>
/// <param name="Exporter">Tool that produced the document, when declared.</param>
/// <param name="ExporterVersion">Version of that tool, when declared.</param>
/// <param name="Name">Document name, when declared.</param>
/// <param name="ExpressionLanguage">Default language for condition and assignment expressions.</param>
/// <param name="TypeLanguage">Default language for item structure references.</param>
public sealed record BpmnDefinitions(
    string Id,
    string TargetNamespace,
    string? Exporter = null,
    string? ExporterVersion = null,
    string? Name = null,
    string? ExpressionLanguage = null,
    string? TypeLanguage = null)
{
    /// <summary>
    /// Namespace prefixes declared on the root element, keyed by prefix.
    /// </summary>
    /// <remarks>
    /// Retained so an export can re-declare the document's own prefixes instead of inventing new
    /// ones. Prefixes are cosmetic — every reference resolves by URI (section 10.6) — but a file
    /// that comes back with <c>ns0:</c> everywhere reads as though the editor rewrote it.
    /// </remarks>
    public IReadOnlyDictionary<string, string> NamespaceDeclarations { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Documentation, foreign attributes and vendor extensions on the root element.</summary>
    public BpmnElementExtras Extras { get; init; } = BpmnElementExtras.None;
}

/// <summary>A QName retained with its resolved namespace identity.</summary>
public sealed record BpmnQName(string LocalName, string? NamespaceUri, string LexicalValue);

/// <summary>A Process root element (specification section 6 <c>process</c>).</summary>
/// <param name="Id">Element id.</param>
/// <param name="Name">Display name.</param>
/// <param name="IsExecutable">Whether the Process is marked executable.</param>
/// <param name="ProcessType">One of <c>None</c>, <c>Public</c> or <c>Private</c>, when declared.</param>
/// <param name="IsClosed">Whether interactions outside the Process are disallowed.</param>
public sealed record BpmnProcess(
    string Id,
    string? Name = null,
    bool? IsExecutable = null,
    string? ProcessType = null,
    bool? IsClosed = null)
{
    /// <summary>Documentation, foreign attributes and vendor extensions.</summary>
    public BpmnElementExtras Extras { get; init; } = BpmnElementExtras.None;
}

/// <summary>A Participant, drawn as a Pool (specification section 1.5).</summary>
/// <param name="Id">Participant id.</param>
/// <param name="Name">Pool label.</param>
/// <param name="ProcessRef">
/// The referenced Process, or <see langword="null"/> for a black-box Pool. Optional by schema:
/// a black-box Participant is a valid Message Flow endpoint with no internal content.
/// </param>
public sealed record BpmnParticipant(string Id, string? Name = null, string? ProcessRef = null)
{
    /// <summary>Documentation, foreign attributes and vendor extensions.</summary>
    public BpmnElementExtras Extras { get; init; } = BpmnElementExtras.None;
}

/// <summary>A Collaboration root element and its Participants.</summary>
/// <param name="Id">Collaboration id.</param>
/// <param name="Name">Display name.</param>
/// <param name="Participants">The Participants it declares, in document order.</param>
public sealed record BpmnCollaboration(string Id, string? Name, IReadOnlyList<BpmnParticipant> Participants)
{
    /// <summary>Documentation, foreign attributes and vendor extensions.</summary>
    public BpmnElementExtras Extras { get; init; } = BpmnElementExtras.None;
}

/// <summary>
/// A Lane: organisational grouping over flow nodes that already belong to a Process.
/// </summary>
/// <param name="Id">Lane id.</param>
/// <param name="Name">Lane label.</param>
/// <param name="LaneSetId">The lane set that contains this Lane.</param>
/// <param name="FlowNodeRefs">
/// Lane membership. Membership is projected onto nodes from here and never stored on the node,
/// so a node cannot claim a Lane that does not claim it back.
/// </param>
/// <param name="ChildLaneSetId">A nested lane set, when this Lane subdivides further.</param>
public sealed record BpmnLane(
    string Id,
    string? Name,
    string LaneSetId,
    IReadOnlyList<string> FlowNodeRefs,
    string? ChildLaneSetId = null)
{
    /// <summary>Documentation, foreign attributes and vendor extensions.</summary>
    public BpmnElementExtras Extras { get; init; } = BpmnElementExtras.None;
}

/// <summary>
/// A lane set. Cardinality is 0..* — a Process or Pool with no Lanes at all is valid and must
/// never have one synthesised for it (specification section 9.1).
/// </summary>
/// <param name="Id">Lane set id.</param>
/// <param name="ContainerId">The Process or Sub-Process that owns the lane set.</param>
/// <param name="LaneIds">Lanes in declaration order.</param>
/// <param name="IsSynthetic">
/// True when the document declared the lane set without an id and the reader named it, so an
/// export can leave the id off again rather than writing one the author never had.
/// </param>
public sealed record BpmnLaneSet(
    string Id,
    string ContainerId,
    IReadOnlyList<string> LaneIds,
    bool IsSynthetic = false);

/// <summary>
/// One <c>*EventDefinition</c> child of an Event, with the reference or expression it carries.
/// </summary>
/// <remarks>
/// <para>
/// Distinct from <see cref="BpmnFlowNode.EventDefinition"/>, which is the single enum the notation
/// branches on. That enum collapses several triggers into <c>Multiple</c> and keeps no references
/// at all — enough to draw the glyph, not enough to write the Event back out. This record is the
/// interchange side: it is what makes an <c>errorRef</c> still resolve after an export
/// (section 10.6).
/// </para>
/// <para>
/// One record covers every trigger rather than a hierarchy, because the fields are mutually
/// exclusive in practice and a closed hierarchy would have to grow a case for each vendor's
/// variations on timers.
/// </para>
/// </remarks>
/// <param name="Type">Which trigger this definition declares.</param>
/// <param name="Id">The definition element's own id, when declared.</param>
/// <param name="Ref">
/// The global definition referenced: <c>messageRef</c>, <c>errorRef</c>, <c>signalRef</c> or
/// <c>escalationRef</c>, whichever the trigger uses.
/// </param>
/// <param name="Expression">A timer or condition expression body, when declared.</param>
/// <param name="ExpressionKind">
/// Which timer expression it was: <c>timeDate</c>, <c>timeDuration</c> or <c>timeCycle</c>. Kept
/// because the three mean different things and a timer rewritten as the wrong one changes the model.
/// </param>
/// <param name="Name">The link name, for paired Link Events.</param>
/// <param name="ActivityRef">The compensated Activity, for compensation definitions.</param>
/// <param name="WaitForCompletion">Whether compensation waits, when declared.</param>
public sealed record BpmnEventDefinition(
    BpmnEventDefinitionType Type,
    string? Id = null,
    string? Ref = null,
    string? Expression = null,
    string? ExpressionKind = null,
    string? Name = null,
    string? ActivityRef = null,
    bool? WaitForCompletion = null)
{
    /// <summary>Documentation, foreign attributes and vendor extensions.</summary>
    public BpmnElementExtras Extras { get; init; } = BpmnElementExtras.None;
}

/// <summary>An expression body and its declared language/type metadata.</summary>
public sealed record BpmnFormalExpression(
    string Body,
    string? Language = null,
    string? TypeQName = null)
{
    /// <summary>The namespace URI resolved from the <c>xsi:type</c> QName, when imported.</summary>
    public string? TypeNamespaceUri { get; init; }
}

/// <summary>The fields of <c>standardLoopCharacteristics</c>.</summary>
public sealed record BpmnStandardLoopDetails(
    bool? TestBefore = null,
    BpmnFormalExpression? LoopCondition = null,
    int? LoopMaximum = null);

/// <summary>One data item declared inside a multi-instance loop.</summary>
public sealed record BpmnLoopDataItem(
    string? Id = null,
    string? Name = null,
    string? ItemSubjectRef = null,
    bool? IsCollection = null);

/// <summary>One Complex Behavior Definition within a multi-instance loop.</summary>
public sealed record BpmnComplexBehaviorDefinition(
    string? Event = null,
    BpmnFormalExpression? Condition = null);

/// <summary>The fields of <c>multiInstanceLoopCharacteristics</c>.</summary>
public sealed record BpmnMultiInstanceLoopDetails(
    bool? IsSequential = null,
    string? Behavior = null,
    BpmnFormalExpression? LoopCardinality = null,
    string? LoopDataInputRef = null,
    BpmnLoopDataItem? InputDataItem = null,
    BpmnFormalExpression? CompletionCondition = null,
    string? LoopDataOutputRef = null,
    BpmnLoopDataItem? OutputDataItem = null,
    IReadOnlyList<BpmnComplexBehaviorDefinition>? ComplexBehaviorDefinitions = null)
{
    /// <summary>Vendor extensions attached to <c>multiInstanceLoopCharacteristics</c>.</summary>
    public BpmnElementExtras Extras { get; init; } = BpmnElementExtras.None;
}

/// <summary>
/// A drawable flow element: Event, Activity or Gateway (specification section 6 <c>flowNode</c>).
/// </summary>
/// <param name="Id">Element id, unique within the document.</param>
/// <param name="Type">The element type, which decides its notation.</param>
/// <param name="ContainerId">
/// The Process or embedded Sub-Process that owns this node. This is the scope Sequence Flows may
/// not cross (SF-007) and the containment the renderer derives its visual tree from.
/// </param>
/// <param name="Name">Label text.</param>
/// <param name="EventDefinition">Trigger type; <see cref="BpmnEventDefinitionType.None"/> for non-events.</param>
/// <param name="IsInterrupting">
/// Boundary Events and Event Sub-Process Start Events only. Non-interrupting events dash their
/// rings; the flag is meaningless elsewhere and defaults to true.
/// </param>
/// <param name="AttachedToRef">The host Activity, for Boundary Events only.</param>
/// <param name="PoolId">The Participant whose Pool draws this node, in a collaboration.</param>
/// <param name="LaneId">Convenience projection of Lane membership; the Lane remains authoritative.</param>
/// <param name="IsForCompensation">Whether this Activity is a compensation handler.</param>
/// <param name="LoopCharacteristics">Loop or multi-instance marker.</param>
/// <param name="Instantiate">Whether the element instantiates its Process when triggered.</param>
/// <param name="CalledElementRef">
/// The referenced global Process or Task, for Call Activities. Mutually exclusive with
/// <paramref name="FlowElementsOwned"/> by construction (section 6 note).
/// </param>
/// <param name="FlowElementsOwned">True for embedded Sub-Process kinds, which own local content.</param>
/// <param name="DefaultFlowRef">The outgoing Sequence Flow marked as this node's default.</param>
public sealed record BpmnFlowNode(
    string Id,
    BpmnFlowNodeType Type,
    string ContainerId,
    string? Name = null,
    BpmnEventDefinitionType EventDefinition = BpmnEventDefinitionType.None,
    bool IsInterrupting = true,
    string? AttachedToRef = null,
    string? PoolId = null,
    string? LaneId = null,
    bool IsForCompensation = false,
    BpmnLoopCharacteristics LoopCharacteristics = BpmnLoopCharacteristics.None,
    bool Instantiate = false,
    string? CalledElementRef = null,
    bool FlowElementsOwned = false,
    string? DefaultFlowRef = null)
{
    /// <summary>The namespace-aware Call Activity reference as imported from XML.</summary>
    public BpmnQName? CalledElementQName { get; init; }

    /// <summary>
    /// The Message a Send Task sends or a Receive Task waits for (<c>messageRef</c>).
    /// </summary>
    /// <remarks>Only meaningful on <see cref="BpmnFlowNodeType.SendTask"/> and <see cref="BpmnFlowNodeType.ReceiveTask"/>.</remarks>
    public string? MessageRef { get; init; }

    /// <summary>
    /// The language a Script Task's script is written in (<c>scriptFormat</c>): a name or media type
    /// the engine understands, such as <c>csharp</c>, <c>javascript</c> or <c>lua</c>.
    /// </summary>
    /// <remarks>Only meaningful on <see cref="BpmnFlowNodeType.ScriptTask"/>.</remarks>
    public string? ScriptFormat { get; init; }

    /// <summary>
    /// The text of a Script Task's <c>script</c> child, which is code the engine will run.
    /// </summary>
    /// <remarks>Only meaningful on <see cref="BpmnFlowNodeType.ScriptTask"/>.</remarks>
    public string? Script { get; init; }

    /// <summary>Full standard-loop attributes and expression values.</summary>
    public BpmnStandardLoopDetails? StandardLoop { get; init; }

    /// <summary>Full multi-instance attributes, data bindings and expressions.</summary>
    public BpmnMultiInstanceLoopDetails? MultiInstanceLoop { get; init; }

    /// <summary>The activation condition of a Complex Gateway.</summary>
    public BpmnFormalExpression? ActivationCondition { get; init; }

    /// <summary>
    /// The Event's <c>*EventDefinition</c> children with their references, in document order.
    /// </summary>
    /// <remarks>
    /// Empty for everything that is not an Event, and for a None Event. Notation never reads this;
    /// it reads <see cref="EventDefinition"/>.
    /// </remarks>
    public IReadOnlyList<BpmnEventDefinition> EventDefinitions { get; init; } = [];

    /// <summary>Documentation, foreign attributes and vendor extensions preserved verbatim.</summary>
    public BpmnElementExtras Extras { get; init; } = BpmnElementExtras.None;
}

/// <summary>A Text Annotation (specification section 1.6).</summary>
/// <param name="Id">Element id.</param>
/// <param name="Text">The annotation body.</param>
/// <param name="ContainerId">The Process, Sub-Process or Collaboration that owns it.</param>
/// <param name="TextFormat">The declared MIME type of <paramref name="Text"/>, when stated.</param>
public sealed record BpmnTextAnnotation(string Id, string Text, string ContainerId, string? TextFormat = null)
{
    /// <summary>Documentation, foreign attributes and vendor extensions.</summary>
    public BpmnElementExtras Extras { get; init; } = BpmnElementExtras.None;
}

/// <summary>
/// A Group: visual categorisation only.
/// </summary>
/// <remarks>
/// A Group never owns the elements it encloses and is never a containment parent (CT-003).
/// Moving, resizing or deleting one changes nothing but the Group itself.
/// </remarks>
/// <param name="Id">Element id.</param>
/// <param name="ContainerId">The Process, Sub-Process or Collaboration that owns it.</param>
/// <param name="Name">Display name, when the document states one directly.</param>
/// <param name="CategoryValueRef">The Category Value whose text the Group draws.</param>
public sealed record BpmnGroup(string Id, string ContainerId, string? Name = null, string? CategoryValueRef = null)
{
    /// <summary>Documentation, foreign attributes and vendor extensions.</summary>
    public BpmnElementExtras Extras { get; init; } = BpmnElementExtras.None;
}

/// <summary>
/// A Data Object Reference: the drawn shape, distinct from the document-scoped Data Object
/// definition it points at (specification section 9.1).
/// </summary>
/// <param name="Id">Element id.</param>
/// <param name="ContainerId">The Process or Sub-Process that owns it.</param>
/// <param name="Name">Display name.</param>
/// <param name="DataObjectRef">The Data Object definition this reference draws.</param>
/// <param name="State">The data state label, when declared.</param>
/// <param name="IsCollection">
/// Whether the referenced object is a collection. Projected from the Data Object definition when
/// the reference itself does not say, because the collection marker is drawn from the reference.
/// </param>
public sealed record BpmnDataObjectReference(
    string Id,
    string ContainerId,
    string? Name = null,
    string? DataObjectRef = null,
    string? State = null,
    bool IsCollection = false)
{
    /// <summary>Documentation, foreign attributes and vendor extensions.</summary>
    public BpmnElementExtras Extras { get; init; } = BpmnElementExtras.None;
}

/// <summary>A Data Store Reference, pointing at a document-scoped Data Store.</summary>
/// <param name="Id">Element id.</param>
/// <param name="ContainerId">The Process or Sub-Process that owns it.</param>
/// <param name="Name">Display name.</param>
/// <param name="DataStoreRef">The Data Store definition this reference draws.</param>
/// <param name="State">The data state label, when declared.</param>
public sealed record BpmnDataStoreReference(
    string Id,
    string ContainerId,
    string? Name = null,
    string? DataStoreRef = null,
    string? State = null)
{
    /// <summary>Documentation, foreign attributes and vendor extensions.</summary>
    public BpmnElementExtras Extras { get; init; } = BpmnElementExtras.None;
}
