using System.Text.Json;

using Hasseware.AspNetCore.Components.Bpmn.Storage;
using Hasseware.AspNetCore.Components.Bpmn.Storage.FileSystem;
using Hasseware.AspNetCore.Components.Bpmn.Tools;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using static Hasseware.AspNetCore.Components.Bpmn.Mcp.Tests.McpHarness;

namespace Hasseware.AspNetCore.Components.Bpmn.Mcp.Tests;

/// <summary>The tools that change stored documents: what is saved, at which revision, and what is refused.</summary>
public abstract class McpWriteTests : IDisposable
{
    protected abstract IBpmnDocumentStore CreateStore();

    public virtual void Dispose() => GC.SuppressFinalize(this);

    private static readonly string[] DeleteReview = ["review"];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task<(McpHarness Server, string Revision)> OpenAsync(Action<BpmnMcpOptions>? configure = null, Action<IServiceCollection>? services = null, ILoggerProvider? logs = null)
    {
        var server = await StartAsync(CreateStore(), configure, logs, writes: true, services);
        await server.SaveAsync("a.bpmn", Diagram());
        return (server, (await server.Store.Inner.StatAsync(Key("a.bpmn"), Token))!.Revision);
    }

    private static async Task<string> StoredRevisionAsync(McpHarness server, string id = "a.bpmn") => (await server.Store.Inner.StatAsync(Key(id), Token))!.Revision;

    private static async Task<string?> NameOfAsync(McpHarness server, string elementId, string id = "a.bpmn")
    {
        var described = (await server.CallAsync("bpmn_describe_element", ("documentId", id), ("elementId", elementId))).Result;
        return described.GetProperty("data").GetProperty("properties").EnumerateArray()
            .Where(property => property.GetProperty("name").GetString() == "name")
            .Select(property => property.GetProperty("value").GetProperty("untrustedText").GetString())
            .FirstOrDefault();
    }

    // --- one edit ----------------------------------------------------------------------------------

    [Fact]
    public async Task SavesAPropertyAndReportsTheRevisionThatIsNowStored()
    {
        var (server, before) = await OpenAsync();
        await using var _ = server;

        var (result, isError, _) = await server.CallAsync("bpmn_set_property", ("documentId", "a.bpmn"), ("elementId", "review"), ("name", "name"), ("value", "Looked at"));

        Assert.False(isError);
        Assert.True(result.GetProperty("ok").GetBoolean(), result.ToString());
        Assert.True(result.GetProperty("applied").GetBoolean());
        var after = result.GetProperty("revision").GetString();
        Assert.NotEqual(before, after);
        Assert.Equal(await StoredRevisionAsync(server), after);
        Assert.Equal("Looked at", await NameOfAsync(server, "review"));
    }

    [Fact]
    public async Task CreatesAnElementConnectedFromItsAnchor()
    {
        var (server, _) = await OpenAsync();
        await using var _ = server;

        var created = (await server.CallAsync("bpmn_create_element", ("documentId", "a.bpmn"), ("type", "task"), ("name", "Archive"), ("anchorId", "review"), ("connectFromAnchor", true))).Result;

        Assert.True(created.GetProperty("ok").GetBoolean(), created.ToString());
        var id = created.GetProperty("data").GetProperty("elementId").GetString()!;
        Assert.NotNull(created.GetProperty("data").GetProperty("connectorId").GetString());
        Assert.Equal("Archive", await NameOfAsync(server, id));
    }

    [Fact]
    public async Task ConnectsTwoElements()
    {
        var (server, _) = await OpenAsync();
        await using var _ = server;

        var connected = (await server.CallAsync("bpmn_connect", ("documentId", "a.bpmn"), ("sourceId", "start"), ("targetId", "end"))).Result;

        Assert.True(connected.GetProperty("ok").GetBoolean(), connected.ToString());
        Assert.Equal("sequenceFlow", connected.GetProperty("data").GetProperty("kind").GetString());
    }

