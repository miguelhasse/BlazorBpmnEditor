using System.Net;
using System.Text.Json;

using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Hasseware.AspNetCore.Components.Bpmn.Storage.SignalR.Tests;

/// <summary>The hub: who is let in, what they may watch, what is on the wire, and what it costs the server.</summary>
public sealed class SignalRServerTests
{
    private static readonly string[] WireProperties = ["kind", "occurredAt", "origin", "path", "revision"];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static BpmnDocumentChange Change(string tenant, string path, string revision) =>
        new(new BpmnDocumentKey(tenant, BpmnDocumentPath.Parse(path)), BpmnDocumentChangeKind.Updated, revision, "writer-1", DateTimeOffset.UnixEpoch);

    private static HubConnection Connect(SignalRHarness harness, string? user = "alice,acme", List<string>? received = null)
    {
        var hub = new HubConnectionBuilder()
            .WithUrl(SignalRHarness.HubUrl, http => SignalRHarness.Configure(http, harness.Handlers, user))
            .Build();
        hub.On<string>("Changed", json =>
        {
            if (received is null) return;
            lock (received) received.Add(json);
        });
        return hub;
    }

    private static async Task WaitForAsync(Func<bool> condition, string because)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for: " + because);
            await Task.Delay(20, Token);
        }
    }

    [Fact]
    public async Task RefusesACallerWhoHasNotAuthenticated()
    {
        await using var harness = await SignalRHarness.StartAsync();
        await using var hub = Connect(harness, user: null);

        var failure = await Assert.ThrowsAsync<HttpRequestException>(async () => await hub.StartAsync(Token));

        Assert.Equal(HttpStatusCode.Unauthorized, failure.StatusCode);
    }

    [Fact]
    public async Task RefusesACallerWithoutATenantClaimInsteadOfServingTheSharedSpace()
    {
        await using var harness = await SignalRHarness.StartAsync();
        await using var hub = Connect(harness, user: "carol");

        var failure = await Assert.ThrowsAsync<HttpRequestException>(async () => await hub.StartAsync(Token));

        Assert.Equal(HttpStatusCode.Forbidden, failure.StatusCode);
        Assert.Equal(0, harness.Feed.Open);
    }

    [Theory]
    [InlineData("../a.bpmn")]
    [InlineData("/a.bpmn")]
    [InlineData("a\\b.bpmn")]
    [InlineData("notes.txt")]
    [InlineData("")]
    public async Task AnswersAPathThatIsNotADocumentAsNotFound(string path)
    {
        await using var harness = await SignalRHarness.StartAsync();
        await using var hub = Connect(harness);
        await hub.StartAsync(Token);

        Assert.Equal("notfound", await hub.InvokeAsync<string>("Subscribe", path, Token));
        Assert.Equal(0, harness.Feed.Open);
    }

    private sealed class DenySecrets : IBpmnDocumentAuthorizer
    {
        public ValueTask<bool> IsAllowedAsync(BpmnDocumentAction action, BpmnDocumentKey key, CancellationToken cancellationToken) =>
            ValueTask.FromResult(!key.Path.ToString().StartsWith("secret", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnswersADocumentTheCallerMayNotReadExactlyLikeAPathThatIsNotADocument()
    {
        await using var harness = await SignalRHarness.StartAsync(services: services => services.AddSingleton<IBpmnDocumentAuthorizer, DenySecrets>());
        await using var hub = Connect(harness);
        await hub.StartAsync(Token);

        Assert.Equal(await hub.InvokeAsync<string>("Subscribe", "../plans.bpmn", Token), await hub.InvokeAsync<string>("Subscribe", "secret/plans.bpmn", Token));
        Assert.Equal(0, harness.Feed.Open);
    }

    [Fact]
    public async Task NeverShowsATenantAChangeOfAnotherTenant()
    {
        await using var harness = await SignalRHarness.StartAsync();
        var received = new List<string>();
        await using var hub = Connect(harness, received: received);
        await hub.StartAsync(Token);
        Assert.Equal("ok", await hub.InvokeAsync<string>("Subscribe", "a.bpmn", Token));

        await harness.PublishAsync(Change("other", "a.bpmn", "theirs"));
        await Task.Delay(100, Token);
        await harness.PublishAsync(Change("acme", "a.bpmn", "mine"));

        await WaitForAsync(() => received.Count > 0, "my change");
        lock (received)
        {
            Assert.DoesNotContain(received, json => json.Contains("theirs", StringComparison.Ordinal));
            Assert.Contains(received, json => json.Contains("mine", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task PutsOnlyPathKindRevisionOriginAndTimeOnTheWire()
    {
        await using var harness = await SignalRHarness.StartAsync();
        var received = new List<string>();
        await using var hub = Connect(harness, received: received);
        await hub.StartAsync(Token);
        await hub.InvokeAsync<string>("Subscribe", "claims/a.bpmn", Token);

        await harness.PublishAsync(Change("acme", "claims/a.bpmn", "r9"));

        await WaitForAsync(() => received.Count > 0, "the change");
        string json;
        lock (received) json = received[0];
        using var parsed = JsonDocument.Parse(json);
        Assert.Equal(WireProperties, parsed.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.DoesNotContain("acme", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HoldsOneSubscriptionPerDocumentAndFreesItOnUnsubscribeAndOnDisconnect()
    {
        await using var harness = await SignalRHarness.StartAsync();
        var hub = Connect(harness);
        await hub.StartAsync(Token);
        await hub.InvokeAsync<string>("Subscribe", "a.bpmn", Token);
        await hub.InvokeAsync<string>("Subscribe", "a.bpmn", Token);
        await hub.InvokeAsync<string>("Subscribe", "b.bpmn", Token);
        Assert.Equal(2, harness.Feed.Open);

        await hub.InvokeAsync("Unsubscribe", "a.bpmn", Token);
        Assert.Equal(1, harness.Feed.Open);

        await hub.DisposeAsync();
        await WaitForAsync(() => harness.Feed.Open == 0, "the disconnect to free the subscription");
    }

    [Fact]
    public async Task RefusesAnotherSubscriptionFromACallerAtTheirLimitAndAnyonesAtTheHostLimit()
    {
        await using var harness = await SignalRHarness.StartAsync(server =>
        {
            server.MaxSubscriptionsPerCaller = 1;
            server.MaxSubscriptions = 2;
        });
        await using var alice = Connect(harness);
        await alice.StartAsync(Token);
        await using var bob = Connect(harness, "bob,acme");
        await bob.StartAsync(Token);
        await using var dave = Connect(harness, "dave,acme");
        await dave.StartAsync(Token);

        Assert.Equal("ok", await alice.InvokeAsync<string>("Subscribe", "a.bpmn", Token));
        Assert.Equal("toomany", await alice.InvokeAsync<string>("Subscribe", "b.bpmn", Token));
        Assert.Equal("ok", await bob.InvokeAsync<string>("Subscribe", "b.bpmn", Token));
        Assert.Equal("toomany", await dave.InvokeAsync<string>("Subscribe", "c.bpmn", Token));
    }

    [Fact]
    public async Task EndsAConnectionAfterItsLifetimeSoThatItNeverOutlivesTheCredentialsItWasOpenedWith()
    {
        await using var harness = await SignalRHarness.StartAsync(server => server.MaxStreamLifetime = TimeSpan.FromMilliseconds(500));
        var hub = Connect(harness);
        var closed = new TaskCompletionSource();
        hub.Closed += _ =>
        {
            closed.TrySetResult();
            return Task.CompletedTask;
        };
        await hub.StartAsync(Token);
        await hub.InvokeAsync<string>("Subscribe", "a.bpmn", Token);

        await closed.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);

        await WaitForAsync(() => harness.Feed.Open == 0, "the ended connection to free its subscription");
        await hub.DisposeAsync();
    }

    [Fact]
    public async Task RefusesToStartWithoutADecisionAboutTenantsOrWithContradictoryOnes()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await SignalRHarness.StartAsync(server => server.TenantClaimType = null));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await SignalRHarness.StartAsync(server => server.SingleTenant = true));
    }

    [Fact]
    public async Task ServesCallersWithoutATenantWhenEveryoneSharesOneSpace()
    {
        await using var harness = await SignalRHarness.StartAsync(
            server =>
            {
                server.TenantClaimType = null;
                server.SingleTenant = true;
            },
            user: "carol");
        await using var hub = Connect(harness, "carol");
        await hub.StartAsync(Token);

        Assert.Equal("ok", await hub.InvokeAsync<string>("Subscribe", "a.bpmn", Token));
    }
}
