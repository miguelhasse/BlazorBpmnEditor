using System.Text.Json;

using Bunit;

using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;
using Hasseware.AspNetCore.Components.Bpmn.Storage;
using Hasseware.AspNetCore.Components.Bpmn.Tools;
using Hasseware.AspNetCore.Components.Bpmn.WebMcp.Tests.Support;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

using static Hasseware.AspNetCore.Components.Bpmn.WebMcp.Tests.Support.Documents;

namespace Hasseware.AspNetCore.Components.Bpmn.WebMcp.Tests;

/// <summary>An agent that is allowed to edit: what it may do, when the user is asked, and what stays out of its reach.</summary>
public sealed class EditingTests : EditorContext
{
    private const string Xml =
        """
        <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="d" targetNamespace="t">
          <process id="p"><startEvent id="s" name="Start" /></process>
        </definitions>
        """;

    private static readonly BpmnDocumentKey Key = new(null, BpmnDocumentPath.Parse("claims/intake.bpmn"));

    private static readonly string[] EditingTools = ["bpmn_create_element", "bpmn_connect", "bpmn_set_property", "bpmn_delete", "bpmn_select", "bpmn_undo", "bpmn_redo"];

    public EditingTests()
    {
        Services.AddBpmnInMemoryDocumentStore();
    }

    private void Allow(Action<BpmnWebMcpOptions>? more = null) => Services.AddBpmnWebMcp(options =>
    {
        options.AllowMutations = true;
        more?.Invoke(options);
    });

    private static BpmnWebMcp Part(IRenderedComponent<BpmnEditorScope> scope) => scope.FindComponent<BpmnWebMcp>().Instance;

    private async Task<(IRenderedComponent<BpmnEditorScope> Scope, FakeBrowser Browser)> OpenAsync(Action<BpmnWebMcpOptions>? more = null, Model.BpmnDocument? document = null, bool readOnly = false)
    {
        Allow(more);
        var (scope, browser) = RenderWithBrowser(document: document, readOnly: readOnly);
        scope.WaitForAssertion(() => Assert.Equal(BpmnWebMcpState.Registered, Part(scope).Status));
        await Task.CompletedTask;
        return (scope, browser);
    }

    private static string Name(IRenderedComponent<BpmnEditorScope> scope, string id = "review") => scope.Instance.Session.EditedDocument!.FindFlowNode(id)?.Name ?? "<gone>";

    private static BpmnEditorSession SessionOf(IRenderedComponent<BpmnEditorScope> scope) => scope.Instance.Session;

    // The question is asked on the renderer's context; a test waits for it and answers it as the user would.
    private static async Task<BpmnConfirmationRequest> AnswerAsync(IRenderedComponent<BpmnEditorScope> scope, bool allowed)
    {
        scope.WaitForState(() => SessionOf(scope).PendingConfirmation is not null);
        var question = SessionOf(scope).PendingConfirmation!;
        await scope.InvokeAsync(() => SessionOf(scope).ResolveConfirmation(allowed));
        return question;
    }

    [Fact]
    public void OffersNoEditingToolUnlessTheHostAllowsIt()
    {
        var (scope, _) = RenderWithBrowser();
        scope.WaitForAssertion(() => Assert.Equal(BpmnWebMcpState.Registered, Part(scope).Status));

        Assert.DoesNotContain(Part(scope).RegisteredTools, tool => EditingTools.Contains(tool));
    }

    [Fact]
    public async Task OffersTheEditingToolsOnceAllowed()
    {
        var (scope, _) = await OpenAsync();

        Assert.All(EditingTools, tool => Assert.Contains(tool, Part(scope).RegisteredTools));
        Assert.DoesNotContain("bpmn_save", Part(scope).RegisteredTools);
        Assert.DoesNotContain("bpmn_get_document_info", Part(scope).RegisteredTools);
    }

