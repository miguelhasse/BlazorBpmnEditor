using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions;

namespace Hasseware.AspNetCore.Components.Bpmn.Model;

/// <summary>A Message definition, referenced by Message Flows and message event definitions.</summary>
/// <param name="Id">Element id.</param>
/// <param name="Name">Display name.</param>
/// <param name="ItemRef">The Item Definition describing the payload.</param>
public sealed record BpmnMessage(string Id, string? Name = null, string? ItemRef = null)
{
    /// <summary>Documentation, foreign attributes and vendor extensions.</summary>
    public BpmnElementExtras Extras { get; init; } = BpmnElementExtras.None;
}

/// <summary>A Signal definition, referenced by signal event definitions.</summary>
/// <param name="Id">Element id.</param>
/// <param name="Name">Display name.</param>
/// <param name="StructureRef">The Item Definition describing the payload.</param>
public sealed record BpmnSignal(string Id, string? Name = null, string? StructureRef = null)
{
    /// <summary>Documentation, foreign attributes and vendor extensions.</summary>
    public BpmnElementExtras Extras { get; init; } = BpmnElementExtras.None;
}

/// <summary>An Error definition, referenced by error event definitions.</summary>
/// <param name="Id">Element id.</param>
/// <param name="Name">Display name.</param>
/// <param name="ErrorCode">The code thrown and caught.</param>
/// <param name="StructureRef">The Item Definition describing the payload.</param>
public sealed record BpmnError(string Id, string? Name = null, string? ErrorCode = null, string? StructureRef = null)
{
    /// <summary>Documentation, foreign attributes and vendor extensions.</summary>
    public BpmnElementExtras Extras { get; init; } = BpmnElementExtras.None;
}

/// <summary>An Escalation definition, referenced by escalation event definitions.</summary>
/// <param name="Id">Element id.</param>
/// <param name="Name">Display name.</param>
/// <param name="EscalationCode">The code thrown and caught.</param>
/// <param name="StructureRef">The Item Definition describing the payload.</param>
public sealed record BpmnEscalation(
    string Id,
    string? Name = null,
    string? EscalationCode = null,
    string? StructureRef = null)
{
    /// <summary>Documentation, foreign attributes and vendor extensions.</summary>
    public BpmnElementExtras Extras { get; init; } = BpmnElementExtras.None;
}

/// <summary>An Item Definition: the type of a payload carried by a Message, Signal or Data Object.</summary>
/// <param name="Id">Element id.</param>
/// <param name="StructureRef">The external type this item stands for.</param>
/// <param name="IsCollection">Whether the item is a collection.</param>
/// <param name="ItemKind">Either <c>Information</c> or <c>Physical</c>.</param>
public sealed record BpmnItemDefinition(
    string Id,
    string? StructureRef = null,
    bool IsCollection = false,
    string? ItemKind = null);

/// <summary>
/// A document-scoped Data Store definition, distinct from the Data Store Reference drawn on a plane.
/// </summary>
/// <param name="Id">Element id.</param>
/// <param name="Name">Display name.</param>
/// <param name="Capacity">Declared capacity, when the document states one.</param>
/// <param name="IsUnlimited">Whether the store is unbounded.</param>
/// <param name="ItemSubjectRef">The Item Definition describing what it holds.</param>
public sealed record BpmnDataStore(
    string Id,
    string? Name = null,
    string? Capacity = null,
    bool? IsUnlimited = null,
    string? ItemSubjectRef = null);

/// <summary>One value of a Category, referenced by Groups through <c>categoryValueRef</c>.</summary>
/// <param name="Id">Element id.</param>
/// <param name="Value">The text a Group draws as its label.</param>
public sealed record BpmnCategoryValue(string Id, string? Value = null);

/// <summary>A Category: the named set a Group's label is drawn from (specification section 1.6).</summary>
/// <param name="Id">Element id.</param>
/// <param name="Name">Category name.</param>
/// <param name="Values">The values this category declares, in document order.</param>
public sealed record BpmnCategory(string Id, string? Name, IReadOnlyList<BpmnCategoryValue> Values);

