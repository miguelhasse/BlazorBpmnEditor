using System.Text;

using Hasseware.AspNetCore.Components.Bpmn.Serialization;

namespace Hasseware.AspNetCore.Components.Bpmn.Storage.Testing;

/// <summary>
/// What every <see cref="IBpmnDocumentStore"/> must do, whatever it is backed by (design doc,
/// "State model: stateless, store-backed").
/// </summary>
/// <remarks>
/// Derive once per store and implement <see cref="CreateStore"/>; the test runner then runs every case
/// against your store. The suite is written against the contract only, so a store that passes it can be
/// used wherever the editor or an agent server asks for an <see cref="IBpmnDocumentStore"/>.
/// </remarks>
public abstract class BpmnDocumentStoreContractTests
{
    private static readonly BpmnDocumentKey Intake = Key("claims/2026/intake.bpmn");

    /// <summary>Creates an empty store for one test.</summary>
    /// <returns>The store under test.</returns>
    protected abstract IBpmnDocumentStore CreateStore();

    // --- reading and writing ---------------------------------------------------------------

    [Fact]
    public async Task OpensWhatWasSaved()
    {
        var store = CreateStore();

        var saved = await store.SaveAsync(Intake, Content("one"), ifMatchRevision: null, Token);
        var opened = await store.OpenAsync(Intake, Token);

        Assert.True(saved.Succeeded);
        Assert.NotNull(opened);
        Assert.Equal("one", Text(opened.Content));
        Assert.Equal(BpmnFileFormat.Xml, opened.Content.Format);
        Assert.Equal(saved.Revision, opened.Revision);
    }

    [Fact]
    public async Task ListsAnEmptyStoreAsEmpty()
    {
        var store = CreateStore();

        var page = await store.ListAsync(new BpmnDocumentQuery(null), Token);
        var recursive = await store.ListAsync(new BpmnDocumentQuery(null, Folder: Path("nothing/here"), Recursive: true), Token);

        Assert.Empty(page.Entries);
        Assert.Null(page.NextCursor);
        Assert.Empty(recursive.Entries);
    }

    [Fact]
    public async Task ReportsAMissingDocumentAsNull()
    {
        var store = CreateStore();

        Assert.Null(await store.OpenAsync(Intake, Token));
        Assert.Null(await store.StatAsync(Intake, Token));
    }

    [Fact]
    public async Task StatReportsRevisionAndSizeWithoutContent()
    {
        var store = CreateStore();
        var saved = await store.SaveAsync(Intake, Content("12345"), ifMatchRevision: null, Token);

        var stat = await store.StatAsync(Intake, Token);

        Assert.NotNull(stat);
        Assert.Equal(saved.Revision, stat.Revision);
        Assert.Equal(5, stat.Size);
    }

    [Fact]
    public async Task KeepsStoredContentWhenTheCallerReusesItsBuffer()
    {
        var store = CreateStore();
        var buffer = Encoding.UTF8.GetBytes("one");

        await store.SaveAsync(Intake, new BpmnDocumentContent(buffer, BpmnFileFormat.Xml), ifMatchRevision: null, Token);
        buffer[0] = (byte)'X';

        Assert.Equal("one", Text((await store.OpenAsync(Intake, Token))!.Content));
    }

    [Fact]
    public async Task RefusesTheDefaultKey()
    {
        var store = CreateStore();

        await Assert.ThrowsAsync<ArgumentException>(async () => await store.OpenAsync(default, Token));
        await Assert.ThrowsAsync<ArgumentException>(async () => await store.SaveAsync(default, Content("x"), null, Token));
    }

    // --- conditional writes ----------------------------------------------------------------

    [Fact]
    public async Task RefusesACreateWhenTheDocumentExists()
    {
        var store = CreateStore();
        var first = await store.SaveAsync(Intake, Content("one"), ifMatchRevision: null, Token);

        var second = await store.SaveAsync(Intake, Content("two"), ifMatchRevision: null, Token);

        Assert.Equal(BpmnStoreStatus.Conflict, second.Status);
        Assert.Equal(first.Revision, second.Revision);
        Assert.Equal("one", Text((await store.OpenAsync(Intake, Token))!.Content));
    }

