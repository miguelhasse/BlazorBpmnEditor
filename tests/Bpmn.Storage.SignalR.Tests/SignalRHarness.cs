using System.Security.Claims;
using System.Text.Encodings.Web;

using Hasseware.AspNetCore.Components.Bpmn.Storage;
using Hasseware.AspNetCore.Components.Bpmn.Storage.SignalR;
using Hasseware.AspNetCore.Components.Bpmn.Storage.SignalR.Client;
using Hasseware.AspNetCore.Components.Bpmn.Storage.Testing;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hasseware.AspNetCore.Components.Bpmn.Storage.SignalR.Tests;

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

// Cuts the network under every request in flight, the way a dropped connection does, while the server keeps running. A request that
// is cut fails with an HttpRequestException, which is what a transport sees from a real network, rather than a cancellation, which
// a long-polling transport takes to mean that it was asked to stop.
internal sealed class KillableHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    private readonly List<(CancellationTokenSource Cancellation, TaskCompletionSource Cut)> _active = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var cut = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_active) _active.Add((cancellation, cut));

        var send = base.SendAsync(request, cancellation.Token);
        if (await Task.WhenAny(send, cut.Task).ConfigureAwait(false) == cut.Task)
        {
            cancellation.Cancel();
            throw new HttpRequestException("The network connection was cut.");
        }

        return await send.ConfigureAwait(false);
    }

    public void KillAll()
    {
        List<(CancellationTokenSource Cancellation, TaskCompletionSource Cut)> active;
        lock (_active)
        {
            active = [.. _active];
            _active.Clear();
        }

        foreach (var (_, cut) in active) cut.TrySetResult();
    }
}

// SignalR disposes the handler of a connection that has closed, so every connection needs a handler of its own; this makes them and cuts them all at once.
internal sealed class HandlerSource(Func<HttpMessageHandler> create)
{
    private readonly List<KillableHandler> _handlers = [];

    public HttpMessageHandler Create()
    {
        var handler = new KillableHandler(create());
        lock (_handlers) _handlers.Add(handler);
        return handler;
    }

    public void KillAll()
    {
        List<KillableHandler> handlers;
        lock (_handlers) handlers = [.. _handlers];
        foreach (var handler in handlers) handler.KillAll();
    }
}

/// <summary>The change hub hosted in-process: a server over a bus, and a subscriber that reaches it through the test server.</summary>
internal sealed class SignalRHarness : IBpmnChangeFeedHarness
{
    private readonly WebApplication _app;
    private readonly HandlerSource _handlers;
    private readonly AuthState _auth;
    private readonly SignalRBpmnDocumentChangeSubscriber _subscriber;

    private SignalRHarness(WebApplication app, HandlerSource handlers, AuthState auth, SignalRBpmnDocumentChangeSubscriber subscriber)
    {
        _app = app;
        _handlers = handlers;
        _auth = auth;
        _subscriber = subscriber;
    }

    public IBpmnDocumentChangeBus Bus => _app.Services.GetRequiredService<IBpmnDocumentChangeBus>();

    public BpmnChangeFeedServer Feed => _app.Services.GetRequiredService<BpmnChangeFeedServer>();

    public IBpmnDocumentChangeSubscriber Subscriber => _subscriber;

    public string? Tenant => "acme";

    public static Uri HubUrl { get; } = new("http://localhost/bpmn/changes");

    public static async Task<SignalRHarness> StartAsync(
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
        builder.Services.AddBpmnChangeHub(options =>
        {
            options.TenantClaimType = "tenant";
            server?.Invoke(options);
        });

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapBpmnDocumentChangeHub("/bpmn/changes");
        await app.StartAsync(TestContext.Current.CancellationToken);

        var handlers = new HandlerSource(() => app.GetTestServer().CreateHandler());
        var subscriber = new SignalRBpmnDocumentChangeSubscriber(
            HubUrl,
            http => Configure(http, handlers, user),
            new BpmnChangeFeedOptions { InitialBackoff = TimeSpan.FromMilliseconds(40), MaxBackoff = TimeSpan.FromMilliseconds(200) });

        return new SignalRHarness(app, handlers, auth, subscriber);
    }

    public static void Configure(Microsoft.AspNetCore.Http.Connections.Client.HttpConnectionOptions http, HandlerSource handlers, string? user)
    {
        http.HttpMessageHandlerFactory = _ => handlers.Create();
        http.Transports = HttpTransportType.LongPolling;
        if (user is not null) http.Headers["X-User"] = user;
    }

    public HandlerSource Handlers => _handlers;

    public ValueTask PublishAsync(BpmnDocumentChange change) => Bus.PublishAsync(change);

    public ValueTask DropConnectionsAsync()
    {
        _handlers.KillAll();
        return ValueTask.CompletedTask;
    }

    public ValueTask RevokeAuthorizationAsync()
    {
        _auth.Revoked = true;
        _handlers.KillAll();
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
        await _app.DisposeAsync();
    }
}

/// <summary>The SignalR transport meets the subscriber contract.</summary>
public sealed class SignalRSubscriberContractTests : BpmnDocumentChangeSubscriberContractTests
{
    /// <inheritdoc />
    protected override async ValueTask<IBpmnChangeFeedHarness> StartAsync() => await SignalRHarness.StartAsync();
}

/// <summary>A change made while the feed was down still reaches the binding, after one catch-up check.</summary>
public sealed class SignalRBindingLongSessionTests : BpmnBindingLongSessionContractTests
{
    /// <inheritdoc />
    protected override async ValueTask<IBpmnChangeFeedHarness> StartAsync() => await SignalRHarness.StartAsync();
}
