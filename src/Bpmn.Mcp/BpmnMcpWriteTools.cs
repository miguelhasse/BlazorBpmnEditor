using System.ComponentModel;

using Hasseware.AspNetCore.Components.Bpmn.Tools;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Hasseware.AspNetCore.Components.Bpmn.Mcp;

/// <summary>The tools that change stored BPMN documents; offered only where the host asks for them.</summary>
/// <remarks>
/// <para>
/// Each call reads the document, edits it in memory with the same handlers the browser uses, and writes it back at the
/// revision it read, or at <c>expectedRevision</c> when the agent passes the one it last saw, so two agents cannot silently
/// overwrite each other: the second gets <c>PERSIST-CONFLICT</c> and the current revision. Nothing is kept between calls.
/// Register with <see cref="BpmnMcpServiceCollectionExtensions.WithBpmnWriteTools"/>.
/// </para>
/// </remarks>
/// <param name="service">What the tools do.</param>
[McpServerToolType]
public sealed class BpmnMcpWriteTools(BpmnMcpService service)
{
    private const string Revision = "The revision from bpmn_open (or from your last edit). The edit is refused with PERSIST-CONFLICT if the document has changed since; leave it out only when you do not need to know.";

    /// <summary>Creates a Flow Node.</summary>
    /// <param name="documentId">The document's logical path.</param>
    /// <param name="type">The Flow Node type in wire form.</param>
    /// <param name="name">The element's name.</param>
    /// <param name="anchorId">Place it beside this element.</param>
    /// <param name="direction">Which side of the anchor.</param>
    /// <param name="containerId">Place it inside this Process or Sub-Process.</param>
    /// <param name="eventDefinition">For events, the trigger.</param>
    /// <param name="connectFromAnchor">Also connect the anchor to it.</param>
    /// <param name="expectedRevision">The revision the agent last saw.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The new element and the new revision.</returns>
    [McpServerTool(Name = "bpmn_create_element", Title = "Create a BPMN element", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Creates a Flow Node in a stored BPMN document, placed automatically clear of other elements: beside anchorId in the given direction, or inside containerId. With connectFromAnchor it also connects the anchor to the new element, refusing connections BPMN forbids. Never takes coordinates. Saves the document and returns its new revision.")]
    public async Task<CallToolResult> CreateElementAsync(
        [Description("The document's logical path.")] string documentId,
        [Description("Flow Node type in wire form, such as task, userTask, exclusiveGateway or endEvent.")] string type,
        [Description("The element's name.")] string? name = null,
        [Description("Place it beside this element.")] string? anchorId = null,
        [Description("right, below, left or above the anchor; right when omitted.")] string? direction = null,
        [Description("Place it inside this Process or expanded Sub-Process instead of beside an anchor.")] string? containerId = null,
        [Description("For events: message, timer, signal, error, and so on.")] string? eventDefinition = null,
        [Description("Also connect the anchor to the new element.")] bool connectFromAnchor = false,
        [Description(Revision)] string? expectedRevision = null,
        CancellationToken cancellationToken = default) =>
        BpmnMcpResults.From(await service.CreateElementAsync(
            documentId,
            new BpmnCreateElementRequest(type, name, anchorId, direction, containerId, eventDefinition, connectFromAnchor),
            expectedRevision,
            cancellationToken).ConfigureAwait(false));

    /// <summary>Connects two elements.</summary>
    /// <param name="documentId">The document's logical path.</param>
    /// <param name="sourceId">Where it starts.</param>
    /// <param name="targetId">Where it ends.</param>
    /// <param name="kind">The connector kind.</param>
    /// <param name="name">Its name.</param>
    /// <param name="expectedRevision">The revision the agent last saw.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The new connector and the new revision.</returns>
    [McpServerTool(Name = "bpmn_connect", Title = "Connect BPMN elements", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Connects two elements of a stored BPMN document with a sequence flow, message flow, association or data association (inferred when kind is omitted). Refuses connections the BPMN rules forbid, reporting the rule ids. Saves the document and returns its new revision.")]
    public async Task<CallToolResult> ConnectAsync(
        [Description("The document's logical path.")] string documentId,
        [Description("The element the connector starts at.")] string sourceId,
        [Description("The element it ends at.")] string targetId,
        [Description("sequenceFlow, messageFlow, association or dataAssociation; inferred when omitted.")] string? kind = null,
        [Description("The connector's name.")] string? name = null,
        [Description(Revision)] string? expectedRevision = null,
        CancellationToken cancellationToken = default) =>
        BpmnMcpResults.From(await service.ConnectAsync(documentId, new BpmnConnectRequest(sourceId, targetId, kind, name), expectedRevision, cancellationToken).ConfigureAwait(false));

    /// <summary>Sets a property.</summary>
    /// <param name="documentId">The document's logical path.</param>
    /// <param name="elementId">The element.</param>
    /// <param name="name">The property.</param>
    /// <param name="value">The new value; null removes it.</param>
    /// <param name="expectedRevision">The revision the agent last saw.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result and the new revision.</returns>
    [McpServerTool(Name = "bpmn_set_property", Title = "Set a BPMN property", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Sets one standard property of an element of a stored BPMN document, such as name, documentation or conditionExpression; bpmn_describe_element lists what an element has. A null value removes it. Saves the document and returns its new revision.")]
    public async Task<CallToolResult> SetPropertyAsync(
        [Description("The document's logical path.")] string documentId,
        [Description("The element's id.")] string elementId,
        [Description("The property's name.")] string name,
        [Description("The new value; null removes the property.")] string? value,
        [Description(Revision)] string? expectedRevision = null,
        CancellationToken cancellationToken = default) =>
        BpmnMcpResults.From(await service.SetPropertyAsync(documentId, new BpmnSetPropertyRequest(elementId, name, value), expectedRevision, cancellationToken).ConfigureAwait(false));

    /// <summary>Deletes elements.</summary>
    /// <param name="documentId">The document's logical path.</param>
    /// <param name="elementIds">The elements.</param>
    /// <param name="expectedRevision">The revision the agent last saw.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>Everything that went, and the new revision.</returns>
    [McpServerTool(Name = "bpmn_delete", Title = "Delete BPMN elements", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Deletes elements of a stored BPMN document, and everything that cannot outlive them (their connectors, a sub-process's contents), which the result lists. Saves the document and returns its new revision.")]
    public async Task<CallToolResult> DeleteAsync(
        [Description("The document's logical path.")] string documentId,
        [Description("The ids of the elements to delete.")] string[] elementIds,
        [Description(Revision)] string? expectedRevision = null,
        CancellationToken cancellationToken = default) =>
        BpmnMcpResults.From(await service.DeleteAsync(documentId, new BpmnDeleteRequest(elementIds), expectedRevision, cancellationToken).ConfigureAwait(false));

    /// <summary>Applies a batch.</summary>
    /// <param name="documentId">The document's logical path.</param>
    /// <param name="operations">The operations, in order.</param>
    /// <param name="expectedRevision">The revision the agent last saw.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>What each operation did, and the new revision.</returns>
    [McpServerTool(Name = "bpmn_apply_batch", Title = "Apply several BPMN edits at once", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Applies up to 50 operations to a stored BPMN document as one edit: all are saved, or none (the first refusal rejects the batch and reports failedAt). Each operation is { op, arguments, as }: op is createElement, connect, setProperty or delete, arguments are that tool's arguments without documentId, and 'as' names what the operation creates so a later one can use it as '@name' wherever an element id is expected. One save, one new revision.")]
    public async Task<CallToolResult> ApplyBatchAsync(
        [Description("The document's logical path.")] string documentId,
        [Description("The operations, in order. Example: [{\"op\":\"createElement\",\"arguments\":{\"type\":\"userTask\",\"name\":\"Review\",\"anchorId\":\"Start_1\",\"connectFromAnchor\":true},\"as\":\"review\"},{\"op\":\"createElement\",\"arguments\":{\"type\":\"endEvent\",\"anchorId\":\"@review\",\"connectFromAnchor\":true}}]")] BpmnBatchOperation[] operations,
        [Description(Revision)] string? expectedRevision = null,
        CancellationToken cancellationToken = default) =>
        BpmnMcpResults.From(await service.ApplyBatchAsync(documentId, operations, expectedRevision, cancellationToken).ConfigureAwait(false));

    /// <summary>Creates a blank document.</summary>
    /// <param name="documentId">The new document's logical path.</param>
    /// <param name="name">The name of its process.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The new document's revision.</returns>
    [McpServerTool(Name = "bpmn_create_document", Title = "Create a BPMN document", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Creates a blank BPMN document (one empty process and diagram) at a logical path that does not exist yet, creating folders as needed. Fails with PERSIST-CONFLICT, writing nothing, if a document is already there.")]
    public async Task<CallToolResult> CreateDocumentAsync(
        [Description("The new document's logical path, such as claims/2026/intake.bpmn.")] string documentId,
        [Description("The name of its process.")] string? name = null,
        CancellationToken cancellationToken = default) =>
        BpmnMcpResults.From(await service.CreateDocumentAsync(documentId, name, cancellationToken).ConfigureAwait(false));
}
