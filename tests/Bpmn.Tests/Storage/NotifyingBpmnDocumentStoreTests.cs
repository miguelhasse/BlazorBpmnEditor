using Hasseware.AspNetCore.Components.Bpmn.Storage;
using Hasseware.AspNetCore.Components.Bpmn.Storage.Testing;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Storage;

/// <summary>
/// The notifying wrapper still meets the store contract, and announces only what actually happened.
/// </summary>
public sealed class NotifyingBpmnDocumentStoreTests : BpmnDocumentStoreContractTests
{
    private static readonly BpmnDocumentKey Intake = new(null, BpmnDocumentPath.Parse("intake.bpmn"));

    /// <inheritdoc />
    protected override IBpmnDocumentStore CreateStore() =>
        new NotifyingBpmnDocumentStore(new InMemoryBpmnDocumentStore(), new InProcessBpmnDocumentChangeBus());

    [Fact]
    public async Task AnnouncesASaveAfterItIsStoredWithTheNewRevision()
    {
        var bus = new InProcessBpmnDocumentChangeBus();
        var store = new NotifyingBpmnDocumentStore(new InMemoryBpmnDocumentStore(), bus);
        var received = new List<BpmnDocumentChange>();
        string? revisionSeenByHandler = null;
        await bus.SubscribeAsync(Intake, async change =>
        {
            revisionSeenByHandler = (await store.StatAsync(Intake, Token))?.Revision;
            received.Add(change);
        }, Token);

        var saved = await store.SaveAsync(Intake, Content("one"), ifMatchRevision: null, Token);

        var change = Assert.Single(received);
        Assert.Equal(BpmnDocumentChangeKind.Updated, change.Kind);
        Assert.Equal(saved.Revision, change.Revision);
        Assert.Equal(saved.Revision, revisionSeenByHandler);
    }

    private sealed class ThrowingBus : IBpmnDocumentChangeBus
    {
        public ValueTask PublishAsync(BpmnDocumentChange change, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The broker is unreachable.");

        public ValueTask<IAsyncDisposable> SubscribeAsync(BpmnDocumentKey key, Func<BpmnDocumentChange, ValueTask> handler, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    [Fact]
    public async Task ASaveAndADeleteStandWhenTheBusCannotBeReached()
    {
        var store = new NotifyingBpmnDocumentStore(new InMemoryBpmnDocumentStore(), new ThrowingBus());

        // The write happened; a notification that cannot be sent must not turn it into a failure the caller would retry into a conflict with itself.
        var saved = await store.SaveAsync(Intake, Content("one"), ifMatchRevision: null, Token);
        var deleted = await store.DeleteAsync(Intake, saved.Revision, Token);

        Assert.True(saved.Succeeded);
        Assert.True(deleted.Succeeded);
        Assert.Null(await store.StatAsync(Intake, Token));
    }

    [Fact]
    public async Task AnnouncesADelete()
    {
        var (store, received) = await ObservedAsync();
        var saved = await store.SaveAsync(Intake, Content("one"), ifMatchRevision: null, Token);
        received.Clear();

        await store.DeleteAsync(Intake, saved.Revision, Token);

        var change = Assert.Single(received);
        Assert.Equal(BpmnDocumentChangeKind.Deleted, change.Kind);
        Assert.Null(change.Revision);
    }

    [Fact]
    public async Task StaysSilentWhenNothingWasWritten()
    {
        var (store, received) = await ObservedAsync();
        await store.SaveAsync(Intake, Content("one"), ifMatchRevision: null, Token);
        received.Clear();

        await store.SaveAsync(Intake, Content("two"), ifMatchRevision: null, Token);
        await store.SaveAsync(Intake, Content("two"), ifMatchRevision: "stale", Token);
        await store.SaveAsync(Key("missing.bpmn"), Content("x"), ifMatchRevision: "r1", Token);
        await store.DeleteAsync(Intake, ifMatchRevision: "stale", Token);
        await store.DeleteAsync(Key("missing.bpmn"), ifMatchRevision: null, Token);

        Assert.Empty(received);
    }

    private static async Task<(IBpmnDocumentStore Store, List<BpmnDocumentChange> Received)> ObservedAsync()
    {
        var bus = new InProcessBpmnDocumentChangeBus();
        var received = new List<BpmnDocumentChange>();
        var store = new NotifyingBpmnDocumentStore(new InMemoryBpmnDocumentStore(), bus);

        foreach (var key in new[] { Intake, Key("missing.bpmn") })
        {
            await bus.SubscribeAsync(key, change =>
            {
                received.Add(change);
                return ValueTask.CompletedTask;
            }, Token);
        }

        return (store, received);
    }
}
