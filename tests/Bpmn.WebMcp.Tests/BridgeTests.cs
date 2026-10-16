using System.Text.Json;

using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Tools;
using Hasseware.AspNetCore.Components.Bpmn.Validation;
using Hasseware.AspNetCore.Components.Bpmn.WebMcp.Tests.Support;

using static Hasseware.AspNetCore.Components.Bpmn.WebMcp.Tests.Support.Documents;

namespace Hasseware.AspNetCore.Components.Bpmn.WebMcp.Tests;

/// <summary>What the browser module sends to .NET for an agent's call, and what comes back.</summary>
public sealed class BridgeTests : EditorContext
{
    private static readonly BpmnWebMcpToolSet Tools = BpmnWebMcpToolSet.Create("bpmn_", string.Empty);

    private static Task<string> Direct(Func<Task<string>> work) => work();

    private static BpmnWebMcpBridge BridgeFor(IBpmnToolHost host) => new(host, Tools, Direct);

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public async Task AnswersAReadingToolExactlyAsTheSharedHandlerDoes()
    {
        var host = new BpmnSessionToolHost(RenderScope().Instance.Session);

        var json = await BridgeFor(host).ExecuteAsync("1", "bpmn_get_diagram", "{}");

        var expected = BpmnToolJson.Serialize(await BpmnTools.GetDiagramAsync(host, new BpmnGetDiagramRequest(), Token));
        Assert.Equal(expected, json);
        var result = Parse(json);
        Assert.True(result.GetProperty("ok").GetBoolean());
        Assert.Contains(result.GetProperty("data").GetProperty("elements").EnumerateArray(), element => element.GetProperty("id").GetString() == "review");
    }

