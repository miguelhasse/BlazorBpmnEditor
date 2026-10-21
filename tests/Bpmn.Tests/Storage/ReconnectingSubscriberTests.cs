using System.Threading.Channels;

using Hasseware.AspNetCore.Components.Bpmn.Storage;

using Microsoft.Extensions.Time.Testing;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Storage;

/// <summary>
/// What every live-update transport inherits: a connection that goes silent is given up on, reconnection backs off
/// and is jittered, every gap is reported once, and an unauthorized feed stops until the host renews its credentials.
/// </summary>
public sealed class ReconnectingSubscriberTests
{
    private static readonly BpmnDocumentKey Intake = new(null, BpmnDocumentPath.Parse("claims/intake.bpmn"));

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static BpmnDocumentChange Change(string revision) =>
        new(Intake, BpmnDocumentChangeKind.Updated, revision, Origin: null, DateTimeOffset.UnixEpoch);

    private sealed class Connection : IBpmnChangeConnection
    {
        private readonly Channel<object> _messages = Channel.CreateUnbounded<object>();

        public bool Disposed { get; private set; }

        public void Send(BpmnDocumentChange change) => _messages.Writer.TryWrite(change);

        public void Beat() => _messages.Writer.TryWrite("beat");

        public void Fail(Exception exception) => _messages.Writer.TryWrite(exception);

        public void End() => _messages.Writer.TryComplete();

        public async ValueTask<BpmnChangeMessage?> ReadAsync(CancellationToken cancellationToken)
        {
            try
            {
                return await _messages.Reader.ReadAsync(cancellationToken).ConfigureAwait(false) switch
                {
                    BpmnDocumentChange change => new BpmnChangeMessage(change),
                    Exception exception => throw exception,
                    _ => BpmnChangeMessage.Heartbeat,
                };
            }
            catch (ChannelClosedException)
            {
                return null;
            }
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Subscriber(FakeTimeProvider time, BpmnChangeFeedOptions options)
        : ReconnectingBpmnDocumentChangeSubscriber(options, time, () => 1.0)
    {
        private readonly Lock _gate = new();

        public List<Connection> Connections { get; } = [];

        public List<DateTimeOffset> Attempts { get; } = [];

        public Queue<Exception> Refusals { get; } = new();

        protected override ValueTask<IBpmnChangeConnection> OpenStreamAsync(BpmnDocumentKey key, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                Attempts.Add(time.GetUtcNow());
                if (Refusals.TryDequeue(out var refusal)) throw refusal;

                var connection = new Connection();
                Connections.Add(connection);
                return ValueTask.FromResult<IBpmnChangeConnection>(connection);
            }
        }

        public Connection Latest
        {
            get
            {
                lock (_gate) return Connections[^1];
            }
        }

        public int Opened
        {
            get
            {
                lock (_gate) return Connections.Count;
            }
        }
    }

    private static BpmnChangeFeedOptions Options() => new()
    {
        HeartbeatInterval = TimeSpan.FromSeconds(30),
        InitialBackoff = TimeSpan.FromSeconds(1),
        MaxBackoff = TimeSpan.FromSeconds(30),
    };

    // The feeds run on the thread pool and wait on the fake clock, so a test moves the clock a little at a time and lets them run.
    private static async Task AdvanceUntilAsync(FakeTimeProvider time, Func<bool> condition, TimeSpan step, int maxSteps = 400)
    {
        for (var index = 0; index < maxSteps; index++)
        {
            if (condition()) return;
            await Task.Delay(2, Token);
            if (condition()) return;
            time.Advance(step);
        }

        Assert.True(condition(), "The condition was not met.");
    }

    private static async Task SettleAsync() => await Task.Delay(50, Token);