/// <summary>A Resource definition, referenced by resource assignments on Activities.</summary>
/// <param name="Id">Element id.</param>
/// <param name="Name">Display name.</param>
public sealed record BpmnResource(string Id, string? Name = null);

/// <summary>An Operation declared by an Interface.</summary>
/// <param name="Id">Element id.</param>
/// <param name="Name">Display name.</param>
/// <param name="InMessageRef">The Message this operation receives.</param>
/// <param name="OutMessageRef">The Message this operation returns.</param>
/// <param name="ImplementationRef">The concrete implementation, when declared.</param>
public sealed record BpmnOperation(
    string Id,
    string? Name = null,
    string? InMessageRef = null,
    string? OutMessageRef = null,
    string? ImplementationRef = null);

/// <summary>An Interface: the operations a Service, Send or Receive Task may be bound to.</summary>
/// <param name="Id">Element id.</param>
/// <param name="Name">Display name.</param>
/// <param name="ImplementationRef">The concrete implementation, when declared.</param>
/// <param name="Operations">The operations it declares, in document order.</param>
public sealed record BpmnInterface(
    string Id,
    string? Name,
    string? ImplementationRef,
    IReadOnlyList<BpmnOperation> Operations);

/// <summary>
/// A global Task definition, referenced by a Call Activity.
/// </summary>
/// <remarks>
/// A Call Activity's <c>calledElement</c> resolves either to a <see cref="BpmnProcess"/> or to one
/// of these. The kind is kept as the source element's local name rather than mapped onto
/// <see cref="BpmnFlowNodeType"/>, because a global Task is a definition and never drawn.
/// </remarks>
/// <param name="Id">Element id.</param>
/// <param name="Kind">The declaring element's local name, for example <c>globalUserTask</c>.</param>
/// <param name="Name">Display name.</param>
public sealed record BpmnGlobalTask(string Id, string Kind, string? Name = null);

/// <summary>A document-level import of an external definition (specification section 1.0).</summary>
/// <param name="ImportType">The namespace URI of the imported document's schema language.</param>
/// <param name="Location">Where the imported document lives.</param>
/// <param name="Namespace">The namespace the import contributes.</param>
public sealed record BpmnImport(string? ImportType = null, string? Location = null, string? Namespace = null);

/// <summary>A document-level relationship between this document and external artifacts.</summary>
/// <param name="Id">Element id, when declared.</param>
/// <param name="Type">The relationship type.</param>
/// <param name="Direction">One of <c>None</c>, <c>Forward</c>, <c>Backward</c> or <c>Both</c>.</param>
/// <param name="Sources">Referenced source elements.</param>
/// <param name="Targets">Referenced target elements.</param>
public sealed record BpmnRelationship(
    string? Id,
    string? Type,
    string? Direction,
    IReadOnlyList<string> Sources,
    IReadOnlyList<string> Targets);

/// <summary>
/// A document-level extension declaration (specification section 6.1).
/// </summary>
/// <param name="Id">Element id, when declared.</param>
/// <param name="Definition">The extension definition this declaration binds.</param>
/// <param name="MustUnderstand">Whether a consumer that cannot interpret it should refuse the document.</param>
/// <param name="Content">The declaration's own XML, preserved verbatim.</param>
public sealed record BpmnExtensionDeclaration(
    string? Id,
    string? Definition,
    bool MustUnderstand,
    IReadOnlyList<BpmnExtensionElement> Content);

/// <summary>
/// A document-scoped Data Object definition, distinct from the References that draw it.
/// </summary>
/// <remarks>
/// BPMN separates the definition from the drawn shape so several References on different planes
/// can point at one object. The editor keeps both: the Reference carries the DI, the definition
/// carries the payload type.
/// </remarks>
/// <param name="Id">Element id.</param>
/// <param name="ContainerId">The Process or Sub-Process that declares it.</param>
/// <param name="Name">Display name.</param>
/// <param name="IsCollection">Whether the object is a collection, which draws the collection marker.</param>
/// <param name="ItemSubjectRef">The Item Definition describing its payload.</param>
public sealed record BpmnDataObject(
    string Id,
    string ContainerId,
    string? Name = null,
    bool IsCollection = false,
    string? ItemSubjectRef = null)
{
    /// <summary>Documentation, foreign attributes and vendor extensions.</summary>
    public BpmnElementExtras Extras { get; init; } = BpmnElementExtras.None;
}

