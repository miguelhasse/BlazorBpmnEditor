using System.Text;

using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;
using Hasseware.AspNetCore.Components.Bpmn.Storage;
using Hasseware.AspNetCore.Components.Bpmn.Tools;
using Hasseware.AspNetCore.Components.Bpmn.Validation;

using Microsoft.Extensions.Logging;

namespace Hasseware.AspNetCore.Components.Bpmn.Mcp;

/// <summary>
/// What each server tool does, over any <see cref="IBpmnDocumentStore"/>: parse the id, check the size, load the
/// document, and hand it to the shared handlers.
/// </summary>
/// <remarks>
/// <para>
/// Every method returns a <see cref="BpmnToolResult"/> and none throws for something an agent did: a bad id, a
/// missing or oversize document, an unreadable file and an unreachable store are all results with a code. The id is
/// parsed before the store is touched and the size is checked before the content is read.
/// </para>
/// <para>
/// The calls are logged with the tool, the document id and the revision, and never with content.
/// </para>
/// </remarks>
public sealed partial class BpmnMcpService
{
    private readonly IBpmnDocumentStore _store;
    private readonly IBpmnMcpTenantResolver _tenants;
    private readonly BpmnMcpOptions _options;
    private readonly IReadOnlyList<IBpmnProfileValidator> _profiles;
    private readonly IBpmnDocumentAuthorizer _authorizer;
    private readonly ILogger<BpmnMcpService> _logger;

    /// <summary>Creates the service.</summary>
    /// <param name="store">Where the documents are.</param>
    /// <param name="tenants">Says whose documents a call may see.</param>
    /// <param name="options">The limits and the agent's name.</param>
    /// <param name="logger">Receives the audit entries.</param>
    /// <param name="profiles">Profile validators to run with validation, such as the scripting ones; none when null.</param>
    /// <param name="authorizer">Decides per document what the caller may do; everything within the tenant when null.</param>
    public BpmnMcpService(
        IBpmnDocumentStore store,
        IBpmnMcpTenantResolver tenants,
        BpmnMcpOptions options,
        ILogger<BpmnMcpService> logger,
        IEnumerable<IBpmnProfileValidator>? profiles = null,
        IBpmnDocumentAuthorizer? authorizer = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(tenants);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _store = store;
        _tenants = tenants;
        _options = options;
        _logger = logger;
        _profiles = [.. profiles ?? []];
        _authorizer = authorizer ?? new AllowAllBpmnDocumentAuthorizer();
    }

