using System.Text.Json;

using Hasseware.AspNetCore.Components.Bpmn.E2E.Tests.Fixtures;

using Microsoft.Playwright.Xunit.v3;

namespace Hasseware.AspNetCore.Components.Bpmn.E2E.Tests;

/// <summary>
/// The editor offered to a browser agent through WebMCP (design doc, "Testing").
/// </summary>
/// <remarks>
/// No real WebMCP is needed or used: a minimal <c>document.modelContext</c> is installed before the page
/// loads, which records what is registered and calls it back the way a browser calls a tool for an agent.
/// That exercises everything this library owns: the module, the interop, the tool handlers and the editor
/// they read.
/// </remarks>
public class WebMcpTests(DemoAppFixture app) : PageTest, IClassFixture<DemoAppFixture>
{
    // What a browser's model context does, reduced to what the module touches. A result is reported with
    // typeof, because the draft serializes whatever object a tool fulfils with, and a string would be
    // double-encoded.
    private const string Stub = """
        (() => {
          const tools = new Map();
          document.modelContext = {
            registerTool(tool, options) {
              if (tools.has(tool.name)) return Promise.reject(new DOMException('A tool named ' + tool.name + ' is already registered.', 'InvalidStateError'));
              tools.set(tool.name, tool);
              options?.signal?.addEventListener('abort', () => tools.delete(tool.name));
              return Promise.resolve();
            },
          };
          window.__webmcp = {
            names: () => [...tools.keys()],
            hints: name => tools.get(name)?.annotations,
            call: async (name, args) => {
              const tool = tools.get(name);
              if (!tool) return { missing: true };
              const result = await tool.execute(args ?? {}, { signal: new AbortController().signal });
              return { type: typeof result, result: JSON.parse(JSON.stringify(result)) };
            },
          };
        })();
        """;

    private static readonly string[] Tools =
    [
        "bpmn_get_diagram", "bpmn_describe_element", "bpmn_get_selection", "bpmn_validate", "bpmn_export", "bpmn_list_creatable",
        "bpmn_get_document_info",
    ];

    private static readonly string[] EditingTools =
        ["bpmn_create_element", "bpmn_connect", "bpmn_set_property", "bpmn_delete", "bpmn_select", "bpmn_undo", "bpmn_redo"];

    [Fact]
    public async Task RegistersTheReadOnlyToolsWhenSwitchedOn()
    {
        await Page.AddInitScriptAsync(Stub);
        await GoToEditorAsync();

        await TurnOnAsync();

        await Expect(Page.Locator(".demo-webmcp-status")).ToContainTextAsync("7 read-only tools");
        Assert.Equal(Tools, await Page.EvaluateAsync<string[]>("() => window.__webmcp.names()"));
    }

    [Fact]
    public async Task MarksWhatEachToolReadsAndChanges()
    {
        await Page.AddInitScriptAsync(Stub);
        await GoToEditorAsync();
        await TurnOnAsync();

        var hints = await Page.EvaluateAsync<JsonElement>("() => window.__webmcp.hints('bpmn_get_diagram')");

        Assert.True(hints.GetProperty("readOnlyHint").GetBoolean());
        Assert.True(hints.GetProperty("untrustedContentHint").GetBoolean());
        Assert.False(hints.GetProperty("consequentialHint").GetBoolean());
    }

    [Fact]
    public async Task AnswersWithAPlainObjectThatAgreesWithTheCanvas()
    {
        await Page.AddInitScriptAsync(Stub);
        await GoToEditorAsync();
        await TurnOnAsync();

        var call = await CallAsync("bpmn_get_diagram");

        Assert.Equal("object", call.GetProperty("type").GetString());
        var result = call.GetProperty("result");
        Assert.True(result.GetProperty("ok").GetBoolean());
        var drawn = result.GetProperty("data").GetProperty("elements").EnumerateArray()
            .Where(element => element.TryGetProperty("bounds", out _))
            .Select(element => element.GetProperty("id").GetString()!)
            .Take(5)
            .ToList();
        Assert.NotEmpty(drawn);
        foreach (var id in drawn)
        {
            await Expect(Page.Locator($".bpmn-canvas .joint-cell[model-id='{id}']")).ToHaveCountAsync(1);
        }
    }

