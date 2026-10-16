namespace Hasseware.AspNetCore.Components.Bpmn.Tools;

/// <summary>A rectangle on a diagram.</summary>
/// <param name="X">Left edge.</param>
/// <param name="Y">Top edge.</param>
/// <param name="Width">Width.</param>
/// <param name="Height">Height.</param>
public sealed record BpmnToolBounds(double X, double Y, double Width, double Height);

/// <summary>One diagram of the document.</summary>
/// <param name="Id">The diagram's id.</param>
/// <param name="Name">Its name.</param>
/// <param name="ElementId">The Process, Collaboration or Sub-Process it draws.</param>
public sealed record BpmnDiagramInfo(string Id, BpmnUntrustedText? Name, string? ElementId);

/// <summary>One element of the document.</summary>
/// <param name="Id">The element's id.</param>
/// <param name="Type">Its type in wire form, such as <c>userTask</c>, <c>participant</c> or <c>lane</c>.</param>
/// <param name="Name">Its name, or a text annotation's text.</param>
/// <param name="ContainerId">The Process or Sub-Process that owns it.</param>
/// <param name="PoolId">The Pool it sits in.</param>
/// <param name="Bounds">Where it is drawn, when it is.</param>
/// <param name="DiagramId">The diagram the bounds are on.</param>
public sealed record BpmnElementSummary(
    string Id,
    string Type,
    BpmnUntrustedText? Name,
    string? ContainerId,
    string? PoolId,
    BpmnToolBounds? Bounds,
    string? DiagramId);

/// <summary>One connector of the document.</summary>
/// <param name="Id">The connector's id.</param>
/// <param name="Kind"><c>sequenceFlow</c>, <c>messageFlow</c>, <c>association</c> or <c>dataAssociation</c>.</param>
/// <param name="SourceId">Where it starts.</param>
/// <param name="TargetId">Where it ends.</param>
/// <param name="Name">Its name.</param>
public sealed record BpmnConnectorSummary(string Id, string Kind, string SourceId, string TargetId, BpmnUntrustedText? Name);

/// <summary>The data of <c>bpmn_get_diagram</c>: one page of the document's contents.</summary>
/// <param name="Diagrams">Every diagram; listed in full on each page.</param>
/// <param name="Elements">This page's elements.</param>
/// <param name="Connectors">This page's connectors, after the elements.</param>
/// <param name="Total">How many elements and connectors there are in all.</param>
/// <param name="NextCursor">The cursor for the next page, or null on the last.</param>
public sealed record BpmnDiagramSummary(
    IReadOnlyList<BpmnDiagramInfo> Diagrams,
    IReadOnlyList<BpmnElementSummary> Elements,
    IReadOnlyList<BpmnConnectorSummary> Connectors,
    int Total,
    string? NextCursor);

/// <summary>The data of <c>bpmn_get_selection</c>: what the user has selected.</summary>
/// <param name="Elements">The selected elements.</param>
/// <param name="Connectors">The selected connectors.</param>
/// <param name="SelectedCount">How many ids are selected; more than the two lists hold when the limit of the call cut them.</param>
public sealed record BpmnSelection(IReadOnlyList<BpmnElementSummary> Elements, IReadOnlyList<BpmnConnectorSummary> Connectors, int SelectedCount = 0);

/// <summary>One entry of a document listing: a document, or a folder that holds one.</summary>
/// <param name="Path">The logical path to pass to the other tools; a file name is text anyone could have written, so it is wrapped.</param>
/// <param name="Kind"><c>document</c> or <c>folder</c>.</param>
/// <param name="Size">A document's size in bytes.</param>
/// <param name="Modified">When a document was last written.</param>
public sealed record BpmnDocumentListEntry(BpmnUntrustedText Path, string Kind, long? Size, DateTimeOffset? Modified);

/// <summary>The data of <c>bpmn_list_documents</c>: one page of a folder.</summary>
/// <param name="Entries">Sub-folders first, then documents, in order.</param>
/// <param name="NextCursor">The cursor for the next page, or null on the last.</param>
public sealed record BpmnDocumentListing(IReadOnlyList<BpmnDocumentListEntry> Entries, string? NextCursor);

/// <summary>What one operation of <c>bpmn_apply_batch</c> did.</summary>
/// <param name="Index">Its position in the batch, from 0.</param>
/// <param name="Op">The operation: <c>createElement</c>, <c>connect</c>, <c>setProperty</c> or <c>delete</c>.</param>
/// <param name="Alias">The alias the operation gave what it created, which later operations may use as <c>@alias</c>.</param>
/// <param name="ElementId">The element it created.</param>
/// <param name="ConnectorId">The connector it created.</param>
/// <param name="DeletedIds">Every element it removed.</param>
public sealed record BpmnBatchStep(int Index, string Op, string? Alias, string? ElementId, string? ConnectorId, IReadOnlyList<string> DeletedIds);

/// <summary>The data of <c>bpmn_apply_batch</c>.</summary>
/// <param name="Steps">What each operation did; on a refusal, the ones before the one that failed.</param>
/// <param name="FailedAt">The index of the operation that was refused, when one was; nothing was saved then.</param>
public sealed record BpmnBatchResult(IReadOnlyList<BpmnBatchStep> Steps, int? FailedAt);

