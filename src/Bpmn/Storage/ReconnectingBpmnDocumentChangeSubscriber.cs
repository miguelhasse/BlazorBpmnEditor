namespace Hasseware.AspNetCore.Components.Bpmn.Storage;

/// <summary>What a live-update stream carries: a change, or a heartbeat that says the connection is alive.</summary>
/// <param name="Change">The change, or null for a heartbeat.</param>
public readonly record struct BpmnChangeMessage(BpmnDocumentChange? Change)
{
    /// <summary>A heartbeat: nothing changed, the stream is simply alive.</summary>
    public static BpmnChangeMessage Heartbeat => default;

    /// <summary>Whether this is a heartbeat.</summary>
    public bool IsHeartbeat => Change is null;
}

/// <summary>One open stream of live updates for one document, as a transport provides it.</summary>
public interface IBpmnChangeConnection : IAsyncDisposable
{
    /// <summary>Waits for the next message.</summary>
    /// <param name="cancellationToken">Cancels the wait, which is how a stale connection is given up on.</param>
    /// <returns>The next message, or null when the server ended the stream.</returns>
    /// <exception cref="BpmnChangeFeedUnauthorizedException">The server refused the caller, or their credentials expired.</exception>
    public ValueTask<BpmnChangeMessage?> ReadAsync(CancellationToken cancellationToken);
}

/// <summary>The server refused a live-update stream because the caller is not, or is no longer, authorized.</summary>
public sealed class BpmnChangeFeedUnauthorizedException : Exception
{
    /// <summary>Creates the exception.</summary>
    public BpmnChangeFeedUnauthorizedException()
        : base("The live-update feed refused the caller.")
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">What was refused.</param>
    public BpmnChangeFeedUnauthorizedException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">What was refused.</param>
    /// <param name="innerException">The cause.</param>
    public BpmnChangeFeedUnauthorizedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>How a live-update subscriber keeps its streams alive.</summary>
public sealed class BpmnChangeFeedOptions
{
    /// <summary>How often the server sends a heartbeat. The default, 30 seconds, is below the idle timeouts of common proxies.</summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How many heartbeat intervals of silence make a connection dead. The default, 2.5, tolerates one lost heartbeat plus jitter.
    /// Zero or less, or infinity, turns the check off, for a transport that has keepalive of its own (SignalR).
    /// </summary>
    public double StalenessFactor { get; set; } = 2.5;

    /// <summary>The wait before the first reconnection attempt.</summary>
    public TimeSpan InitialBackoff { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>The longest wait between reconnection attempts.</summary>
    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How long silence may last before the connection is given up on; infinite when the check is off.</summary>
    public TimeSpan StalenessTimeout => StalenessFactor <= 0 || double.IsInfinity(StalenessFactor)
        ? Timeout.InfiniteTimeSpan
        : TimeSpan.FromTicks((long)(HeartbeatInterval.Ticks * StalenessFactor));
}

/// <summary>
/// The part of a live-update subscriber every transport needs and none should write twice: it keeps one stream open
/// per subscribed document, notices a connection that has gone silent, reconnects with backoff and jitter, and tells
/// the binding after every gap so that it makes its single catch-up check.
/// </summary>
/// <remarks>
/// <para>
/// A transport implements only <see cref="OpenStreamAsync"/>. There is no replay: after a gap the binding compares the
/// stored revision with its own, which is correct whatever was missed, so a stream needs no memory.
/// </para>
/// <para>
/// When a stream is refused as unauthorized the subscriber stops retrying, moves to
/// <see cref="BpmnChangeFeedState.Unauthorized"/> and raises <see cref="AuthorizationRequired"/>; the host renews the
/// credentials and calls <see cref="RetryAsync"/>, after which the streams resume and the binding catches up.
/// </para>
/// </remarks>
public abstract class ReconnectingBpmnDocumentChangeSubscriber : IBpmnDocumentChangeSubscriber, IAsyncDisposable
{
    /// <summary>
    /// The feed whose loop is running a listener on this async flow, if any.
    /// </summary>
    /// <remarks>
    /// A binding that reloads from inside a notification disposes its subscription and opens a new one, which is the feed whose loop is awaiting
    /// that very listener. Disposing a feed waits for its loop, so without this the dispose waits for the listener that is waiting for the dispose.
    /// </remarks>
    private static readonly AsyncLocal<object?> RunningListenerOf = new();

    private readonly Lock _gate = new();
    private readonly List<Feed> _feeds = [];
    private readonly CancellationTokenSource _disposal = new();
    private readonly TimeProvider _time;
    private readonly Func<double> _random;
    private BpmnChangeFeedState _state = BpmnChangeFeedState.Live;
    private bool _disposed;