    [Fact]
    public async Task UpdatesAtTheRevisionTheCallerHolds()
    {
        var store = CreateStore();
        var first = await store.SaveAsync(Intake, Content("one"), ifMatchRevision: null, Token);

        var second = await store.SaveAsync(Intake, Content("two"), first.Revision, Token);

        Assert.True(second.Succeeded);
        Assert.NotEqual(first.Revision, second.Revision);
        Assert.Equal("two", Text((await store.OpenAsync(Intake, Token))!.Content));
    }

    [Fact]
    public async Task RefusesAnUpdateAtAStaleRevision()
    {
        var store = CreateStore();
        var first = await store.SaveAsync(Intake, Content("one"), ifMatchRevision: null, Token);
        var second = await store.SaveAsync(Intake, Content("two"), first.Revision, Token);

        var stale = await store.SaveAsync(Intake, Content("three"), first.Revision, Token);

        Assert.Equal(BpmnStoreStatus.Conflict, stale.Status);
        Assert.Equal(second.Revision, stale.Revision);
        Assert.Equal("two", Text((await store.OpenAsync(Intake, Token))!.Content));
    }

    [Fact]
    public async Task ReportsAnUpdateOfAMissingDocumentAsNotFound()
    {
        var store = CreateStore();

        var result = await store.SaveAsync(Intake, Content("one"), ifMatchRevision: "anything", Token);

        Assert.Equal(BpmnStoreStatus.NotFound, result.Status);
        Assert.Null(await store.OpenAsync(Intake, Token));
    }

    [Fact]
    public async Task LetsExactlyOneOfManyConcurrentWritersWin()
    {
        var store = CreateStore();
        var first = await store.SaveAsync(Intake, Content("one"), ifMatchRevision: null, Token);

        var results = await Task.WhenAll(Enumerable.Range(0, 16).Select(i =>
            Task.Run(async () => await store.SaveAsync(Intake, Content($"writer {i}"), first.Revision, Token), Token)));

        Assert.Single(results, result => result.Succeeded);
        Assert.All(results.Where(result => !result.Succeeded), result => Assert.Equal(BpmnStoreStatus.Conflict, result.Status));
    }

    [Fact]
    public async Task DeletesOnlyAtTheRevisionTheCallerHolds()
    {
        var store = CreateStore();
        var first = await store.SaveAsync(Intake, Content("one"), ifMatchRevision: null, Token);
        await store.SaveAsync(Intake, Content("two"), first.Revision, Token);

        var stale = await store.DeleteAsync(Intake, first.Revision, Token);
        var unconditional = await store.DeleteAsync(Intake, ifMatchRevision: null, Token);
        var missing = await store.DeleteAsync(Intake, ifMatchRevision: null, Token);

        Assert.Equal(BpmnStoreStatus.Conflict, stale.Status);
        Assert.True(unconditional.Succeeded);
        Assert.Equal(BpmnStoreStatus.NotFound, missing.Status);
        Assert.Null(await store.OpenAsync(Intake, Token));
    }

    [Fact]
    public async Task NeverOverwritesADocumentWhoseNameDiffersOnlyByCase()
    {
        var store = CreateStore();
        await store.SaveAsync(Key("Order.bpmn"), Content("upper"), ifMatchRevision: null, Token);

        // A case-sensitive store keeps both; a case-insensitive one must refuse. Neither may overwrite.
        await store.SaveAsync(Key("order.bpmn"), Content("lower"), ifMatchRevision: null, Token);

        Assert.Equal("upper", Text((await store.OpenAsync(Key("Order.bpmn"), Token))!.Content));
    }

    [Fact]
    public async Task KeepsTenantsApart()
    {
        var store = CreateStore();
        var alpha = new BpmnDocumentKey("alpha", Intake.Path);
        var beta = new BpmnDocumentKey("beta", Intake.Path);
        await store.SaveAsync(alpha, Content("alpha"), ifMatchRevision: null, Token);

        Assert.Null(await store.OpenAsync(beta, Token));
        Assert.Null(await store.OpenAsync(Intake, Token));
        Assert.Empty((await store.ListAsync(new BpmnDocumentQuery("beta", Recursive: true), Token)).Entries);
        Assert.Single((await store.ListAsync(new BpmnDocumentQuery("alpha", Recursive: true), Token)).Entries);
    }