    [Fact]
    public async Task ExportsTheDocumentThatIsOpen()
    {
        await Page.AddInitScriptAsync(Stub);
        await GoToEditorAsync();
        await TurnOnAsync();
        var diagram = (await CallAsync("bpmn_get_diagram")).GetProperty("result").GetProperty("data");
        var firstId = diagram.GetProperty("elements").EnumerateArray().First().GetProperty("id").GetString()!;

        var exported = (await CallAsync("bpmn_export", """{"format":"xml"}""")).GetProperty("result");

        Assert.True(exported.GetProperty("ok").GetBoolean());
        var xml = exported.GetProperty("data").GetProperty("content").GetProperty("untrustedText").GetString()!;
        Assert.StartsWith("<", xml.TrimStart(), StringComparison.Ordinal);
        Assert.Contains($"id=\"{firstId}\"", xml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReportsAFailureAsAResultNotARejection()
    {
        await Page.AddInitScriptAsync(Stub);
        await GoToEditorAsync();
        await TurnOnAsync();

        var call = await CallAsync("bpmn_describe_element", """{"elementId":"no-such-element"}""");

        Assert.Equal("object", call.GetProperty("type").GetString());
        Assert.False(call.GetProperty("result").GetProperty("ok").GetBoolean());
        Assert.Equal("NOT-FOUND", call.GetProperty("result").GetProperty("code").GetString());
    }

    [Fact]
    public async Task OffersNoToolThatChangesTheDiagram()
    {
        await Page.AddInitScriptAsync(Stub);
        await GoToEditorAsync();
        await TurnOnAsync();

        var call = await CallAsync("bpmn_delete", """{"elementIds":["anything"]}""");

        Assert.True(call.GetProperty("missing").GetBoolean());
    }

    [Fact]
    public async Task WithdrawsTheToolsWhenSwitchedOff()
    {
        await Page.AddInitScriptAsync(Stub);
        await GoToEditorAsync();
        await TurnOnAsync();

        await Page.SetDemoSwitchAsync("Offer to browser agents (WebMCP)", false);

        await Expect(Page.Locator(".demo-webmcp-status")).ToBeEmptyAsync();
        await Page.WaitForFunctionAsync("() => window.__webmcp.names().length === 0");
    }

    [Fact]
    public async Task DoesNothingQuietlyWhereTheBrowserHasNoWebMcp()
    {
        var errors = new List<string>();
        Page.Console += (_, message) =>
        {
            if (message.Type == "error") errors.Add(message.Text);
        };
        await GoToEditorAsync();

        await TurnOnAsync(expectRegistered: false);

        await Expect(Page.Locator(".demo-webmcp-status")).ToContainTextAsync("no WebMCP");
        Assert.Empty(errors);
    }

    [Fact]
    public async Task OffersTheEditingToolsOnlyWhenTheHostAllowsIt()
    {
        await Page.AddInitScriptAsync(Stub);
        await GoToEditorAsync();
        await TurnOnAsync();
        Assert.DoesNotContain("bpmn_delete", await Page.EvaluateAsync<string[]>("() => window.__webmcp.names()"));

        await AllowEditingAsync();

        var names = await Page.EvaluateAsync<string[]>("() => window.__webmcp.names()");
        Assert.All(EditingTools, tool => Assert.Contains(tool, names));
        Assert.True((await Page.EvaluateAsync<JsonElement>("() => window.__webmcp.hints('bpmn_delete')")).GetProperty("consequentialHint").GetBoolean());
    }

    [Fact]
    public async Task DrawsWhatTheAgentCreatesAndTheUserCanUndoIt()
    {
        await Page.AddInitScriptAsync(Stub);
        await GoToEditorAsync();
        await TurnOnAsync();
        await AllowEditingAsync();

        var result = (await CallAsync("bpmn_create_element", """{"type":"task","name":"Added by an agent"}""")).GetProperty("result");

        Assert.True(result.GetProperty("ok").GetBoolean(), result.ToString());
        var id = result.GetProperty("data").GetProperty("elementId").GetString()!;
        await Expect(Page.Locator($".bpmn-canvas .joint-cell[model-id='{id}']")).ToHaveCountAsync(1);

        await Page.Locator(".bpmn-canvas").ClickAsync(new() { Position = new() { X = 4, Y = 4 } });
        await Page.Keyboard.PressAsync("Control+z");

        await Expect(Page.Locator($".bpmn-canvas .joint-cell[model-id='{id}']")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task AsksTheUserBeforeDeletingAndHonoursTheAnswer()
    {
        await Page.AddInitScriptAsync(Stub);
        await GoToEditorAsync();
        await TurnOnAsync();
        await AllowEditingAsync();
        var diagram = (await CallAsync("bpmn_get_diagram")).GetProperty("result").GetProperty("data");
        var id = diagram.GetProperty("elements").EnumerateArray()
            .First(element => element.TryGetProperty("bounds", out _) && element.GetProperty("type").GetString() is not ("participant" or "lane" or "subProcess"))
            .GetProperty("id").GetString()!;
        var cell = Page.Locator($".bpmn-canvas .joint-cell[model-id='{id}']");

        await StartCallAsync("bpmn_delete", $$"""{"elementIds":["{{id}}"]}""");
        await Expect(Page.Locator(".bpmn-confirm__title")).ToBeVisibleAsync();
        await Page.Locator(".bpmn-confirm__deny").ClickAsync();
        var denied = (await FinishCallAsync()).GetProperty("result");
        Assert.Equal("DENIED", denied.GetProperty("code").GetString());
        await Expect(cell).ToHaveCountAsync(1);

        await StartCallAsync("bpmn_delete", $$"""{"elementIds":["{{id}}"]}""");
        await Expect(Page.Locator(".bpmn-confirm__details")).ToContainTextAsync(id);
        await Page.Locator(".bpmn-confirm__allow").ClickAsync();
        var allowed = (await FinishCallAsync()).GetProperty("result");
        Assert.True(allowed.GetProperty("ok").GetBoolean(), allowed.ToString());
        await Expect(cell).ToHaveCountAsync(0);
    }

    private async Task AllowEditingAsync()
    {
        await Page.SetDemoSwitchAsync("Let agents edit (deleting and saving ask you first)");
        await Expect(Page.Locator(".demo-webmcp-status")).ToContainTextAsync("editing allowed");
    }

    // A call that waits for the user is started without being awaited, so the test can answer it.
    private async Task StartCallAsync(string tool, string argumentsJson) =>
        await Page.EvaluateAsync("([name, json]) => { window.__pending = window.__webmcp.call(name, JSON.parse(json)); }", new object[] { tool, argumentsJson });

    private async Task<JsonElement> FinishCallAsync() => await Page.EvaluateAsync<JsonElement>("() => window.__pending");

    private async Task<JsonElement> CallAsync(string tool, string argumentsJson = "{}") =>
        await Page.EvaluateAsync<JsonElement>("([name, json]) => window.__webmcp.call(name, JSON.parse(json))", new object[] { tool, argumentsJson });

    // Registration happens after the next render, so a test that calls a tool waits for the status that follows it.
    private async Task TurnOnAsync(bool expectRegistered = true)
    {
        await Page.SetDemoSwitchAsync("Offer to browser agents (WebMCP)");
        if (expectRegistered) await Expect(Page.Locator(".demo-webmcp-status")).ToContainTextAsync("7 read-only tools");
    }

    private async Task GoToEditorAsync()
    {
        await Page.GotoAsync(app.Url(this));

        await Expect(Page.Locator(".bpmn-canvas__paper > svg")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.Locator(".bpmn-canvas .joint-cell").First).ToBeVisibleAsync();
    }
}
