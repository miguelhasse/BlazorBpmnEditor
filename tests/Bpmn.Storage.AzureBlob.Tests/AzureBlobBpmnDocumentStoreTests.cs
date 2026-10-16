using System.Runtime.CompilerServices;
using System.Text;

using Azure.Storage.Blobs;

using Hasseware.AspNetCore.Components.Bpmn.Serialization;
using Hasseware.AspNetCore.Components.Bpmn.Storage.Testing;

namespace Hasseware.AspNetCore.Components.Bpmn.Storage.AzureBlob.Tests;

/// <summary>
/// A blob container held in memory with the service's own semantics for what the store relies on: a new ETag on every write, a create
/// that is refused over an existing blob, an update that is refused at another ETag, each decided atomically.
/// </summary>
internal sealed class FakeBlobContainer : IBpmnBlobContainer
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, (byte[] Bytes, string ETag, DateTimeOffset Modified)> _blobs = new(StringComparer.Ordinal);
    private long _counter;

    public IReadOnlyList<string> Names
    {
        get
        {
            lock (_gate) return [.. _blobs.Keys.Order(StringComparer.Ordinal)];
        }
    }

    public int Reads { get; private set; }

    public ValueTask<BpmnBlob?> ReadAsync(string name, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Reads++;
            return ValueTask.FromResult(_blobs.TryGetValue(name, out var blob) ? new BpmnBlob(blob.Bytes.ToArray(), blob.ETag, blob.Modified) : null);
        }
    }

    public ValueTask<BpmnBlobInfo?> StatAsync(string name, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return ValueTask.FromResult(_blobs.TryGetValue(name, out var blob) ? new BpmnBlobInfo(blob.ETag, blob.Bytes.Length, blob.Modified) : null);
        }
    }

    public ValueTask<BpmnBlobWrite> WriteAsync(string name, byte[] bytes, string? ifMatch, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var exists = _blobs.TryGetValue(name, out var current);
            if (ifMatch is null ? exists : !exists || current.ETag != ifMatch) return ValueTask.FromResult(new BpmnBlobWrite(BpmnBlobOutcome.ConditionFailed));

            var etag = $"\"0x{(++_counter).ToString("X", System.Globalization.CultureInfo.InvariantCulture)}\"";
            var modified = DateTimeOffset.UtcNow;
            _blobs[name] = (bytes.ToArray(), etag, modified);
            return ValueTask.FromResult(new BpmnBlobWrite(BpmnBlobOutcome.Succeeded, etag, modified));
        }
    }

    public ValueTask<BpmnBlobOutcome> DeleteAsync(string name, string? ifMatch, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_blobs.TryGetValue(name, out var current)) return ValueTask.FromResult(BpmnBlobOutcome.NotFound);
            if (ifMatch is not null && current.ETag != ifMatch) return ValueTask.FromResult(BpmnBlobOutcome.ConditionFailed);

            _blobs.Remove(name);
            return ValueTask.FromResult(BpmnBlobOutcome.Succeeded);
        }
    }

    public async IAsyncEnumerable<BpmnBlobListing> ListAsync(string prefix, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        List<BpmnBlobListing> found;
        lock (_gate)
        {
            found = [.. _blobs.Where(blob => blob.Key.StartsWith(prefix, StringComparison.Ordinal)).Select(blob => new BpmnBlobListing(blob.Key, blob.Value.Bytes.Length, blob.Value.Modified))];
        }

        foreach (var item in found) yield return item;
        await Task.CompletedTask.ConfigureAwait(false);
    }
}

/// <summary>The store over the in-memory container, which stands in for the service.</summary>
public sealed class AzureBlobFakeStoreTests : BpmnDocumentStoreContractTests
{
    private readonly FakeBlobContainer _container = new();

    /// <inheritdoc />
    protected override IBpmnDocumentStore CreateStore() => new AzureBlobBpmnDocumentStore(_container, "prod/diagrams");

    private static BpmnDocumentContent Bytes(string text) => new(Encoding.UTF8.GetBytes(text), BpmnFileFormat.Xml);

