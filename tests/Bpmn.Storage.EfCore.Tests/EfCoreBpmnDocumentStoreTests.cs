using System.Text;

using Hasseware.AspNetCore.Components.Bpmn.Serialization;
using Hasseware.AspNetCore.Components.Bpmn.Storage.Testing;

using Microsoft.EntityFrameworkCore;

using IoPath = System.IO.Path;

namespace Hasseware.AspNetCore.Components.Bpmn.Storage.EfCore.Tests;

/// <summary>A host's context, as the package asks for it.</summary>
public sealed class BpmnTestDbContext(DbContextOptions<BpmnTestDbContext> options, string table, string? collation) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.ApplyBpmnDocuments(table, pathCollation: collation);
}

internal sealed class ContextFactory(DbContextOptions<BpmnTestDbContext> options, string table, string? collation) : IDbContextFactory<BpmnTestDbContext>
{
    public BpmnTestDbContext CreateDbContext() => new(options, table, collation);
}

/// <summary>The store over SQLite, in a file of its own for each test.</summary>
public sealed class EfCoreSqliteStoreTests : BpmnDocumentStoreContractTests, IDisposable
{
    private readonly string _file = IoPath.Combine(IoPath.GetTempPath(), "bpmn-efcore-" + Guid.NewGuid().ToString("N") + ".db");
    private readonly ContextFactory _factory;

    public EfCoreSqliteStoreTests()
    {
        // A busy writer waits for the lock instead of failing, and each operation has its own connection, as a web host's would.
        var options = new DbContextOptionsBuilder<BpmnTestDbContext>().UseSqlite($"Data Source={_file};Default Timeout=30;Pooling=False").Options;
        _factory = new ContextFactory(options, "BpmnDocuments", collation: null);
        using var context = _factory.CreateDbContext();
        context.Database.EnsureCreated();
        context.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
    }

    /// <inheritdoc />
    protected override IBpmnDocumentStore CreateStore() => new EfCoreBpmnDocumentStore<BpmnTestDbContext>(_factory);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            if (File.Exists(_file + suffix)) File.Delete(_file + suffix);
        }
    }

    private static BpmnDocumentContent Bytes(string text) => new(Encoding.UTF8.GetBytes(text), BpmnFileFormat.Xml);

    [Fact]
    public async Task GoesUpByOneWithEveryWriteWhateverTheContent()
    {
        var store = CreateStore();

        var first = await store.SaveAsync(Key("a.bpmn"), Bytes("one"), ifMatchRevision: null, Token);
        var second = await store.SaveAsync(Key("a.bpmn"), Bytes("one"), first.Revision, Token);
        var third = await store.SaveAsync(Key("a.bpmn"), Bytes("two"), second.Revision, Token);

        var start = long.Parse(first.Revision!, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal([start.ToString(System.Globalization.CultureInfo.InvariantCulture), (start + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), (start + 2).ToString(System.Globalization.CultureInfo.InvariantCulture)], new[] { first.Revision, second.Revision, third.Revision });
    }

    [Fact]
    public async Task NeverReusesARevisionForADocumentDeletedAndCreatedAgain()
    {
        var store = CreateStore();
        var first = await store.SaveAsync(Key("a.bpmn"), Bytes("one"), ifMatchRevision: null, Token);
        await store.DeleteAsync(Key("a.bpmn"), first.Revision, Token);
        await Task.Delay(5, Token);

        var again = await store.SaveAsync(Key("a.bpmn"), Bytes("one"), ifMatchRevision: null, Token);
        var staleSave = await store.SaveAsync(Key("a.bpmn"), Bytes("stale"), first.Revision, Token);

        Assert.NotEqual(first.Revision, again.Revision);
        Assert.Equal(BpmnStoreStatus.Conflict, staleSave.Status);
    }

    [Theory]
    [InlineData("not-a-number")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("")]
    public async Task TreatsARevisionThisStoreNeverGaveAsOneThatMatchesNothing(string revision)
    {
        var store = CreateStore();
        var saved = await store.SaveAsync(Key("a.bpmn"), Bytes("one"), ifMatchRevision: null, Token);

        var update = await store.SaveAsync(Key("a.bpmn"), Bytes("two"), revision, Token);
        var delete = await store.DeleteAsync(Key("a.bpmn"), revision, Token);
        var missing = await store.SaveAsync(Key("none.bpmn"), Bytes("x"), revision, Token);

        Assert.Equal(BpmnStoreStatus.Conflict, update.Status);
        Assert.Equal(saved.Revision, update.Revision);
        Assert.Equal(BpmnStoreStatus.Conflict, delete.Status);
        Assert.Equal(BpmnStoreStatus.NotFound, missing.Status);
        Assert.Equal("one", Text((await store.OpenAsync(Key("a.bpmn"), Token))!.Content));
    }

    [Fact]
    public async Task RecordsTheSizeBesideTheContentSoThatAStatNeverLoadsIt()
    {
        var store = CreateStore();
        await store.SaveAsync(Key("big.bpmn"), Bytes(new string('x', 5000)), ifMatchRevision: null, Token);

        var stat = await store.StatAsync(Key("big.bpmn"), Token);

        Assert.Equal(5000, stat!.Size);
    }

    [Fact]
    public async Task ReadsAFolderAsARangeSoThatAnUnderscoreInANameWidensNothing()
    {
        var store = CreateStore();
        await store.SaveAsync(Key("a_b/inside.bpmn"), Bytes("1"), ifMatchRevision: null, Token);
        await store.SaveAsync(Key("axb/outside.bpmn"), Bytes("2"), ifMatchRevision: null, Token);
        await store.SaveAsync(Key("a_b0/near.bpmn"), Bytes("4"), ifMatchRevision: null, Token);

        var listed = await store.ListAsync(new BpmnDocumentQuery(null, Folder: Path("a_b"), Recursive: true), Token);

        Assert.Equal(["a_b/inside.bpmn"], listed.Entries.Select(entry => entry.Path.ToString()));
    }

    [Fact]
    public async Task RefusesToListMoreThanItIsAllowedToConsiderInsteadOfSilentlyCuttingTheListing()
    {
        var store = new EfCoreBpmnDocumentStore<BpmnTestDbContext>(_factory) { MaxListEntries = 3 };
        for (var index = 0; index < 5; index++) await store.SaveAsync(Key($"d{index}.bpmn"), Bytes("x"), ifMatchRevision: null, Token);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await store.ListAsync(new BpmnDocumentQuery(null), Token));
    }

    [Fact]
    public async Task LetsExactlyOneUpdaterWinWhenManyStoresHoldTheSameRevision()
    {
        var first = CreateStore();
        var revision = (await first.SaveAsync(Key("race.bpmn"), Bytes("base"), ifMatchRevision: null, Token)).Revision;
        var stores = new[] { first, CreateStore(), CreateStore() };

        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(async index =>
            await stores[index % 3].SaveAsync(Key("race.bpmn"), Bytes("update " + index), revision, Token)));

        Assert.Equal(1, results.Count(result => result.Succeeded));
        Assert.Equal(11, results.Count(result => result.Status == BpmnStoreStatus.Conflict));
    }

    [Fact]
    public async Task LetsExactlyOneCreatorWinWhenManyCreateTheSameDocument()
    {
        var stores = new[] { CreateStore(), CreateStore() };

        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(async index =>
            await stores[index % 2].SaveAsync(Key("created.bpmn"), Bytes("writer " + index), ifMatchRevision: null, Token)));

        Assert.Equal(1, results.Count(result => result.Succeeded));
        Assert.Equal(11, results.Count(result => result.Status == BpmnStoreStatus.Conflict));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a/b")]
    [InlineData("..")]
    public async Task RefusesATenantThatIsNotOneValidSegment(string tenant)
    {
        var store = CreateStore();

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await store.SaveAsync(new BpmnDocumentKey(tenant, Path("a.bpmn")), Bytes("x"), ifMatchRevision: null, Token));
    }
}