    /// <summary>Lists one page of a folder.</summary>
    /// <param name="folder">The folder; the root when empty.</param>
    /// <param name="recursive">Whether to list every document beneath it instead of its own entries.</param>
    /// <param name="namePrefix">Only names that start with this.</param>
    /// <param name="cursor">The cursor of the previous page.</param>
    /// <param name="limit">The most entries; capped.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The page.</returns>
    public async Task<BpmnToolResult<BpmnDocumentListing>> ListAsync(
        string? folder,
        bool recursive,
        string? namePrefix,
        string? cursor,
        int? limit,
        CancellationToken cancellationToken)
    {
        const string tool = "bpmn_list_documents";
        var path = BpmnDocumentPath.Root;
        if (!string.IsNullOrEmpty(folder) && !BpmnDocumentPath.TryParse(folder, out path))
        {
            return Audit(tool, folder, Failed<BpmnDocumentListing>(BpmnToolCodes.InvalidPath, "The folder is not a valid logical path."));
        }

        if (!TryResolveTenant(out var tenant))
        {
            return Audit(tool, folder, Failed<BpmnDocumentListing>(BpmnToolCodes.Forbidden, NoTenant));
        }

        var query = new BpmnDocumentQuery(tenant, path, recursive, namePrefix, cursor, Math.Min(limit ?? BpmnDocumentQuery.DefaultLimit, _options.MaxElementsPerCall));

        return Audit(tool, folder, await GuardAsync<BpmnDocumentListing>(async () =>
        {
            // A document the caller may not read is not listed, and neither is a folder with nothing in it the caller may read: its name would tell them
            // what exists. Whether a folder holds something readable is a bounded look, so a very large folder the caller cannot see into is hidden, not scanned.
            // A page the caller may see none of is skipped rather than returned empty with a cursor, which a client
            // would take for the end of the listing.
            var current = query;
            for (var attempt = 0; attempt < MaxListPagesSkipped; attempt++)
            {
                BpmnDocumentPage page;
                try
                {
                    page = await _store.ListAsync(current, cancellationToken).ConfigureAwait(false);
                }
                catch (ArgumentException) when (current.Cursor is not null)
                {
                    return Failed<BpmnDocumentListing>(BpmnToolCodes.InvalidArgument, "The cursor was not produced by this tool.");
                }

                var visible = new List<BpmnDocumentEntry>();
                var probed = 0;
                foreach (var entry in page.Entries)
                {
                    var allowed = entry.IsFolder
                        ? _authorizer is AllowAllBpmnDocumentAuthorizer || (probed++ < MaxFolderProbes && await HoldsReadableAsync(query.Tenant, entry.Path, cancellationToken).ConfigureAwait(false))
                        : await _authorizer.IsAllowedAsync(BpmnDocumentAction.Read, new BpmnDocumentKey(query.Tenant, entry.Path), cancellationToken).ConfigureAwait(false);

                    if (allowed) visible.Add(entry);
                }

                if (visible.Count == 0 && page.NextCursor is not null && attempt + 1 < MaxListPagesSkipped)
                {
                    current = current with { Cursor = page.NextCursor };
                    continue;
                }

                var entries = visible.Select(entry => new BpmnDocumentListEntry(
                    BpmnUntrustedText.From(entry.Path.ToString(), BpmnDocumentPath.MaxLength)!,
                    entry.IsFolder ? "folder" : "document",
                    entry.Size,
                    entry.Modified)).ToList();

                return new BpmnToolResult<BpmnDocumentListing> { Ok = true, Data = new BpmnDocumentListing(entries, page.NextCursor) };
            }

            return Failed<BpmnDocumentListing>(BpmnToolCodes.InvalidArgument, "Unreachable.");
        }).ConfigureAwait(false));
    }

    private const int MaxFolderProbes = 10;
    private const int FolderProbeSize = 50;
    private const int FolderProbePages = 2;

    // Whether anything beneath a folder is readable by the caller, looking at no more than FolderProbePages pages of FolderProbeSize documents.
    private async Task<bool> HoldsReadableAsync(string? tenant, BpmnDocumentPath folder, CancellationToken cancellationToken)
    {
        var probe = new BpmnDocumentQuery(tenant, folder, Recursive: true, Limit: FolderProbeSize);

        for (var page = 0; page < FolderProbePages; page++)
        {
            var found = await _store.ListAsync(probe, cancellationToken).ConfigureAwait(false);
            foreach (var entry in found.Entries)
            {
                if (!entry.IsFolder && await _authorizer.IsAllowedAsync(BpmnDocumentAction.Read, new BpmnDocumentKey(tenant, entry.Path), cancellationToken).ConfigureAwait(false)) return true;
            }

            if (found.NextCursor is null) return false;
            probe = probe with { Cursor = found.NextCursor };
        }

        return false;
    }

    /// <summary>Opens a document: its diagram summary, its revision, and what the reader changed or dropped.</summary>
    /// <param name="documentId">The document's logical path.</param>
    /// <param name="cursor">The cursor of the previous page of elements.</param>
    /// <param name="limit">The most elements on this page; capped.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The summary, with the import findings among the diagnostics.</returns>
    public Task<BpmnToolResult<BpmnDiagramSummary>> OpenAsync(string documentId, string? cursor, int? limit, CancellationToken cancellationToken) =>
        RunAsync(
            "bpmn_open",
            documentId,
            async (host, loaded) =>
            {
                var summary = await BpmnTools.GetDiagramAsync(host, new BpmnGetDiagramRequest(cursor, limit), cancellationToken).ConfigureAwait(false);
                return summary with { Diagnostics = [.. summary.Diagnostics, .. loaded.Diagnostics.Select(finding => ImportFinding(finding, host.Limits))] };
            },
            cancellationToken);

