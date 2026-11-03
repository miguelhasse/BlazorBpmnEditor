using Bunit;
using Hasseware.AspNetCore.Components.Bpmn;
using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.FeelSharp;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Hasseware.FluentUI.AspNetCore.Components.Bpmn.FeelSharp.Tests;

public sealed class EditorTests : BunitContext
{
    private const string ModulePath = "./_content/Hasseware.AspNetCore.Components.Bpmn.FeelSharp/feelsharp-editor.js";

    [Fact]
    public async Task PropertyPresentationUsesExistingFluentInputAndNoInlineButtons()
    {
        var module = JSInterop.SetupModule(ModulePath);
        module.SetupModule("attachProperty", _ => true);
        var values = new List<string?>();
        var editor = RenderEditor(Context(Entry(BpmnFeelMode.Required, "=amount"), values.Add), propertyInput: true);
        Assert.Single(editor.FindComponents<FluentBpmnTextEntry>());
        Assert.Single(editor.FindComponents<FluentTextInput>());
        Assert.Empty(editor.FindComponents<FluentTextArea>());
        Assert.Empty(editor.FindAll("button, fluent-button, pre, textarea"));
        await editor.Instance.PropertySnapshotAsync("amount + 1", 10, 10, 1, 1, false, true);
        await editor.Instance.PropertyKeyAsync("commit", 1, 1);
        Assert.Equal(["=amount + 1"], values);
    }

    [Theory]
    [InlineData(BpmnPropertyEntryKind.Text)]
    [InlineData(BpmnPropertyEntryKind.TextArea)]
    public async Task EnhancedPropertyControlDoesNotReapplyValueOnItsOwnFocusEvents(BpmnPropertyEntryKind kind)
    {
        var module = JSInterop.SetupModule(ModulePath);
        module.SetupModule("attachProperty", _ => true);
        var editor = RenderEditor(Context(Entry(BpmnFeelMode.Required, "=amount") with { Kind = kind }),
            propertyInput: true);
        IRenderedComponent<ComponentBase> control = kind == BpmnPropertyEntryKind.Text
            ? editor.FindComponent<FluentTextInput>()
            : editor.FindComponent<FluentTextArea>();
        var renders = control.RenderCount;

        await editor.InvokeAsync(() => ((IHandleEvent)control.Instance).HandleEventAsync(
            new EventCallbackWorkItem((Action)(() => { })), null));

        Assert.Equal(renders, control.RenderCount);
    }

    [Fact]
    public void PropertyTextareaRetainsExistingMultilineRendererAndReadOnlyValue()
    {
        var entry = Entry(BpmnFeelMode.Required, "=amount") with { Kind = BpmnPropertyEntryKind.TextArea };
        var editor = RenderEditor(Context(entry) with { IsReadOnly = true }, propertyInput: true);
        Assert.Single(editor.FindComponents<FluentBpmnTextAreaEntry>());
        Assert.Equal("=amount", editor.FindComponent<FluentTextArea>().Instance.Value);
        Assert.Empty(editor.FindAll("button, fluent-button, pre"));
        Assert.DoesNotContain(JSInterop.Invocations, invocation => invocation.Identifier == "attachProperty");
    }

    [Fact]
    public void FluentRegistrationIsOptionalAndEvaluationRemainsSeparate()
    {
        var services = new ServiceCollection().AddFluentBpmnEditor().AddBpmnFeelSharpEditor()
            .AddFluentBpmnFeelSharpEditor();
        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IOptions<BpmnOptions>>().Value.Properties.Editors;
        Assert.Equal(typeof(FluentBpmnFeelSharpCodeEditor), registry.Resolve(Entry(BpmnFeelMode.Required, "=1")));
        Assert.Null(provider.GetService<IBpmnFeelSharpEvaluation>());
        registry.AddEditor<BpmnFeelSharpCodeEditor>(entry => entry.Id == "value");
        Assert.Equal(typeof(BpmnFeelSharpCodeEditor), registry.Resolve(Entry(BpmnFeelMode.Required, "=1")));
    }