    [Fact]
    public async Task EditsAsTheAgentAndTheEditHasItsPlaceInTheUsersHistory()
    {
        var (scope, browser) = await OpenAsync();

        var result = await browser.Module.CallAsync("bpmn_set_property", """{"elementId":"review","name":"name","value":"Look again"}""");

        Assert.True(result.GetProperty("ok").GetBoolean(), result.ToString());
        Assert.Equal("Look again", Name(scope));
        Assert.Equal(BpmnEditOrigin.FromAgent("webmcp"), SessionOf(scope).UndoOrigin);
    }

    [Fact]
    public async Task AsksBeforeDeletingAndDeletesOnYes()
    {
        var (scope, browser) = await OpenAsync();

        var call = browser.Module.CallAsync("bpmn_delete", """{"elementIds":["review"]}""");
        var question = await AnswerAsync(scope, allowed: true);
        var result = await call;

        Assert.True(question.IsDestructive);
        Assert.True(question.Origin.IsAgent);
        Assert.Contains("Review (review)", question.Details);
        Assert.True(result.GetProperty("ok").GetBoolean(), result.ToString());
        Assert.Equal("<gone>", Name(scope));
    }

    [Fact]
    public async Task DeletesNothingOnNo()
    {
        var (scope, browser) = await OpenAsync();

        var call = browser.Module.CallAsync("bpmn_delete", """{"elementIds":["review"]}""");
        await AnswerAsync(scope, allowed: false);
        var result = await call;

        Assert.Equal(BpmnToolCodes.Denied, result.GetProperty("code").GetString());
        Assert.Equal("Review", Name(scope));
    }

    [Fact]
    public async Task WithdrawsTheQuestionWhenTheAgentGivesUp()
    {
        var (scope, browser) = await OpenAsync();
        var bridge = browser.Module.Reference!.Value;

        var call = bridge.ExecuteAsync("call-1", "bpmn_delete", """{"elementIds":["review"]}""");
        scope.WaitForState(() => SessionOf(scope).PendingConfirmation is not null);
        bridge.Cancel("call-1");
        var result = JsonElement.Parse(await call);

        Assert.Null(SessionOf(scope).PendingConfirmation);
        Assert.Equal(BpmnWebMcpBridge.Cancelled, result.GetProperty("code").GetString());
        Assert.Equal("Review", Name(scope));
    }

    [Fact]
    public async Task DeclinesASecondQuestionWhileOneIsOpen()
    {
        var (scope, browser) = await OpenAsync();

        var first = browser.Module.CallAsync("bpmn_delete", """{"elementIds":["review"]}""");
        scope.WaitForState(() => SessionOf(scope).PendingConfirmation is not null);
        var second = await browser.Module.CallAsync("bpmn_delete", """{"elementIds":["end"]}""");
        await AnswerAsync(scope, allowed: false);
        await first;

        // Not "the user declined": they were never asked this one, and the agent is told to try again, not that it was refused.
        Assert.Equal(BpmnToolCodes.Busy, second.GetProperty("code").GetString());
        Assert.Equal("Done", Name(scope, "end"));
    }