    /// <summary>Validates a document with the editor's rules and the registered profiles.</summary>
    /// <param name="documentId">The document's logical path.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The error and warning counts, with each finding.</returns>
    public Task<BpmnToolResult<BpmnValidationSummary>> ValidateAsync(string documentId, CancellationToken cancellationToken) =>
        RunAsync("bpmn_validate", documentId, async (host, _) => await BpmnTools.ValidateAsync(host, cancellationToken).ConfigureAwait(false), cancellationToken);

    /// <summary>Describes one element of a document.</summary>
    /// <param name="documentId">The document's logical path.</param>
    /// <param name="elementId">The element.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>Its type, properties, references and findings.</returns>
    public Task<BpmnToolResult<BpmnElementDescription>> DescribeElementAsync(string documentId, string elementId, CancellationToken cancellationToken) =>
        RunAsync(
            "bpmn_describe_element",
            documentId,
            async (host, _) => await BpmnTools.DescribeElementAsync(host, new BpmnDescribeElementRequest(elementId), cancellationToken).ConfigureAwait(false),
            cancellationToken);

    /// <summary>Returns a document as XML or JSON.</summary>
    /// <param name="documentId">The document's logical path.</param>
    /// <param name="format"><c>xml</c> or <c>json</c>.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The content, as untrusted text.</returns>
    public Task<BpmnToolResult<BpmnExportData>> ExportAsync(string documentId, string? format, CancellationToken cancellationToken) =>
        RunAsync(
            "bpmn_export",
            documentId,
            async (host, _) => await BpmnTools.ExportAsync(host, new BpmnExportRequest(format ?? "xml"), cancellationToken).ConfigureAwait(false),
            cancellationToken);

    // --- shared steps ----------------------------------------------------------------------------

    private async Task<BpmnToolResult<TData>> RunAsync<TData>(
        string tool,
        string documentId,
        Func<BpmnStoredDocumentToolHost, BpmnLoadedDocument, Task<BpmnToolResult<TData>>> handler,
        CancellationToken cancellationToken)
    {
        return Audit(tool, documentId, await GuardAsync(async () =>
        {
            var (_, loaded, failure) = await LoadAsync<TData>(documentId, BpmnDocumentAction.Read, cancellationToken).ConfigureAwait(false);
            if (failure is not null) return failure;

            var host = new BpmnStoredDocumentToolHost(loaded!.Document, loaded.Revision, Origin, _profiles, Limits);
            return await handler(host, loaded).ConfigureAwait(false);
        }).ConfigureAwait(false));
    }

    private const int MaxListPagesSkipped = 20;

    private const string NoTenant = "This caller has no tenant, so there is nothing for them to see.";

    private bool TryResolveTenant(out string? tenant)
    {
        try
        {
            tenant = _tenants.GetTenant();

            // A tenant the stores would refuse, whoever supplied it, is the same as no tenant: a caller we cannot place, not an exception from a store.
            if (tenant is not null && !BpmnDocumentKey.IsValidTenant(tenant))
            {
                tenant = null;
                return false;
            }

            return true;
        }
        catch (BpmnMcpTenantUnavailableException)
        {
            tenant = null;
            return false;
        }
    }

    private BpmnEditOrigin Origin => BpmnEditOrigin.FromAgent(_options.AgentName);

    private BpmnToolLimits Limits => BpmnToolLimits.Default with { MaxElements = _options.MaxElementsPerCall, MaxExportBytes = _options.MaxXmlBytes };

