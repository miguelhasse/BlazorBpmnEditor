using System.Text.Json;
using System.Text.RegularExpressions;

using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;
using Hasseware.AspNetCore.Components.Bpmn.Storage;
using Hasseware.AspNetCore.Components.Bpmn.Tools;

using Microsoft.Extensions.Logging;

namespace Hasseware.AspNetCore.Components.Bpmn.Mcp;

/// <summary>One operation of <c>bpmn_apply_batch</c>.</summary>
/// <param name="Op"><c>createElement</c>, <c>connect</c>, <c>setProperty</c> or <c>delete</c>.</param>
/// <param name="Arguments">The arguments of the tool of that name, without <c>documentId</c>; an element id may be <c>@alias</c>.</param>
/// <param name="As">A name for what the operation creates, which later operations use as <c>@name</c>.</param>
public sealed record BpmnBatchOperation(string Op, JsonElement Arguments, string? As = null);

public sealed partial class BpmnMcpService
{
    private static readonly Regex AliasPattern = new("^[A-Za-z][A-Za-z0-9_]{0,31}$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    /// <summary>Compares a document with another stored one.</summary>
    /// <param name="documentId">The document that changed.</param>
    /// <param name="baselineDocumentId">The document to compare it with.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>What was added, removed and changed, by id.</returns>
    public async Task<BpmnToolResult<BpmnDocumentDiffResult>> DiffAsync(string documentId, string baselineDocumentId, CancellationToken cancellationToken)
    {
        const string tool = "bpmn_diff";
        return Audit(tool, documentId, await GuardAsync(async () =>
        {
            var (_, current, failure) = await LoadAsync<BpmnDocumentDiffResult>(documentId, BpmnDocumentAction.Read, cancellationToken).ConfigureAwait(false);
            if (failure is not null) return failure;

            var (_, baseline, baselineFailure) = await LoadAsync<BpmnDocumentDiffResult>(baselineDocumentId, BpmnDocumentAction.Read, cancellationToken).ConfigureAwait(false);
            if (baselineFailure is not null) return baselineFailure;

            var host = new BpmnStoredDocumentToolHost(current!.Document, current.Revision, Origin, _profiles, Limits);
            return await BpmnTools.DiffAsync(host, baseline!.Document, cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false));
    }

    /// <summary>Creates a Flow Node in a stored document.</summary>
    /// <param name="documentId">The document.</param>
    /// <param name="request">What to create.</param>
    /// <param name="expectedRevision">The revision the agent last saw; the edit is refused if the document has moved.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The new element, with the new stored revision.</returns>
    public Task<BpmnToolResult> CreateElementAsync(string documentId, BpmnCreateElementRequest request, string? expectedRevision, CancellationToken cancellationToken) =>
        WriteAsync("bpmn_create_element", documentId, expectedRevision, async host => await BpmnTools.CreateElementAsync(host, request, cancellationToken).ConfigureAwait(false), cancellationToken);

    /// <summary>Connects two elements of a stored document.</summary>
    /// <param name="documentId">The document.</param>
    /// <param name="request">What to connect.</param>
    /// <param name="expectedRevision">The revision the agent last saw.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The new connector, with the new stored revision.</returns>
    public Task<BpmnToolResult> ConnectAsync(string documentId, BpmnConnectRequest request, string? expectedRevision, CancellationToken cancellationToken) =>
        WriteAsync("bpmn_connect", documentId, expectedRevision, async host => await BpmnTools.ConnectAsync(host, request, cancellationToken).ConfigureAwait(false), cancellationToken);

    /// <summary>Sets one standard property of an element of a stored document.</summary>
    /// <param name="documentId">The document.</param>
    /// <param name="request">Which property, and the value.</param>
    /// <param name="expectedRevision">The revision the agent last saw.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result, with the new stored revision.</returns>
    public Task<BpmnToolResult> SetPropertyAsync(string documentId, BpmnSetPropertyRequest request, string? expectedRevision, CancellationToken cancellationToken) =>
        WriteAsync("bpmn_set_property", documentId, expectedRevision, async host => await BpmnTools.SetPropertyAsync(host, request, cancellationToken).ConfigureAwait(false), cancellationToken);

    /// <summary>Deletes elements of a stored document.</summary>
    /// <param name="documentId">The document.</param>
    /// <param name="request">What to delete.</param>
    /// <param name="expectedRevision">The revision the agent last saw.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>Everything that went, with the new stored revision.</returns>
    public Task<BpmnToolResult> DeleteAsync(string documentId, BpmnDeleteRequest request, string? expectedRevision, CancellationToken cancellationToken) =>
        WriteAsync("bpmn_delete", documentId, expectedRevision, async host => await BpmnTools.DeleteAsync(host, request, cancellationToken).ConfigureAwait(false), cancellationToken);

    /// <summary>Applies several operations as one edit: all of them are saved, or none.</summary>
    /// <param name="documentId">The document.</param>
    /// <param name="operations">The operations, in order; a later one may name what an earlier one created as <c>@alias</c>.</param>
    /// <param name="expectedRevision">The revision the agent last saw.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>What each operation did, with the new stored revision; on a refusal, which operation failed.</returns>
    public Task<BpmnToolResult> ApplyBatchAsync(string documentId, IReadOnlyList<BpmnBatchOperation>? operations, string? expectedRevision, CancellationToken cancellationToken)
    {
        if (operations is null || operations.Count == 0)
        {
            return Task.FromResult(Audit("bpmn_apply_batch", documentId, Refused(BpmnToolCodes.InvalidArgument, "Give at least one operation.")));
        }

        if (operations.Count > BpmnToolLimits.Default.MaxBatch)
        {
            return Task.FromResult(Audit("bpmn_apply_batch", documentId, Refused(BpmnToolCodes.TooLarge, "That is more operations than one batch may hold.")));
        }

        return WriteAsync("bpmn_apply_batch", documentId, expectedRevision, host => RunBatchAsync(host, operations, cancellationToken), cancellationToken);
    }

    /// <summary>Creates a blank document, if there is none by that id.</summary>
    /// <param name="documentId">The new document's logical path.</param>
    /// <param name="name">The name of its process.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result, with the stored revision of the new document.</returns>
    public async Task<BpmnToolResult> CreateDocumentAsync(string documentId, string? name, CancellationToken cancellationToken)
    {
        const string tool = "bpmn_create_document";
        if (!_options.AllowWrites) return Audit(tool, documentId, ReadOnlyRefusal());
        if (!BpmnDocumentPath.TryParse(documentId, out var path) || !path.IsDocumentName)
        {
            return Audit(tool, documentId, Refused(BpmnToolCodes.InvalidPath, "The document id is not a valid logical path to a .bpmn, .xml or .json document."));
        }

        if (name?.Length > BpmnToolLimits.Default.MaxTextLength) return Audit(tool, documentId, Refused(BpmnToolCodes.InvalidArgument, "The name is too long."));

        if (!TryResolveTenant(out var tenant)) return Audit(tool, documentId, Refused(BpmnToolCodes.Forbidden, NoTenant));

        var key = new BpmnDocumentKey(tenant, path);
        if (!await _authorizer.IsAllowedAsync(BpmnDocumentAction.Create, key, cancellationToken).ConfigureAwait(false))
        {
            return Audit(tool, documentId, Refused(BpmnToolCodes.Forbidden, "This caller may not create a document there."));
        }

        var content = BpmnDocumentStoreExtensions.Serialize(key, new BpmnDocumentBuilder("Process_1", string.IsNullOrWhiteSpace(name) ? null : name).Build());
        if (content.Bytes.Length > _options.MaxXmlBytes) return Audit(tool, documentId, Refused(BpmnToolCodes.TooLarge, "The new document would be larger than this server stores; nothing was written."));

        BpmnStoreResult saved;
        try
        {
            saved = await _store.SaveAsync(key, content, ifMatchRevision: null, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException))
        {
            LogStoreFailed(exception);
            return Audit(tool, documentId, Refused(BpmnToolCodes.StoreUnavailable, AmbiguousWrite));
        }

        if (saved.Succeeded) return Audit(tool, documentId, new BpmnToolResult { Ok = true, Applied = true, Revision = saved.Revision });

        // That a document exists, and its revision, is something only a caller who may read it is told: for anyone else this is the refusal they would
        // have had for a place they may not create in.
        var mayRead = await _authorizer.IsAllowedAsync(BpmnDocumentAction.Read, key, cancellationToken).ConfigureAwait(false);
        return Audit(tool, documentId, mayRead
            ? Refused(BpmnToolCodes.PersistConflict, "A document with that id already exists; nothing was written.", saved.Revision)
            : Refused(BpmnToolCodes.Forbidden, "This caller may not create a document there."));
    }

    // --- the one way every edit is saved -----------------------------------------------------------

    private const string AmbiguousWrite = "The document store failed while saving, so the edit may or may not have been written. Open the document to find out before trying again.";

    // Read, edit in memory, write back at the revision that was read (or the one the agent names): never a session, never a blind retry.
    private async Task<BpmnToolResult> WriteAsync(
        string tool,
        string documentId,
        string? expectedRevision,
        Func<BpmnDocumentToolHost, Task<BpmnToolResult>> edit,
        CancellationToken cancellationToken)
    {
        if (!_options.AllowWrites) return Audit(tool, documentId, ReadOnlyRefusal());

        var before = string.Empty;
        var result = await GuardWriteAsync(async () =>
        {
            var (key, loaded, failure) = await LoadAsync<object>(documentId, BpmnDocumentAction.Write, cancellationToken).ConfigureAwait(false);
            if (failure is not null) return failure;

            before = loaded!.Revision;
            if (expectedRevision is not null && !string.Equals(expectedRevision, loaded.Revision, StringComparison.Ordinal))
            {
                return Refused(BpmnToolCodes.PersistConflict, "The document changed since the revision you hold; nothing was written. Open it again and redo the edit.", loaded.Revision);
            }

            var host = new BpmnDocumentToolHost(loaded.Document, Origin, profiles: _profiles, limits: Limits);
            var edited = await edit(host).ConfigureAwait(false);

            // A refusal, or an edit that changed nothing, leaves the stored document alone.
            if (!edited.Ok || ReferenceEquals(host.Document, loaded.Document)) return edited with { Revision = loaded.Revision, IsDirty = null };

            BpmnDocumentContent content;
            try
            {
                content = BpmnDocumentStoreExtensions.Serialize(key, host.Document, loaded.Format);
            }
            catch (BpmnExportException)
            {
                return Refused(BpmnToolCodes.InvalidArgument, "The edited document cannot be written in its format; nothing was written.", loaded.Revision);
            }

            if (content.Bytes.Length > _options.MaxXmlBytes)
            {
                return Refused(BpmnToolCodes.TooLarge, "The edited document would be larger than this server stores; nothing was written.", loaded.Revision);
            }

            BpmnStoreResult saved;
            try
            {
                saved = await _store.SaveAsync(key, content, expectedRevision ?? loaded.Revision, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException))
            {
                // Only here is it unknown whether anything was written.
                LogStoreFailed(exception);
                return Refused(BpmnToolCodes.StoreUnavailable, AmbiguousWrite);
            }

            return saved.Status switch
            {
                BpmnStoreStatus.Succeeded => edited with { Revision = saved.Revision, IsDirty = null },
                BpmnStoreStatus.NotFound => Refused(BpmnToolCodes.PersistConflict, "The document was removed while you edited it; nothing was written.", null),
                _ => Refused(BpmnToolCodes.PersistConflict, "Someone else changed the document first; nothing was written. Open it again and redo the edit.", saved.Revision),
            };
        }).ConfigureAwait(false);

        var caller = _tenants.GetCallerId() ?? string.Empty;
        var outcome = result.Ok ? "ok" : result.Code ?? "error";
        LogWrite(tool, caller, documentId, outcome, before, result.Revision ?? string.Empty);
        return result;
    }

    private async Task<BpmnToolResult> GuardWriteAsync(Func<Task<BpmnToolResult>> work)
    {
        try
        {
            return await work().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException))
        {
            // Before the save: nothing was attempted, so nothing may have been written.
            LogStoreFailed(exception);
            return Refused(BpmnToolCodes.StoreUnavailable, "The document store could not be used; nothing was written.");
        }
    }

    private static BpmnToolResult Refused(string code, string message, string? revision = null) =>
        new() { Ok = false, Code = code, Message = message, Revision = revision };

    private static BpmnToolResult ReadOnlyRefusal() => Refused(BpmnToolCodes.ReadOnly, "This server does not change documents.");

    private BpmnToolResult Audit(string tool, string documentId, BpmnToolResult result)
    {
        var caller = _tenants.GetCallerId() ?? string.Empty;
        var outcome = result.Ok ? "ok" : result.Code ?? "error";
        LogCall(tool, caller, documentId, outcome, result.Revision ?? string.Empty);
        return result;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "MCP tool {Tool} by '{Caller}' on '{DocumentId}': {Outcome} (revision '{Before}' to '{After}')")]
    private partial void LogWrite(string tool, string caller, string documentId, string outcome, string before, string after);

    // --- the batch ---------------------------------------------------------------------------------

    private static async Task<BpmnToolResult> RunBatchAsync(BpmnDocumentToolHost host, IReadOnlyList<BpmnBatchOperation> operations, CancellationToken cancellationToken)
    {
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        var steps = new List<BpmnBatchStep>();

        for (var index = 0; index < operations.Count; index++)
        {
            var operation = operations[index];
            var alias = operation.As;
            if (alias is not null && (!AliasPattern.IsMatch(alias) || aliases.ContainsKey(alias)))
            {
                return BatchRefused(index, steps, BpmnToolCodes.InvalidArgument, null, "An alias must be a new name of letters, digits and underscores, starting with a letter.");
            }

            BpmnBatchStep step;
            try
            {
                var (failure, done) = await RunOperationAsync(host, index, operation, aliases, cancellationToken).ConfigureAwait(false);
                if (failure is not null) return BatchRefused(index, steps, failure.Code ?? BpmnToolCodes.Refused, failure, null);

                step = done!;
            }
            catch (UnknownAliasException)
            {
                return BatchRefused(index, steps, BpmnToolCodes.InvalidArgument, null, "An operation names an alias no earlier operation made.");
            }

            if (alias is not null)
            {
                var made = step.ElementId ?? step.ConnectorId;
                if (made is null) return BatchRefused(index, steps, BpmnToolCodes.InvalidArgument, null, "Only an operation that creates something can be given an alias.");

                aliases[alias] = made;
                step = step with { Alias = alias };
            }

            steps.Add(step);
        }

        return new BpmnToolResult<BpmnBatchResult>
        {
            Ok = true,
            Applied = true,
            Data = new BpmnBatchResult(steps, FailedAt: null),
            CreatedIds = [.. steps.SelectMany(step => new[] { step.ElementId, step.ConnectorId }).OfType<string>()],
        };
    }

    private static async Task<(BpmnToolResult? Failure, BpmnBatchStep? Step)> RunOperationAsync(
        BpmnDocumentToolHost host,
        int index,
        BpmnBatchOperation operation,
        Dictionary<string, string> aliases,
        CancellationToken cancellationToken)
    {
        string? Resolve(string? id) => id is { Length: > 1 } && id[0] == '@' ? aliases.TryGetValue(id[1..], out var made) ? made : throw new UnknownAliasException() : id;

        if (operation.Arguments.ValueKind != JsonValueKind.Object) return (Refused(BpmnToolCodes.InvalidArgument, "Every operation needs an arguments object."), null);

        switch (operation.Op)
        {
            case "createElement" when BpmnToolJson.TryRead<BpmnCreateElementRequest>(operation.Arguments, out var create):
                {
                    var request = create! with { AnchorId = Resolve(create.AnchorId), ContainerId = Resolve(create.ContainerId) };
                    var result = await BpmnTools.CreateElementAsync(host, request, cancellationToken).ConfigureAwait(false);
                    return result.Ok ? (null, new BpmnBatchStep(index, operation.Op, null, result.Data!.ElementId, result.Data.ConnectorId, [])) : (result, null);
                }

            case "connect" when BpmnToolJson.TryRead<BpmnConnectRequest>(operation.Arguments, out var connect):
                {
                    var request = connect! with { SourceId = Resolve(connect.SourceId)!, TargetId = Resolve(connect.TargetId)! };
                    var result = await BpmnTools.ConnectAsync(host, request, cancellationToken).ConfigureAwait(false);
                    return result.Ok ? (null, new BpmnBatchStep(index, operation.Op, null, null, result.Data!.ConnectorId, [])) : (result, null);
                }

            case "setProperty" when BpmnToolJson.TryRead<BpmnSetPropertyRequest>(operation.Arguments, out var set):
                {
                    var result = await BpmnTools.SetPropertyAsync(host, set! with { ElementId = Resolve(set.ElementId)! }, cancellationToken).ConfigureAwait(false);
                    return result.Ok ? (null, new BpmnBatchStep(index, operation.Op, null, null, null, [])) : (result, null);
                }

            case "delete" when BpmnToolJson.TryRead<BpmnDeleteRequest>(operation.Arguments, out var delete):
                {
                    var request = delete! with { ElementIds = [.. (delete.ElementIds ?? []).Select(id => Resolve(id)!)] };
                    var result = await BpmnTools.DeleteAsync(host, request, cancellationToken).ConfigureAwait(false);
                    return result.Ok ? (null, new BpmnBatchStep(index, operation.Op, null, null, null, result.Data!.DeletedIds)) : (result, null);
                }

            default:
                return (Refused(BpmnToolCodes.InvalidArgument, "An operation is createElement, connect, setProperty or delete, with that tool's arguments."), null);
        }
    }

    private sealed class UnknownAliasException : Exception;

    private static BpmnToolResult<BpmnBatchResult> BatchRefused(int index, List<BpmnBatchStep> done, string code, BpmnToolResult? cause, string? message) => new()
    {
        Ok = false,
        Code = code,
        Message = message ?? $"Operation {index.ToString(System.Globalization.CultureInfo.InvariantCulture)} was refused; nothing was saved.",
        Diagnostics = cause?.Diagnostics ?? [],
        Data = new BpmnBatchResult(done, FailedAt: index),
    };
}
