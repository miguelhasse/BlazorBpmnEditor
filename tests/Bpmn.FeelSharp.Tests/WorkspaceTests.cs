using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Hasseware.AspNetCore.Components.Bpmn.FeelSharp.Tests;

public sealed class WorkspaceTests : BunitContext
{
    private const string ModulePath = "./_content/Hasseware.AspNetCore.Components.Bpmn.FeelSharp/feelsharp-editor.js";

    [Fact]
    public async Task ManagerPresentationHasOneEditorNoRedundantChromeAndOneDiagnostic()
    {
        Setup("1");
        var workspace = Render<BpmnFeelSharpWorkspace>(parameters => parameters
            .Add(component => component.Value, "1")
            .Add(component => component.MinimalEditor, true)
            .Add(component => component.ShowCommitActions, false));
        Assert.Equal(4, workspace.FindAll("[role=tab]").Count);
        Assert.Single(workspace.FindAll("textarea"));
        Assert.Empty(workspace.FindAll(".bpmn-feelsharp-complete, .bpmn-properties__feel-badge, .bpmn-feelsharp-tools"));
        Assert.Empty(workspace.FindAll("[role=tabpanel][id$='-Editor-panel'] button"));
        var editor = workspace.FindComponent<BpmnFeelSharpCodeEditor>();
        await editor.Instance.SnapshotAsync("1 + )", 5, 5, 1, editor.Instance._generation, false, true);
        var warning = workspace.Find(".bpmn-properties__feel-warning").TextContent;
        Assert.Single(workspace.FindAll(".bpmn-properties__feel-warning"));
        Assert.DoesNotContain(workspace.FindAll("p"), element => element.TextContent == warning);
    }

    [Fact]
    public void OptionalHostActionsAreGroupedBehindToolsDisclosure()
    {
        Setup("1");
        var workspace = Render<BpmnFeelSharpWorkspace>(parameters => parameters
            .Add(component => component.MinimalEditor, true)
            .Add(component => component.ShowCommitActions, false)
            .Add(component => component.Actions, new BpmnFeelSharpHostActions
            {
                Suggest = (_, _) => Task.FromResult("2"),
            }));
        Assert.Equal("Tools", workspace.Find("details summary").TextContent);
        Assert.False(workspace.Find("details").HasAttribute("open"));
        Assert.Single(workspace.FindAll("details button"));
    }

    [Fact]
    public async Task TypingDoesNotRerenderWorkspaceOrVisitedReferenceContent()
    {
        Setup("1");
        var workspace = Render<TrackedWorkspace>(parameters => parameters.Add(component => component.Value, "1"));
        workspace.Find("[role=tab][id$='-Reference']").Click();
        workspace.Find("[role=tab][id$='-Editor']").Click();
        var renders = workspace.Instance.Renders;
        var editor = workspace.FindComponent<BpmnFeelSharpCodeEditor>();
        await editor.Instance.SnapshotAsync("2", 1, 1, 1, editor.Instance._generation, false, true);
        Assert.Equal(renders, workspace.Instance.Renders);
        Assert.NotEmpty(workspace.FindAll("[id$='-Reference-panel'] strong"));
    }

    private sealed class TrackedWorkspace : BpmnFeelSharpWorkspace
    {
        public int Renders { get; private set; }

        protected override void OnAfterRender(bool firstRender)
        {
            Renders++;
            base.OnAfterRender(firstRender);
        }
    }

    [Fact]
    public async Task TabsPreserveEditorDraftIdentityAndDoNotApply()
    {
        Setup("draft");
        var values = new List<string?>();
        var workspace = Render<BpmnFeelSharpWorkspace>(parameters => parameters.Add(component => component.Value, "initial")
            .Add(component => component.Applied, EventCallback.Factory.Create<string?>(this, values.Add)));
        var editor = workspace.FindComponent<BpmnFeelSharpCodeEditor>();
        var captured = await editor.Instance.CaptureAsync();
        foreach (var tab in new[] { "Reference", "Variables", "Evaluation", "Editor" })
        {
            workspace.Find($"[role=tab][id$='-{tab}']").Click();
            Assert.Same(editor.Instance, workspace.FindComponent<BpmnFeelSharpCodeEditor>().Instance);
        }
        Assert.Same(captured.SourceIdentity, (await editor.Instance.CaptureAsync()).SourceIdentity);
        Assert.Empty(values);
    }