/// <summary>
/// The document-scoped definitions that are referenced but never drawn (specification section 1.0).
/// </summary>
/// <remarks>
/// <para>
/// These have no shape of their own, yet dropping them breaks the references that do: an error
/// Boundary Event whose <c>errorRef</c> no longer resolves, a Message Flow that has lost its
/// <c>messageRef</c>, a Group whose <c>categoryValueRef</c> points at nothing. Section 1.0 makes
/// owning them an explicit editor responsibility, and the round-trip suite asserts every
/// reference still resolves after an export.
/// </para>
/// <para>
/// Everything at <c>definitions</c> level that this catalog does not type — including the
/// Choreography and Conversation constructs excluded by section 8 — is kept verbatim in
/// <see cref="Preserved"/> instead of being dropped, so an export can put it back exactly.
/// </para>
/// </remarks>
public sealed record BpmnRootCatalog
{
    /// <summary>An empty catalog, used by documents that declare no global definitions.</summary>
    public static readonly BpmnRootCatalog Empty = new();

    /// <summary>Message definitions.</summary>
    public IReadOnlyList<BpmnMessage> Messages { get; init; } = [];

    /// <summary>Signal definitions.</summary>
    public IReadOnlyList<BpmnSignal> Signals { get; init; } = [];

    /// <summary>Error definitions.</summary>
    public IReadOnlyList<BpmnError> Errors { get; init; } = [];

    /// <summary>Escalation definitions.</summary>
    public IReadOnlyList<BpmnEscalation> Escalations { get; init; } = [];

    /// <summary>Item Definitions.</summary>
    public IReadOnlyList<BpmnItemDefinition> ItemDefinitions { get; init; } = [];

    /// <summary>Data Store definitions.</summary>
    public IReadOnlyList<BpmnDataStore> DataStores { get; init; } = [];

    /// <summary>Categories and their values.</summary>
    public IReadOnlyList<BpmnCategory> Categories { get; init; } = [];

    /// <summary>Resource definitions.</summary>
    public IReadOnlyList<BpmnResource> Resources { get; init; } = [];

    /// <summary>Interfaces and their operations.</summary>
    public IReadOnlyList<BpmnInterface> Interfaces { get; init; } = [];

    /// <summary>Global Task definitions a Call Activity may reference.</summary>
    public IReadOnlyList<BpmnGlobalTask> GlobalTasks { get; init; } = [];

    /// <summary>Document-level imports.</summary>
    public IReadOnlyList<BpmnImport> Imports { get; init; } = [];

    /// <summary>Document-level relationships.</summary>
    public IReadOnlyList<BpmnRelationship> Relationships { get; init; } = [];

    /// <summary>Document-level extension declarations.</summary>
    public IReadOnlyList<BpmnExtensionDeclaration> ExtensionDeclarations { get; init; } = [];

    /// <summary>
    /// Root children this catalog does not model, kept verbatim so an export restores them exactly.
    /// </summary>
    public IReadOnlyList<BpmnExtensionElement> Preserved { get; init; } = [];

    /// <summary>True when the document declared none of these.</summary>
    public bool IsEmpty =>
        Messages.Count == 0
        && Signals.Count == 0
        && Errors.Count == 0
        && Escalations.Count == 0
        && ItemDefinitions.Count == 0
        && DataStores.Count == 0
        && Categories.Count == 0
        && Resources.Count == 0
        && Interfaces.Count == 0
        && GlobalTasks.Count == 0
        && Imports.Count == 0
        && Relationships.Count == 0
        && ExtensionDeclarations.Count == 0
        && Preserved.Count == 0;
}
