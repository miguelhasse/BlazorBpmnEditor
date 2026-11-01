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

    private void SetupBridge()
    {
        var module = JSInterop.SetupModule(ModulePath);
        var bridge = module.SetupModule("attachFluent", _ => true);
        bridge.SetupVoid("dispose", _ => true).SetVoidResult();
    }

    private IRenderedComponent<FluentBpmnFeelSharpCodeEditor> RenderEditor(BpmnPropertyEntryContext context)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
        Services.AddFluentBpmnEditor();
        var registry = Services.GetRequiredService<IOptions<BpmnOptions>>().Value.Properties.Editors;
        var scope = Render<BpmnPropertyScopeProvider>(parameters => parameters.Add(component => component.Editors, registry)
            .AddChildContent<FluentBpmnFeelSharpCodeEditor>(child => child.Add(component => component.Context, context)));
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
