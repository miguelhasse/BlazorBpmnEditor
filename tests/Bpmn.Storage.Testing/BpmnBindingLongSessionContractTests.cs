using System.Text;

using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;

namespace Hasseware.AspNetCore.Components.Bpmn.Storage.Testing;

/// <summary>
/// The long-session promise of the design, checked through a real transport: a change that happens while the feed is down is
/// never delivered, yet after the feed is back the binding shows it, because the subscriber tells it about the gap and it makes
/// one state-based catch-up check.
/// </summary>
/// <remarks>Derive once per transport and implement <see cref="StartAsync"/>; the harness's bus must be the one the store publishes to.</remarks>
public abstract class BpmnBindingLongSessionContractTests
{
    private static readonly BpmnDocumentKey Key = new(null, BpmnDocumentPath.Parse("claims/intake.bpmn"));

    /// <summary>Starts a transport for one test, with callers in the tenant the test's keys use.</summary>
    /// <returns>The wired harness, with the bus a store publishes to.</returns>
    protected abstract ValueTask<IBpmnChangeFeedHarness> StartAsync();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private sealed class CountingStore(IBpmnDocumentStore inner) : IBpmnDocumentStore
    {
        public int Stats { get; private set; }

        public ValueTask<BpmnStoredDocument?> OpenAsync(BpmnDocumentKey key, CancellationToken cancellationToken = default) => inner.OpenAsync(key, cancellationToken);

        public ValueTask<BpmnDocumentStat?> StatAsync(BpmnDocumentKey key, CancellationToken cancellationToken = default)
        {
            Stats++;
            return inner.StatAsync(key, cancellationToken);
        }

        public ValueTask<BpmnStoreResult> SaveAsync(BpmnDocumentKey key, BpmnDocumentContent content, string? ifMatchRevision, CancellationToken cancellationToken = default) =>
            inner.SaveAsync(key, content, ifMatchRevision, cancellationToken);

        public ValueTask<BpmnDocumentPage> ListAsync(BpmnDocumentQuery query, CancellationToken cancellationToken = default) => inner.ListAsync(query, cancellationToken);

        public ValueTask<BpmnStoreResult> DeleteAsync(BpmnDocumentKey key, string? ifMatchRevision, CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(key, ifMatchRevision, cancellationToken);
    }

    private static BpmnDocumentContent Content(string text) => new(Encoding.UTF8.GetBytes(text), BpmnFileFormat.Xml);

    private static string Xml(string name) => $"""
        <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="d" targetNamespace="t">
          <process id="p"><startEvent id="s" name="{name}" /></process>
        </definitions>
        """;

    [Fact]
    public async Task ShowsAChangeMadeWhileTheFeedWasDownAfterExactlyOneCatchUpCheck()
    {
        await using var harness = await StartAsync();
        var key = new BpmnDocumentKey(harness.Tenant, Key.Path);
        var counting = new CountingStore(new NotifyingBpmnDocumentStore(new InMemoryBpmnDocumentStore(), harness.Bus));
        await counting.SaveAsync(key, Content(Xml("Mine")), ifMatchRevision: null, Token);

        await using var binding = new BpmnDocumentBinding(counting, harness.Subscriber);
        await binding.LoadAsync(key, Token);
        await WaitAsync(() => harness.Subscriber.State == BpmnChangeFeedState.Live, "the feed to be live");

        // The feed goes down and stays down, as an expired token keeps it, while somebody else saves.
        await harness.RevokeAuthorizationAsync();
        await WaitAsync(() => harness.Subscriber.State == BpmnChangeFeedState.Unauthorized, "the feed to give up");
        var theirs = (await counting.SaveAsync(key, Content(Xml("Theirs")), (await counting.StatAsync(key, Token))!.Revision, Token)).Revision;
        await Task.Delay(300, Token);
        Assert.Null(binding.ExternalChange);

        var before = counting.Stats;
        await harness.RestoreAuthorizationAsync();
        await WaitAsync(() => binding.ExternalChange is not null, "the binding to learn of the change it missed");
        await Task.Delay(500, Token);

        Assert.Equal(BpmnDocumentChangeKind.Updated, binding.ExternalChange!.Kind);
        Assert.Equal(theirs, binding.ExternalChange.Revision);
        Assert.Equal(1, counting.Stats - before);
    }

    [Fact]
    public async Task ShowsAChangeMadeWhileConnectedWithoutAnyCatchUpCheck()
    {
        await using var harness = await StartAsync();
        var key = new BpmnDocumentKey(harness.Tenant, Key.Path);
        var counting = new CountingStore(new NotifyingBpmnDocumentStore(new InMemoryBpmnDocumentStore(), harness.Bus));
        await counting.SaveAsync(key, Content(Xml("Mine")), ifMatchRevision: null, Token);
        await using var binding = new BpmnDocumentBinding(counting, harness.Subscriber);
        await binding.LoadAsync(key, Token);
        await WaitAsync(() => harness.Subscriber.State == BpmnChangeFeedState.Live, "the feed to be live");
        await Task.Delay(500, Token);
        var before = counting.Stats;

        var theirs = (await counting.SaveAsync(key, Content(Xml("Theirs")), (await counting.StatAsync(key, Token))!.Revision, Token)).Revision;
        await WaitAsync(() => binding.ExternalChange is not null, "the pushed change to arrive");

        Assert.Equal(theirs, binding.ExternalChange!.Revision);

        // The one stat above is the test's own, to learn the revision; the push needed none.
        Assert.Equal(1, counting.Stats - before);
    }

    [Fact]
    public async Task ReloadsFromInsideANotificationAndKeepsReceivingAfterwards()
    {
        await using var harness = await StartAsync();
        var key = new BpmnDocumentKey(harness.Tenant, Key.Path);
        var store = new NotifyingBpmnDocumentStore(new InMemoryBpmnDocumentStore(), harness.Bus);
        await store.SaveAsync(key, Content(Xml("Mine")), ifMatchRevision: null, Token);
        await using var binding = new BpmnDocumentBinding(store, harness.Subscriber, new StorageOptions { ExternalChangePolicy = BpmnExternalChangePolicy.AutoReloadWhenClean });
        await binding.LoadAsync(key, Token);
        await WaitAsync(() => harness.Subscriber.State == BpmnChangeFeedState.Live, "the feed to be live");
        await Task.Delay(300, Token);

        // The reload runs inside the notification, and replaces the subscription that is delivering it: that used to wait for itself for ever.
        var first = (await store.SaveAsync(key, Content(Xml("Theirs")), (await store.StatAsync(key, Token))!.Revision, Token)).Revision;
        await WaitAsync(() => binding.StoredRevision == first, "the first change to be reloaded");
        await Task.Delay(300, Token);

        var second = (await store.SaveAsync(key, Content(Xml("Theirs again")), (await store.StatAsync(key, Token))!.Revision, Token)).Revision;
        await WaitAsync(() => binding.StoredRevision == second, "the second change to arrive over the new subscription");

        Assert.Null(binding.ExternalChange);
    }

    private static async Task WaitAsync(Func<bool> condition, string because)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for: " + because);
            await Task.Delay(20, Token);
        }
    }
}