    [Fact]
    public async Task StandaloneFieldSeparatesNativeDraftFromPrefixFreeCommit()
    {
        Setup("2");
        var commits = new List<string?>();
        var drafts = new List<BpmnFeelSharpDraft>();
        var field = Render<BpmnFeelSharpField>(parameters => parameters.Add(component => component.Value, "1")
            .Add(component => component.ValueChanged, EventCallback.Factory.Create<string?>(this, commits.Add))
            .Add(component => component.DraftChanged, EventCallback.Factory.Create<BpmnFeelSharpDraft>(this, drafts.Add)));
        var editor = field.FindComponent<BpmnFeelSharpCodeEditor>();
        await editor.Instance.SnapshotAsync("2", 1, 1, 1, editor.Instance._generation, false, true);
        Assert.Empty(commits);
        Assert.Equal("2", Assert.Single(drafts).Text);
        await editor.Instance.NativeKeyAsync("commit", 1, editor.Instance._generation);
        Assert.Equal(["2"], commits);
    }

    [Fact]
    public async Task StandaloneBlurCommitDoesNotInvalidateAnAwaitingDraftCapture()
    {
        Services.AddBpmnEditor();
        Services.AddLocalization();
        JSInterop.Mode = JSRuntimeMode.Loose;
        var module = JSInterop.SetupModule(ModulePath);
        var bridge = module.SetupModule("attach", _ => true);
        bridge.SetupVoid("dispose", _ => true).SetVoidResult();
        var read = bridge.Setup<BpmnFeelSharpDraft>("read", _ => true);
        var field = Render<BpmnFeelSharpField>(parameters => parameters.Add(component => component.Value, "1"));
        var editor = field.FindComponent<BpmnFeelSharpCodeEditor>();
        var generation = editor.Instance._generation;
        await editor.Instance.SnapshotAsync("2", 1, 1, 1, generation, false, true);
        var capture = field.InvokeAsync(field.Instance.CaptureAsync);
        await editor.Instance.NativeKeyAsync("commit", 1, generation);
        field.Render();
        read.SetResult(new("2", 1, 1));
        Assert.Equal("2", (await capture).Text);
        Assert.Equal(generation, editor.Instance._generation);
    }

    [Fact]
    public async Task WorkspaceCommitsOnlyThroughApplyAndCancelNeverApplies()
    {
        var read = Setup("2");
        var applied = new List<string?>();
        var workspace = Render<BpmnFeelSharpWorkspace>(parameters => parameters.Add(component => component.Value, "1")
            .Add(component => component.Applied, EventCallback.Factory.Create<string?>(this, applied.Add)));
        var editor = workspace.FindComponent<BpmnFeelSharpCodeEditor>();
        await editor.Instance.SnapshotAsync("2", 1, 1, 1, editor.Instance._generation, false, true);
        await editor.Instance.NativeKeyAsync("commit", 1, editor.Instance._generation);
        Assert.Empty(applied);
        await workspace.InvokeAsync(workspace.Instance.CancelAsync);
        workspace.Render();
        read(new("1", 1, 1));
        Assert.Empty(applied);
        await workspace.InvokeAsync(workspace.Instance.ApplyAsync);
        Assert.Equal(["1"], applied);
    }

    [Theory]
    [InlineData("null", "NullValue", "null")]
    [InlineData("false", "BooleanValue", "false")]
    [InlineData("[]", "ListValue", "[]")]
    public async Task PlaygroundDistinguishesSuccessfulNullFalseAndEmptyList(string expression, string type, string json)
    {
        Setup(expression);
        var workspace = Render<BpmnFeelSharpWorkspace>(parameters => parameters
            .Add(component => component.Value, expression)
            .Add(component => component.Evaluator, new BpmnFeelSharpEvaluation()));
        await workspace.InvokeAsync(workspace.Instance.RunAsync);
        workspace.Render();
        Assert.Contains(type, workspace.Find("output").TextContent, StringComparison.Ordinal);
        Assert.Equal(json, workspace.Find("output pre").TextContent);
    }

