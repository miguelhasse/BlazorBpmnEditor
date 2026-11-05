using System.Net;
using System.Text;
using System.Text.Json;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;
using Hasseware.AspNetCore.Components.Bpmn.Storage;
using Hasseware.AspNetCore.Components.Bpmn.Storage.ServerSentEvents;
using Hasseware.AspNetCore.Components.Bpmn.Storage.ServerSentEvents.Client;
#if BPMN_SMOKE_MCP
using Hasseware.AspNetCore.Components.Bpmn.Mcp.AspNetCore;
#endif
#if BPMN_SMOKE_SIGNALR
using Hasseware.AspNetCore.Components.Bpmn.Storage.SignalR;
using Hasseware.AspNetCore.Components.Bpmn.Storage.SignalR.Client;
#endif

if (JsonSerializer.IsReflectionEnabledByDefault)
{
    throw new InvalidOperationException("The HTTP smoke host must disable reflection-based JSON.");
}

using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
var token = timeout.Token;
var builder = WebApplication.CreateSlimBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Services.AddBpmnInMemoryDocumentStore();
builder.Services.AddBpmnChangeEvents(options =>
{
    options.AllowAnonymous = true;
    options.SingleTenant = true;
});
#if BPMN_SMOKE_MCP
builder.Services.AddBpmnMcpHttp(options =>
{
    options.AllowAnonymous = true;
    options.SingleTenant = true;
});
#endif
#if BPMN_SMOKE_SIGNALR
builder.Services.AddBpmnChangeHub();
#endif
await using var app = builder.Build();
app.MapBpmnDocumentChangeEvents();
#if BPMN_SMOKE_MCP
app.MapBpmnMcp();
#endif
#if BPMN_SMOKE_SIGNALR
app.MapBpmnDocumentChangeHub("/bpmn/hub");
#endif
var store = app.Services.GetRequiredService<IBpmnDocumentStore>();
var key = new BpmnDocumentKey(null, BpmnDocumentPath.Parse("smoke.bpmn"));
var content = new BpmnDocumentContent(Encoding.UTF8.GetBytes("<definitions/>"), BpmnFileFormat.Xml);
var saved = await store.SaveAsync(key, content, null, token);
Require(saved.Succeeded, "Could not seed the stream's document.");

await app.StartAsync(token);
try
{
    using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
#if BPMN_SMOKE_SIGNALR
    await using (var subscriber = new SignalRBpmnDocumentChangeSubscriber(new Uri(client.BaseAddress, "/bpmn/hub")))
    {
        await CheckSubscriberAsync(subscriber);
    }
#endif
#if BPMN_SMOKE_MCP
    using var tools = await CallMcpAsync(client,
        """{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}""", token);
    Require(tools.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray()
        .Any(tool => tool.GetProperty("name").GetString() == "bpmn_list_documents"), "MCP did not list BPMN tools.");
    using var listing = await CallMcpAsync(client,
        """{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"bpmn_list_documents","arguments":{}}}""", token);
    var listed = listing.RootElement.GetProperty("result").GetProperty("structuredContent");
    Require(listed.GetProperty("ok").GetBoolean(), "MCP tool invocation failed.");
    Require(listed.GetProperty("data").GetProperty("entries")[0].GetProperty("path").GetProperty("untrustedText").GetString() == "smoke.bpmn",
        "MCP tool result lost the stored document.");
#endif
    using var missing = await client.GetAsync("/bpmn/changes", token);
    Require(missing.StatusCode == HttpStatusCode.NotFound, "Missing paths must be refused.");
    using var duplicated = await client.GetAsync("/bpmn/changes?path=smoke.bpmn&path=other.bpmn", token);
    Require(duplicated.StatusCode == HttpStatusCode.NotFound, "Duplicate query values must not silently select a document.");

    using var stream = await client.GetAsync("/bpmn/changes?path=smoke.bpmn", HttpCompletionOption.ResponseHeadersRead, token);
    Require(stream.StatusCode == HttpStatusCode.OK, "Could not open the native SSE endpoint.");
    Require(stream.Content.Headers.ContentType?.MediaType == "text/event-stream", "SSE content type changed.");
    using var reader = new StreamReader(await stream.Content.ReadAsStreamAsync(token));
    await ReadEventAsync(reader, "ready", token);
    saved = await store.SaveAsync(key, content, saved.Revision, token);
    Require(saved.Succeeded, "Could not update the subscribed document.");
    var data = await ReadEventAsync(reader, "change", token);
    using var payload = JsonDocument.Parse(data);
    Require(payload.RootElement.GetProperty("path").GetString() == "smoke.bpmn", "SSE change payload lost its document path.");
    await using var sseSubscriber = new SseBpmnDocumentChangeSubscriber(client);
    await CheckSubscriberAsync(sseSubscriber);
}
finally
{
    await app.StopAsync(CancellationToken.None);
}

Console.WriteLine("BPMN Native AOT HTTP smoke passed.");

async Task CheckSubscriberAsync(IBpmnDocumentChangeSubscriber subscriber)
{
    var feed = app.Services.GetRequiredService<BpmnChangeFeedServer>();
    var expected = feed.Open + 1;
    var delivered = new TaskCompletionSource<BpmnDocumentChange>(TaskCreationOptions.RunContinuationsAsynchronously);
    await using var subscription = await subscriber.SubscribeAsync(key, change =>
    {
        delivered.TrySetResult(change);
        return ValueTask.CompletedTask;
    }, token);
    while (feed.Open < expected) await Task.Delay(10, token);
    saved = await store.SaveAsync(key, content, saved.Revision, token);
    Require(saved.Succeeded, "Could not update the subscribed document.");
    var notification = await delivered.Task.WaitAsync(token);
    Require(notification.Key == key && notification.Revision == saved.Revision, "Subscriber delivered the wrong change.");
}

#if BPMN_SMOKE_MCP
static async Task<JsonDocument> CallMcpAsync(HttpClient client, string payload, CancellationToken token)
{
    using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp");
    request.Headers.Accept.ParseAdd("application/json");
    request.Headers.Accept.ParseAdd("text/event-stream");
    request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
    using var response = await client.SendAsync(request, token);
    response.EnsureSuccessStatusCode();
    var body = await response.Content.ReadAsStringAsync(token);
    return JsonDocument.Parse(response.Content.Headers.ContentType?.MediaType == "text/event-stream"
        ? body.Split('\n').First(line => line.StartsWith("data: ", StringComparison.Ordinal))[6..]
        : body);
}
#endif

static async Task<string> ReadEventAsync(StreamReader reader, string expected, CancellationToken token)
{
    var eventName = "";
    var data = "";
    while (await reader.ReadLineAsync(token) is { } line)
    {
        if (line.StartsWith("event: ", StringComparison.Ordinal)) eventName = line[7..];
        if (line.StartsWith("data: ", StringComparison.Ordinal)) data = line[6..];
        if (line.Length == 0 && eventName == expected) return data;
    }

    throw new InvalidOperationException($"SSE stream ended before '{expected}'.");
}

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
