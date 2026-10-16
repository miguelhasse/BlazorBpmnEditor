using System.Text;

using Hasseware.AspNetCore.Components.Bpmn.Serialization;

using Microsoft.EntityFrameworkCore;

using IoPath = System.IO.Path;

namespace Hasseware.AspNetCore.Components.Bpmn.Storage.EfCore.Tests;

/// <summary>
/// A path column that does not compare ordinally, which is what the default collation of SQL Server is: the store has to stay finite and correct even where it cannot be exact.
/// </summary>
public sealed class EfCoreCollationTests : IDisposable
{
    private readonly string _file = IoPath.Combine(IoPath.GetTempPath(), "bpmn-efcore-nocase-" + Guid.NewGuid().ToString("N") + ".db");
    private readonly ContextFactory _factory;

    public EfCoreCollationTests()
    {
        var options = new DbContextOptionsBuilder<BpmnTestDbContext>().UseSqlite($"Data Source={_file};Default Timeout=30;Pooling=False").Options;
        _factory = new ContextFactory(options, "BpmnDocuments", collation: "NOCASE");
        using var context = _factory.CreateDbContext();
        context.Database.EnsureCreated();
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_file)) File.Delete(_file);
    }

    [Fact]
    public async Task ListingAFolderTerminatesWhenTheColumnMatchesAnotherCaseOfIt()
    {
        var store = new EfCoreBpmnDocumentStore<BpmnTestDbContext>(_factory);
        var content = new BpmnDocumentContent(Encoding.UTF8.GetBytes("<x/>"), BpmnFileFormat.Xml);
        await store.SaveAsync(new BpmnDocumentKey(null, BpmnDocumentPath.Parse("A/B/x.bpmn")), content, ifMatchRevision: null, Token);
        await store.SaveAsync(new BpmnDocumentKey(null, BpmnDocumentPath.Parse("a/b/y.bpmn")), content, ifMatchRevision: null, Token);

        // Before the guard, the row A/B/x.bpmn was inside the range but not under the folder a/b, and the walk up to the folder never ended.
        var page = await Task.Run(() => store.ListAsync(new BpmnDocumentQuery(null, BpmnDocumentPath.Parse("a/b"), Recursive: false), Token).AsTask(), Token)
            .WaitAsync(TimeSpan.FromSeconds(20), Token);

        Assert.Equal(["a/b/y.bpmn"], page.Entries.Select(entry => entry.Path.ToString()));
    }
}
