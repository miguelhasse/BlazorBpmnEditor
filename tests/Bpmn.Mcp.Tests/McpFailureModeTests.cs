using Hasseware.AspNetCore.Components.Bpmn.Storage;
using Hasseware.AspNetCore.Components.Bpmn.Tools;

using Microsoft.Extensions.DependencyInjection;

using static Hasseware.AspNetCore.Components.Bpmn.Mcp.Tests.McpHarness;

namespace Hasseware.AspNetCore.Components.Bpmn.Mcp.Tests;

/// <summary>What an agent is told when the store fails in a way the service did not expect, and what it is not told about documents it may not read.</summary>
public sealed class McpFailureModeTests
{
    /// <summary>Fails with whatever a test hands it, as a database or a blob service would.</summary>
    private sealed class FailingStore(IBpmnDocumentStore inner) : IBpmnDocumentStore
    {
        public Exception? OnStat { get; set; }

        public Exception? OnSave { get; set; }

        public ValueTask<BpmnStoredDocument?> OpenAsync(BpmnDocumentKey key, CancellationToken cancellationToken = default) => inner.OpenAsync(key, cancellationToken);

        public ValueTask<BpmnDocumentStat?> StatAsync(BpmnDocumentKey key, CancellationToken cancellationToken = default) =>
            OnStat is { } failure ? throw failure : inner.StatAsync(key, cancellationToken);

        public ValueTask<BpmnStoreResult> SaveAsync(BpmnDocumentKey key, BpmnDocumentContent content, string? ifMatchRevision, CancellationToken cancellationToken = default) =>
            OnSave is { } failure ? throw failure : inner.SaveAsync(key, content, ifMatchRevision, cancellationToken);

        public ValueTask<BpmnDocumentPage> ListAsync(BpmnDocumentQuery query, CancellationToken cancellationToken = default) => inner.ListAsync(query, cancellationToken);

        public ValueTask<BpmnStoreResult> DeleteAsync(BpmnDocumentKey key, string? ifMatchRevision, CancellationToken cancellationToken = default) => inner.DeleteAsync(key, ifMatchRevision, cancellationToken);
    }

    private sealed class FixedTenant(string? tenant) : IBpmnMcpTenantResolver
    {
        public string? GetTenant() => tenant;
    }

    // Reads nothing under secret, writes and creates anywhere.
    private sealed class NoReadingSecrets : IBpmnDocumentAuthorizer
    {
        public ValueTask<bool> IsAllowedAsync(BpmnDocumentAction action, BpmnDocumentKey key, CancellationToken cancellationToken) =>
            ValueTask.FromResult(!(action == BpmnDocumentAction.Read && key.Path.ToString().StartsWith("secret", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task ReportsAStoreFailureOfAnyKindAsAResultTheAgentCanRead()
    {
        var store = new FailingStore(new InMemoryBpmnDocumentStore());
        await using var server = await StartAsync(store);
        await server.SaveAsync("a.bpmn", Diagram());
        store.OnStat = new HttpRequestException("The blob service said 503 for https://secret-account.blob.example/");

        var (result, isError, _) = await server.CallAsync("bpmn_open", ("documentId", "a.bpmn"));

        Assert.True(isError);
        Assert.Equal(BpmnToolCodes.StoreUnavailable, result.GetProperty("code").GetString());
        Assert.DoesNotContain("secret-account", result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaysThatAFailedWriteMayOrMayNotHaveHappenedWhateverTheStoreRaised()
    {
        var store = new FailingStore(new InMemoryBpmnDocumentStore());
        await using var server = await StartAsync(store, writes: true);
        await server.SaveAsync("a.bpmn", Diagram());
        store.OnSave = new HttpRequestException("The connection was reset after the request was sent.");

        var (edit, _, _) = await server.CallAsync("bpmn_set_property", ("documentId", "a.bpmn"), ("elementId", "review"), ("name", "name"), ("value", "x"));
        var (create, _, _) = await server.CallAsync("bpmn_create_document", ("documentId", "b.bpmn"));

        Assert.Equal(BpmnToolCodes.StoreUnavailable, edit.GetProperty("code").GetString());
        Assert.Contains("may or may not", edit.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal(BpmnToolCodes.StoreUnavailable, create.GetProperty("code").GetString());
        Assert.Contains("may or may not", create.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("alice@example.com")]
    [InlineData("a/b")]
    [InlineData("..")]
    public async Task RefusesACallerWhoseTenantNoStoreCouldHoldInsteadOfFailingEveryCall(string tenant)
    {
        await using var server = await StartAsync(new InMemoryBpmnDocumentStore(), writes: true, services: services => services.AddSingleton<IBpmnMcpTenantResolver>(new FixedTenant(tenant)));

        foreach (var (tool, arguments) in new (string, (string, object?)[])[]
        {
            ("bpmn_list_documents", []),
            ("bpmn_open", [("documentId", "a.bpmn")]),
            ("bpmn_create_document", [("documentId", "a.bpmn")]),
        })
        {
            var (result, _, _) = await server.CallAsync(tool, arguments);

            Assert.Equal(BpmnToolCodes.Forbidden, result.GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task DoesNotCallABadTenantABadCursor()
    {
        await using var server = await StartAsync(new InMemoryBpmnDocumentStore(), services: services => services.AddSingleton<IBpmnMcpTenantResolver>(new FixedTenant("a@b.com")));

        var (result, _, _) = await server.CallAsync("bpmn_list_documents");

        Assert.DoesNotContain("cursor", result.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DoesNotListAFolderWithNothingInItTheCallerMayRead()
    {
        await using var server = await StartAsync(new InMemoryBpmnDocumentStore(), services: services => services.AddSingleton<IBpmnDocumentAuthorizer>(new NoReadingSecrets()));
        await server.SaveAsync("a.bpmn", Diagram());
        await server.SaveAsync("secret/plans.bpmn", Diagram());
        await server.SaveAsync("open/notes.bpmn", Diagram());

        var (listed, _, _) = await server.CallAsync("bpmn_list_documents");

        var names = listed.GetProperty("data").GetProperty("entries").EnumerateArray()
            .Select(entry => entry.GetProperty("path").GetProperty("untrustedText").GetString()).Order(StringComparer.Ordinal).ToList();

        // "secret" tells the caller something exists; "open" holds something they may read.
        Assert.Equal(["a.bpmn", "open"], names);
    }

    [Fact]
    public async Task AnswersACreateOnADocumentTheCallerCannotReadWithoutSayingItExists()
    {
        await using var server = await StartAsync(new InMemoryBpmnDocumentStore(), writes: true, services: services => services.AddSingleton<IBpmnDocumentAuthorizer>(new NoReadingSecrets()));
        await server.SaveAsync("secret/plans.bpmn", Diagram());

        var (blocked, _, _) = await server.CallAsync("bpmn_create_document", ("documentId", "secret/plans.bpmn"));
        var (readable, _, _) = await server.CallAsync("bpmn_create_document", ("documentId", "a.bpmn"));
        var (again, _, _) = await server.CallAsync("bpmn_create_document", ("documentId", "a.bpmn"));

        // Not "already exists", and no revision: the same refusal as a place they may not create in.
        Assert.Equal(BpmnToolCodes.Forbidden, blocked.GetProperty("code").GetString());
        Assert.False(blocked.TryGetProperty("revision", out _));
        Assert.True(readable.GetProperty("ok").GetBoolean());

        // For a document they may read, the conflict still says so.
        Assert.Equal(BpmnToolCodes.PersistConflict, again.GetProperty("code").GetString());
    }
}