    [Fact]
    public async Task NamesABlobByPrefixTenantAndLogicalPath()
    {
        var store = CreateStore();

        await store.SaveAsync(Key("claims/a.bpmn"), Bytes("1"), ifMatchRevision: null, Token);
        await store.SaveAsync(new BpmnDocumentKey("acme", Path("claims/a.bpmn")), Bytes("2"), ifMatchRevision: null, Token);

        Assert.Equal(["prod/diagrams/claims/a.bpmn", "prod/diagrams/~tenants/acme/claims/a.bpmn"], _container.Names);
    }

    [Fact]
    public async Task NeverListsATenantsDocumentsInTheDefaultSpace()
    {
        var store = CreateStore();
        await store.SaveAsync(new BpmnDocumentKey("acme", Path("secret.bpmn")), Bytes("1"), ifMatchRevision: null, Token);
        await store.SaveAsync(Key("shared.bpmn"), Bytes("2"), ifMatchRevision: null, Token);

        var listed = await store.ListAsync(new BpmnDocumentQuery(null, Recursive: true), Token);

        Assert.Equal(["shared.bpmn"], listed.Entries.Select(entry => entry.Path.ToString()));
    }

    [Fact]
    public async Task DoesNotCountATenantsBlobsAgainstTheDefaultSpacesListingLimit()
    {
        var store = new AzureBlobBpmnDocumentStore(_container, "prod") { MaxListEntries = 3 };
        for (var index = 0; index < 10; index++) await store.SaveAsync(new BpmnDocumentKey("acme", Path($"d{index}.bpmn")), Bytes("1"), ifMatchRevision: null, Token);
        await store.SaveAsync(Key("shared.bpmn"), Bytes("2"), ifMatchRevision: null, Token);

        var listed = await store.ListAsync(new BpmnDocumentQuery(null, Recursive: true), Token);

        Assert.Equal(["shared.bpmn"], listed.Entries.Select(entry => entry.Path.ToString()));
    }

    [Fact]
    public async Task IgnoresBlobsThatAreNotDocumentsUnderItsPrefix()
    {
        var store = CreateStore();
        await _container.WriteAsync("prod/diagrams/readme.txt", [1], ifMatch: null, Token);
        await _container.WriteAsync("other/elsewhere.bpmn", [1], ifMatch: null, Token);
        await store.SaveAsync(Key("real.bpmn"), Bytes("1"), ifMatchRevision: null, Token);

        var listed = await store.ListAsync(new BpmnDocumentQuery(null, Recursive: true), Token);

        Assert.Equal(["real.bpmn"], listed.Entries.Select(entry => entry.Path.ToString()));
    }

    [Fact]
    public async Task ReportsTheServicesETagAsTheRevisionAndRefusesAStaleOne()
    {
        var store = CreateStore();
        var first = await store.SaveAsync(Key("a.bpmn"), Bytes("one"), ifMatchRevision: null, Token);
        var second = await store.SaveAsync(Key("a.bpmn"), Bytes("two"), first.Revision, Token);

        var stale = await store.SaveAsync(Key("a.bpmn"), Bytes("three"), first.Revision, Token);

        Assert.StartsWith("\"0x", first.Revision, StringComparison.Ordinal);
        Assert.Equal(BpmnStoreStatus.Conflict, stale.Status);
        Assert.Equal(second.Revision, stale.Revision);
        Assert.Equal("two", Text((await store.OpenAsync(Key("a.bpmn"), Token))!.Content));
    }

    [Fact]
    public async Task LetsExactlyOneUpdaterWinWhenManyHoldTheSameRevision()
    {
        var store = CreateStore();
        var revision = (await store.SaveAsync(Key("race.bpmn"), Bytes("base"), ifMatchRevision: null, Token)).Revision;

        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(async index =>
            await store.SaveAsync(Key("race.bpmn"), Bytes("update " + index), revision, Token)));

