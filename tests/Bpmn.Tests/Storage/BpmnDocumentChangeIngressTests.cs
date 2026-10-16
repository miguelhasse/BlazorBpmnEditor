using Hasseware.AspNetCore.Components.Bpmn.Storage;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Storage;

/// <summary>Signals from outside the store reach the bus once each, and a repeated one does not.</summary>
public sealed class BpmnDocumentChangeIngressTests
{
    private static readonly BpmnDocumentKey Intake = new(null, BpmnDocumentPath.Parse("claims/intake.bpmn"));

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task<List<BpmnDocumentChange>> CollectAsync(InProcessBpmnDocumentChangeBus bus, BpmnDocumentKey key, List<BpmnDocumentChange> received)
    {
        await bus.SubscribeAsync(key, change =>
        {
            lock (received) received.Add(change);
            return ValueTask.CompletedTask;
        }, Token);
        return received;
    }

    [Fact]
    public async Task PublishesAnUpdateWithItsRevisionAndTheTimeItWasPublished()
    {
        var bus = new InProcessBpmnDocumentChangeBus();
        var received = await CollectAsync(bus, Intake, []);
        var ingress = new BpmnDocumentChangeIngress(bus);

        var published = await ingress.PublishUpdatedAsync(Intake, "\"0x1\"", Token);

        Assert.True(published);
        var change = Assert.Single(received);
        Assert.Equal(BpmnDocumentChangeKind.Updated, change.Kind);
        Assert.Equal("\"0x1\"", change.Revision);
        Assert.Equal(Intake, change.Key);
    }

    [Fact]
    public async Task DropsASignalThatRepeatsTheRevisionLastAnnouncedBecauseDeliveryIsAtLeastOnce()
    {
        var bus = new InProcessBpmnDocumentChangeBus();
        var received = await CollectAsync(bus, Intake, []);
        var ingress = new BpmnDocumentChangeIngress(bus);

        await ingress.PublishUpdatedAsync(Intake, "r1", Token);
        var repeated = await ingress.PublishUpdatedAsync(Intake, "r1", Token);
        await ingress.PublishUpdatedAsync(Intake, "r2", Token);
        var backToTheFirst = await ingress.PublishUpdatedAsync(Intake, "r1", Token);

        Assert.False(repeated);
        Assert.True(backToTheFirst);
        Assert.Equal(["r1", "r2", "r1"], received.Select(change => change.Revision));
    }

    [Fact]
    public async Task PublishesADeletionOnceAndAnUpdateAfterIt()
    {
        var bus = new InProcessBpmnDocumentChangeBus();
        var received = await CollectAsync(bus, Intake, []);
        var ingress = new BpmnDocumentChangeIngress(bus);

        await ingress.PublishDeletedAsync(Intake, Token);
        var again = await ingress.PublishDeletedAsync(Intake, Token);
        await ingress.PublishUpdatedAsync(Intake, "r5", Token);

        Assert.False(again);
        Assert.Equal([BpmnDocumentChangeKind.Deleted, BpmnDocumentChangeKind.Updated], received.Select(change => change.Kind));
    }

    [Fact]
    public async Task KeepsAnEntryPerDocumentWithoutGrowingWithoutBound()
    {
        var bus = new InProcessBpmnDocumentChangeBus();
        var ingress = new BpmnDocumentChangeIngress(bus);

        for (var index = 0; index < 6000; index++)
        {
            await ingress.PublishUpdatedAsync(new BpmnDocumentKey(null, BpmnDocumentPath.Parse($"d{index}.bpmn")), "r1", Token);
        }

        // The oldest were forgotten, so announcing one again publishes; the newest are still remembered.
        Assert.True(await ingress.PublishUpdatedAsync(new BpmnDocumentKey(null, BpmnDocumentPath.Parse("d0.bpmn")), "r1", Token));
        Assert.False(await ingress.PublishUpdatedAsync(new BpmnDocumentKey(null, BpmnDocumentPath.Parse("d5999.bpmn")), "r1", Token));
    }

    private sealed class FlakyBus(InProcessBpmnDocumentChangeBus inner) : IBpmnDocumentChangeBus
    {
        public bool Fail { get; set; }

        public ValueTask PublishAsync(BpmnDocumentChange change, CancellationToken cancellationToken = default) =>
            Fail ? throw new InvalidOperationException("The bus is down.") : inner.PublishAsync(change, cancellationToken);

        public ValueTask<IAsyncDisposable> SubscribeAsync(BpmnDocumentKey key, Func<BpmnDocumentChange, ValueTask> handler, CancellationToken cancellationToken = default) =>
            inner.SubscribeAsync(key, handler, cancellationToken);
    }

    [Fact]
    public async Task LetsARedeliveredSignalThroughWhenTheFirstPublicationFailed()
    {
        var bus = new FlakyBus(new InProcessBpmnDocumentChangeBus()) { Fail = true };
        var ingress = new BpmnDocumentChangeIngress(bus);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await ingress.PublishUpdatedAsync(Intake, "r1", Token));
        bus.Fail = false;

        Assert.True(await ingress.PublishUpdatedAsync(Intake, "r1", Token));
    }

    [Fact]
    public async Task DoesNotTakeAnUpdateWithoutARevisionForADeletionOrTheReverse()
    {
        var ingress = new BpmnDocumentChangeIngress(new InProcessBpmnDocumentChangeBus());

        await ingress.PublishDeletedAsync(Intake, Token);
        Assert.True(await ingress.PublishUpdatedAsync(Intake, revision: null, Token));
        Assert.True(await ingress.PublishDeletedAsync(Intake, Token));
    }
}
