using Hasseware.AspNetCore.Components.Bpmn.Storage;
using Hasseware.AspNetCore.Components.Bpmn.Storage.Testing;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Storage;

/// <summary>The in-memory store meets the store contract, plus what is particular to it.</summary>
public sealed class InMemoryBpmnDocumentStoreTests : BpmnDocumentStoreContractTests
{
    /// <inheritdoc />
    protected override IBpmnDocumentStore CreateStore() => new InMemoryBpmnDocumentStore();

    [Fact]
    public async Task RefusesACursorItDidNotProduce()
    {
        var store = CreateStore();

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await store.ListAsync(new BpmnDocumentQuery(null, Cursor: "not-a-cursor!"), Token));
    }

    [Fact]
    public async Task KeepsNamesThatDifferOnlyByCaseAsTwoDocuments()
    {
        var store = CreateStore();

        var upper = await store.SaveAsync(Key("Order.bpmn"), Content("upper"), ifMatchRevision: null, Token);
        var lower = await store.SaveAsync(Key("order.bpmn"), Content("lower"), ifMatchRevision: null, Token);

        Assert.True(upper.Succeeded);
        Assert.True(lower.Succeeded);
        Assert.Equal("lower", Text((await store.OpenAsync(Key("order.bpmn"), Token))!.Content));
    }

    [Fact]
    public async Task StampsWritesWithTheGivenClock()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 10, 13, 9, 0, 0, TimeSpan.Zero));
        var store = new InMemoryBpmnDocumentStore(clock);

        var saved = await store.SaveAsync(Key("a.bpmn"), Content("a"), ifMatchRevision: null, Token);

        Assert.Equal(clock.GetUtcNow(), saved.Modified);
        Assert.Equal(clock.GetUtcNow(), (await store.StatAsync(Key("a.bpmn"), Token))!.Modified);
    }
}