    [Fact]
    public async Task DeletesElementsAndListsWhatWentWithThem()
    {
        var (server, _) = await OpenAsync();
        await using var _ = server;

        var deleted = (await server.CallAsync("bpmn_delete", ("documentId", "a.bpmn"), ("elementIds", DeleteReview))).Result;

        Assert.True(deleted.GetProperty("ok").GetBoolean(), deleted.ToString());
        var gone = deleted.GetProperty("data").GetProperty("deletedIds").EnumerateArray().Select(id => id.GetString()).ToList();
        Assert.Contains("review", gone);
        Assert.Contains("f1", gone);
        Assert.Equal(BpmnToolCodes.NotFound, (await server.CallAsync("bpmn_describe_element", ("documentId", "a.bpmn"), ("elementId", "review"))).Result.GetProperty("code").GetString());
    }

    [Fact]
    public async Task WritesNothingWhenTheEditorRefusesTheEdit()
    {
        var (server, before) = await OpenAsync();
        await using var _ = server;

        var (illegal, illegalError, _) = await server.CallAsync("bpmn_connect", ("documentId", "a.bpmn"), ("sourceId", "end"), ("targetId", "start"));
        var (missing, missingError, _) = await server.CallAsync("bpmn_set_property", ("documentId", "a.bpmn"), ("elementId", "nope"), ("name", "name"), ("value", "x"));

        Assert.True(illegalError);
        Assert.Equal(BpmnToolCodes.IllegalConnection, illegal.GetProperty("code").GetString());
        Assert.True(missingError);
        Assert.Equal(BpmnToolCodes.NotFound, missing.GetProperty("code").GetString());
        Assert.Equal(before, await StoredRevisionAsync(server));
        Assert.Equal(0, server.Store.Saves);
    }

    // --- concurrency -------------------------------------------------------------------------------

    [Fact]
    public async Task RefusesAnEditMadeAgainstARevisionThatHasMovedOn()
    {
        var (server, first) = await OpenAsync();
        await using var _ = server;
        await server.CallAsync("bpmn_set_property", ("documentId", "a.bpmn"), ("elementId", "review"), ("name", "name"), ("value", "Theirs"));

        var (result, isError, _) = await server.CallAsync("bpmn_set_property", ("documentId", "a.bpmn"), ("elementId", "review"), ("name", "name"), ("value", "Mine"), ("expectedRevision", first));

        Assert.True(isError);
        Assert.Equal(BpmnToolCodes.PersistConflict, result.GetProperty("code").GetString());
        Assert.Equal(await StoredRevisionAsync(server), result.GetProperty("revision").GetString());
        Assert.Equal("Theirs", await NameOfAsync(server, "review"));
    }

    [Fact]
    public async Task AcceptsTheRevisionOfTheLastEdit()
    {
        var (server, first) = await OpenAsync();
        await using var _ = server;

        var one = (await server.CallAsync("bpmn_set_property", ("documentId", "a.bpmn"), ("elementId", "review"), ("name", "name"), ("value", "One"), ("expectedRevision", first))).Result;
        var two = (await server.CallAsync("bpmn_set_property", ("documentId", "a.bpmn"), ("elementId", "review"), ("name", "name"), ("value", "Two"), ("expectedRevision", one.GetProperty("revision").GetString()))).Result;

        Assert.True(two.GetProperty("ok").GetBoolean(), two.ToString());
        Assert.Equal("Two", await NameOfAsync(server, "review"));
    }

    [Fact]
    public async Task LetsExactlyOneOfManyWritersHoldingTheSameRevisionWin()
    {
        var (server, revision) = await OpenAsync();
        await using var _ = server;

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(async index =>
            (await server.CallAsync("bpmn_set_property", ("documentId", "a.bpmn"), ("elementId", "review"), ("name", "name"), ("value", "Writer " + index), ("expectedRevision", revision))).Result));