        Assert.Equal(1, results.Count(result => result.Succeeded));
        Assert.Equal(11, results.Count(result => result.Status == BpmnStoreStatus.Conflict));
    }

    [Fact]
    public async Task RefusesToListMoreThanItIsAllowedToConsider()
    {
        var store = new AzureBlobBpmnDocumentStore(_container) { MaxListEntries = 3 };
        for (var index = 0; index < 5; index++) await store.SaveAsync(Key($"d{index}.bpmn"), Bytes("x"), ifMatchRevision: null, Token);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await store.ListAsync(new BpmnDocumentQuery(null), Token));
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

    [Theory]
    [InlineData("../escape")]
    [InlineData("/rooted")]
    [InlineData("a\\b")]
    public void RefusesAPrefixThatIsNotAFolderPath(string prefix)
    {
        Assert.Throws<ArgumentException>(() => new AzureBlobBpmnDocumentStore(_container, prefix));
    }
}

/// <summary>
/// The store over a real container, run only when <c>BPMN_AZURITE_CONNECTION</c> names an Azure Storage account or the Azurite emulator
/// that it may create containers in: the contract is the same, and what differs is that the service itself decides each condition.
/// </summary>
public sealed class AzureBlobAzuriteStoreTests : BpmnDocumentStoreContractTests, IAsyncDisposable
{
    private static readonly string? Connection = Environment.GetEnvironmentVariable("BPMN_AZURITE_CONNECTION");

    private BlobContainerClient? _container;

    /// <inheritdoc />
    protected override IBpmnDocumentStore CreateStore()
    {
        if (Connection is null) Assert.Skip("BPMN_AZURITE_CONNECTION is not set.");

        if (_container is null)
        {
            _container = new BlobContainerClient(Connection, "bpmn-" + Guid.NewGuid().ToString("N"));
            _container.CreateIfNotExists();
        }

        return new AzureBlobBpmnDocumentStore(_container);
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null) await _container.DeleteIfExistsAsync();
        GC.SuppressFinalize(this);
    }
}

/// <summary>Working out which document a blob is, which an ingress needs to turn a storage event into a change.</summary>
public sealed class BlobNameTests
{
    [Theory]
    [InlineData("prod/diagrams/claims/a.bpmn", "prod/diagrams", null, "claims/a.bpmn")]
    [InlineData("claims/a.bpmn", null, null, "claims/a.bpmn")]
    [InlineData("prod/~tenants/acme/claims/a.bpmn", "prod", "acme", "claims/a.bpmn")]
    [InlineData("~tenants/acme/a.json", "", "acme", "a.json")]
    public void MapsABlobNameBackToItsKey(string blob, string? prefix, string? tenant, string path)
    {
        Assert.True(AzureBlobBpmnDocumentStore.TryKeyOf(blob, prefix, out var key));

        Assert.Equal(tenant, key.Tenant);
        Assert.Equal(path, key.Path.ToString());
    }

    [Theory]
    [InlineData("other/claims/a.bpmn", "prod")]
    [InlineData("prod/readme.txt", "prod")]
    [InlineData("prod/~tenants/acme", "prod")]
    [InlineData("prod/~tenants//a.bpmn", "prod")]
    [InlineData("prod/../escape.bpmn", "prod")]
    [InlineData("prod/a\b.bpmn", "prod")]
    public void RefusesABlobThatIsNotADocumentOfTheStore(string blob, string? prefix)
    {
        Assert.False(AzureBlobBpmnDocumentStore.TryKeyOf(blob, prefix, out _));
    }

    [Fact]
    public async Task IsTheInverseOfTheNameTheStoreGivesADocument()
    {
        var container = new FakeBlobContainer();
        var store = new AzureBlobBpmnDocumentStore(container, "prod");
        var key = new BpmnDocumentKey("acme", BpmnDocumentPath.Parse("claims/2026/a.bpmn"));
        await store.SaveAsync(key, new BpmnDocumentContent(new byte[] { 1 }, BpmnFileFormat.Xml), ifMatchRevision: null, TestContext.Current.CancellationToken);

        Assert.True(AzureBlobBpmnDocumentStore.TryKeyOf(container.Names.Single(), "prod", out var mapped));
        Assert.Equal(key, mapped);
    }
}