    [Theory]
    [InlineData("bpmn_validate", "{}")]
    [InlineData("bpmn_export", """{"format":"xml"}""")]
    [InlineData("bpmn_list_creatable", "{}")]
    [InlineData("bpmn_describe_element", """{"elementId":"review"}""")]
    [InlineData("bpmn_get_selection", "{}")]
    public async Task AnswersEveryReadingTool(string tool, string arguments)
    {
        var host = new BpmnSessionToolHost(RenderScope().Instance.Session);

        var result = Parse(await BridgeFor(host).ExecuteAsync("1", tool, arguments));

        Assert.True(result.GetProperty("ok").GetBoolean(), result.ToString());
        Assert.StartsWith("r", result.GetProperty("revision").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task KeepsDiagramTextInsideUntrustedText()
    {
        var host = new BpmnSessionToolHost(RenderScope(Hostile()).Instance.Session);
        var bridge = BridgeFor(host);

        foreach (var (tool, arguments) in new[]
        {
            ("bpmn_get_diagram", "{}"), ("bpmn_describe_element", """{"elementId":"evil"}"""), ("bpmn_validate", "{}"),
            ("bpmn_export", "{}"), ("bpmn_get_selection", "{}"), ("bpmn_list_creatable", "{}"),
        })
        {
            var result = Parse(await bridge.ExecuteAsync("1", tool, arguments));
            Assert.All(StringsOutsideUntrustedText(result), text => Assert.DoesNotContain("IGNORE PREVIOUS", text, StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task StillReportsTheTextWhereItBelongs()
    {
        var host = new BpmnSessionToolHost(RenderScope(Hostile()).Instance.Session);

        var result = Parse(await BridgeFor(host).ExecuteAsync("1", "bpmn_get_diagram", "{}"));

        var element = result.GetProperty("data").GetProperty("elements").EnumerateArray().Single(item => item.GetProperty("id").GetString() == "evil");
        Assert.Equal(Injection, element.GetProperty("name").GetProperty("untrustedText").GetString());
    }

    [Theory]
    [InlineData("{", BpmnToolCodes.InvalidArgument)]
    [InlineData("[1]", BpmnToolCodes.InvalidArgument)]
    [InlineData("\"text\"", BpmnToolCodes.InvalidArgument)]
    public async Task AnswersMalformedArgumentsWithAResultNotAnException(string arguments, string code)
    {
        var host = new BpmnSessionToolHost(RenderScope().Instance.Session);

        var result = Parse(await BridgeFor(host).ExecuteAsync("1", "bpmn_get_diagram", arguments));

        Assert.False(result.GetProperty("ok").GetBoolean());
        Assert.Equal(code, result.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("bpmn_launch")]
    [InlineData("bpmn_delete")]
    [InlineData("bpmn_create_element")]
    [InlineData("get_diagram")]
    public async Task DoesNotKnowAToolItDidNotRegister(string tool)
    {
        var host = new BpmnSessionToolHost(RenderScope().Instance.Session);

        var result = Parse(await BridgeFor(host).ExecuteAsync("1", tool, "{}"));

        Assert.Equal(BpmnToolCodes.NotFound, result.GetProperty("code").GetString());
        Assert.Equal("Review", RenderScope().Instance.Session.EditedDocument!.FindFlowNode("review")!.Name);
    }

    [Fact]
    public async Task DoesNotKnowADisabledTool()
    {
        var host = new BpmnSessionToolHost(RenderScope().Instance.Session);
        var bridge = new BpmnWebMcpBridge(host, BpmnWebMcpToolSet.Create("bpmn_", string.Empty, ["bpmn_export"]), Direct);

        var result = Parse(await bridge.ExecuteAsync("1", "bpmn_export", "{}"));

        Assert.Equal(BpmnToolCodes.NotFound, result.GetProperty("code").GetString());
    }

    [Fact]
    public async Task TurnsAFailingHandlerIntoAnInternalErrorWithoutLeakingItsMessage()
    {
        var bridge = BridgeFor(new ThrowingHost("secret-detail"));

        var result = Parse(await bridge.ExecuteAsync("1", "bpmn_validate", "{}"));

        Assert.Equal(BpmnToolCodes.InternalError, result.GetProperty("code").GetString());
        Assert.DoesNotContain("secret-detail", result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task StopsACallTheAgentCancelled()
    {
        var host = new BlockingHost();
        var bridge = BridgeFor(host);

        var running = bridge.ExecuteAsync("call-1", "bpmn_validate", "{}");
        await host.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
        bridge.Cancel("call-1");

        var result = Parse(await running.WaitAsync(TimeSpan.FromSeconds(5), Token));
        Assert.Equal(BpmnWebMcpBridge.Cancelled, result.GetProperty("code").GetString());
    }

    [Fact]
    public async Task AnswersNothingForACallCancelledBeforeItStarted()
    {
        var host = new BpmnSessionToolHost(RenderScope().Instance.Session);
        var bridge = BridgeFor(host);

        // The module sends the cancellation first when the agent had already given up.
        bridge.Cancel("early");
        var result = Parse(await bridge.ExecuteAsync("early", "bpmn_validate", "{}"));

        Assert.Equal(BpmnWebMcpBridge.Cancelled, result.GetProperty("code").GetString());
    }

    [Fact]
    public async Task CancelsEveryCallInFlightWhenTheEditorGoesAway()
    {
        var host = new BlockingHost();
        var bridge = BridgeFor(host);
        var running = bridge.ExecuteAsync("call-1", "bpmn_validate", "{}");
        await host.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);

        bridge.CancelAll();

        var result = Parse(await running.WaitAsync(TimeSpan.FromSeconds(5), Token));
        Assert.Equal(BpmnWebMcpBridge.Cancelled, result.GetProperty("code").GetString());
    }

    [Fact]
    public async Task RunsEveryCallThroughTheDispatcher()
    {
        var host = new BpmnSessionToolHost(RenderScope().Instance.Session);
        var dispatched = 0;
        var bridge = new BpmnWebMcpBridge(host, Tools, work =>
        {
            dispatched++;
            return work();
        });

        await bridge.ExecuteAsync("1", "bpmn_validate", "{}");
        await bridge.ExecuteAsync("2", "bpmn_export", "{}");

        Assert.Equal(2, dispatched);
    }

    // Every string in the result except the ones carried as untrustedText.
    private static IEnumerable<string> StringsOutsideUntrustedText(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                yield return element.GetString()!;
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray().SelectMany(StringsOutsideUntrustedText)) yield return item;
                break;

            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject().Where(property => property.Name != "untrustedText"))
                {
                    foreach (var text in StringsOutsideUntrustedText(property.Value)) yield return text;
                }

                break;
        }
    }

    private class ThrowingHost(string message) : IBpmnToolHost
    {
        public BpmnEditOrigin Origin => BpmnEditOrigin.FromAgent("test");

        public bool IsReadOnly => false;

        public string? ActiveDiagramId => null;

        public EditingOptions Editing => EditingOptions.Default;

        public IReadOnlyList<IBpmnProfileValidator> Profiles => [];

        public BpmnToolLimits Limits => BpmnToolLimits.Default;

        public string? Revision => "r0";

        public virtual ValueTask<BpmnDocument?> GetDocumentAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(message);

        public ValueTask<BpmnEditResult> ApplyAsync(BpmnEditCommand command, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    // A host whose document never arrives until the call is cancelled.
    private sealed class BlockingHost() : ThrowingHost("unused")
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<BpmnDocument?> GetDocumentAsync(CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return null;
        }
    }
}