        Assert.Equal(1, results.Count(result => result.GetProperty("ok").GetBoolean()));
        Assert.Equal(5, results.Count(result => result.TryGetProperty("code", out var code) && code.GetString() == BpmnToolCodes.PersistConflict));
    }

    [Fact]
    public async Task NeverReportsAnEditAsSavedThatAnotherWriterOverwrote()
    {
        var (server, _) = await OpenAsync();
        await using var _ = server;

        // Without a named revision each call still saves only at the revision it read, so a lost race is a conflict, not a lost edit.
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async index =>
            (await server.CallAsync("bpmn_create_element", ("documentId", "a.bpmn"), ("type", "task"), ("name", "T" + index), ("anchorId", "review"))).Result));

        var winners = results.Where(result => result.GetProperty("ok").GetBoolean()).Select(result => result.GetProperty("data").GetProperty("elementId").GetString()).ToList();
        Assert.NotEmpty(winners);
        var listed = (await server.CallAsync("bpmn_open", ("documentId", "a.bpmn"))).Result.GetProperty("data").GetProperty("elements").EnumerateArray().Select(element => element.GetProperty("id").GetString()).ToList();
        Assert.All(winners, id => Assert.Contains(id, listed));
        Assert.All(results.Where(result => !result.GetProperty("ok").GetBoolean()), result => Assert.Equal(BpmnToolCodes.PersistConflict, result.GetProperty("code").GetString()));
    }

    [Fact]
    public async Task RefusesAnEditThatWouldMakeTheDocumentTooLarge()
    {
        var (server, _) = await OpenAsync();
        await using var _ = server;
        await server.CallAsync("bpmn_set_property", ("documentId", "a.bpmn"), ("elementId", "review"), ("name", "name"), ("value", "Resave"));
        var stored = await server.Store.Inner.StatAsync(Key("a.bpmn"), Token);
        server.Options.MaxXmlBytes = (int)stored!.Size + 20;

        var (result, isError, _) = await server.CallAsync("bpmn_create_element", ("documentId", "a.bpmn"), ("type", "task"), ("anchorId", "review"));

        Assert.True(isError);
        Assert.Equal(BpmnToolCodes.TooLarge, result.GetProperty("code").GetString());
        Assert.Equal(stored.Revision, await StoredRevisionAsync(server));
    }

    [Fact]
    public async Task SaysAWriteMayHaveHappenedWhenTheStoreFailsWhileSaving()
    {
        var (server, before) = await OpenAsync();
        await using var _ = server;
        server.Store.SaveFailure = new IOException("connection string=secret-detail");

        var (result, isError, _) = await server.CallAsync("bpmn_set_property", ("documentId", "a.bpmn"), ("elementId", "review"), ("name", "name"), ("value", "x"));

        Assert.True(isError);
        Assert.Equal(BpmnToolCodes.StoreUnavailable, result.GetProperty("code").GetString());
        Assert.Contains("may or may not", result.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("secret-detail", result.ToString(), StringComparison.Ordinal);
        Assert.Equal(before, await StoredRevisionAsync(server));
    }

    // --- batch -------------------------------------------------------------------------------------

    private static object[] Operations(params string[] json) => [.. json.Select(text => JsonElement.Parse(text))];

    [Fact]
    public async Task AppliesABatchWhereLaterStepsUseWhatEarlierOnesMadeAndSavesOnce()
    {
        var (server, before) = await OpenAsync();
        await using var _ = server;
        var saves = server.Store.Saves;

        var (result, isError, _) = await server.CallAsync(
            "bpmn_apply_batch",
            ("documentId", "a.bpmn"),
            ("operations", Operations(
                """{"op":"createElement","arguments":{"type":"task","name":"Archive","anchorId":"review","connectFromAnchor":true},"as":"archive"}""",
                """{"op":"createElement","arguments":{"type":"endEvent","anchorId":"@archive","connectFromAnchor":true},"as":"done"}""",
                """{"op":"setProperty","arguments":{"elementId":"@archive","name":"name","value":"Archived"}}""")));

        Assert.False(isError);
        Assert.True(result.GetProperty("ok").GetBoolean(), result.ToString());
        var steps = result.GetProperty("data").GetProperty("steps");
        Assert.Equal(3, steps.GetArrayLength());
        Assert.Equal("archive", steps[0].GetProperty("alias").GetString());
        Assert.Equal(saves + 1, server.Store.Saves);
        Assert.NotEqual(before, await StoredRevisionAsync(server));
        Assert.Equal("Archived", await NameOfAsync(server, steps[0].GetProperty("elementId").GetString()!));
    }

    [Fact]
    public async Task SavesNoneOfABatchWhenAStepIsRefused()
    {
        var (server, before) = await OpenAsync();
        await using var _ = server;
        var saves = server.Store.Saves;

        var (result, isError, _) = await server.CallAsync(
            "bpmn_apply_batch",
            ("documentId", "a.bpmn"),
            ("operations", Operations(
                """{"op":"createElement","arguments":{"type":"task","name":"Orphan","anchorId":"review"}}""",
                """{"op":"connect","arguments":{"sourceId":"end","targetId":"start"}}""")));

        Assert.True(isError);
        Assert.Equal(BpmnToolCodes.IllegalConnection, result.GetProperty("code").GetString());
        Assert.Equal(1, result.GetProperty("data").GetProperty("failedAt").GetInt32());
        Assert.Equal(saves, server.Store.Saves);
        Assert.Equal(before, await StoredRevisionAsync(server));
    }

    [Theory]
    [InlineData("""{"op":"connect","arguments":{"sourceId":"@nobody","targetId":"end"}}""")]
    [InlineData("""{"op":"explode","arguments":{}}""")]
    [InlineData("""{"op":"createElement","arguments":{"type":"task"},"as":"1bad"}""")]
    [InlineData("""{"op":"setProperty","arguments":{"elementId":"review","name":"name","value":"x"},"as":"nothing_made"}""")]
    public async Task RejectsABatchWithAnUnknownAliasOperationOrAliasName(string operation)
    {
        var (server, before) = await OpenAsync();
        await using var _ = server;

        var (result, isError, _) = await server.CallAsync("bpmn_apply_batch", ("documentId", "a.bpmn"), ("operations", Operations(operation)));

        Assert.True(isError);
        Assert.Equal(BpmnToolCodes.InvalidArgument, result.GetProperty("code").GetString());
        Assert.Equal(before, await StoredRevisionAsync(server));
    }

    [Fact]
    public async Task RejectsAnEmptyBatchAndOneThatIsTooLong()
    {
        var (server, _) = await OpenAsync();
        await using var _ = server;
        var one = """{"op":"createElement","arguments":{"type":"task","anchorId":"review"}}""";

        var empty = (await server.CallAsync("bpmn_apply_batch", ("documentId", "a.bpmn"), ("operations", Array.Empty<object>()))).Result;
        var tooMany = (await server.CallAsync("bpmn_apply_batch", ("documentId", "a.bpmn"), ("operations", Operations([.. Enumerable.Repeat(one, 51)])))).Result;

        Assert.Equal(BpmnToolCodes.InvalidArgument, empty.GetProperty("code").GetString());
        Assert.Equal(BpmnToolCodes.TooLarge, tooMany.GetProperty("code").GetString());
    }

    // --- new documents -----------------------------------------------------------------------------

    [Fact]
    public async Task CreatesADocumentOnlyWhereThereIsNone()
    {
        var (server, _) = await OpenAsync();
        await using var _ = server;

        var created = (await server.CallAsync("bpmn_create_document", ("documentId", "new/folder/fresh.bpmn"), ("name", "Fresh"))).Result;
        var opened = (await server.CallAsync("bpmn_open", ("documentId", "new/folder/fresh.bpmn"))).Result;
        var again = (await server.CallAsync("bpmn_create_document", ("documentId", "new/folder/fresh.bpmn"))).Result;
        var overExisting = (await server.CallAsync("bpmn_create_document", ("documentId", "a.bpmn"))).Result;
        var badId = (await server.CallAsync("bpmn_create_document", ("documentId", "../escape.bpmn"))).Result;

        Assert.True(created.GetProperty("ok").GetBoolean(), created.ToString());
        Assert.True(opened.GetProperty("ok").GetBoolean(), opened.ToString());
        Assert.Equal(created.GetProperty("revision").GetString(), opened.GetProperty("revision").GetString());
        Assert.Equal(BpmnToolCodes.PersistConflict, again.GetProperty("code").GetString());
        Assert.Equal(BpmnToolCodes.PersistConflict, overExisting.GetProperty("code").GetString());
        Assert.Equal(BpmnToolCodes.InvalidPath, badId.GetProperty("code").GetString());
        Assert.Equal("Diagram_1", opened.GetProperty("data").GetProperty("diagrams")[0].GetProperty("id").GetString());
    }

    // --- switched off ------------------------------------------------------------------------------

    [Fact]
    public async Task RefusesEveryWriteWhenTheHostDidNotAllowWriting()
    {
        var (server, before) = await OpenAsync(options => options.AllowWrites = false);
        await using var _ = server;
        var calls = new (string Tool, (string, object?)[] Arguments)[]
        {
            ("bpmn_set_property", [("documentId", "a.bpmn"), ("elementId", "review"), ("name", "name"), ("value", "x")]),
            ("bpmn_create_element", [("documentId", "a.bpmn"), ("type", "task")]),
            ("bpmn_connect", [("documentId", "a.bpmn"), ("sourceId", "start"), ("targetId", "end")]),
            ("bpmn_delete", [("documentId", "a.bpmn"), ("elementIds", DeleteReview)]),
            ("bpmn_create_document", [("documentId", "b.bpmn")]),
        };

        foreach (var (tool, arguments) in calls)
        {
            var (result, isError, _) = await server.CallAsync(tool, arguments);

            Assert.True(isError, tool);
            Assert.Equal(BpmnToolCodes.ReadOnly, result.GetProperty("code").GetString());
        }

        Assert.Equal(before, await StoredRevisionAsync(server));
        Assert.Equal(0, server.Store.Saves);
    }

    [Fact]
    public async Task ListsTheWritingToolsWithTheirHints()
    {
        var (server, _) = await OpenAsync();
        await using var _ = server;

        var tools = (await server.Client.ListToolsAsync(cancellationToken: Token)).ToDictionary(tool => tool.Name);

        Assert.Equal(
            ["bpmn_apply_batch", "bpmn_connect", "bpmn_create_document", "bpmn_create_element", "bpmn_delete", "bpmn_describe_element", "bpmn_diff", "bpmn_export", "bpmn_list_documents", "bpmn_open", "bpmn_set_property", "bpmn_validate"],
            tools.Keys.Order(StringComparer.Ordinal));
        Assert.False(tools["bpmn_set_property"].ProtocolTool.Annotations!.ReadOnlyHint);
        Assert.True(tools["bpmn_set_property"].ProtocolTool.Annotations!.IdempotentHint);
        Assert.True(tools["bpmn_delete"].ProtocolTool.Annotations!.DestructiveHint);
        Assert.True(tools["bpmn_apply_batch"].ProtocolTool.Annotations!.DestructiveHint);
        Assert.False(tools["bpmn_create_element"].ProtocolTool.Annotations!.DestructiveHint);
        Assert.All(tools.Values, tool => Assert.DoesNotContain("tenant", tool.JsonSchema.GetProperty("properties").EnumerateObject().Select(property => property.Name), StringComparer.OrdinalIgnoreCase));
    }

    // --- who may do what ---------------------------------------------------------------------------

    private sealed class Denying(params BpmnDocumentAction[] denied) : IBpmnDocumentAuthorizer
    {
        public ValueTask<bool> IsAllowedAsync(BpmnDocumentAction action, BpmnDocumentKey key, CancellationToken cancellationToken) =>
            ValueTask.FromResult(!denied.Contains(action) && !key.Path.ToString().StartsWith("secret", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnswersADeniedWriteLikeAMissingDocumentWithoutTouchingTheStore()
    {
        var (server, _) = await OpenAsync(services: collection => collection.AddSingleton<IBpmnDocumentAuthorizer>(new Denying(BpmnDocumentAction.Write)));
        await using var _ = server;
        var opens = server.Store.Opens;
        var stats = server.Store.Stats;

        var (result, isError, _) = await server.CallAsync("bpmn_set_property", ("documentId", "a.bpmn"), ("elementId", "review"), ("name", "name"), ("value", "x"));

        Assert.True(isError);
        Assert.Equal(BpmnToolCodes.NotFound, result.GetProperty("code").GetString());
        Assert.Equal((opens, stats), (server.Store.Opens, server.Store.Stats));
        Assert.True((await server.CallAsync("bpmn_open", ("documentId", "a.bpmn"))).Result.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task HidesADocumentTheCallerMayNotReadAndRefusesToCreateWhereForbidden()
    {
        var (server, _) = await OpenAsync(services: collection => collection.AddSingleton<IBpmnDocumentAuthorizer>(new Denying(BpmnDocumentAction.Create)));
        await using var _ = server;
        await server.SaveAsync("secret/plans.bpmn", Diagram());

        var opened = (await server.CallAsync("bpmn_open", ("documentId", "secret/plans.bpmn"))).Result;
        var listed = (await server.CallAsync("bpmn_list_documents", ("recursive", true))).Result.GetProperty("data").GetProperty("entries");
        var created = (await server.CallAsync("bpmn_create_document", ("documentId", "c.bpmn"))).Result;
        var diffed = (await server.CallAsync("bpmn_diff", ("documentId", "a.bpmn"), ("baselineDocumentId", "secret/plans.bpmn"))).Result;

        Assert.Equal(BpmnToolCodes.NotFound, opened.GetProperty("code").GetString());
        Assert.Equal(["a.bpmn"], listed.EnumerateArray().Select(entry => entry.GetProperty("path").GetProperty("untrustedText").GetString()));
        Assert.Equal(BpmnToolCodes.Forbidden, created.GetProperty("code").GetString());
        Assert.Equal(BpmnToolCodes.NotFound, diffed.GetProperty("code").GetString());
        Assert.Null(await server.Store.Inner.StatAsync(Key("c.bpmn"), Token));
    }

    private sealed class NoTenant : IBpmnMcpTenantResolver
    {
        public string? GetTenant() => throw new BpmnMcpTenantUnavailableException();
    }

    [Fact]
    public async Task RefusesACallerWhoseTenantCannotBeTold()
    {
        var (server, _) = await OpenAsync(services: collection => collection.AddSingleton<IBpmnMcpTenantResolver>(new NoTenant()));
        await using var _ = server;

        foreach (var (tool, arguments) in new (string, (string, object?)[])[]
        {
            ("bpmn_list_documents", []),
            ("bpmn_open", [("documentId", "a.bpmn")]),
            ("bpmn_set_property", [("documentId", "a.bpmn"), ("elementId", "review"), ("name", "name"), ("value", "x")]),
            ("bpmn_create_document", [("documentId", "c.bpmn")]),
        })
        {
            var (result, isError, _) = await server.CallAsync(tool, arguments);

            Assert.True(isError, tool);
            Assert.Equal(BpmnToolCodes.Forbidden, result.GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task DoesNotCallAFailureBeforeTheSaveAnAmbiguousWrite()
    {
        var (server, _) = await OpenAsync();
        await using var _ = server;
        server.Store.Failure = new IOException("down");

        var (result, isError, _) = await server.CallAsync("bpmn_set_property", ("documentId", "a.bpmn"), ("elementId", "review"), ("name", "name"), ("value", "x"));

        Assert.True(isError);
        Assert.Equal(BpmnToolCodes.StoreUnavailable, result.GetProperty("code").GetString());
        Assert.DoesNotContain("may or may not", result.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal(0, server.Store.Saves);
    }

    [Fact]
    public async Task RefusesToCreateADocumentLargerThanTheServerStores()
    {
        var (server, _) = await OpenAsync(options => options.MaxXmlBytes = 100);
        await using var _ = server;

        var (result, isError, _) = await server.CallAsync("bpmn_create_document", ("documentId", "huge.bpmn"));

        Assert.True(isError);
        Assert.Equal(BpmnToolCodes.TooLarge, result.GetProperty("code").GetString());
        Assert.Null(await server.Store.Inner.StatAsync(Key("huge.bpmn"), Token));
    }

    [Fact]
    public async Task NeverReturnsAnEmptyPageWithACursorBecauseTheCallerMaySeeNothingOnIt()
    {
        var (server, _) = await OpenAsync(services: collection => collection.AddSingleton<IBpmnDocumentAuthorizer>(new Denying()));
        await using var _ = server;
        await server.SaveAsync("secret-a.bpmn", Diagram());
        await server.SaveAsync("secret-b.bpmn", Diagram());

        var page = (await server.CallAsync("bpmn_list_documents", ("limit", 1))).Result.GetProperty("data");

        Assert.NotEmpty(page.GetProperty("entries").EnumerateArray());
        Assert.Equal("a.bpmn", page.GetProperty("entries")[0].GetProperty("path").GetProperty("untrustedText").GetString());
    }

    // --- text and logs -----------------------------------------------------------------------------

    [Fact]
    public async Task KeepsHostileTextInsideUntrustedTextInWriteResults()
    {
        var (server, _) = await OpenAsync();
        await using var _ = server;

        var created = (await server.CallAsync("bpmn_create_element", ("documentId", "a.bpmn"), ("type", "task"), ("name", Injection), ("anchorId", "review"))).Result;
        var set = (await server.CallAsync("bpmn_set_property", ("documentId", "a.bpmn"), ("elementId", "review"), ("name", "documentation"), ("value", Injection))).Result;
        var batch = (await server.CallAsync("bpmn_apply_batch", ("documentId", "a.bpmn"), ("operations", Operations(JsonSerializer.Serialize(new { op = "setProperty", arguments = new { elementId = "review", name = "name", value = Injection } }))))).Result;

        foreach (var result in new[] { created, set, batch })
        {
            Assert.All(StringsOutsideUntrustedText(result), text => Assert.DoesNotContain("IGNORE PREVIOUS", text, StringComparison.Ordinal));
        }
    }

    private sealed class CapturingLogs : ILoggerProvider
    {
        public List<string> Lines { get; } = [];

        public ILogger CreateLogger(string categoryName) => new Capture(this);

        public void Dispose()
        {
        }

        private sealed class Capture(CapturingLogs owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (owner.Lines) owner.Lines.Add(formatter(state, exception));
            }
        }
    }

    private sealed class NamedCaller : IBpmnMcpTenantResolver
    {
        public string? GetTenant() => null;

        public string? GetCallerId() => "alice@example";
    }

    [Fact]
    public async Task LogsWhoChangedWhichDocumentFromWhichRevisionToWhichAndNeverTheContent()
    {
        var logs = new CapturingLogs();
        var (server, before) = await OpenAsync(services: collection => collection.AddSingleton<IBpmnMcpTenantResolver>(new NamedCaller()), logs: logs);
        await using var _ = server;

        var result = (await server.CallAsync("bpmn_set_property", ("documentId", "a.bpmn"), ("elementId", "review"), ("name", "name"), ("value", "TOP-SECRET-NAME"))).Result;

        var after = result.GetProperty("revision").GetString()!;
        string line;
        lock (logs.Lines) line = Assert.Single(logs.Lines, text => text.Contains("bpmn_set_property", StringComparison.Ordinal));
        Assert.Contains("alice@example", line, StringComparison.Ordinal);
        Assert.Contains("a.bpmn", line, StringComparison.Ordinal);
        Assert.Contains(before, line, StringComparison.Ordinal);
        Assert.Contains(after, line, StringComparison.Ordinal);
        lock (logs.Lines) Assert.DoesNotContain(logs.Lines, text => text.Contains("TOP-SECRET-NAME", StringComparison.Ordinal));
    }

    // --- diff --------------------------------------------------------------------------------------

    [Fact]
    public async Task ComparesTwoDocumentsByElementId()
    {
        var (server, _) = await OpenAsync();
        await using var _ = server;
        await server.SaveAsync("b.bpmn", Diagram("Renamed"));

        var diff = (await server.CallAsync("bpmn_diff", ("documentId", "b.bpmn"), ("baselineDocumentId", "a.bpmn"))).Result;

        Assert.True(diff.GetProperty("ok").GetBoolean(), diff.ToString());
        var changed = diff.GetProperty("data").GetProperty("changed").EnumerateArray().Single();
        Assert.Equal("review", changed.GetProperty("id").GetString());
        Assert.Empty(diff.GetProperty("data").GetProperty("added").EnumerateArray());
        Assert.Empty(diff.GetProperty("data").GetProperty("removed").EnumerateArray());
        Assert.Contains("Renamed", changed.ToString(), StringComparison.Ordinal);
        Assert.Equal(await StoredRevisionAsync(server, "b.bpmn"), diff.GetProperty("revision").GetString());
    }
}

/// <summary>The writing tools over the in-memory store.</summary>
public sealed class InMemoryMcpWriteTests : McpWriteTests
{
    protected override IBpmnDocumentStore CreateStore() => new InMemoryBpmnDocumentStore();
}

/// <summary>The writing tools over the file store.</summary>
public sealed class FileMcpWriteTests : McpWriteTests
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bpmn-mcp-write-" + Guid.NewGuid().ToString("N"));

    protected override IBpmnDocumentStore CreateStore() => new FileBpmnDocumentStore(_root);

    public override void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        base.Dispose();
    }
}