    [Fact]
    public async Task DeliversChangesAndKeepsAliveOnHeartbeatsAlone()
    {
        var time = new FakeTimeProvider();
        await using var subscriber = new Subscriber(time, Options());
        var received = new List<string?>();
        await using var _ = await subscriber.SubscribeAsync(Intake, change =>
        {
            lock (received) received.Add(change.Revision);
            return ValueTask.CompletedTask;
        }, Token);
        await AdvanceUntilAsync(time, () => subscriber.Opened == 1, TimeSpan.FromMilliseconds(1));

        // A long quiet session with a heartbeat every 20 s never looks stale.
        for (var index = 0; index < 30; index++)
        {
            time.Advance(TimeSpan.FromSeconds(20));
            subscriber.Latest.Beat();
            await SettleAsync();
        }

        subscriber.Latest.Send(Change("r2"));
        await AdvanceUntilAsync(time, () => received.Count == 1, TimeSpan.FromMilliseconds(1));

        Assert.Equal(1, subscriber.Opened);
        Assert.Equal(BpmnChangeFeedState.Live, subscriber.State);
        Assert.Equal(["r2"], received);
    }

    [Fact]
    public async Task GivesUpOnAConnectionThatWentSilentAndTellsTheBindingOnceAfterItReconnects()
    {
        var time = new FakeTimeProvider();
        await using var subscriber = new Subscriber(time, Options());
        var gaps = 0;
        var states = new List<BpmnChangeFeedState>();
        subscriber.Resubscribed += () =>
        {
            Interlocked.Increment(ref gaps);
            return ValueTask.CompletedTask;
        };
        subscriber.StateChanged += state =>
        {
            lock (states) states.Add(state);
            return ValueTask.CompletedTask;
        };
        await using var _ = await subscriber.SubscribeAsync(Intake, _ => ValueTask.CompletedTask, Token);
        await AdvanceUntilAsync(time, () => subscriber.Opened == 1, TimeSpan.FromMilliseconds(1));

        // 2.5 intervals of silence is 75 s.
        time.Advance(TimeSpan.FromSeconds(74));
        await SettleAsync();
        Assert.Equal(BpmnChangeFeedState.Live, subscriber.State);

        await AdvanceUntilAsync(time, () => subscriber.Opened == 2, TimeSpan.FromSeconds(1));
        await SettleAsync();

        Assert.Equal(1, gaps);
        Assert.True(subscriber.Connections[0].Disposed);
        Assert.Equal(BpmnChangeFeedState.Live, subscriber.State);
        lock (states) Assert.Equal([BpmnChangeFeedState.Reconnecting, BpmnChangeFeedState.Live], states);
    }

    [Fact]
    public async Task ReconnectsAfterAFailureAndAfterTheServerEndsTheStream()
    {
        var time = new FakeTimeProvider();
        await using var subscriber = new Subscriber(time, Options());
        var gaps = 0;
        subscriber.Resubscribed += () =>
        {
            Interlocked.Increment(ref gaps);
            return ValueTask.CompletedTask;
        };
        await using var _ = await subscriber.SubscribeAsync(Intake, _ => ValueTask.CompletedTask, Token);
        await AdvanceUntilAsync(time, () => subscriber.Opened == 1, TimeSpan.FromMilliseconds(1));

        subscriber.Latest.Fail(new IOException("reset"));
        await AdvanceUntilAsync(time, () => subscriber.Opened == 2, TimeSpan.FromSeconds(1));
        subscriber.Latest.End();
        await AdvanceUntilAsync(time, () => subscriber.Opened == 3, TimeSpan.FromSeconds(1));
        await SettleAsync();

        Assert.Equal(2, gaps);
        Assert.Equal(BpmnChangeFeedState.Live, subscriber.State);
    }

