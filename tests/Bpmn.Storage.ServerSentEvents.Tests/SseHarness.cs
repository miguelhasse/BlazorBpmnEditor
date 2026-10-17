using System.Security.Claims;
using System.Text.Encodings.Web;
using Hasseware.AspNetCore.Components.Bpmn.Storage.ServerSentEvents.Client;
using Hasseware.AspNetCore.Components.Bpmn.Storage.Testing;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hasseware.AspNetCore.Components.Bpmn.Storage.ServerSentEvents.Tests;

internal sealed class AuthState
{
    public volatile bool Revoked;
}

// Signs a caller in from a header, "name" or "name,tenant", unless the test has revoked access: a stand-in for a real scheme.
internal sealed class HeaderAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, AuthState state)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (state.Revoked || !Request.Headers.TryGetValue("X-User", out var value)) return Task.FromResult(AuthenticateResult.NoResult());

        var parts = value.ToString().Split(',');
        List<Claim> claims = [new(ClaimTypes.NameIdentifier, parts[0]), new(ClaimTypes.Name, parts[0])];
        if (parts.Length > 1) claims.Add(new Claim("tenant", parts[1]));

        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, "Header")), "Header")));
    }
}

// Cuts the network under every request in flight, the way a dropped connection does, while the server keeps running.
internal sealed class KillableHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    private readonly List<CancellationTokenSource> _active = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_active) _active.Add(cancellation);

        return await base.SendAsync(request, cancellation.Token).ConfigureAwait(false);
    }

    public void KillAll()
    {
        List<CancellationTokenSource> active;
        lock (_active)
        {
            active = [.. _active];
            _active.Clear();
        }

        foreach (var cancellation in active) cancellation.Cancel();
    }
}

/// <summary>The change feed hosted in-process: a server over a bus, and a subscriber that reaches it through the test server.</summary>
internal sealed class SseHarness : IBpmnChangeFeedHarness
{
    public static readonly TimeSpan Heartbeat = TimeSpan.FromMilliseconds(150);

    private readonly WebApplication _app;
    private readonly KillableHandler _handler;
    private readonly AuthState _auth;
    private readonly SseBpmnDocumentChangeSubscriber _subscriber;

    private SseHarness(WebApplication app, KillableHandler handler, AuthState auth, SseBpmnDocumentChangeSubscriber subscriber, HttpClient http)
    {
        _app = app;
        _handler = handler;
        _auth = auth;
        _subscriber = subscriber;
        Http = http;
    }

    public HttpClient Http { get; }

    public HttpMessageHandler Handler => _app.GetTestServer().CreateHandler();

    public IBpmnDocumentChangeBus Bus => _app.Services.GetRequiredService<IBpmnDocumentChangeBus>();

    public BpmnChangeFeedServer Feed => _app.Services.GetRequiredService<BpmnChangeFeedServer>();

    public IBpmnDocumentChangeSubscriber Subscriber => _subscriber;

    public string? Tenant => "acme";

    public static async Task<SseHarness> StartAsync(
        Action<BpmnChangeFeedServerOptions>? server = null,
        Action<IServiceCollection>? services = null,
        string? user = "alice,acme")
    {
        var auth = new AuthState();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(auth);
        builder.Services.AddAuthentication("Header").AddScheme<AuthenticationSchemeOptions, HeaderAuthentication>("Header", null);
        builder.Services.AddAuthorization();
        services?.Invoke(builder.Services);
        builder.Services.AddBpmnChangeEvents(options =>
        {
            options.TenantClaimType = "tenant";
            options.HeartbeatInterval = Heartbeat;
            server?.Invoke(options);
        });

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapBpmnDocumentChangeEvents("/bpmn/changes");
        await app.StartAsync(TestContext.Current.CancellationToken);

        var handler = new KillableHandler(app.GetTestServer().CreateHandler());
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        if (user is not null) http.DefaultRequestHeaders.Add("X-User", user);

        var subscriber = new SseBpmnDocumentChangeSubscriber(
            http,
            options: new BpmnChangeFeedOptions { HeartbeatInterval = Heartbeat, InitialBackoff = TimeSpan.FromMilliseconds(40), MaxBackoff = TimeSpan.FromMilliseconds(200) });

        return new SseHarness(app, handler, auth, subscriber, http);
    }

    public ValueTask PublishAsync(BpmnDocumentChange change) => Bus.PublishAsync(change);

    public ValueTask DropConnectionsAsync()
    {
        _handler.KillAll();
        return ValueTask.CompletedTask;
    }

    public ValueTask RevokeAuthorizationAsync()
    {
        _auth.Revoked = true;
        _handler.KillAll();
        return ValueTask.CompletedTask;
    }

    public async ValueTask RestoreAuthorizationAsync()
    {
        _auth.Revoked = false;
        await _subscriber.RetryAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _subscriber.DisposeAsync();
        Http.Dispose();
        await _app.DisposeAsync();
    }
}

/// <summary>The Server-Sent Events transport meets the subscriber contract.</summary>
public sealed class SseSubscriberContractTests : BpmnDocumentChangeSubscriberContractTests
{
    /// <inheritdoc />
    protected override async ValueTask<IBpmnChangeFeedHarness> StartAsync() => await SseHarness.StartAsync();
}

/// <summary>A change made while the feed was down still reaches the binding, after one catch-up check.</summary>
public sealed class SseBindingLongSessionTests : BpmnBindingLongSessionContractTests
{
    /// <inheritdoc />
    protected override async ValueTask<IBpmnChangeFeedHarness> StartAsync() => await SseHarness.StartAsync();
}