    [Fact]
    public void NativeEditorCanOverrideFluentRegistration()
    {
        var services = new ServiceCollection().AddFluentBpmnFeelSharpEditor().AddBpmnFeelSharpEditor();
        using var provider = services.BuildServiceProvider();
        Assert.Equal(typeof(BpmnFeelSharpCodeEditor), provider.GetRequiredService<IOptions<BpmnOptions>>()
            .Value.Properties.Editors.Resolve(Entry(BpmnFeelMode.Required, "=1")));
    }

    [Fact]
    public void OptionalLiteralUsesFluentKit()
    {
        var editor = RenderEditor(Context(Entry(BpmnFeelMode.Optional, "literal")));
        Assert.Single(editor.FindComponents<FluentBpmnTextEntry>());
        Assert.DoesNotContain(JSInterop.Invocations, invocation => invocation.Identifier == "import" &&
            invocation.Arguments.Contains(ModulePath));
    }

    [Fact]
    public void ReadOnlyUsesFluentTextAreaWithoutEditorBridge()
    {
        var editor = RenderEditor(Context(Entry(BpmnFeelMode.Optional, "=amount")) with { IsReadOnly = true });
        var input = editor.FindComponent<FluentTextArea>();
        Assert.True(input.Instance.ReadOnly);
        Assert.Equal("=amount", input.Instance.Value);
        Assert.Single(editor.FindComponents<FluentLabel>());
        Assert.DoesNotContain(JSInterop.Invocations, invocation => invocation.Identifier == "import" &&
            invocation.Arguments.Contains(ModulePath));
    }

    [Fact]
    public async Task SharedSnapshotsRenderFluentSuggestionsAndAdvisoryDiagnostics()
    {
        SetupBridge();
        string? submitted = null;
        var editor = RenderEditor(Context(Entry(BpmnFeelMode.Required, "=cou"), value => submitted = value) with
        {
            Variables = [new("count") { Detail = "Item count" }],
        });
        await editor.Instance.SnapshotAsync("cou", 3, 3, 1, 1, false, true);
        Assert.NotEmpty(editor.FindComponents<FluentButton>());
        Assert.Single(editor.FindAll("[role=option]"), option => option.TextContent.Contains("Item count", StringComparison.Ordinal));
        await editor.Instance.SnapshotAsync("1 + )", 5, 5, 2, 1, false, false);
        Assert.Single(editor.FindComponents<FluentMessageBar>());
        await editor.Instance.NativeKeyAsync("commit", 2, 1);
        Assert.Equal("=1 + )", submitted);
    }

    [Fact]
    public async Task EnhancedFluentValueEventsDoNotOverwriteNativeSnapshots()
    {
        SetupBridge();
        string? submitted = null;
        var editor = RenderEditor(Context(Entry(BpmnFeelMode.Required, "=1"), value => submitted = value));
        await editor.Instance.SnapshotAsync("2", 1, 1, 1, 1, false, true);
        await editor.InvokeAsync(() => editor.FindComponent<FluentFeelSharpInput>().Instance.ValueChanged.InvokeAsync("old"));
        await editor.Instance.NativeKeyAsync("commit", 1, 1);
        Assert.Equal("=2", submitted);
    }