    [Fact]
    public async Task BacksOffExponentiallyUpToTheCapWhileTheServerIsDown()
    {
        var time = new FakeTimeProvider();
        await using var subscriber = new Subscriber(time, Options());
        for (var index = 0; index < 8; index++) subscriber.Refusals.Enqueue(new HttpRequestException("down"));
        await using var _ = await subscriber.SubscribeAsync(Intake, _ => ValueTask.CompletedTask, Token);

        await AdvanceUntilAsync(time, () => subscriber.Opened == 1, TimeSpan.FromMilliseconds(500), maxSteps: 2000);

        var gaps = subscriber.Attempts.Zip(subscriber.Attempts.Skip(1), (earlier, later) => (later - earlier).TotalSeconds).ToList();
        Assert.Equal(8, gaps.Count);

        // With the jitter fixed at its maximum each wait is the full ceiling: 1, 2, 4, 8, 16, then capped at 30 (to the step of the clock).
        Assert.InRange(gaps[0], 1, 1.6);
        Assert.InRange(gaps[1], 2, 2.6);
        Assert.InRange(gaps[3], 8, 8.6);
        Assert.InRange(gaps[5], 30, 30.6);
        Assert.InRange(gaps[7], 30, 30.6);
    }

    [Fact]
    public async Task StopsRetryingWhenUnauthorizedAndResumesWhenTheHostRetries()
    {
        var time = new FakeTimeProvider();
        await using var subscriber = new Subscriber(time, Options());
        var required = 0;
        var gaps = 0;
        subscriber.AuthorizationRequired += () =>
        {
            Interlocked.Increment(ref required);
            return ValueTask.CompletedTask;
        };
        subscriber.Resubscribed += () =>
        {
            Interlocked.Increment(ref gaps);
            return ValueTask.CompletedTask;
        };
        subscriber.Refusals.Enqueue(new BpmnChangeFeedUnauthorizedException());
        await using var _ = await subscriber.SubscribeAsync(Intake, _ => ValueTask.CompletedTask, Token);

        await AdvanceUntilAsync(time, () => subscriber.State == BpmnChangeFeedState.Unauthorized, TimeSpan.FromMilliseconds(1));
        var attempts = subscriber.Attempts.Count;
        time.Advance(TimeSpan.FromMinutes(10));
        await SettleAsync();

        Assert.Equal(attempts, subscriber.Attempts.Count);
        Assert.Equal(1, required);

        await subscriber.RetryAsync();
        await AdvanceUntilAsync(time, () => subscriber.Opened == 1, TimeSpan.FromMilliseconds(1));
        await SettleAsync();

        Assert.Equal(BpmnChangeFeedState.Live, subscriber.State);
        Assert.Equal(1, gaps);
    }

    [Fact]
    public async Task StopsDeliveringToASubscriptionOnceItIsDisposed()
    {
        var time = new FakeTimeProvider();
        await using var subscriber = new Subscriber(time, Options());
        var received = 0;
        var handle = await subscriber.SubscribeAsync(Intake, _ =>
        {
            Interlocked.Increment(ref received);
            return ValueTask.CompletedTask;
        }, Token);
        await AdvanceUntilAsync(time, () => subscriber.Opened == 1, TimeSpan.FromMilliseconds(1));
        var connection = subscriber.Latest;

        await handle.DisposeAsync();
        connection.Send(Change("r2"));
        await SettleAsync();

        Assert.Equal(0, received);
        Assert.True(connection.Disposed);
    }

    [Fact]
    public async Task ClosesWhenTheSubscriberIsDisposed()
    {
        var time = new FakeTimeProvider();
        var subscriber = new Subscriber(time, Options());
        await subscriber.SubscribeAsync(Intake, _ => ValueTask.CompletedTask, Token);
        await AdvanceUntilAsync(time, () => subscriber.Opened == 1, TimeSpan.FromMilliseconds(1));

        await subscriber.DisposeAsync();

        Assert.Equal(BpmnChangeFeedState.Closed, subscriber.State);
        Assert.True(subscriber.Connections[0].Disposed);
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await subscriber.SubscribeAsync(Intake, _ => ValueTask.CompletedTask, Token));
    }

