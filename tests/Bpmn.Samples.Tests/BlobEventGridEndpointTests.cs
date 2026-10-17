using System.Net;
using System.Text;
using System.Text.Json;

using BlobEventGridIngress;

using Hasseware.AspNetCore.Components.Bpmn.Storage;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Hasseware.AspNetCore.Components.Bpmn.Samples.Tests;

public sealed class BlobEventGridEndpointTests : IAsyncLifetime
{
    private const string Secret = "shared-secret";

    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private InProcessBpmnDocumentChangeBus _bus = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _bus = new InProcessBpmnDocumentChangeBus();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IBpmnDocumentChangeBus>(_bus);
        builder.Services.AddSingleton(provider => new BpmnDocumentChangeIngress(provider.GetRequiredService<IBpmnDocumentChangeBus>()));
        _app = builder.Build();
        _app.MapBlobEventGridIngress(new BlobIngressOptions("bpmn", null, Secret));
        await _app.StartAsync(Token);
        _client = _app.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    private async Task<HttpResponseMessage> PostAsync(string body, string? key = Secret)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/events/blob") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (key is not null) request.Headers.Add("aeg-sas-key", key);
        return await _client.SendAsync(request, Token);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong")]
    public async Task RefusesADeliveryWithoutTheSharedKeyAndPublishesNothing(string? key)
    {
        var received = new List<BpmnDocumentChange>();
        var intake = new BpmnDocumentKey(null, BpmnDocumentPath.Parse("a.bpmn"));
        await _bus.SubscribeAsync(intake, change => { received.Add(change); return ValueTask.CompletedTask; }, Token);

        using var response = await PostAsync(BlobEventGridParserTests.Created("bpmn", "a.bpmn"), key);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(received);
    }

    [Fact]
    public async Task EchoesTheValidationCodeToProveTheEndpointIsOurs()
    {
        using var response = await PostAsync("""[{"eventType":"Microsoft.EventGrid.SubscriptionValidationEvent","data":{"validationCode":"abc123"}}]""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        Assert.Equal("abc123", json.RootElement.GetProperty("validationResponse").GetString());
    }

    [Fact]
    public async Task PublishesACreatedBlobAndADeletedBlobToTheBus()
    {
        var received = new List<BpmnDocumentChange>();
        var key = new BpmnDocumentKey(null, BpmnDocumentPath.Parse("claims/a.bpmn"));
        await _bus.SubscribeAsync(key, change => { received.Add(change); return ValueTask.CompletedTask; }, Token);

        using var created = await PostAsync(BlobEventGridParserTests.Created("bpmn", "claims/a.bpmn"));
        using var deleted = await PostAsync("""[{"eventType":"Microsoft.Storage.BlobDeleted","subject":"/blobServices/default/containers/bpmn/blobs/claims/a.bpmn","data":{}}]""");

        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        Assert.Collection(
            received,
            first => { Assert.Equal(BpmnDocumentChangeKind.Updated, first.Kind); Assert.Equal("\"0x8DD1\"", first.Revision); },
            second => Assert.Equal(BpmnDocumentChangeKind.Deleted, second.Kind));
    }

    [Fact]
    public async Task IgnoresEventsAboutOtherContainers()
    {
        var received = new List<BpmnDocumentChange>();
        var key = new BpmnDocumentKey(null, BpmnDocumentPath.Parse("a.bpmn"));
        await _bus.SubscribeAsync(key, change => { received.Add(change); return ValueTask.CompletedTask; }, Token);

        using var response = await PostAsync(BlobEventGridParserTests.Created("elsewhere", "a.bpmn"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(received);
    }

    [Fact]
    public async Task RefusesABodyBeyondTheCapBeforeReadingIt()
    {
        using var content = new ByteArrayContent(new byte[1_000_001]);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/events/blob") { Content = content };
        request.Headers.Add("aeg-sas-key", Secret);

        using var response = await _client.SendAsync(request, Token);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }
}
