using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Storage;

using Microsoft.Extensions.DependencyInjection;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Storage;

/// <summary>
/// Storage registration wraps the store for notifications whatever order the calls come in, and keeps
/// the store's lifetime.
/// </summary>
public sealed class StorageRegistrationTests
{
    [Fact]
    public void WrapsTheStoreWhenChangesAreRegisteredFirst()
    {
        using var provider = new ServiceCollection()
            .AddBpmnDocumentChanges()
            .AddBpmnInMemoryDocumentStore()
            .BuildServiceProvider();

        var store = Assert.IsType<NotifyingBpmnDocumentStore>(provider.GetRequiredService<IBpmnDocumentStore>());
        Assert.IsType<InMemoryBpmnDocumentStore>(store.Inner);
    }

    [Fact]
    public void WrapsTheStoreWhenChangesAreRegisteredLast()
    {
        using var provider = new ServiceCollection()
            .AddBpmnInMemoryDocumentStore()
            .AddBpmnDocumentChanges()
            .BuildServiceProvider();

        Assert.IsType<NotifyingBpmnDocumentStore>(provider.GetRequiredService<IBpmnDocumentStore>());
    }

    [Fact]
    public void LeavesTheStoreUnwrappedWithoutABus()
    {
        using var provider = new ServiceCollection().AddBpmnInMemoryDocumentStore().BuildServiceProvider();

        Assert.IsType<InMemoryBpmnDocumentStore>(provider.GetRequiredService<IBpmnDocumentStore>());
        Assert.Null(provider.GetService<IBpmnDocumentChangeSubscriber>());
    }

    [Fact]
    public void LetsALaterStoreReplaceAnEarlierOne()
    {
        using var provider = new ServiceCollection()
            .AddBpmnInMemoryDocumentStore()
            .AddBpmnDocumentStore<OtherStore>()
            .BuildServiceProvider();

        Assert.IsType<OtherStore>(provider.GetRequiredService<IBpmnDocumentStore>());
    }

    [Fact]
    public void GivesAScopedStoreAScopedWrapper()
    {
        using var provider = new ServiceCollection()
            .AddBpmnDocumentChanges()
            .AddBpmnDocumentStore<OtherStore>(ServiceLifetime.Scoped)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        using var first = provider.CreateScope();
        using var second = provider.CreateScope();
        var a = (NotifyingBpmnDocumentStore)first.ServiceProvider.GetRequiredService<IBpmnDocumentStore>();
        var b = (NotifyingBpmnDocumentStore)second.ServiceProvider.GetRequiredService<IBpmnDocumentStore>();

        Assert.Same(a, first.ServiceProvider.GetRequiredService<IBpmnDocumentStore>());
        Assert.NotSame(a.Inner, b.Inner);
    }

    [Fact]
    public void CreatesBindingsThatFollowTheConfiguredPolicy()
    {
        using var provider = new ServiceCollection()
            .AddBpmnComponents(options => options.Storage = new StorageOptions { ExternalChangePolicy = BpmnExternalChangePolicy.Ignore })
            .AddBpmnInMemoryDocumentStore()
            .AddBpmnDocumentChanges()
            .BuildServiceProvider();
        using var scope = provider.CreateScope();

        var binding = scope.ServiceProvider.GetRequiredService<IBpmnDocumentBindingFactory>().Create();

        Assert.Null(binding.Key);
        Assert.Equal(BpmnChangeFeedState.Live, binding.FeedState);
    }

    private sealed class OtherStore : IBpmnDocumentStore
    {
        private readonly InMemoryBpmnDocumentStore _inner = new();

        public ValueTask<BpmnStoredDocument?> OpenAsync(BpmnDocumentKey key, CancellationToken cancellationToken = default) =>
            _inner.OpenAsync(key, cancellationToken);

        public ValueTask<BpmnDocumentStat?> StatAsync(BpmnDocumentKey key, CancellationToken cancellationToken = default) =>
            _inner.StatAsync(key, cancellationToken);

        public ValueTask<BpmnStoreResult> SaveAsync(BpmnDocumentKey key, BpmnDocumentContent content, string? ifMatchRevision, CancellationToken cancellationToken = default) =>
            _inner.SaveAsync(key, content, ifMatchRevision, cancellationToken);

        public ValueTask<BpmnDocumentPage> ListAsync(BpmnDocumentQuery query, CancellationToken cancellationToken = default) =>
            _inner.ListAsync(query, cancellationToken);

        public ValueTask<BpmnStoreResult> DeleteAsync(BpmnDocumentKey key, string? ifMatchRevision, CancellationToken cancellationToken = default) =>
            _inner.DeleteAsync(key, ifMatchRevision, cancellationToken);
    }
}