/// <summary>
/// The store over SQL Server, run only when <c>BPMN_SQLSERVER_CONNECTION</c> names a server it may create a table on: the contract is the same,
/// and the provider-specific part is the ordinal collation of the path column.
/// </summary>
public sealed class EfCoreSqlServerStoreTests : BpmnDocumentStoreContractTests, IDisposable
{
    private static readonly string? Connection = Environment.GetEnvironmentVariable("BPMN_SQLSERVER_CONNECTION");

    private readonly string _table = "BpmnDocuments_" + Guid.NewGuid().ToString("N");
    private ContextFactory? _factory;

#pragma warning disable EF1002 // The table name is a GUID this test made; there is no user text in the statements.
    private ContextFactory Factory
    {
        get
        {
            if (Connection is null) Assert.Skip("BPMN_SQLSERVER_CONNECTION is not set.");

            if (_factory is null)
            {
                var options = new DbContextOptionsBuilder<BpmnTestDbContext>().UseSqlServer(Connection).Options;
                _factory = new ContextFactory(options, _table, collation: "Latin1_General_100_BIN2");
                using var context = _factory.CreateDbContext();
                context.Database.ExecuteSqlRaw($"CREATE TABLE [{_table}] ([Tenant] nvarchar(128) NOT NULL, [Path] nvarchar(512) COLLATE Latin1_General_100_BIN2 NOT NULL, [Format] nvarchar(8) NOT NULL, [Content] varbinary(max) NOT NULL, [Size] bigint NOT NULL, [Version] bigint NOT NULL, [ModifiedUtc] datetime2 NOT NULL, CONSTRAINT [PK_{_table}] PRIMARY KEY ([Tenant], [Path]))");
            }

            return _factory;
        }
    }

#pragma warning restore EF1002

    /// <inheritdoc />
    protected override IBpmnDocumentStore CreateStore() => new EfCoreBpmnDocumentStore<BpmnTestDbContext>(Factory);

    public void Dispose()
    {
        if (_factory is null) return;

        using var context = _factory.CreateDbContext();
#pragma warning disable EF1002
        context.Database.ExecuteSqlRaw($"DROP TABLE [{_table}]");
#pragma warning restore EF1002
    }
}
