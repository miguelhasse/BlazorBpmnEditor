namespace Hasseware.AspNetCore.Components.Bpmn.Storage.Testing;

/// <summary>A live-update transport wired end to end for one test: a server that publishes and a client subscriber, with ways to break the connection.</summary>
public interface IBpmnChangeFeedHarness : IAsyncDisposable
{
    /// <summary>The client side, as the binding uses it.</summary>
    public IBpmnDocumentChangeSubscriber Subscriber { get; }

    /// <summary>The bus the server adapter listens to, which a store publishes to.</summary>
    public IBpmnDocumentChangeBus Bus { get; }

    /// <summary>The tenant the caller works in, which keys in the test carry; null for the unpartitioned space.</summary>
    public string? Tenant { get; }

    /// <summary>Publishes a change the way the server's writer would, through the bus the server adapter listens to.</summary>
    /// <param name="change">The change.</param>
    /// <returns>A task that completes when it was published.</returns>
    public ValueTask PublishAsync(BpmnDocumentChange change);

    /// <summary>Cuts every open connection from the network side while the server keeps running.</summary>
    /// <returns>A task that completes when they are cut.</returns>
    public ValueTask DropConnectionsAsync();

    /// <summary>Makes the server refuse the caller from now on, and closes open connections, as an expired token does.</summary>
    /// <returns>A task that completes when the caller is refused.</returns>
    public ValueTask RevokeAuthorizationAsync();

    /// <summary>Lets the caller in again and tells the subscriber to resume, as a host does after renewing credentials.</summary>
    /// <returns>A task that completes when the subscriber was told.</returns>
    public ValueTask RestoreAuthorizationAsync();
}

/// <summary>
/// What every live-update transport must do, whatever it speaks on the wire (design doc, "Long editing sessions"):
/// deliver a document's changes and no others, stop on disposal, survive a dropped connection and say so once, coalesce a
/// burst to its latest revision, and stop and resume around an authorization failure.
/// </summary>
/// <remarks>Derive once per transport and implement <see cref="StartAsync"/>.</remarks>
public abstract class BpmnDocumentChangeSubscriberContractTests
{
    /// <summary>How long a test waits for something that is expected to happen.</summary>
    protected static TimeSpan Patience { get; } = TimeSpan.FromSeconds(20);

    /// <summary>Starts a transport for one test.</summary>
    /// <returns>The wired harness.</returns>
    protected abstract ValueTask<IBpmnChangeFeedHarness> StartAsync();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static BpmnDocumentKey Key(IBpmnChangeFeedHarness harness, string path) => new(harness.Tenant, BpmnDocumentPath.Parse(path));

    private static BpmnDocumentChange Change(BpmnDocumentKey key, string revision, BpmnDocumentChangeKind kind = BpmnDocumentChangeKind.Updated) =>
        new(key, kind, kind == BpmnDocumentChangeKind.Deleted ? null : revision, Origin: null, DateTimeOffset.UnixEpoch);

    private sealed class Recorder
    {
        private readonly List<BpmnDocumentChange> _changes = [];

        public int Count
        {
            get
            {
                lock (_changes) return _changes.Count;
            }
        }

        public IReadOnlyList<BpmnDocumentChange> Changes
        {
            get
            {
                lock (_changes) return [.. _changes];
            }
        }

        public ValueTask Handle(BpmnDocumentChange change)
        {
            lock (_changes) _changes.Add(change);
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Waits until a condition holds.</summary>
    /// <param name="condition">What to wait for.</param>
    /// <param name="because">What to say if it never holds.</param>
    /// <returns>A task that completes when it holds.</returns>
    protected static async Task WaitForAsync(Func<bool> condition, string because)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for: " + because);
            await Task.Delay(20, Token);
        }
    }

    // A subscription is live once the first publication reaches it; publishing until then absorbs the time a transport takes to connect.
    private static async Task PublishUntilDeliveredAsync(IBpmnChangeFeedHarness harness, Recorder recorder, BpmnDocumentKey key, string revisionPrefix)
    {
        var attempt = 0;
        await WaitForAsync(
            () =>
            {
                if (recorder.Count > 0) return true;
                harness.PublishAsync(Change(key, revisionPrefix + ++attempt)).AsTask().GetAwaiter().GetResult();
                Thread.Sleep(50);
                return recorder.Count > 0;
            },
            "a change to reach a new subscription");
    }

    [Fact]
    public async Task DeliversTheSubscribedDocumentsChanges()
    {
        await using var harness = await StartAsync();
        var key = Key(harness, "claims/intake.bpmn");
        var recorder = new Recorder();
        await using var _ = await harness.Subscriber.SubscribeAsync(key, recorder.Handle, Token);
        await PublishUntilDeliveredAsync(harness, recorder, key, "warm-");

        await harness.PublishAsync(Change(key, "final"));
        await WaitForAsync(() => recorder.Changes.Any(change => change.Revision == "final"), "the change to arrive");

        var delivered = recorder.Changes.Single(change => change.Revision == "final");
        Assert.Equal(key.Path, delivered.Key.Path);
        Assert.Equal(BpmnDocumentChangeKind.Updated, delivered.Kind);
    }

    [Fact]
    public async Task DeliversADeletionWithNoRevision()
    {
        await using var harness = await StartAsync();
        var key = Key(harness, "claims/intake.bpmn");
        var recorder = new Recorder();
        await using var _ = await harness.Subscriber.SubscribeAsync(key, recorder.Handle, Token);
        await PublishUntilDeliveredAsync(harness, recorder, key, "warm-");

        await harness.PublishAsync(Change(key, "ignored", BpmnDocumentChangeKind.Deleted));

        await WaitForAsync(() => recorder.Changes.Any(change => change.Kind == BpmnDocumentChangeKind.Deleted), "the deletion to arrive");
        Assert.Null(recorder.Changes.First(change => change.Kind == BpmnDocumentChangeKind.Deleted).Revision);
    }

