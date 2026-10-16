namespace Hasseware.AspNetCore.Components.Bpmn.Tools;

/// <summary>Arguments of <c>bpmn_get_diagram</c>.</summary>
/// <param name="Cursor">The <see cref="BpmnDiagramSummary.NextCursor"/> of the previous page, or null for the first.</param>
/// <param name="Limit">The most items on this page; capped by <see cref="BpmnToolLimits.MaxElements"/>.</param>
public sealed record BpmnGetDiagramRequest(string? Cursor = null, int? Limit = null);

/// <summary>Arguments of <c>bpmn_describe_element</c>.</summary>
/// <param name="ElementId">The element.</param>
public sealed record BpmnDescribeElementRequest(string ElementId);

/// <summary>Arguments of <c>bpmn_export</c>.</summary>
/// <param name="Format"><c>xml</c> (BPMN 2.0 XML) or <c>json</c> (the JSON interchange format).</param>
public sealed record BpmnExportRequest(string Format = "xml");

/// <summary>Arguments of <c>bpmn_list_creatable</c>.</summary>
/// <param name="ContainerId">The Process or Sub-Process to create into; the active diagram's when null.</param>
public sealed record BpmnListCreatableRequest(string? ContainerId = null);

/// <summary>Arguments of <c>bpmn_create_element</c>.</summary>
/// <param name="Type">The Flow Node type in wire form, such as <c>userTask</c> or <c>exclusiveGateway</c>.</param>
/// <param name="Name">The new element's name.</param>
/// <param name="AnchorId">Place it beside this element.</param>
/// <param name="Direction"><c>right</c> (default), <c>below</c>, <c>left</c> or <c>above</c> of the anchor.</param>
/// <param name="ContainerId">Place it inside this Process or expanded Sub-Process instead of beside an anchor.</param>
/// <param name="EventDefinition">For an Event, its trigger in wire form, such as <c>message</c> or <c>timer</c>.</param>
/// <param name="ConnectFromAnchor">Also connect the anchor to the new element, in the same single edit.</param>
public sealed record BpmnCreateElementRequest(
    string Type,
    string? Name = null,
    string? AnchorId = null,
    string? Direction = null,
    string? ContainerId = null,
    string? EventDefinition = null,
    bool ConnectFromAnchor = false);

/// <summary>Arguments of <c>bpmn_connect</c>.</summary>
/// <param name="SourceId">Where the connector starts.</param>
/// <param name="TargetId">Where it ends.</param>
/// <param name="Kind">
/// <c>sequenceFlow</c>, <c>messageFlow</c>, <c>association</c> or <c>dataAssociation</c>; inferred as the
/// canvas would when null.
/// </param>
/// <param name="Name">The connector's name.</param>
public sealed record BpmnConnectRequest(string SourceId, string TargetId, string? Kind = null, string? Name = null);

/// <summary>Arguments of <c>bpmn_set_property</c>.</summary>
/// <param name="ElementId">The element.</param>
/// <param name="Name">The property, such as <c>name</c>, <c>documentation</c> or <c>conditionExpression</c>.</param>
/// <param name="Value">The new value; null removes it.</param>
public sealed record BpmnSetPropertyRequest(string ElementId, string Name, string? Value);

/// <summary>Arguments of <c>bpmn_select</c>.</summary>
/// <param name="ElementIds">The elements to select; empty clears the selection.</param>
public sealed record BpmnSelectRequest(IReadOnlyList<string> ElementIds);

/// <summary>Arguments of <c>bpmn_delete</c>.</summary>
/// <param name="ElementIds">The elements; what cannot outlive them goes too, and is reported.</param>
public sealed record BpmnDeleteRequest(IReadOnlyList<string> ElementIds);