    // Parse, authorize, check the size, then read: in that order, so each refusal costs as little as it can.
    private async Task<(BpmnDocumentKey Key, BpmnLoadedDocument? Loaded, BpmnToolResult<TData>? Failure)> LoadAsync<TData>(
        string documentId,
        BpmnDocumentAction action,
        CancellationToken cancellationToken)
    {
        if (!BpmnDocumentPath.TryParse(documentId, out var path) || !path.IsDocumentName)
        {
            return (default, null, Failed<TData>(BpmnToolCodes.InvalidPath, "The document id is not a valid logical path to a .bpmn, .xml or .json document."));
        }

        if (!TryResolveTenant(out var tenant)) return (default, null, Failed<TData>(BpmnToolCodes.Forbidden, NoTenant));

        var key = new BpmnDocumentKey(tenant, path);
        if (!await _authorizer.IsAllowedAsync(action, key, cancellationToken).ConfigureAwait(false)) return (key, null, NotFound<TData>());

        // The size is known before any content is read, so a huge document costs a stat, not a download.
        var stat = await _store.StatAsync(key, cancellationToken).ConfigureAwait(false);
        if (stat is null) return (key, null, NotFound<TData>());
        if (stat.Size > _options.MaxXmlBytes) return (key, null, Failed<TData>(BpmnToolCodes.TooLarge, "The document is larger than this server reads.", stat.Revision));

        BpmnLoadedDocument? loaded;
        try
        {
            loaded = await _store.LoadDocumentAsync(key, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is BpmnImportException or FormatException or DecoderFallbackException)
        {
            return (key, null, Failed<TData>(BpmnToolCodes.InvalidDocument, "The document cannot be read as BPMN.", stat.Revision));
        }

        return loaded is null ? (key, null, NotFound<TData>()) : (key, loaded, null);
    }

    // A store that cannot be reached is a result the agent can report, not a crash and not a leaked exception message.
    private async Task<BpmnToolResult<TData>> GuardAsync<TData>(Func<Task<BpmnToolResult<TData>>> work)
    {
        try
        {
            return await work().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException))
        {
            // Whatever the store raised (a file system error, a database or blob service failure, a network error): the agent gets a result it can report, and
            // the operator gets the exception in the log, not a message that names a server it should know nothing about.
            LogStoreFailed(exception);
            return Failed<TData>(BpmnToolCodes.StoreUnavailable, "The document store could not be used. Nothing is known about the document; try again, or ask the operator.");
        }
    }

    private static BpmnToolResult<TData> NotFound<TData>() => Failed<TData>(BpmnToolCodes.NotFound, "There is no document with that id.");

    private static BpmnToolResult<TData> Failed<TData>(string code, string message, string? revision = null) =>
        new() { Ok = false, Code = code, Message = message, Revision = revision };

    private BpmnToolResult<TData> Audit<TData>(string tool, string? documentId, BpmnToolResult<TData> result)
    {
        var caller = _tenants.GetCallerId() ?? string.Empty;
        var outcome = result.Ok ? "ok" : result.Code ?? "error";
        LogCall(tool, caller, documentId ?? string.Empty, outcome, result.Revision ?? string.Empty);
        return result;
    }

    private static BpmnToolDiagnostic ImportFinding(BpmnImportDiagnostic finding, BpmnToolLimits limits) => new(
        finding.Severity switch
        {
            BpmnImportSeverity.Info => "IMPORT-INFO",
            BpmnImportSeverity.Degraded => "IMPORT-DEGRADED",
            BpmnImportSeverity.Dropped => "IMPORT-DROPPED",
            _ => "IMPORT-SCHEMA-ERROR",
        },
        finding.Severity switch
        {
            BpmnImportSeverity.Info => "info",
            BpmnImportSeverity.SchemaError => "error",
            _ => "warning",
        },
        BpmnUntrustedText.From(finding.Message, limits.MaxTextLength),
        finding.ElementId);

    [LoggerMessage(Level = LogLevel.Information, Message = "MCP tool {Tool} by '{Caller}' on '{DocumentId}': {Outcome} (revision '{Revision}')")]
    private partial void LogCall(string tool, string caller, string documentId, string outcome, string revision);

    [LoggerMessage(Level = LogLevel.Error, Message = "The document store failed during an MCP tool call.")]
    private partial void LogStoreFailed(Exception exception);
}
