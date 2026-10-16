using Hasseware.AspNetCore.Components.Bpmn.Storage;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Storage;

/// <summary>The in-process bus delivers each change to that document's subscribers, and only theirs.</summary>
public sealed class InProcessBpmnDocumentChangeBusTests
{
    private static readonly BpmnDocumentKey First = new(null, BpmnDocumentPath.Parse("first.bpmn"));
    private static readonly BpmnDocumentKey Second = new(null, BpmnDocumentPath.Parse("second.bpmn"));

    [Fact]
    public async Task DeliversAChangeToThatDocumentsSubscribersOnly()
    {
        var bus = new InProcessBpmnDocumentChangeBus();
        var first = new List<BpmnDocumentChange>();
        var second = new List<BpmnDocumentChange>();
        await bus.SubscribeAsync(First, Collect(first), Token);
        await bus.SubscribeAsync(Second, Collect(second), Token);

        await bus.PublishAsync(Change(First, "r1"), Token);

        Assert.Equal("r1", Assert.Single(first).Revision);
        Assert.Empty(second);
    }

    [Fact]
    public async Task StopsDeliveringOnceTheSubscriptionIsDisposed()
    {
        var bus = new InProcessBpmnDocumentChangeBus();
        var received = new List<BpmnDocumentChange>();
        var subscription = await bus.SubscribeAsync(First, Collect(received), Token);

        await subscription.DisposeAsync();
        await bus.PublishAsync(Change(First, "r1"), Token);

        Assert.Empty(received);
    }

    [Fact]
    public async Task KeepsDeliveringWhenAHandlerThrows()
    {
        var bus = new InProcessBpmnDocumentChangeBus();
        var received = new List<BpmnDocumentChange>();
        await bus.SubscribeAsync(First, _ => throw new InvalidOperationException("boom"), Token);
        await bus.SubscribeAsync(First, Collect(received), Token);

        await bus.PublishAsync(Change(First, "r1"), Token);

        Assert.Single(received);
    }

    [Fact]
    public async Task LetsAHandlerUnsubscribeWhileItIsBeingCalled()
    {
        var bus = new InProcessBpmnDocumentChangeBus();
        var calls = 0;
        IAsyncDisposable? subscription = null;
        subscription = await bus.SubscribeAsync(First, async _ =>
        {
            calls++;
            await subscription!.DisposeAsync();
        }, Token);

        await bus.PublishAsync(Change(First, "r1"), Token);
        await bus.PublishAsync(Change(First, "r2"), Token);

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task DoesNotMakeTheWriterWaitForSlowSubscribers()
    {
        // One editor reloading slowly must not hold up the save that caused it, nor anyone else's notice.
        var bus = new InProcessBpmnDocumentChangeBus();
        var never = new TaskCompletionSource();
        var fast = new List<BpmnDocumentChange>();
        await bus.SubscribeAsync(First, _ => new ValueTask(never.Task), Token);
        await bus.SubscribeAsync(First, Collect(fast), Token);

        var publishing = bus.PublishAsync(Change(First, "r1"), Token);

        Assert.True(publishing.IsCompletedSuccessfully);
        Assert.Single(fast);
        never.SetResult();
    }

    [Fact]
    public async Task SurvivesASubscriberThatFailsLater()
    {
        var bus = new InProcessBpmnDocumentChangeBus();
        var failing = new TaskCompletionSource();
        await bus.SubscribeAsync(First, _ => new ValueTask(failing.Task), Token);

        await bus.PublishAsync(Change(First, "r1"), Token);
        failing.SetException(new InvalidOperationException("late failure"));
        await Task.Yield();

        await bus.PublishAsync(Change(First, "r2"), Token);
    }

    [Fact]
    public async Task RefusesASubscriptionToNothing()
    {
        var bus = new InProcessBpmnDocumentChangeBus();

        await Assert.ThrowsAsync<ArgumentException>(async () => await bus.SubscribeAsync(default, _ => ValueTask.CompletedTask, Token));
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static BpmnDocumentChange Change(BpmnDocumentKey key, string revision) =>
        new(key, BpmnDocumentChangeKind.Updated, revision, Origin: null, DateTimeOffset.UnixEpoch);

    private static Func<BpmnDocumentChange, ValueTask> Collect(List<BpmnDocumentChange> into) => change =>
    {
        into.Add(change);
        return ValueTask.CompletedTask;
    };
}