    /// <summary>Creates a subscriber.</summary>
    /// <param name="options">The heartbeat, staleness and backoff; the defaults when null.</param>
    /// <param name="timeProvider">The clock; the system clock when null.</param>
    /// <param name="random">Gives a number in [0, 1) for jitter; <see cref="Random.Shared"/> when null.</param>
    protected ReconnectingBpmnDocumentChangeSubscriber(BpmnChangeFeedOptions? options = null, TimeProvider? timeProvider = null, Func<double>? random = null)
    {
        Options = options ?? new BpmnChangeFeedOptions();
        _time = timeProvider ?? TimeProvider.System;
        _random = random ?? (() => Random.Shared.NextDouble());
    }

    /// <inheritdoc />
    public event Func<BpmnChangeFeedState, ValueTask>? StateChanged;

    /// <inheritdoc />
    public event Func<ValueTask>? Resubscribed;

    /// <summary>Raised when a stream is refused as unauthorized, so the host can renew the credentials and call <see cref="RetryAsync"/>.</summary>
    public event Func<ValueTask>? AuthorizationRequired;

    /// <summary>The heartbeat, staleness and backoff in force.</summary>
    protected BpmnChangeFeedOptions Options { get; }

    /// <inheritdoc />
    public BpmnChangeFeedState State
    {
        get
        {
            lock (_gate) return _state;
        }
    }

    /// <inheritdoc />
    public ValueTask<IAsyncDisposable> SubscribeAsync(
        BpmnDocumentKey key,
        Func<BpmnDocumentChange, ValueTask> handler,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var feed = new Feed(this, key, handler, CancellationTokenSource.CreateLinkedTokenSource(_disposal.Token, cancellationToken));
        lock (_gate) _feeds.Add(feed);
        feed.Start();

        return ValueTask.FromResult<IAsyncDisposable>(feed);
    }