/// <summary>The data of <c>bpmn_get_document_info</c>: how the open document relates to the stored one.</summary>
/// <param name="Name">The stored document's name.</param>
/// <param name="StoredRevision">The stored revision the open document was loaded from or last saved as.</param>
/// <param name="IsDirty">Whether there are edits not yet saved.</param>
/// <param name="ExternalChange"><c>none</c>, <c>updated</c> or <c>deleted</c>: whether the stored document changed behind the editor.</param>
public sealed record BpmnDocumentInfo(BpmnUntrustedText? Name, string? StoredRevision, bool IsDirty, string ExternalChange);

/// <summary>The data of <c>bpmn_save</c>.</summary>
/// <param name="StoredRevision">The revision the store gave the saved document.</param>
public sealed record BpmnSaved(string? StoredRevision);

/// <summary>One property of a described element.</summary>
/// <param name="Name">The name to pass to <c>bpmn_set_property</c>.</param>
/// <param name="Kind">The value's kind, such as <c>text</c>, <c>boolean</c> or <c>choice</c>.</param>
/// <param name="Value">The current value.</param>
/// <param name="IsReadOnly">Whether it can be changed.</param>
/// <param name="Choices">The allowed values, for a choice.</param>
public sealed record BpmnPropertyInfo(string Name, string Kind, BpmnUntrustedText? Value, bool IsReadOnly, IReadOnlyList<string> Choices);

/// <summary>Something a described element points at.</summary>
/// <param name="Name">The reference's name, such as <c>sourceRef</c> or <c>calledElement</c>.</param>
/// <param name="TargetId">The id it points at.</param>
/// <param name="IsResolved">Whether that id exists.</param>
public sealed record BpmnReferenceInfo(string Name, string TargetId, bool IsResolved);

/// <summary>The data of <c>bpmn_describe_element</c>.</summary>
/// <param name="Id">The element's id.</param>
/// <param name="Type">Its type in wire form.</param>
/// <param name="Category">Event, Activity, Gateway, Connector, Artifact, Data, Pool, Lane or Process.</param>
/// <param name="Name">Its label.</param>
/// <param name="ContainerId">The Process or Sub-Process that owns it.</param>
/// <param name="PoolId">The Pool it sits in.</param>
/// <param name="LaneId">The Lane it sits in.</param>
/// <param name="Bounds">Where it is drawn.</param>
/// <param name="Properties">Its editable and read-only properties.</param>
/// <param name="References">What it points at.</param>
/// <param name="Diagnostics">What the editor would say about it.</param>
/// <param name="CanConnect">Whether a connector may start at it.</param>
/// <param name="CanDelete">Whether it may be deleted.</param>
public sealed record BpmnElementDescription(
    string Id,
    string Type,
    string Category,
    BpmnUntrustedText? Name,
    string? ContainerId,
    string? PoolId,
    string? LaneId,
    BpmnToolBounds? Bounds,
    IReadOnlyList<BpmnPropertyInfo> Properties,
    IReadOnlyList<BpmnReferenceInfo> References,
    IReadOnlyList<BpmnToolDiagnostic> Diagnostics,
    bool CanConnect,
    bool CanDelete);

/// <summary>The data of <c>bpmn_validate</c>.</summary>
/// <param name="Errors">How many errors there are in all.</param>
/// <param name="Warnings">How many warnings there are in all.</param>
/// <param name="Truncated">Whether more findings exist than <c>Diagnostics</c> lists.</param>
public sealed record BpmnValidationSummary(int Errors, int Warnings, bool Truncated);

/// <summary>The data of <c>bpmn_export</c>.</summary>
/// <param name="Format"><c>xml</c> or <c>json</c>.</param>
/// <param name="Content">The serialized document; it is diagram content, so untrusted.</param>
/// <param name="IsLossless">Whether everything the document holds was written.</param>
public sealed record BpmnExportData(string Format, BpmnUntrustedText Content, bool IsLossless);

/// <summary>One thing that could be created.</summary>
/// <param name="Type">The type to pass to <c>bpmn_create_element</c>.</param>
/// <param name="Label">What the palette calls it.</param>
/// <param name="EventDefinition">The trigger it comes with, for an Event.</param>
/// <param name="IsEnabled">Whether it can be created here now.</param>
/// <param name="DisabledReason">Why not, when it cannot.</param>
public sealed record BpmnCreatableEntry(string Type, string Label, string? EventDefinition, bool IsEnabled, string? DisabledReason);

/// <summary>The data of <c>bpmn_list_creatable</c>.</summary>
/// <param name="ContainerId">The container the list is for.</param>
/// <param name="Entries">What the palette offers there; only Flow Nodes, which the tools can create.</param>
public sealed record BpmnCreatableList(string? ContainerId, IReadOnlyList<BpmnCreatableEntry> Entries);

/// <summary>The data of <c>bpmn_create_element</c>.</summary>
/// <param name="ElementId">The new element.</param>
/// <param name="ContainerId">Where it was created.</param>
/// <param name="Bounds">Where it was placed.</param>
/// <param name="ConnectorId">The connector from the anchor, when one was asked for.</param>
public sealed record BpmnCreatedElement(string ElementId, string ContainerId, BpmnToolBounds Bounds, string? ConnectorId);

/// <summary>The data of <c>bpmn_connect</c>.</summary>
/// <param name="ConnectorId">The new connector.</param>
/// <param name="Kind">Its kind, as given or inferred.</param>
public sealed record BpmnCreatedConnector(string ConnectorId, string Kind);

/// <summary>The data of <c>bpmn_delete</c>.</summary>
/// <param name="DeletedIds">Every element that went: those asked for, and those that could not outlive them.</param>
public sealed record BpmnDeletedElements(IReadOnlyList<string> DeletedIds);