    [Fact]
    public async Task ChangedDraftClearsEvaluationWithoutWaitingForAnotherWorkspaceAction()
    {
        Setup("1");
        var workspace = Render<BpmnFeelSharpWorkspace>(parameters => parameters.Add(component => component.Value, "1")
            .Add(component => component.Evaluator, new BpmnFeelSharpEvaluation()));
        await workspace.InvokeAsync(workspace.Instance.RunAsync);
        workspace.Render();
        Assert.NotEmpty(workspace.FindAll("output"));
        var editor = workspace.FindComponent<BpmnFeelSharpCodeEditor>();
        await editor.Instance.SnapshotAsync("2", 1, 1, 1, editor.Instance._generation, false, true);
        Assert.Empty(workspace.FindAll("output"));
    }

    [Fact]
    public void PlaygroundDoesNotResolveRegisteredEvaluatorImplicitly()
    {
        Setup("1");
        Services.AddBpmnFeelSharpEvaluation();
        var workspace = Render<BpmnFeelSharpWorkspace>();
        workspace.Find("[role=tab][id$='-Evaluation']").Click();
        Assert.Contains("Evaluation unavailable", workspace.Markup, StringComparison.Ordinal);
        Assert.Empty(workspace.FindAll("output"));
    }

    [Fact]
    public async Task ResetDiscardsAwaitingHostProposal()
    {
        Setup("1");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var workspace = Render<BpmnFeelSharpWorkspace>(parameters => parameters.Add(component => component.Value, "1")
            .Add(component => component.Actions, new BpmnFeelSharpHostActions
            {
                Suggest = async (_, _) => { entered.SetResult(); return await pending.Task; },
            }));
        var request = workspace.InvokeAsync(workspace.Instance.SuggestAsync);
        await entered.Task.WaitAsync(Xunit.TestContext.Current.CancellationToken);
        workspace.Render(parameters => parameters.Add(component => component.Value, "2").Add(component => component.Revision, 2));
        pending.SetResult("malicious stale proposal");
        await request;
        Assert.DoesNotContain("malicious stale proposal", workspace.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IdleDiagnosticsCoalesceAndCommitFlushesPendingAnalysis()
    {
        Setup("1");
        Services.Configure<BpmnFeelSharpEditorOptions>(options => options.DiagnosticsDelay = TimeSpan.FromSeconds(10));
        var field = Render<BpmnFeelSharpField>(parameters => parameters.Add(component => component.Value, "1"));
        var editor = field.FindComponent<BpmnFeelSharpCodeEditor>();
        var generation = editor.Instance._generation;
        await editor.Instance.SnapshotAsync("1 + )", 5, 5, 1, generation, false, true);
        Assert.Empty(editor.Instance.Diagnostics);
        await editor.Instance.NativeKeyAsync("commit", 1, generation);
        Assert.Single(editor.Instance.Diagnostics);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IdleDiagnosticsRescheduleAfterRevertOrCompositionInterruptsPendingAnalysis(bool composing)
    {
        Setup("1");
        Services.Configure<BpmnFeelSharpEditorOptions>(options => options.DiagnosticsDelay = TimeSpan.FromMilliseconds(60));
        var field = Render<BpmnFeelSharpField>(parameters => parameters.Add(component => component.Value, "1"));
        var editor = field.FindComponent<BpmnFeelSharpCodeEditor>();
        var generation = editor.Instance._generation;
        await editor.Instance.SnapshotAsync("1 + )", 5, 5, 1, generation, false, true);
        var interrupted = composing ? "1 + )" : "1";
        await editor.Instance.SnapshotAsync(interrupted, interrupted.Length, interrupted.Length, 2, generation, composing, true);
        await Task.Delay(150, Xunit.TestContext.Current.CancellationToken);
        Assert.Empty(editor.Instance.Diagnostics);
        await editor.Instance.SnapshotAsync("1 + )", 5, 5, 3, generation, false, true);
        editor.WaitForAssertion(() => Assert.Single(editor.Instance.Diagnostics));
    }

    [Fact]
    public async Task NativeExpansionRendersWithoutSuggestionsAndResetDiscardsIt()
    {
        Setup("1");
        Services.Configure<BpmnFeelSharpEditorOptions>(options => options.ExpandedEditor = true);
        var field = Render<BpmnFeelSharpField>(parameters => parameters.Add(component => component.Value, "1"));
        var editor = field.FindComponent<BpmnFeelSharpCodeEditor>();
        await editor.InvokeAsync(editor.Instance.OpenExpandedAsync);
        editor.Render();
        Assert.Single(editor.FindComponents<BpmnFeelSharpDialog>());
        Assert.Empty(editor.FindAll("[role=option]"));
        field.Render(parameters => parameters.Add(component => component.Revision, 1));
        Assert.Empty(editor.FindComponents<BpmnFeelSharpDialog>());
    }

    [Fact]
    public async Task CapturedDraftCannotApplyToAnotherGenerationWithIdenticalText()
    {
        Setup("1");
        var field = Render<BpmnFeelSharpField>(parameters => parameters.Add(component => component.Value, "1"));
        var captured = await field.InvokeAsync(field.Instance.CaptureAsync);
        field.Render(parameters => parameters.Add(component => component.Revision, 1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => field.InvokeAsync(() => field.Instance.ApplyDraftAsync(captured, "2")));
        var current = await field.InvokeAsync(field.Instance.CaptureAsync);
        await field.InvokeAsync(() => field.Instance.ApplyDraftAsync(current, "2"));
    }

    [Fact]
    public async Task InitialDiagnosticsCanWaitForAttachmentAndCommitFlushesThem()
    {
        Setup("1 + )");
        Services.Configure<BpmnFeelSharpEditorOptions>(options =>
        {
            options.DeferInitialDiagnostics = true;
            options.DiagnosticsDelay = TimeSpan.FromSeconds(10);
        });
        var updates = new List<BpmnFeelSharpDiagnosticsUpdate>();
        var field = Render<BpmnFeelSharpField>(parameters => parameters.Add(component => component.Value, "1 + )")
            .Add(component => component.DiagnosticsChanged,
                EventCallback.Factory.Create<BpmnFeelSharpDiagnosticsUpdate>(this, updates.Add)));
        var editor = field.FindComponent<BpmnFeelSharpCodeEditor>();
        Assert.True(editor.Instance._enhanced);
        Assert.Empty(updates);
        Assert.Empty(editor.Instance.Diagnostics);
        await editor.Instance.NativeKeyAsync("commit", 0, editor.Instance._generation);
        Assert.Single(Assert.Single(updates).Diagnostics);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeferredInitialDiagnosticsPublishOnlyCurrentGeneration(bool readOnly)
    {
        Setup("1 + )");
        Services.Configure<BpmnFeelSharpEditorOptions>(options =>
        {
            options.DeferInitialDiagnostics = true;
            options.DiagnosticsDelay = TimeSpan.FromMilliseconds(60);
        });
        var updates = new List<BpmnFeelSharpDiagnosticsUpdate>();
        var field = Render<BpmnFeelSharpField>(parameters => parameters.Add(component => component.Value, "1 + )")
            .Add(component => component.ReadOnly, readOnly)
            .Add(component => component.DiagnosticsChanged,
                EventCallback.Factory.Create<BpmnFeelSharpDiagnosticsUpdate>(this, updates.Add)));
        field.Render(parameters => parameters.Add(component => component.Value, "2").Add(component => component.Revision, 1));
        field.WaitForAssertion(() =>
        {
            var update = Assert.Single(updates);
            Assert.Empty(update.Diagnostics);
            Assert.Equal(2, update.Generation);
        });
        await Task.Delay(150, Xunit.TestContext.Current.CancellationToken);
        Assert.Single(updates);
    }

    [Fact]
    public async Task DisposingEditorCancelsDeferredInitialDiagnostics()
    {
        Setup("1 + )");
        Services.Configure<BpmnFeelSharpEditorOptions>(options =>
        {
            options.DeferInitialDiagnostics = true;
            options.DiagnosticsDelay = TimeSpan.FromMilliseconds(60);
        });
        var updates = new List<BpmnFeelSharpDiagnosticsUpdate>();
        var field = Render<BpmnFeelSharpField>(parameters => parameters.Add(component => component.Value, "1 + )")
            .Add(component => component.DiagnosticsChanged,
                EventCallback.Factory.Create<BpmnFeelSharpDiagnosticsUpdate>(this, updates.Add)));
        var editor = field.FindComponent<BpmnFeelSharpCodeEditor>();
        await editor.InvokeAsync(() => editor.Instance.DisposeAsync().AsTask());
        await Task.Delay(150, Xunit.TestContext.Current.CancellationToken);
        Assert.Empty(updates);
    }

    [Fact]
    public async Task DeferredDiagnosticsObserverFailuresReachRenderer()
    {
        Setup("1 + )");
        Services.Configure<BpmnFeelSharpEditorOptions>(options =>
        {
            options.DeferInitialDiagnostics = true;
            options.DiagnosticsDelay = TimeSpan.FromMilliseconds(20);
            options.DiagnosticsObserver = _ => Task.FromException(new InvalidOperationException("Observer failed"));
        });
        Render<BpmnFeelSharpField>(parameters => parameters.Add(component => component.Value, "1 + )"));
        var exception = await Renderer.UnhandledException.WaitAsync(TimeSpan.FromSeconds(5), Xunit.TestContext.Current.CancellationToken);
        Assert.Equal("Observer failed", Assert.IsType<InvalidOperationException>(exception).Message);
    }

    [Fact]
    public async Task DeferredInitialDiagnosticsYieldWithZeroDelay()
    {
        Setup("1 + )");
        Services.Configure<BpmnFeelSharpEditorOptions>(options => options.DeferInitialDiagnostics = true);
        var field = Render<BpmnFeelSharpField>(parameters => parameters.Add(component => component.Value, "1 + )"));
        var editor = field.FindComponent<BpmnFeelSharpCodeEditor>();
        field.WaitForAssertion(() =>
        {
            Assert.True(editor.Instance._enhanced);
            Assert.Single(editor.Instance.Diagnostics);
        });
        await editor.Instance.SnapshotAsync("2", 1, 1, 1, editor.Instance._generation, false, true);
        Assert.Empty(editor.Instance.Diagnostics);
    }

    [Fact]
    public async Task CompositionInterruptsDeferredInitialDiagnosticsUntilFinished()
    {
        Setup("1 + )");
        Services.Configure<BpmnFeelSharpEditorOptions>(options =>
        {
            options.DeferInitialDiagnostics = true;
            options.DiagnosticsDelay = TimeSpan.FromMilliseconds(60);
        });
        var field = Render<BpmnFeelSharpField>(parameters => parameters.Add(component => component.Value, "1 + )"));
        var editor = field.FindComponent<BpmnFeelSharpCodeEditor>();
        var generation = editor.Instance._generation;
        await editor.Instance.SnapshotAsync("1 + )", 5, 5, 1, generation, true, true);
        await Task.Delay(150, Xunit.TestContext.Current.CancellationToken);
        Assert.Empty(editor.Instance.Diagnostics);
        await editor.Instance.SnapshotAsync("1 + )", 5, 5, 2, generation, false, true);
        field.WaitForAssertion(() => Assert.Single(editor.Instance.Diagnostics));
    }

    [Fact]
    public void WorkspaceReusesVariableMetadataUntilDeclarationsChange()
    {
        Setup("1");
        var variables = new List<BpmnFeelVariable> { new("amount", "number") };
        var workspace = Render<BpmnFeelSharpWorkspace>(parameters => parameters.Add(component => component.Variables, variables));
        var original = workspace.FindComponent<BpmnFeelSharpCodeEditor>().Instance.Context.Variables;
        workspace.Find("[role=tab][id$='-Reference']").Click();
        Assert.Same(original, workspace.FindComponent<BpmnFeelSharpCodeEditor>().Instance.Context.Variables);
        variables.Add(new("customer", "string"));
        workspace.Render();
        var current = workspace.FindComponent<BpmnFeelSharpCodeEditor>().Instance.Context.Variables;
        Assert.NotSame(original, current);
        Assert.Equal(["amount", "customer"], current.Select(variable => variable.Name));
    }

    [Fact]
    public void FunctionReferenceSupportsCategoryAndCaseTolerantSearch()
    {
        Setup("1");
        var workspace = Render<BpmnFeelSharpWorkspace>();
        Assert.Empty(workspace.FindAll("select"));
        workspace.Find("[role=tab][id$='-Reference']").Click();
        workspace.Find("select").Change("temporal");
        Assert.Contains("date and time", workspace.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("abs(-3)", workspace.Markup, StringComparison.Ordinal);
        workspace.Find("[role=tabpanel] input").Input("TODAY");
        Assert.Contains("today()", workspace.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("date and time", workspace.Markup, StringComparison.Ordinal);
        workspace.Find("[role=tab][id$='-Editor']").Click();
        Assert.NotNull(workspace.Find("[role=tabpanel][id$='-Reference-panel']").GetAttribute("hidden"));
        workspace.Find("[role=tab][id$='-Reference']").Click();
        Assert.Equal("TODAY", workspace.Find("[role=tabpanel] input").GetAttribute("value"));
        Assert.Contains("today()", workspace.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RevokingHostAuthorizationDiscardsPendingProposal()
    {
        Setup("1");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var workspace = Render<BpmnFeelSharpWorkspace>(parameters => parameters.Add(component => component.Value, "1")
            .Add(component => component.Actions, new BpmnFeelSharpHostActions
            {
                Suggest = async (_, _) => { entered.SetResult(); return await pending.Task; },
            }));
        var request = workspace.InvokeAsync(workspace.Instance.SuggestAsync);
        await entered.Task.WaitAsync(Xunit.TestContext.Current.CancellationToken);
        workspace.Render(parameters => parameters.Add(component => component.Actions, null));
        pending.SetResult("revoked proposal");
        await request;
        Assert.DoesNotContain("revoked proposal", workspace.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancelledRequestCannotReleaseBusyStateOfReplacementRequest()
    {
        Setup("1");
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstPending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondPending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var workspace = Render<BpmnFeelSharpWorkspace>(parameters => parameters.Add(component => component.Value, "1")
            .Add(component => component.Actions, new BpmnFeelSharpHostActions
            {
                Suggest = async (_, _) => { firstEntered.SetResult(); return await firstPending.Task; },
            }));
        var first = workspace.InvokeAsync(workspace.Instance.SuggestAsync);
        await firstEntered.Task.WaitAsync(Xunit.TestContext.Current.CancellationToken);
        workspace.Render(parameters => parameters.Add(component => component.Actions, new BpmnFeelSharpHostActions
        {
            Suggest = async (_, _) => { secondEntered.TrySetResult(); return await secondPending.Task; },
        }));
        var second = workspace.InvokeAsync(workspace.Instance.SuggestAsync);
        await secondEntered.Task.WaitAsync(Xunit.TestContext.Current.CancellationToken);
        firstPending.SetResult("cancelled");
        await first;
        workspace.Render();
        try
        {
            var button = Assert.Single(workspace.FindAll("button"), button => button.TextContent.Contains("Request host", StringComparison.Ordinal));
            Assert.True(button.HasAttribute("disabled"));
        }
        finally
        {
            secondPending.SetResult("current");
            await second;
        }
    }

    [Fact]
    public async Task SuggestionOwnsBusyStateWhileNativeCaptureIsPending()
    {
        Services.AddBpmnEditor();
        Services.AddLocalization();
        JSInterop.Mode = JSRuntimeMode.Loose;
        var module = JSInterop.SetupModule(ModulePath);
        var bridge = module.SetupModule("attach", _ => true);
        var read = bridge.Setup<BpmnFeelSharpDraft>("read", _ => true);
        var calls = 0;
        var workspace = Render<BpmnFeelSharpWorkspace>(parameters => parameters.Add(component => component.Value, "1")
            .Add(component => component.Actions, new BpmnFeelSharpHostActions
            {
                Suggest = (_, _) => { calls++; return Task.FromResult("2"); },
            }));
        var first = workspace.InvokeAsync(workspace.Instance.SuggestAsync);
        workspace.WaitForAssertion(() => Assert.Single(bridge.Invocations, invocation => invocation.Identifier == "read"));
        var second = workspace.InvokeAsync(workspace.Instance.SuggestAsync);
        read.SetResult(new("1", 1, 1));
        await Task.WhenAll(first, second);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task IndentationDoesNotChangeMultilineTemporalLiteral()
    {
        const string source = "@\"2025-01-01\n12:00:00\"";
        Setup(source);
        var workspace = Render<BpmnFeelSharpWorkspace>(parameters => parameters.Add(component => component.Value, source));
        var button = Assert.Single(workspace.FindAll("button"), button => button.TextContent.Contains("Indent selection", StringComparison.Ordinal));
        await workspace.InvokeAsync(() => button.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));
        Assert.DoesNotContain(JSInterop.Invocations, invocation => invocation.Identifier == "replace");
        Assert.NotEmpty(workspace.Find(".bpmn-feelsharp-workspace > [role=status]").TextContent);
    }

    [Fact]
    public async Task DisposedDialogDoesNotOpenAfterDelayedModuleImport()
    {
        Services.AddBpmnEditor();
        Services.AddLocalization();
        var runtime = new DelayedDialogRuntime();
        Services.AddSingleton<IJSRuntime>(runtime);
        var dialog = Render<BpmnFeelSharpDialog>(parameters => parameters.Add(component => component.ReadOnly, true));
        await runtime.Entered.Task.WaitAsync(Xunit.TestContext.Current.CancellationToken);
        await dialog.FindComponent<BpmnDialog>().Instance.DisposeAsync();
        runtime.Pending.SetResult(runtime.Module);
        dialog.WaitForAssertion(() => Assert.True(runtime.Module.Disposed));
        Assert.Equal(0, runtime.Module.ShowCalls);
    }

    private sealed class DelayedDialogRuntime : IJSRuntime
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<IJSObjectReference> Pending { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public DialogModule Module { get; } = new();
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, default, args);
        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            Assert.Equal("import", identifier);
            Entered.SetResult();
            return (TValue)await Pending.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class DialogModule : IJSObjectReference
    {
        public bool Disposed { get; private set; }
        public int ShowCalls { get; private set; }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, default, args);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (identifier == "showDialog")
            {
                ShowCalls++;
            }
            return ValueTask.FromResult(default(TValue)!);
        }
        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task StaleAcceptedProposalReportsRejectionWithoutBreakingWorkspace()
    {
        Setup("1", false);
        var applied = new List<string?>();
        var workspace = Render<BpmnFeelSharpWorkspace>(parameters => parameters.Add(component => component.Value, "1")
            .Add(component => component.Applied, EventCallback.Factory.Create<string?>(this, applied.Add))
            .Add(component => component.Actions, new BpmnFeelSharpHostActions { Suggest = (_, _) => Task.FromResult("2") }));
        await workspace.InvokeAsync(workspace.Instance.SuggestAsync);
        await workspace.InvokeAsync(workspace.Instance.AcceptProposalAsync);
        workspace.Render();
        Assert.Contains("Proposal rejected", workspace.Markup, StringComparison.Ordinal);
        Assert.Empty(applied);
        Assert.Single(workspace.FindComponents<BpmnFeelSharpCodeEditor>());
    }

    [Fact]
    public void EditingSampleClearsPreviouslyInferredHintsAndReportsInvalidJson()
    {
        Setup("1");
        var workspace = Render<BpmnFeelSharpWorkspace>();
        workspace.Find("[role=tab][id$='-Variables']").Click();
        var sample = workspace.Find("[role=tabpanel][id$='-Variables-panel'] textarea");
        sample.Input("""{"sampleOnly":1}""");
        var infer = Assert.Single(workspace.FindAll("button"), button => button.TextContent.Contains("Infer", StringComparison.Ordinal));
        infer.Click();
        Assert.Contains("sampleOnly: number", workspace.Markup, StringComparison.Ordinal);
        sample.Input("{");
        Assert.DoesNotContain("sampleOnly: number", workspace.Markup, StringComparison.Ordinal);
        infer.Click();
        Assert.NotEmpty(workspace.Find(".bpmn-feelsharp-workspace > [role=status]").TextContent);
    }

    [Fact]
    public async Task ClipboardDenialReportsVisibleFailureWithoutDestroyingResult()
    {
        Setup("false");
        JSInterop.SetupVoid("navigator.clipboard.writeText", _ => true).SetException(new JSException("Permission denied"));
        var workspace = Render<BpmnFeelSharpWorkspace>(parameters => parameters.Add(component => component.Value, "false")
            .Add(component => component.Evaluator, new BpmnFeelSharpEvaluation()));
        await workspace.InvokeAsync(workspace.Instance.RunAsync);
        workspace.Render();
        Assert.Single(workspace.FindAll("button"), button => button.TextContent.Contains("Copy result", StringComparison.Ordinal)).Click();
        Assert.Contains("Could not copy", workspace.Markup, StringComparison.Ordinal);
        Assert.Equal("false", workspace.Find("output pre").TextContent);
    }

    private Action<BpmnFeelSharpDraft> Setup(string draft, bool replacement = true)
    {
        Services.AddBpmnEditor();
        Services.AddLocalization();
        JSInterop.Mode = JSRuntimeMode.Loose;
        var module = JSInterop.SetupModule(ModulePath);
        var bridge = module.SetupModule("attach", _ => true);
        bridge.SetupVoid("dispose", _ => true).SetVoidResult();
        var read = bridge.Setup<BpmnFeelSharpDraft>("read", _ => true);
        read.SetResult(new(draft, draft.Length, draft.Length));
        bridge.Setup<bool>("replace", _ => true).SetResult(replacement);
        return value => read.SetResult(value);
    }
}
