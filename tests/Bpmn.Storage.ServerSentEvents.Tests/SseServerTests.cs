using System.Net;
using System.Net.ServerSentEvents;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Hasseware.AspNetCore.Components.Bpmn.Storage.ServerSentEvents.Tests;

/// <summary>The Server-Sent Events endpoint: who is let in, what they may watch, what is on the wire, and what it costs the server.</summary>
public sealed class SseServerTests
{
    private static readonly string[] WireProperties = ["kind", "occurredAt", "origin", "path", "revision"];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static BpmnDocumentChange Change(string tenant, string path, string revision) =>
        new(new BpmnDocumentKey(tenant, BpmnDocumentPath.Parse(path)), BpmnDocumentChangeKind.Updated, revision, "writer-1", DateTimeOffset.UnixEpoch);

    private static async Task<HttpResponseMessage> OpenAsync(HttpClient http, string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/bpmn/changes?path=" + Uri.EscapeDataString(path));
        request.Headers.Accept.ParseAdd("text/event-stream");
        return await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Token);
    }

    private static async Task<List<SseItem<string>>> ReadAsync(HttpResponseMessage response, int count)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        var items = new List<SseItem<string>>();
        await foreach (var item in SseParser.Create(stream, (_, bytes) => System.Text.Encoding.UTF8.GetString(bytes)).EnumerateAsync(timeout.Token))
        {
            items.Add(item);
            if (items.Count == count) break;
        }

        return items;
    }

    private static async Task ReadFromAsync(Stream stream, int count)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var seen = 0;
        await foreach (var _ in SseParser.Create(stream).EnumerateAsync(timeout.Token))
        {
            if (++seen == count) break;
        }
    }

    [Fact]
    public async Task RefusesACallerWhoHasNotAuthenticated()
    {
        await using var harness = await SseHarness.StartAsync(user: null);

        using var response = await OpenAsync(harness.Http, "a.bpmn");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RefusesACallerWithoutATenantClaimInsteadOfServingTheSharedSpace()
    {
        await using var harness = await SseHarness.StartAsync(user: "carol");

        using var response = await OpenAsync(harness.Http, "a.bpmn");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
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
        await using var harness = await SseHarness.StartAsync();

        using var response = await OpenAsync(harness.Http, path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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
        await using var harness = await SseHarness.StartAsync(services: services => services.AddSingleton<IBpmnDocumentAuthorizer, DenySecrets>());

        using var forbidden = await OpenAsync(harness.Http, "secret/plans.bpmn");
        using var invalid = await OpenAsync(harness.Http, "../plans.bpmn");

        Assert.Equal(HttpStatusCode.NotFound, forbidden.StatusCode);
        Assert.Equal(invalid.StatusCode, forbidden.StatusCode);
        Assert.Equal(await invalid.Content.ReadAsStringAsync(Token), await forbidden.Content.ReadAsStringAsync(Token));
    }

    [Fact]
    public async Task NeverShowsATenantAChangeOfAnotherTenant()
    {
        await using var harness = await SseHarness.StartAsync();
        using var response = await OpenAsync(harness.Http, "a.bpmn");
        var reading = ReadAsync(response, 3);

        await harness.PublishAsync(Change("other", "a.bpmn", "theirs"));
        await Task.Delay(100, Token);
        await harness.PublishAsync(Change("acme", "a.bpmn", "mine"));
        var items = await reading;

        Assert.DoesNotContain(items, item => item.Data.Contains("theirs", StringComparison.Ordinal));
        Assert.Contains(items, item => item.EventType == "change" && item.Data.Contains("mine", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PutsOnlyPathKindRevisionOriginAndTimeOnTheWire()
    {
        await using var harness = await SseHarness.StartAsync();
        using var response = await OpenAsync(harness.Http, "claims/a.bpmn");
        var reading = ReadAsync(response, 3);

        await Task.Delay(50, Token);
        await harness.PublishAsync(Change("acme", "claims/a.bpmn", "r9"));
        var items = await reading;

        var change = items.First(item => item.EventType == "change");
        using var json = JsonDocument.Parse(change.Data);
        Assert.Equal(WireProperties, json.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.DoesNotContain("acme", change.Data, StringComparison.Ordinal);
        Assert.Equal("claims/a.bpmn", json.RootElement.GetProperty("path").GetString());
    }

    [Fact]
    public async Task SaysReadyThenPingsSoThatAProxyAndAClientCanTellTheConnectionIsAlive()
    {
        await using var harness = await SseHarness.StartAsync();
        using var response = await OpenAsync(harness.Http, "a.bpmn");

        var items = await ReadAsync(response, 4);

        Assert.Equal(["ready", "ping", "ping", "ping"], items.Select(item => item.EventType));
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task HoldsOneSubscriptionPerOpenStreamAndFreesItWhenTheStreamEnds()
    {
        await using var harness = await SseHarness.StartAsync();
        var response = await OpenAsync(harness.Http, "a.bpmn");
        await ReadAsync(response, 1);
        Assert.Equal(1, harness.Feed.Open);

        response.Dispose();
        harness.Http.CancelPendingRequests();
        await WaitForOpenAsync(harness, 0);
    }

    private static async Task WaitForOpenAsync(SseHarness harness, int expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (harness.Feed.Open != expected)
        {
            Assert.True(DateTime.UtcNow < deadline, $"Expected {expected} open subscriptions, found {harness.Feed.Open}.");
            await Task.Delay(20, Token);
        }
    }

    [Fact]
    public async Task RefusesAnotherStreamFromACallerAtTheirLimitAndAnyonesAtTheHostLimit()
    {
        await using var harness = await SseHarness.StartAsync(server =>
        {
            server.MaxSubscriptionsPerCaller = 1;
            server.MaxSubscriptions = 2;
        });
        using var first = await OpenAsync(harness.Http, "a.bpmn");
        await using var firstStream = await first.Content.ReadAsStreamAsync(Token);
        await ReadFromAsync(firstStream, 1);

        using var sameCaller = await OpenAsync(harness.Http, "b.bpmn");

        Assert.Equal(HttpStatusCode.TooManyRequests, sameCaller.StatusCode);

        using var other = new HttpClient(new KillableHandler(harness.Handler)) { BaseAddress = harness.Http.BaseAddress };
        other.DefaultRequestHeaders.Add("X-User", "bob,acme");
        using var bob = await OpenAsync(other, "b.bpmn");
        Assert.Equal(HttpStatusCode.OK, bob.StatusCode);
        await using var bobStream = await bob.Content.ReadAsStreamAsync(Token);
        await ReadFromAsync(bobStream, 1);

        using var third = new HttpClient(new KillableHandler(harness.Handler)) { BaseAddress = harness.Http.BaseAddress };
        third.DefaultRequestHeaders.Add("X-User", "dave,acme");
        using var overflow = await OpenAsync(third, "c.bpmn");
        Assert.Equal(HttpStatusCode.TooManyRequests, overflow.StatusCode);
    }

    [Fact]
    public async Task EndsAStreamAfterItsLifetimeSoThatItNeverOutlivesTheCredentialsItWasOpenedWith()
    {
        await using var harness = await SseHarness.StartAsync(server => server.MaxStreamLifetime = TimeSpan.FromMilliseconds(400));
        using var response = await OpenAsync(harness.Http, "a.bpmn");

        await using var stream = await response.Content.ReadAsStreamAsync(Token);
        var items = 0;
        await foreach (var _ in SseParser.Create(stream).EnumerateAsync(Token)) items++;

        Assert.InRange(items, 1, 10);
        await WaitForOpenAsync(harness, 0);
    }

    [Fact]
    public async Task RefusesToStartWithoutADecisionAboutTenantsOrWithContradictoryOnes()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await SseHarness.StartAsync(server => server.TenantClaimType = null));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await SseHarness.StartAsync(server => server.SingleTenant = true));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await SseHarness.StartAsync(server =>
        {
            server.TenantClaimType = "tenant";
            server.AllowAnonymous = true;
        }));
    }

    [Fact]
    public async Task ServesCallersWithoutATenantWhenEveryoneSharesOneSpace()
    {
        await using var harness = await SseHarness.StartAsync(
            server =>
            {
                server.TenantClaimType = null;
                server.SingleTenant = true;
            },
            user: "carol");

        using var response = await OpenAsync(harness.Http, "a.bpmn");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
