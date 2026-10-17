using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Storage;
using Hasseware.AspNetCore.Components.Bpmn.Tools;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Hasseware.AspNetCore.Components.Bpmn.Mcp.AspNetCore.Tests;

// Signs a caller in from a header, as "name" or "name,tenant": a stand-in for a real scheme, so the host's own rules are what is tested.
internal sealed class HeaderAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-User", out var value)) return Task.FromResult(AuthenticateResult.NoResult());

        var parts = value.ToString().Split(',');
        List<Claim> claims = [new(ClaimTypes.NameIdentifier, parts[0]), new(ClaimTypes.Name, parts[0])];
        if (parts.Length > 1) claims.Add(new Claim("tenant", parts[1]));

        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, "Header")), "Header")));
    }
}

/// <summary>The MCP server over HTTP, hosted in-process: who gets in, whose documents they see, and what is refused at the door.</summary>
public sealed class HttpHostTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly Uri Endpoint = new("http://localhost/mcp");

    private sealed class Host : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private Host(WebApplication app, InMemoryBpmnDocumentStore store)
        {
            _app = app;
            Store = store;
        }

        public InMemoryBpmnDocumentStore Store { get; }

        public static async Task<Host> StartAsync(
            Action<BpmnMcpHttpOptions>? http = null,
            Action<BpmnMcpOptions>? mcp = null,
            Action<IServiceCollection>? services = null,
            bool writes = false,
            Action<WebApplication>? beforeMap = null)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            builder.Services.AddAuthentication("Header").AddScheme<AuthenticationSchemeOptions, HeaderAuthentication>("Header", null);
            builder.Services.AddAuthorization();

            var store = new InMemoryBpmnDocumentStore();
            builder.Services.AddSingleton<IBpmnDocumentStore>(store);
            services?.Invoke(builder.Services);

            var server = builder.Services.AddBpmnMcpHttp(http, options =>
            {
                options.AllowWrites = writes;
                mcp?.Invoke(options);
            });
            _ = server;

            var app = builder.Build();
            app.UseAuthentication();
            app.UseAuthorization();
            beforeMap?.Invoke(app);
            app.MapBpmnMcp("/mcp");
            await app.StartAsync(Token);

            return new Host(app, store);
        }

        public HttpClient Client(string? user = null, Action<HttpClient>? configure = null)
        {
            var client = _app.GetTestClient();
            if (user is not null) client.DefaultRequestHeaders.Add("X-User", user);
            configure?.Invoke(client);
            return client;
        }

        public async Task<McpClient> ConnectAsync(string? user = null) =>
            await McpClient.CreateAsync(
                new HttpClientTransport(new HttpClientTransportOptions { Endpoint = Endpoint, TransportMode = HttpTransportMode.StreamableHttp }, Client(user), ownsHttpClient: true),
                cancellationToken: Token);

        public async ValueTask DisposeAsync() => await _app.DisposeAsync();
    }

    private static async Task<JsonElement> CallAsync(McpClient client, string tool, params (string Name, object? Value)[] arguments)
    {
        var result = await client.CallToolAsync(tool, arguments.ToDictionary(argument => argument.Name, argument => argument.Value), cancellationToken: Token);
        return JsonDocument.Parse(((TextContentBlock)result.Content[0]).Text).RootElement.Clone();
    }

    private static StringContent Rpc() => new("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""", Encoding.UTF8, "application/json");

    private static async Task<int> PostAsync(HttpClient client, HttpContent? content = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = content ?? Rpc() };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        using var response = await client.SendAsync(request, Token);
        return (int)response.StatusCode;
    }

    // --- who gets in ---------------------------------------------------------------------------------

    [Fact]
    public async Task RefusesACallerWhoHasNotAuthenticated()
    {
        await using var host = await Host.StartAsync(http => http.TenantClaimType = "tenant");

        Assert.Equal(401, await PostAsync(host.Client()));
    }

    [Fact]
    public async Task RefusesACallerWithoutATenantClaimInsteadOfServingTheSharedSpace()
    {
        await using var host = await Host.StartAsync(http => http.TenantClaimType = "tenant");
        await host.Store.SaveDocumentAsync(new BpmnDocumentKey(null, BpmnDocumentPath.Parse("shared.bpmn")), new BpmnDocumentBuilder().Build(), ifMatchRevision: null, Token);

        Assert.Equal(403, await PostAsync(host.Client("carol")));
    }

    [Theory]
    [InlineData("alice@example.com")]
    [InlineData("https://tenant.example")]
    [InlineData("a/b")]
    public async Task RefusesACallerWhoseTenantClaimNamesNoTenantTheStoresCouldHold(string tenant)
    {
        await using var host = await Host.StartAsync(http => http.TenantClaimType = "tenant");

        // An email address is a good claim and not a tenant; the endpoint says so at the door, not as an exception on every tool call.
        Assert.Equal(403, await PostAsync(host.Client($"carol,{tenant}")));
        Assert.Equal(403, await PostAsync(host.Client($"carol,{new string('t', 129)}")));
    }

    [Fact]
    public async Task RefusesToStartWhenAPolicyIsSetThatAnonymousCallersWouldNeverMeet()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await Host.StartAsync(http =>
        {
            http.SingleTenant = true;
            http.AllowAnonymous = true;
            http.AuthorizationPolicy = "needs-scope";
        }));
    }

    [Fact]
    public async Task ServesADocumentSpacePerTenantFromTheCallersClaim()
    {
        await using var host = await Host.StartAsync(http => http.TenantClaimType = "tenant", writes: true);
        await using var alice = await host.ConnectAsync("alice,acme");
        await using var bob = await host.ConnectAsync("bob,other");

        var created = await CallAsync(alice, "bpmn_create_document", ("documentId", "plans/q3.bpmn"), ("name", "Q3"));
        var aliceSees = await CallAsync(alice, "bpmn_list_documents", ("recursive", true));
        var bobSees = await CallAsync(bob, "bpmn_list_documents", ("recursive", true));
        var bobOpens = await CallAsync(bob, "bpmn_open", ("documentId", "plans/q3.bpmn"));

        Assert.True(created.GetProperty("ok").GetBoolean(), created.ToString());
        Assert.Single(aliceSees.GetProperty("data").GetProperty("entries").EnumerateArray());
        Assert.Empty(bobSees.GetProperty("data").GetProperty("entries").EnumerateArray());
        Assert.Equal(BpmnToolCodes.NotFound, bobOpens.GetProperty("code").GetString());
        Assert.NotNull(await host.Store.StatAsync(new BpmnDocumentKey("acme", BpmnDocumentPath.Parse("plans/q3.bpmn")), Token));
        Assert.Null(await host.Store.StatAsync(new BpmnDocumentKey(null, BpmnDocumentPath.Parse("plans/q3.bpmn")), Token));
    }

    [Fact]
    public async Task KeepsNoStateBetweenRequests()
    {
        await using var host = await Host.StartAsync(http => http.SingleTenant = true);
        await using var first = await host.ConnectAsync("alice");
        await using var second = await host.ConnectAsync("alice");

        Assert.Null(first.SessionId);
        Assert.Null(second.SessionId);
        Assert.True((await CallAsync(first, "bpmn_list_documents")).GetProperty("ok").GetBoolean());
        Assert.True((await CallAsync(second, "bpmn_list_documents")).GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task ArbitratesWritersOnDifferentConnectionsByRevision()
    {
        await using var host = await Host.StartAsync(http => http.TenantClaimType = "tenant", writes: true);
        await using var alice = await host.ConnectAsync("alice,acme");
        await using var alice2 = await host.ConnectAsync("alice2,acme");
        var created = await CallAsync(alice, "bpmn_create_document", ("documentId", "a.bpmn"));
        var revision = created.GetProperty("revision").GetString();
        var first = await CallAsync(alice, "bpmn_create_element", ("documentId", "a.bpmn"), ("type", "task"), ("expectedRevision", revision));

        var second = await CallAsync(alice2, "bpmn_create_element", ("documentId", "a.bpmn"), ("type", "task"), ("expectedRevision", revision));

        Assert.True(first.GetProperty("ok").GetBoolean(), first.ToString());
        Assert.Equal(BpmnToolCodes.PersistConflict, second.GetProperty("code").GetString());
    }

    [Fact]
    public async Task LetsTheHostsOwnAuthorizerDecidePerCallerAndDocument()
    {
        await using var host = await Host.StartAsync(
            http => http.TenantClaimType = "tenant",
            services: services => services.AddSingleton<IBpmnDocumentAuthorizer, OwnerOnly>(),
            writes: true);
        await using var alice = await host.ConnectAsync("alice,acme");
        await using var bob = await host.ConnectAsync("bob,acme");
        await CallAsync(alice, "bpmn_create_document", ("documentId", "alice/notes.bpmn"));

        var bobReads = await CallAsync(bob, "bpmn_open", ("documentId", "alice/notes.bpmn"));
        var bobCreates = await CallAsync(bob, "bpmn_create_document", ("documentId", "alice/mine.bpmn"));
        var aliceReads = await CallAsync(alice, "bpmn_open", ("documentId", "alice/notes.bpmn"));

        Assert.Equal(BpmnToolCodes.NotFound, bobReads.GetProperty("code").GetString());
        Assert.Equal(BpmnToolCodes.Forbidden, bobCreates.GetProperty("code").GetString());
        Assert.True(aliceReads.GetProperty("ok").GetBoolean(), aliceReads.ToString());
    }

    private sealed class OwnerOnly(IHttpContextAccessor accessor) : IBpmnDocumentAuthorizer
    {
        public ValueTask<bool> IsAllowedAsync(BpmnDocumentAction action, BpmnDocumentKey key, CancellationToken cancellationToken)
        {
            var user = accessor.HttpContext?.User.Identity?.Name;
            var owner = key.Path.Segments[0];
            return ValueTask.FromResult(owner != "alice" || user == "alice");
        }
    }

    // --- what is refused at the door -----------------------------------------------------------------

    [Fact]
    public async Task RefusesAHostHeaderThatIsNotLoopbackUnlessListed()
    {
        await using var host = await Host.StartAsync(http => http.SingleTenant = true);
        await using var listed = await Host.StartAsync(http =>
        {
            http.SingleTenant = true;
            http.AllowedHosts.Add("bpmn.example.com");
        });

        Assert.Equal(403, await PostAsync(host.Client("alice", client => client.DefaultRequestHeaders.Host = "evil.example.com")));
        Assert.Equal(403, await PostAsync(listed.Client("alice")));
        Assert.NotEqual(403, await PostAsync(listed.Client("alice", client => client.DefaultRequestHeaders.Host = "bpmn.example.com")));
    }

    [Fact]
    public async Task RefusesACrossOriginBrowserRequestButNotOneWithNoOrigin()
    {
        await using var host = await Host.StartAsync(http =>
        {
            http.SingleTenant = true;
            http.AllowedOrigins.Add("https://app.example.com");
        });

        Assert.Equal(403, await PostAsync(host.Client("alice", client => client.DefaultRequestHeaders.Add("Origin", "https://evil.example.com"))));
        Assert.NotEqual(403, await PostAsync(host.Client("alice", client => client.DefaultRequestHeaders.Add("Origin", "https://app.example.com"))));
        Assert.NotEqual(403, await PostAsync(host.Client("alice", client => client.DefaultRequestHeaders.Add("Origin", "http://localhost"))));
        Assert.NotEqual(403, await PostAsync(host.Client("alice")));
    }

    [Fact]
    public async Task RefusesABodyOverTheLimit()
    {
        await using var host = await Host.StartAsync(http =>
        {
            http.SingleTenant = true;
            http.MaxRequestBodyBytes = 200;
        });

        var big = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{"pad":"%PAD%"}}""".Replace("%PAD%", new string('x', 500), StringComparison.Ordinal), Encoding.UTF8, "application/json");

        Assert.Equal(413, await PostAsync(host.Client("alice"), big));
    }

    // --- how it is set up ----------------------------------------------------------------------------

    [Fact]
    public async Task RefusesToStartWithoutADecisionAboutTenants()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await Host.StartAsync());
    }

    [Fact]
    public async Task RefusesToStartWhenTheTenantOptionsContradictEachOther()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await Host.StartAsync(http =>
        {
            http.SingleTenant = true;
            http.TenantClaimType = "tenant";
        }));
    }

    [Fact]
    public async Task OffersTheWritingToolsExactlyWhenWritingIsAllowed()
    {
        await using var reading = await Host.StartAsync(http => http.SingleTenant = true);
        await using var writing = await Host.StartAsync(http => http.SingleTenant = true, writes: true);
        await using var readClient = await reading.ConnectAsync("alice");
        await using var writeClient = await writing.ConnectAsync("alice");

        Assert.DoesNotContain("bpmn_create_document", (await readClient.ListToolsAsync(cancellationToken: Token)).Select(tool => tool.Name));
        Assert.Contains("bpmn_create_document", (await writeClient.ListToolsAsync(cancellationToken: Token)).Select(tool => tool.Name));
    }

    [Fact]
    public async Task RefusesAnonymousCallersUnlessEveryoneSharesOneSpace()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await Host.StartAsync(http =>
        {
            http.TenantClaimType = "tenant";
            http.AllowAnonymous = true;
        }));

        await using var host = await Host.StartAsync(http =>
        {
            http.SingleTenant = true;
            http.AllowAnonymous = true;
        });
        await using var anonymous = await host.ConnectAsync();

        Assert.True((await CallAsync(anonymous, "bpmn_list_documents")).GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task OffersOnlyTheReadingToolsUnlessWritingWasAskedFor()
    {
        await using var host = await Host.StartAsync(http => http.SingleTenant = true);
        await using var client = await host.ConnectAsync("alice");

        var names = (await client.ListToolsAsync(cancellationToken: Token)).Select(tool => tool.Name).Order(StringComparer.Ordinal).ToList();

        Assert.Equal(["bpmn_describe_element", "bpmn_diff", "bpmn_export", "bpmn_list_documents", "bpmn_open", "bpmn_validate"], names);
    }
}