    [Fact]
    public async Task KeepsTheFeedAliveWhenTheHandlerThrows()
    {
        var time = new FakeTimeProvider();
        await using var subscriber = new Subscriber(time, Options());
        var received = new List<string?>();
        await using var _ = await subscriber.SubscribeAsync(Intake, change =>
        {
            lock (received) received.Add(change.Revision);
            return change.Revision == "r2" ? throw new InvalidOperationException("handler bug") : ValueTask.CompletedTask;
        }, Token);
        await AdvanceUntilAsync(time, () => subscriber.Opened == 1, TimeSpan.FromMilliseconds(1));

        subscriber.Latest.Send(Change("r2"));
        subscriber.Latest.Send(Change("r3"));
        await AdvanceUntilAsync(time, () => received.Count == 2, TimeSpan.FromMilliseconds(1));

        Assert.Equal(1, subscriber.Opened);
        Assert.Equal(["r2", "r3"], received);
    }

    [Fact]
    public async Task ALiveHandlerMayDisposeItsOwnSubscriptionWithoutDeadlockingTheFeed()
    {
        // What a binding does when it reloads and resubscribes from inside a notification (AutoReloadWhenClean).
        var time = new FakeTimeProvider();
        await using var subscriber = new Subscriber(time, Options());
        var handle = new TaskCompletionSource<IAsyncDisposable>(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        handle.SetResult(await subscriber.SubscribeAsync(Intake, async _ =>
        {
            await (await handle.Task).DisposeAsync();
            disposed.TrySetResult();
        }, Token));
        await AdvanceUntilAsync(time, () => subscriber.Opened == 1, TimeSpan.FromMilliseconds(1));

        subscriber.Latest.Send(Change("r2"));

        await disposed.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
        await subscriber.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10), Token);
        Assert.Equal(BpmnChangeFeedState.Closed, subscriber.State);
    }

    [Fact]
    public async Task AResubscribedHandlerMayDisposeTheSubscriptionItIsAnswering()
    {
        var time = new FakeTimeProvider();
        await using var subscriber = new Subscriber(time, Options());
        var handle = new TaskCompletionSource<IAsyncDisposable>(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        subscriber.Resubscribed += async () =>
        {
            await (await handle.Task).DisposeAsync();
            disposed.TrySetResult();
        };
        handle.SetResult(await subscriber.SubscribeAsync(Intake, _ => ValueTask.CompletedTask, Token));
        await AdvanceUntilAsync(time, () => subscriber.Opened == 1, TimeSpan.FromMilliseconds(1));

        subscriber.Latest.End();
        await AdvanceUntilAsync(time, () => subscriber.Opened == 2, TimeSpan.FromSeconds(1));

        await disposed.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
        await subscriber.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10), Token);
    }

    [Fact]
    public async Task ResumesWhenTheHostRetriesFromInsideTheAuthorizationRequiredHandler()
    {
        var time = new FakeTimeProvider();
        await using var subscriber = new Subscriber(time, Options());
        subscriber.AuthorizationRequired += subscriber.RetryAsync;
        subscriber.Refusals.Enqueue(new BpmnChangeFeedUnauthorizedException());
        await using var _ = await subscriber.SubscribeAsync(Intake, _ => ValueTask.CompletedTask, Token);

        // Before the fix the retry completed the wait that was about to be replaced, and the feed sat Unauthorized for good.
        await AdvanceUntilAsync(time, () => subscriber.Opened == 1, TimeSpan.FromMilliseconds(1));

        Assert.Equal(BpmnChangeFeedState.Live, subscriber.State);
    }

    [Fact]
    public async Task ReconnectsWithAnUnlimitedBackoffCapAndWithNoInitialBackoff()
    {
        var time = new FakeTimeProvider();
        var options = Options();
        options.MaxBackoff = Timeout.InfiniteTimeSpan;
        options.InitialBackoff = TimeSpan.Zero;
        await using var subscriber = new Subscriber(time, options);
        for (var index = 0; index < 3; index++) subscriber.Refusals.Enqueue(new HttpRequestException("down"));
        await using var _ = await subscriber.SubscribeAsync(Intake, _ => ValueTask.CompletedTask, Token);

        await AdvanceUntilAsync(time, () => subscriber.Opened == 1, TimeSpan.FromMilliseconds(5));

        Assert.Equal(BpmnChangeFeedState.Live, subscriber.State);
    }
}