    /// <summary>Resumes streams that stopped because they were unauthorized, after the host renewed the credentials.</summary>
    /// <returns>A task that completes when the streams have been told to resume.</returns>
    public ValueTask RetryAsync()
    {
        List<Feed> waiting;
        lock (_gate) waiting = [.. _feeds];

        foreach (var feed in waiting) feed.Resume();
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await _disposal.CancelAsync().ConfigureAwait(false);
        List<Feed> feeds;
        lock (_gate) feeds = [.. _feeds];
        foreach (var feed in feeds) await feed.DisposeAsync().ConfigureAwait(false);

        await SetStateAsync(BpmnChangeFeedState.Closed).ConfigureAwait(false);
        _disposal.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Opens a stream of live updates for one document.</summary>
    /// <param name="key">The document.</param>
    /// <param name="cancellationToken">Cancels the attempt.</param>
    /// <returns>The open stream, which delivers changes and heartbeats until it ends or fails.</returns>
    /// <exception cref="BpmnChangeFeedUnauthorizedException">The server refused the caller.</exception>
    protected abstract ValueTask<IBpmnChangeConnection> OpenStreamAsync(BpmnDocumentKey key, CancellationToken cancellationToken);

    private static async ValueTask RaiseAsync(Func<ValueTask>? handlers)
    {
        if (handlers is null) return;

        foreach (var handler in handlers.GetInvocationList().Cast<Func<ValueTask>>())
        {
            try
            {
                await handler().ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // A listener that fails must not take the feed down with it.
            }
        }
    }

    private async ValueTask SetStateAsync(BpmnChangeFeedState state)
    {
        lock (_gate)
        {
            if (_state == state) return;
            _state = state;
        }

        if (StateChanged is not { } handlers) return;

        foreach (var handler in handlers.GetInvocationList().Cast<Func<BpmnChangeFeedState, ValueTask>>())
        {
            try
            {
                await handler(state).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // As above.
            }
        }
    }

    private async ValueTask RecomputeStateAsync()
    {
        BpmnChangeFeedState state;
        lock (_gate)
        {
            if (_disposed) return;

            state = _feeds.Any(feed => feed.Phase == FeedPhase.Unauthorized) ? BpmnChangeFeedState.Unauthorized
                : _feeds.Any(feed => feed.Phase == FeedPhase.Reconnecting) ? BpmnChangeFeedState.Reconnecting
                : BpmnChangeFeedState.Live;
        }

        await SetStateAsync(state).ConfigureAwait(false);
    }

    private TimeSpan Backoff(int attempt)
    {
        // No cap when it is infinite or not positive, and never a zero wait: both would otherwise end in a negative delay or a hot loop.
        var cap = Options.MaxBackoff <= TimeSpan.Zero ? double.MaxValue : Options.MaxBackoff.TotalMilliseconds;
        var initial = Math.Max(1d, Options.InitialBackoff.TotalMilliseconds);
        var ceiling = Math.Min(cap, initial * Math.Pow(2, Math.Min(attempt, 20)));

        // Half fixed and half random, so a crowd of clients that lost one server does not come back in one step.
        return TimeSpan.FromMilliseconds(ceiling * (0.5 + (_random() * 0.5)));
    }

    private enum FeedPhase
    {
        Connecting,
        Live,
        Reconnecting,
        Unauthorized,
    }

    private sealed class Feed : IAsyncDisposable
    {
        private readonly ReconnectingBpmnDocumentChangeSubscriber _owner;
        private readonly BpmnDocumentKey _key;
        private readonly Func<BpmnDocumentChange, ValueTask> _handler;
        private readonly CancellationTokenSource _cancellation;
        private TaskCompletionSource _resume = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Task _loop = Task.CompletedTask;

        public Feed(ReconnectingBpmnDocumentChangeSubscriber owner, BpmnDocumentKey key, Func<BpmnDocumentChange, ValueTask> handler, CancellationTokenSource cancellation)
        {
            _owner = owner;
            _key = key;
            _handler = handler;
            _cancellation = cancellation;
        }

        public FeedPhase Phase { get; private set; } = FeedPhase.Connecting;

        public void Start() => _loop = Task.Run(RunAsync);

        public void Resume() => Volatile.Read(ref _resume).TrySetResult();

        public async ValueTask DisposeAsync()
        {
            await _cancellation.CancelAsync().ConfigureAwait(false);

            if (ReferenceEquals(RunningListenerOf.Value, this))
            {
                // Called from the loop's own listener: the loop ends as soon as the listener returns, so it cannot be waited for here, and it is
                // the loop that releases the cancellation source once it has stopped.
                _ = _loop.ContinueWith(_ => _cancellation.Dispose(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            }
            else
            {
                try
                {
                    await _loop.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception)
                {
                    // The loop failed for its own reasons, which it has already answered by reconnecting or by stopping; disposal is not the place to hear of them.
                }

                _cancellation.Dispose();
            }

            lock (_owner._gate) _owner._feeds.Remove(this);
            await _owner.RecomputeStateAsync().ConfigureAwait(false);
        }

        private async ValueTask RaiseListenersAsync(Func<ValueTask>? handlers)
        {
            RunningListenerOf.Value = this;
            try
            {
                await RaiseAsync(handlers).ConfigureAwait(false);
            }
            finally
            {
                RunningListenerOf.Value = null;
            }
        }

        private async Task RunAsync()
        {
            var token = _cancellation.Token;
            var attempt = 0;
            var gap = false;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    await using var stream = await _owner.OpenStreamAsync(_key, token).ConfigureAwait(false);
                    await SetPhaseAsync(FeedPhase.Live).ConfigureAwait(false);
                    attempt = 0;

                    // A reconnect is a gap in delivery, however short: the binding is told, and makes its one catch-up check.
                    if (gap) await RaiseListenersAsync(_owner.Resubscribed).ConfigureAwait(false);
                    gap = true;

                    await PumpAsync(stream, token).ConfigureAwait(false);
                }
                catch (BpmnChangeFeedUnauthorizedException)
                {
                    // The wait is created before anyone is told: a host that renews its credentials and retries from inside the listener completes this
                    // one, and a wait created after the listener returned would never be completed by anybody.
                    var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    Volatile.Write(ref _resume, resume);
                    await SetPhaseAsync(FeedPhase.Unauthorized).ConfigureAwait(false);
                    await RaiseListenersAsync(_owner.AuthorizationRequired).ConfigureAwait(false);
                    try
                    {
                        await resume.Task.WaitAsync(token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }

                    attempt = 0;
                    gap = true;
                    continue;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    // Stale, dropped, refused to connect, a failing server: all are the same gap to the binding.
                }

                await SetPhaseAsync(FeedPhase.Reconnecting).ConfigureAwait(false);
                try
                {
                    await Task.Delay(_owner.Backoff(attempt++), _owner._time, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        private async Task PumpAsync(IBpmnChangeConnection stream, CancellationToken token)
        {
            while (true)
            {
                // Every message, heartbeat included, restarts the wait; silence for too long ends the connection.
                using var stale = CancellationTokenSource.CreateLinkedTokenSource(token);
                using var timer = _owner._time.CreateTimer(_ => CancelStale(stale), null, _owner.Options.StalenessTimeout, Timeout.InfiniteTimeSpan);

                BpmnChangeMessage? message;
                try
                {
                    message = await stream.ReadAsync(stale.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    throw new TimeoutException("The live-update connection went silent.");
                }

                if (message is null) return;

                if (message.Value.Change is { } change)
                {
                    RunningListenerOf.Value = this;
                    try
                    {
                        await _handler(change).ConfigureAwait(false);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        // The binding's handler failing is the binding's business; the feed carries on.
                    }
                    finally
                    {
                        RunningListenerOf.Value = null;
                    }
                }
            }
        }

        // A callback already queued when the message arrived runs after the iteration has disposed its source, and cancelling a disposed one throws on a thread-pool thread.
        private static void CancelStale(CancellationTokenSource stale)
        {
            try
            {
                stale.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private async ValueTask SetPhaseAsync(FeedPhase phase)
        {
            Phase = phase;
            await _owner.RecomputeStateAsync().ConfigureAwait(false);
        }
    }
}