    [Fact]
    public void EnhancedInputDoesNotReapplyParentValue()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
        var input = Render<FluentFeelSharpInput>(parameters => parameters
            .Add(component => component.InitialValue, "initial")
            .Add(component => component.Enhanced, true));
        var textarea = input.FindComponent<FluentTextArea>();
        var renders = textarea.RenderCount;
        input.Render(parameters => parameters.Add(component => component.InitialValue, "stale"));
        Assert.Equal(renders, textarea.RenderCount);
        Assert.Equal("initial", textarea.Instance.Value);
    }

    [Fact]
    public void UnavailableEnhancementRetainsFluentInputWarningsAndCommits()
    {
        Services.AddSingleton<IJSRuntime>(new UnavailableEditorRuntime(JSInterop.JSRuntime));
        string? submitted = null;
        var editor = RenderEditor(Context(Entry(BpmnFeelMode.Required, "=1"), value => submitted = value));
        editor.Find("fluent-textarea").TriggerEvent("oninput", new ChangeEventArgs { Value = "2 + )" });
        Assert.Single(editor.FindComponents<FluentMessageBar>());
        editor.Find("fluent-textarea").TriggerEvent("onfocusout", new Microsoft.AspNetCore.Components.Web.FocusEventArgs());
        Assert.Equal("=2 + )", submitted);
        editor.Find("fluent-textarea").TriggerEvent("oninput", new ChangeEventArgs { Value = "3" });
        editor.Find("fluent-textarea").TriggerEvent("onkeydown", new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs
        {
            Key = "Enter",
            CtrlKey = true,
        });
        Assert.Equal("=3", submitted);
    }

    [Fact]
    public async Task OldFallbackCallbacksCannotChangeResetEntry()
    {
        Services.AddSingleton<IJSRuntime>(new UnavailableEditorRuntime(JSInterop.JSRuntime));
        string? submitted = null;
        var editor = RenderEditor(Context(Entry(BpmnFeelMode.Required, "=first"), value => submitted = value));
        var oldInput = editor.FindComponent<FluentFeelSharpInput>().Instance;
        var oldDraft = oldInput.ValueChanged;
        var oldFocusOut = oldInput.FocusOut;
        editor.Render(parameters => parameters.Add(component => component.Context,
            Context(Entry(BpmnFeelMode.Required, "=reset"), value => submitted = value) with { Revision = 2 }));

        await editor.InvokeAsync(() => oldDraft.InvokeAsync("stale"));
        await editor.InvokeAsync(() => oldFocusOut.InvokeAsync(new()));

        Assert.Null(submitted);
        Assert.Equal("reset", editor.FindComponent<FluentTextArea>().Instance.Value);
    }

    [Fact]
    public async Task CompositionReadOnlyAndDisposalPreventCommits()
    {
        SetupBridge();
        string? submitted = null;
        var editor = RenderEditor(Context(Entry(BpmnFeelMode.Required, "=1"), value => submitted = value));
        await editor.Instance.SnapshotAsync("2", 1, 1, 1, 1, true, true);
        await editor.Instance.NativeKeyAsync("commit", 1, 1);
        Assert.Null(submitted);
        editor.Render(parameters => parameters.Add(component => component.Context,
            Context(Entry(BpmnFeelMode.Required, "=reset")) with { IsReadOnly = true, Revision = 2 }));
        await editor.Instance.SnapshotAsync("stale", 5, 5, 2, 1, false, true);
        Assert.Equal("=reset", editor.FindComponent<FluentTextArea>().Instance.Value);
        await editor.InvokeAsync(() => editor.Instance.DisposeAsync().AsTask());
        await editor.Instance.NativeKeyAsync("commit", 2, 1);
        Assert.Null(submitted);
    }

    [Fact]
    public void BindingErrorsAndEscapedHighlightsRemainVisible()
    {
        var editor = RenderEditor(Context(Entry(BpmnFeelMode.Required, "=\"<script>\"") with
        {
            Error = "Binding failed",
        }) with
        { IsReadOnly = true });
        Assert.Single(editor.FindComponents<FluentMessageBar>());
        Assert.Empty(editor.FindAll("script"));
        Assert.Contains("<script>", editor.Find("pre").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FluentTogglePreservesLiteralAndExpressionPrefix()
    {
        SetupBridge();
        string? submitted = null;
        var editor = RenderEditor(Context(Entry(BpmnFeelMode.Optional, "amount"), value => submitted = value));
        await editor.InvokeAsync(() => editor.FindComponent<FluentButton>().Instance.OnClick.InvokeAsync(new()));
        Assert.Equal("=amount", submitted);
        Assert.Equal("amount", editor.FindComponent<FluentTextArea>().Instance.Value);
        await editor.InvokeAsync(() => editor.FindComponents<FluentButton>()[0].Instance.OnClick.InvokeAsync(new()));
        Assert.Equal("amount", submitted);
        Assert.Single(editor.FindComponents<FluentBpmnTextEntry>());
    }

    [Fact]
    public async Task EmptyExpressionCommitsNull()
    {
        SetupBridge();
        string? submitted = "unchanged";
        var editor = RenderEditor(Context(Entry(BpmnFeelMode.Required, "=1"), value => submitted = value));
        await editor.Instance.SnapshotAsync("", 0, 0, 1, 1, false, false);
        await editor.Instance.NativeKeyAsync("commit", 1, 1);
        Assert.Null(submitted);
    }

    [Fact]
    public async Task FluentExplicitCompletionRendersFunctionHelpAndUsesBridge()
    {
        SetupBridge();
        var editor = RenderEditor(Context(Entry(BpmnFeelMode.Required, "=")));
        editor.Find("[aria-keyshortcuts]").Click();
        Assert.Contains(JSInterop.Invocations, invocation => invocation.Identifier == "complete");
        await editor.Instance.SnapshotAsync("", 0, 0, 1, 1, false, true);
        await editor.Instance.NativeKeyAsync("complete", 1, 1);
        Assert.NotEmpty(editor.FindAll("[role=option]"));
        Assert.Equal("abs(-3)", editor.Find(".bpmn-feelsharp-help code").TextContent);
        Assert.Contains("Absolute value", editor.Find(".bpmn-feelsharp-help").TextContent, StringComparison.Ordinal);
        var helpId = editor.Find(".bpmn-feelsharp-help").Id;
        Assert.NotNull(helpId);
        Assert.Contains(helpId, editor.Find(".fluent-bpmn-feelsharp-surface")
            .GetAttribute("data-input-describedby"), StringComparison.Ordinal);
        await editor.Instance.NativeKeyAsync("Escape", 1, 1);
        Assert.Empty(editor.FindAll(".bpmn-feelsharp-help"));
        Assert.Equal("", editor.Find(".bpmn-feelsharp-announcement").TextContent);
    }

    [Fact]
    public async Task FluentRegistrationObserverReceivesAdvisoryUpdatesWithoutEvaluation()
    {
        var updates = new List<BpmnFeelSharpDiagnosticsUpdate>();
        Services.AddFluentBpmnFeelSharpEditor(options => options.DiagnosticsObserver = update =>
        {
            updates.Add(update);
            return Task.CompletedTask;
        });
        SetupBridge();
        string? submitted = null;
        var editor = RenderEditor(Context(Entry(BpmnFeelMode.Required, "=1"), value => submitted = value));
        Assert.Single(updates);
        Assert.Null(Services.GetService<IBpmnFeelSharpEvaluation>());
        await editor.Instance.SnapshotAsync("1 + )", 5, 5, 1, 1, false, true);
        Assert.Single(updates[1].Diagnostics);
        Assert.Single(editor.FindComponents<FluentMessageBar>());
        await editor.Instance.NativeKeyAsync("commit", 1, 1);
        Assert.Equal("=1 + )", submitted);
        Assert.Equal(2, updates.Count);
        await editor.Instance.SnapshotAsync("1", 1, 1, 2, 1, false, true);
        Assert.Empty(updates[2].Diagnostics);
    }

    private void SetupBridge()
    {
        var module = JSInterop.SetupModule(ModulePath);
        var bridge = module.SetupModule("attachFluent", _ => true);
        bridge.SetupVoid("dispose", _ => true).SetVoidResult();
        bridge.SetupVoid("complete", _ => true).SetVoidResult();
    }

    [Fact]
    public async Task FluentStandardModalAppliesOnceAndSurvivesParentReentry()
    {
        SetupBridge();
        var hide = JSInterop.SetupVoid("Microsoft.FluentUI.Blazor.Components.Dialog.Hide", _ => true);
        var module = JSInterop.SetupModule(ModulePath);
        var bridge = module.SetupModule("attachFluent", _ => true);
        bridge.Setup<BpmnFeelSharpDraft>("read", _ => true).SetResult(new("42", 2, 2));
        Services.AddFluentBpmnFeelSharpEditor(options => options.ExpandedEditor = true);
        IRenderedComponent<FluentBpmnFeelSharpCodeEditor>? editor = null;
        var values = new List<string?>();
        void Commit(string? value)
        {
            values.Add(value);
            editor!.Render(parameters => parameters.Add(component => component.Context, Context(Entry(BpmnFeelMode.Required, value), Commit)));
        }
        editor = RenderEditor(Context(Entry(BpmnFeelMode.Required, "=1"), Commit));
        var button = Assert.Single(editor.FindComponents<FluentButton>(), component =>
            component.Markup.Contains("Expand FEEL", StringComparison.Ordinal));
        var expanded = editor.InvokeAsync(() => button.Instance.OnClick.InvokeAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));
        editor.WaitForAssertion(() => Assert.Single(editor.FindComponents<FluentFeelSharpManager>()));
        Assert.True(editor.FindComponent<FluentFeelSharpManager>().Instance.Closed.HasDelegate);
        var manager = editor.FindComponent<FluentFeelSharpManager>();
        Assert.Single(manager.FindAll("textarea, fluent-textarea"));
        Assert.Empty(manager.FindAll("fluent-label, .bpmn-properties__feel-badge, [aria-keyshortcuts]"));
        Assert.Empty(manager.FindAll("[role=tabpanel][id$='-Editor-panel'] button, [role=tabpanel][id$='-Editor-panel'] fluent-button"));
        Assert.Equal(4, manager.FindAll("fluent-button[role=tab]").Count);
        manager.Find("[role=tab][id$='-Evaluation']").Click();
        var run = manager.Find("[data-action=run]");
        Assert.True(run.HasAttribute("disabled"));
        Assert.Equal("button", run.GetAttribute("role"));
        Assert.Equal("true", run.GetAttribute("aria-disabled"));
        Assert.Empty(manager.FindAll("output"));
        manager.Find("[role=tab][id$='-Editor']").Click();
        Assert.Equal("true", editor.Find(".fluent-bpmn-feelsharp-surface").GetAttribute("data-input-readonly"));
        var workspace = editor.FindComponent<BpmnFeelSharpWorkspace>();
        await workspace.InvokeAsync(() => workspace.Instance.Applied.InvokeAsync("42"));
        await expanded;
        Assert.Equal(["=42"], values);
        Assert.False(editor.FindComponent<FluentFeelSharpManager>().Instance.Visible);
        Assert.Single(editor.FindAll("fluent-dialog"));
        hide.SetVoidResult();
        editor.WaitForAssertion(() => Assert.Empty(editor.FindComponents<FluentFeelSharpManager>()));
    }

    [Fact]
    public void FluentStandaloneWorkspaceUsesFluentFieldWithoutImplicitEvaluation()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentBpmnEditor();
        Services.AddFluentBpmnFeelSharpEditor();
        Services.AddFluentUIComponents();
        var workspace = Render<FluentBpmnFeelSharpWorkspace>(parameters => parameters.Add(component => component.Value, "false"));
        workspace.Find("[role=tab][id$='-Evaluation']").Click();
        Assert.Single(workspace.FindComponents<FluentTextArea>());
        Assert.Contains("Evaluation unavailable", workspace.Markup, StringComparison.Ordinal);
        Assert.Empty(workspace.FindAll("output"));
    }

    private IRenderedComponent<FluentBpmnFeelSharpCodeEditor> RenderEditor(BpmnPropertyEntryContext context, bool propertyInput = false)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
        Services.AddFluentBpmnEditor();
        var registry = Services.GetRequiredService<IOptions<BpmnOptions>>().Value.Properties.Editors;
        var scope = Render<BpmnPropertyScopeProvider>(parameters => parameters.Add(component => component.Editors, registry)
            .AddChildContent<FluentBpmnFeelSharpCodeEditor>(child => child.Add(component => component.Context, context)
                .Add(component => component.PropertyInput, propertyInput)));
        return scope.FindComponent<FluentBpmnFeelSharpCodeEditor>();
    }

    private static BpmnPropertyEntry Entry(BpmnFeelMode mode, string? value) =>
        new("value", "Value", BpmnPropertyEntryKind.Text) { Feel = mode, Value = value };

    private static BpmnPropertyEntryContext Context(BpmnPropertyEntry entry, Action<string?>? changed = null) =>
        new(entry, EventCallback.Factory.Create<string?>(new object(), changed ?? (_ => { })), default, false, 0)
        { ElementId = "element" };

    private sealed class UnavailableEditorRuntime(IJSRuntime runtime) : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, default, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            identifier == "import" && args?.FirstOrDefault() as string == ModulePath
                ? ValueTask.FromException<TValue>(new JSException("Editor bridge unavailable"))
                : runtime.InvokeAsync<TValue>(identifier, cancellationToken, args);
    }
}