    // --- listing ---------------------------------------------------------------------------

    [Fact]
    public async Task ListsAFoldersSubFoldersFirstThenItsDocuments()
    {
        var store = await SeededAsync("b.bpmn", "a.bpmn", "z/one.bpmn", "y/deep/two.bpmn", "y/three.bpmn");

        var page = await store.ListAsync(new BpmnDocumentQuery(null), Token);

        Assert.Equal(["y/", "z/", "a.bpmn", "b.bpmn"], Describe(page));
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public async Task ListsOneFolderWithoutItsSiblings()
    {
        var store = await SeededAsync("y/deep/two.bpmn", "y/three.bpmn", "z/one.bpmn");

        var page = await store.ListAsync(new BpmnDocumentQuery(null, Folder: Path("y")), Token);

        Assert.Equal(["y/deep/", "y/three.bpmn"], Describe(page));
    }

    [Fact]
    public async Task ListsEveryDocumentBeneathAFolderRecursively()
    {
        var store = await SeededAsync("y/deep/two.bpmn", "y/three.bpmn", "z/one.bpmn", "top.bpmn");

        var page = await store.ListAsync(new BpmnDocumentQuery(null, Folder: Path("y"), Recursive: true), Token);

        Assert.Equal(["y/deep/two.bpmn", "y/three.bpmn"], Describe(page));
        Assert.All(page.Entries, entry => Assert.NotNull(entry.Size));
    }

    [Fact]
    public async Task FiltersByNamePrefix()
    {
        var store = await SeededAsync("order-1.bpmn", "order-2.bpmn", "invoice.bpmn", "orders/x.bpmn");

        var page = await store.ListAsync(new BpmnDocumentQuery(null, NamePrefix: "order"), Token);

        Assert.Equal(["orders/", "order-1.bpmn", "order-2.bpmn"], Describe(page));
    }

    [Fact]
    public async Task PagesWithoutLosingOrRepeatingEntriesWhileTheStoreChanges()
    {
        var store = await SeededAsync("a.bpmn", "c.bpmn", "e.bpmn", "g.bpmn");

        var first = await store.ListAsync(new BpmnDocumentQuery(null, Limit: 2), Token);
        await store.SaveAsync(Key("b.bpmn"), Content("late"), ifMatchRevision: null, Token);
        await store.SaveAsync(Key("f.bpmn"), Content("late"), ifMatchRevision: null, Token);
        var second = await store.ListAsync(new BpmnDocumentQuery(null, Cursor: first.NextCursor, Limit: 10), Token);

        Assert.Equal(["a.bpmn", "c.bpmn"], Describe(first));
        Assert.NotNull(first.NextCursor);
        Assert.Equal(["e.bpmn", "f.bpmn", "g.bpmn"], Describe(second));
    }

    [Fact]
    public async Task CapsThePageSize()
    {
        var store = await SeededAsync([.. Enumerable.Range(0, 3).Select(i => $"d{i}.bpmn")]);

        var page = await store.ListAsync(new BpmnDocumentQuery(null, Limit: 0), Token);

        Assert.Single(page.Entries);
        Assert.NotNull(page.NextCursor);
    }

    // --- helpers -----------------------------------------------------------------------------

    protected static CancellationToken Token => TestContext.Current.CancellationToken;

    protected static BpmnDocumentPath Path(string value) => BpmnDocumentPath.Parse(value);

    protected static BpmnDocumentKey Key(string path) => new(null, BpmnDocumentPath.Parse(path));

    protected static BpmnDocumentContent Content(string text) => new(Encoding.UTF8.GetBytes(text), BpmnFileFormat.Xml);

    protected static string Text(BpmnDocumentContent content) => Encoding.UTF8.GetString(content.Bytes.Span);

    private async Task<IBpmnDocumentStore> SeededAsync(params string[] paths)
    {
        var store = CreateStore();
        foreach (var path in paths) await store.SaveAsync(Key(path), Content(path), ifMatchRevision: null, Token);

        return store;
    }

    private static List<string> Describe(BpmnDocumentPage page) =>
        [.. page.Entries.Select(entry => entry.IsFolder ? entry.Path + "/" : entry.Path.ToString())];
}