    [Fact]
    public async Task DeliversNothingAboutAnotherDocument()
    {
        await using var harness = await StartAsync();
        var mine = Key(harness, "mine.bpmn");
        var other = Key(harness, "other.bpmn");
        var recorder = new Recorder();
        await using var _ = await harness.Subscriber.SubscribeAsync(mine, recorder.Handle, Token);
        await PublishUntilDeliveredAsync(harness, recorder, mine, "warm-");

        await harness.PublishAsync(Change(other, "theirs"));
        await harness.PublishAsync(Change(mine, "mine-final"));
        await WaitForAsync(() => recorder.Changes.Any(change => change.Revision == "mine-final"), "my change to arrive");

        Assert.DoesNotContain(recorder.Changes, change => change.Revision == "theirs");
    }

    [Fact]
    public async Task StopsDeliveringOnceTheSubscriptionIsDisposed()
    {
        await using var harness = await StartAsync();
        var key = Key(harness, "claims/intake.bpmn");
        var recorder = new Recorder();
        var subscription = await harness.Subscriber.SubscribeAsync(key, recorder.Handle, Token);
        await PublishUntilDeliveredAsync(harness, recorder, key, "warm-");

        await subscription.DisposeAsync();
        var before = recorder.Count;
        await harness.PublishAsync(Change(key, "after-dispose"));
        await Task.Delay(500, Token);

        Assert.Equal(before, recorder.Count);
        Assert.DoesNotContain(recorder.Changes, change => change.Revision == "after-dispose");
    }

    [Fact]
    public async Task IsLiveOnceSubscribed()
    {
        await using var harness = await StartAsync();
        var key = Key(harness, "claims/intake.bpmn");
        var recorder = new Recorder();
        await using var _ = await harness.Subscriber.SubscribeAsync(key, recorder.Handle, Token);
        await PublishUntilDeliveredAsync(harness, recorder, key, "warm-");

        Assert.Equal(BpmnChangeFeedState.Live, harness.Subscriber.State);
    }

    [Fact]
    public async Task SaysOnceThatAConnectionWasLostAndKeepsDeliveringAfterwards()
    {
        await using var harness = await StartAsync();
        var key = Key(harness, "claims/intake.bpmn");
        var recorder = new Recorder();
        var gaps = 0;
        harness.Subscriber.Resubscribed += () =>
        {
            Interlocked.Increment(ref gaps);
            return ValueTask.CompletedTask;
        };
        await using var _ = await harness.Subscriber.SubscribeAsync(key, recorder.Handle, Token);
        await PublishUntilDeliveredAsync(harness, recorder, key, "warm-");

        await harness.DropConnectionsAsync();
        await WaitForAsync(() => Volatile.Read(ref gaps) >= 1, "the subscriber to reconnect and say so");
        await WaitForAsync(() => harness.Subscriber.State == BpmnChangeFeedState.Live, "the feed to be live again");
        await Task.Delay(300, Token);

        Assert.Equal(1, Volatile.Read(ref gaps));
        await harness.PublishAsync(Change(key, "after-drop"));
        await WaitForAsync(() => recorder.Changes.Any(change => change.Revision == "after-drop"), "a change after the reconnect");
    }

    [Fact]
    public async Task CoalescesABurstButAlwaysDeliversTheLatestRevision()
    {
        await using var harness = await StartAsync();
        var key = Key(harness, "claims/intake.bpmn");
        var recorder = new Recorder();
        await using var _ = await harness.Subscriber.SubscribeAsync(key, recorder.Handle, Token);
        await PublishUntilDeliveredAsync(harness, recorder, key, "warm-");

        for (var index = 1; index <= 1000; index++) await harness.PublishAsync(Change(key, "burst-" + index));

        await WaitForAsync(() => recorder.Changes.Any(change => change.Revision == "burst-1000"), "the last revision of the burst");
        Assert.Equal("burst-1000", recorder.Changes[^1].Revision);
    }

    [Fact]
    public async Task StopsWhenTheCallerIsRefusedAndResumesWhenTheHostRetries()
    {
        await using var harness = await StartAsync();
        var key = Key(harness, "claims/intake.bpmn");
        var recorder = new Recorder();
        var gaps = 0;
        harness.Subscriber.Resubscribed += () =>
        {
            Interlocked.Increment(ref gaps);
            return ValueTask.CompletedTask;
        };
        await using var _ = await harness.Subscriber.SubscribeAsync(key, recorder.Handle, Token);
        await PublishUntilDeliveredAsync(harness, recorder, key, "warm-");

        await harness.RevokeAuthorizationAsync();
        await WaitForAsync(() => harness.Subscriber.State == BpmnChangeFeedState.Unauthorized, "the subscriber to report that it is unauthorized");

        await harness.RestoreAuthorizationAsync();
        await WaitForAsync(() => harness.Subscriber.State == BpmnChangeFeedState.Live, "the feed to resume");
        await WaitForAsync(() => Volatile.Read(ref gaps) >= 1, "the binding to be told of the gap");

        await harness.PublishAsync(Change(key, "after-renewal"));
        await WaitForAsync(() => recorder.Changes.Any(change => change.Revision == "after-renewal"), "a change after the credentials were renewed");
    }
}