    [Fact]
    public async Task AsksAboutEverythingThatWillGoNotOnlyWhatWasNamed()
    {
        var document = new BpmnDocumentBuilder()
            .Node("sub", BpmnFlowNodeType.SubProcess, new Bounds(100, 100, 400, 300), "Handle claim", isExpanded: true)
            .Node("a", BpmnFlowNodeType.Task, new Bounds(150, 150, 100, 80), "A", containerId: "sub")
            .Node("b", BpmnFlowNodeType.Task, new Bounds(300, 150, 100, 80), "B", containerId: "sub")
            .Build();
        var (scope, browser) = await OpenAsync(document: document);

        var call = browser.Module.CallAsync("bpmn_delete", """{"elementIds":["sub"]}""");
        var question = await AnswerAsync(scope, allowed: false);
        await call;

        // One element was named; the Sub-Process takes its two tasks with it, and the user is told three.
        Assert.Contains("3", question.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShortensALongNameInTheQuestionAndStillNamesTheId()
    {
        var longName = new string('x', 500);
        var document = new BpmnDocumentBuilder().Node("long", BpmnFlowNodeType.Task, new Bounds(100, 100, 100, 80), longName).Build();
        var (scope, browser) = await OpenAsync(document: document);

        var call = browser.Module.CallAsync("bpmn_delete", """{"elementIds":["long"]}""");
        var question = await AnswerAsync(scope, allowed: false);
        await call;

        var detail = Assert.Single(question.Details);
        Assert.True(detail.Length < 100, detail.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Contains("(long)", detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DoesNotAskTheUserAboutACallThatCouldNotRun()
    {
        var (scope, browser) = await OpenAsync();
        var tooMany = string.Join(",", Enumerable.Range(0, 5000).Select(index => $"\"e{index}\""));

        var big = await browser.Module.CallAsync("bpmn_delete", $$"""{"elementIds":[{{tooMany}}]}""");
        var empty = await browser.Module.CallAsync("bpmn_delete", """{"elementIds":[]}""");
        var missing = await browser.Module.CallAsync("bpmn_delete", "{}");

        Assert.Equal(BpmnToolCodes.TooLarge, big.GetProperty("code").GetString());
        Assert.Equal(BpmnToolCodes.InvalidArgument, empty.GetProperty("code").GetString());
        Assert.Equal(BpmnToolCodes.InvalidArgument, missing.GetProperty("code").GetString());
        Assert.Null(SessionOf(scope).PendingConfirmation);
    }

    [Fact]
    public async Task RefusesAnApprovedDeleteWhenTheStoredDocumentMovedWhileTheUserWasDeciding()
    {
        var (scope, browser, binding) = await OpenBoundAsync();
        var store = Services.GetRequiredService<IBpmnDocumentStore>();

        var call = browser.Module.CallAsync("bpmn_delete", """{"elementIds":["s"]}""");
        scope.WaitForState(() => SessionOf(scope).PendingConfirmation is not null);
        var current = await store.StatAsync(Key, Token);
        await store.SaveDocumentAsync(Key, BpmnXmlReader.Read(Xml.Replace("Start", "Theirs", StringComparison.Ordinal)).Document, current!.Revision, Token);
        await scope.InvokeAsync(() => binding.CheckForChangesAsync(Token));
        await AnswerAsync(scope, allowed: true);
        var result = await call;

        // The user allowed a change to the document they were shown, and it is no longer the stored one.
        Assert.Equal(BpmnToolCodes.StaleDocument, result.GetProperty("code").GetString());
        Assert.Equal("Start", Name(scope, "s"));
    }

    [Fact]
    public async Task DeletesWithoutAskingWhenTheHostTurnedConfirmationOff()
    {
        var (scope, browser) = await OpenAsync(options => options.RequireConfirmationForConsequential = false);

        var result = await browser.Module.CallAsync("bpmn_delete", """{"elementIds":["review"]}""");

        Assert.True(result.GetProperty("ok").GetBoolean(), result.ToString());
        Assert.Null(SessionOf(scope).PendingConfirmation);
        Assert.Equal("<gone>", Name(scope));
    }

    [Fact]
    public async Task KeepsDiagramTextOutOfTheQuestionsOwnWords()
    {
        var (scope, browser) = await OpenAsync(document: Hostile());

        var call = browser.Module.CallAsync("bpmn_delete", """{"elementIds":["evil"]}""");
        var question = await AnswerAsync(scope, allowed: false);
        await call;

        Assert.DoesNotContain("IGNORE", question.Title, StringComparison.Ordinal);
        Assert.DoesNotContain("IGNORE", question.Summary, StringComparison.Ordinal);
        Assert.Contains(question.Details, detail => detail.Contains("IGNORE PREVIOUS", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RefusesToEditAReadOnlyEditor()
    {
        var (scope, browser) = await OpenAsync(readOnly: true);

        var result = await browser.Module.CallAsync("bpmn_set_property", """{"elementId":"review","name":"name","value":"Hacked"}""");

        Assert.Equal(BpmnToolCodes.ReadOnly, result.GetProperty("code").GetString());
        Assert.Equal("Review", Name(scope));
    }

    [Fact]
    public async Task UndoesItsOwnEditButNeverTheUsers()
    {
        var (scope, browser) = await OpenAsync();
        await scope.InvokeAsync(() => SessionOf(scope).ApplyAsync(new UpdatePropertiesCommand("review", "name", "Users")));

        var refused = await browser.Module.CallAsync("bpmn_undo");
        Assert.Equal(BpmnToolCodes.Refused, refused.GetProperty("code").GetString());
        Assert.Equal("Users", Name(scope));

        await browser.Module.CallAsync("bpmn_set_property", """{"elementId":"review","name":"name","value":"Agents"}""");
        var undone = await browser.Module.CallAsync("bpmn_undo");

        Assert.True(undone.GetProperty("ok").GetBoolean(), undone.ToString());
        Assert.Equal("Users", Name(scope));
    }

    [Fact]
    public async Task SelectsWhatTheAgentPointsAt()
    {
        var (scope, browser) = await OpenAsync();

        var result = await browser.Module.CallAsync("bpmn_select", """{"elementIds":["review"]}""");

        Assert.True(result.GetProperty("ok").GetBoolean(), result.ToString());
        Assert.Equal(["review"], SessionOf(scope).State.Selection);
    }

    [Fact]
    public async Task RefusesAnEditingToolThatReachesABridgeWithoutAGuard()
    {
        var (scope, _) = await OpenAsync();
        var host = new BpmnSessionToolHost(SessionOf(scope));
        var bridge = new BpmnWebMcpBridge(host, BpmnWebMcpToolSet.Create("bpmn_", string.Empty, allowMutations: true), work => work());

        var result = JsonElement.Parse(await bridge.ExecuteAsync("1", "bpmn_delete", """{"elementIds":["review"]}"""));

        Assert.Equal(BpmnToolCodes.Denied, result.GetProperty("code").GetString());
        Assert.Equal("Review", Name(scope));
    }

    // --- an editor bound to a stored document -----------------------------------------------------

    private async Task<(IRenderedComponent<BpmnEditorScope> Scope, FakeBrowser Browser, BpmnDocumentBinding Binding)> OpenBoundAsync(Action<BpmnWebMcpOptions>? more = null)
    {
        Allow(more);
        var browser = new FakeBrowser();
        Services.AddSingleton<IJSRuntime>(browser);
        var store = Services.GetRequiredService<IBpmnDocumentStore>();
        await store.SaveDocumentAsync(Key, BpmnXmlReader.Read(Xml).Document, ifMatchRevision: null, Token);
        var binding = Services.GetRequiredService<IBpmnDocumentBindingFactory>().Create();
        await binding.LoadAsync(Key, Token);

        var (scope, _) = RenderWithBrowser(browser, binding: binding, browserRegistered: true);
        scope.WaitForAssertion(() => Assert.Equal(BpmnWebMcpState.Registered, Part(scope).Status));
        return (scope, browser, binding);
    }

    [Fact]
    public async Task OffersThePersistenceToolsOnlyToABoundEditor()
    {
        var (scope, _, _) = await OpenBoundAsync();

        Assert.Contains("bpmn_save", Part(scope).RegisteredTools);
        Assert.Contains("bpmn_get_document_info", Part(scope).RegisteredTools);
    }

    [Fact]
    public async Task ReportsUnsavedEditsInEveryResult()
    {
        var (_, browser, _) = await OpenBoundAsync();

        var before = await browser.Module.CallAsync("bpmn_validate");
        await browser.Module.CallAsync("bpmn_set_property", """{"elementId":"s","name":"name","value":"Begin"}""");
        var after = await browser.Module.CallAsync("bpmn_validate");
        var info = await browser.Module.CallAsync("bpmn_get_document_info");

        Assert.False(before.GetProperty("isDirty").GetBoolean());
        Assert.True(after.GetProperty("isDirty").GetBoolean());
        Assert.True(info.GetProperty("data").GetProperty("isDirty").GetBoolean());
        Assert.Equal("intake.bpmn", info.GetProperty("data").GetProperty("name").GetProperty("untrustedText").GetString());
    }

    [Fact]
    public async Task AsksBeforeSavingAndSavesOnYes()
    {
        var (scope, browser, binding) = await OpenBoundAsync();
        await browser.Module.CallAsync("bpmn_set_property", """{"elementId":"s","name":"name","value":"Begin"}""");

        var call = browser.Module.CallAsync("bpmn_save");
        var question = await AnswerAsync(scope, allowed: true);
        var result = await call;

        Assert.True(question.IsDestructive);
        Assert.Contains("intake.bpmn", question.Details);
        Assert.True(result.GetProperty("ok").GetBoolean(), result.ToString());
        Assert.False(binding.IsDirty);
    }

    [Fact]
    public async Task SavesNothingOnNo()
    {
        var (scope, browser, binding) = await OpenBoundAsync();
        await browser.Module.CallAsync("bpmn_set_property", """{"elementId":"s","name":"name","value":"Begin"}""");

        var call = browser.Module.CallAsync("bpmn_save");
        await AnswerAsync(scope, allowed: false);
        var result = await call;

        Assert.Equal(BpmnToolCodes.Denied, result.GetProperty("code").GetString());
        Assert.True(binding.IsDirty);
    }

    [Fact]
    public async Task SavesWithoutAskingOnlyWhenPersistenceConfirmationIsOff()
    {
        // Relaxing confirmation for deleting does not relax it for saving.
        var (scope, browser, _) = await OpenBoundAsync(options => options.RequireConfirmationForConsequential = false);
        var asked = browser.Module.CallAsync("bpmn_save");
        await AnswerAsync(scope, allowed: false);
        Assert.Equal(BpmnToolCodes.Denied, (await asked).GetProperty("code").GetString());
    }

    [Fact]
    public async Task SavesStraightAwayWhenPersistenceConfirmationIsOff()
    {
        var (_, browser, _) = await OpenBoundAsync(options => options.RequireConfirmationForPersistence = false);

        var result = await browser.Module.CallAsync("bpmn_save");

        Assert.True(result.GetProperty("ok").GetBoolean(), result.ToString());
    }

    [Fact]
    public async Task RefusesEveryChangeOnceTheStoredDocumentMovedButStillReads()
    {
        var (scope, browser, binding) = await OpenBoundAsync();
        var store = Services.GetRequiredService<IBpmnDocumentStore>();
        var current = await store.StatAsync(Key, Token);
        await store.SaveDocumentAsync(Key, BpmnXmlReader.Read(Xml.Replace("Start", "Theirs", StringComparison.Ordinal)).Document, current!.Revision, Token);
        await scope.InvokeAsync(() => binding.CheckForChangesAsync(Token));

        foreach (var (tool, arguments) in new[]
        {
            ("bpmn_set_property", """{"elementId":"s","name":"name","value":"Mine"}"""),
            ("bpmn_create_element", """{"type":"task"}"""),
            ("bpmn_delete", """{"elementIds":["s"]}"""),
            ("bpmn_save", "{}"),
        })
        {
            var result = await browser.Module.CallAsync(tool, arguments);
            Assert.Equal(BpmnToolCodes.StaleDocument, result.GetProperty("code").GetString());
        }

        var info = await browser.Module.CallAsync("bpmn_get_document_info");
        Assert.Equal("updated", info.GetProperty("data").GetProperty("externalChange").GetString());
        Assert.Null(SessionOf(scope).PendingConfirmation);
        Assert.Equal("Start", Name(scope, "s"));
    }
}
