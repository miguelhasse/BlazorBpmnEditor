using Bunit;
using Hasseware.AspNetCore.Components.Bpmn.CamundaFeel;
using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.FluentUI.AspNetCore.Components.Bpmn;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;
using Microsoft.FluentUI.AspNetCore.Components;

namespace Hasseware.AspNetCore.Components.Bpmn.FeelSharp.Tests;

public sealed class EditorTests : BunitContext
{
    private const string ModulePath = "./_content/Hasseware.AspNetCore.Components.Bpmn.FeelSharp/feelsharp-editor.js";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EditorOverridesBothKits(bool fluent)
    {
        if (fluent)
        {
            Services.AddFluentBpmnEditor();
        }
        else
        {
            Services.AddBpmnEditor();
        }
        Services.AddBpmnFeelSharpEditor();
        var registry = Services.GetRequiredService<IOptions<BpmnOptions>>().Value.Properties.Editors;
        Assert.Equal(typeof(BpmnFeelSharpCodeEditor), registry.Resolve(Entry(BpmnFeelMode.Required, "=1")));
        registry.AddEditor<BpmnFeelCodeEditor>(entry => entry.Id == "value");
        Assert.Equal(typeof(BpmnFeelCodeEditor), registry.Resolve(Entry(BpmnFeelMode.Required, "=1")));
    }

    [Fact]
    public void BothOptionalRegistrationsFollowLastWins()
    {
        var services = new ServiceCollection().AddBpmnFeelEditor().AddBpmnFeelSharpEditor();
        using var provider = services.BuildServiceProvider();
        Assert.Equal(typeof(BpmnFeelSharpCodeEditor), provider.GetRequiredService<IOptions<BpmnOptions>>()
            .Value.Properties.Editors.Resolve(Entry(BpmnFeelMode.Required, "=1")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OptionalLiteralUsesActiveKit(bool fluent)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var editor = RenderEditor(Context(Entry(BpmnFeelMode.Optional, "3")), fluent);
        if (fluent)
        {
            Assert.Single(editor.FindComponents<FluentBpmnTextEntry>());
        }
        else
        {
            Assert.NotNull(editor.Find("input"));
        }
        Assert.DoesNotContain(JSInterop.Invocations, invocation => invocation.Identifier == "import" &&
            invocation.Arguments.Contains(ModulePath));
    }

    [Fact]
    public void AdvisoryWarningsDoNotBlockFallbackCommitsOrHideBindingErrors()
    {
        Services.AddSingleton<IJSRuntime>(new UnavailableRuntime());
        string? submitted = null;
        var entry = Entry(BpmnFeelMode.Required, "=1 + )") with { Error = "binding failed" };
        var editor = RenderEditor(Context(entry, value => submitted = value));
        Assert.Contains("binding failed", editor.Markup, StringComparison.Ordinal);
        Assert.Contains("FEEL", editor.Find("[role=status]:not(.bpmn-feelsharp-announcement)").TextContent, StringComparison.Ordinal);
        editor.Find("textarea").Input("2 + )");
        editor.Find("textarea").Change("2 + )");
        Assert.Equal("=2 + )", submitted);
        editor.Find("textarea").Change("");
        Assert.Null(submitted);
    }

    [Fact]
    public void DeferredDiagnosticsStillAnalyzeResetWhenDomEnhancementIsUnavailable()
    {
        Services.AddSingleton<IJSRuntime>(new UnavailableRuntime());
        Services.Configure<BpmnFeelSharpEditorOptions>(options =>
        {
            options.DeferInitialDiagnostics = true;
            options.DiagnosticsDelay = TimeSpan.FromMilliseconds(20);
        });
        var editor = RenderEditor(Context(Entry(BpmnFeelMode.Required, "=1 + )")));
        editor.WaitForAssertion(() => Assert.Single(editor.Instance.Diagnostics));
        editor.Render(parameters => parameters.Add(component => component.Context,
            Context(Entry(BpmnFeelMode.Required, "=2")) with { Revision = 2 }));
        editor.WaitForAssertion(() => Assert.Empty(editor.Instance.Diagnostics));
        editor.Render(parameters => parameters.Add(component => component.Context,
            Context(Entry(BpmnFeelMode.Required, "=2 + )")) with { Revision = 3 }));
        editor.WaitForAssertion(() => Assert.Single(editor.Instance.Diagnostics));
    }

    [Fact]
    public async Task OldFallbackCallbacksCannotChangeResetEntry()
    {
        Services.AddSingleton<IJSRuntime>(new UnavailableRuntime());
        string? submitted = null;
        var editor = RenderEditor(Context(Entry(BpmnFeelMode.Required, "=first"), value => submitted = value));
        var oldGeneration = editor.Instance._generation;
        editor.Render(parameters => parameters.Add(component => component.Context,
            Context(Entry(BpmnFeelMode.Required, "=reset"), value => submitted = value) with { Revision = 2 }));

        await editor.InvokeAsync(async () =>
        {
            await editor.Instance.InputAsync(new() { Value = "stale" }, oldGeneration);
            await editor.Instance.ChangeAsync(new() { Value = "stale" }, oldGeneration);
            await editor.Instance.KeyDownAsync(new() { Key = "Enter", CtrlKey = true }, oldGeneration);
        });

        Assert.Null(submitted);
        Assert.Equal("reset", editor.Instance.Expression);
        editor.Find("textarea").Change("current");
        Assert.Equal("=current", submitted);
    }

    [Fact]
    public async Task OldLiteralCallbackCannotChangeResetEntry()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        string? submitted = null;
        var editor = RenderEditor(Context(Entry(BpmnFeelMode.Optional, "first"), value => submitted = value));
        var oldContext = (BpmnPropertyEntryContext)editor.Instance.EditorParameters["Context"];
        editor.Render(parameters => parameters.Add(component => component.Context,
            Context(Entry(BpmnFeelMode.Optional, "reset"), value => submitted = value) with { Revision = 2 }));

        await editor.InvokeAsync(() => oldContext.OnValueChanged.InvokeAsync("stale"));

        Assert.Null(submitted);
        Assert.Equal("reset", editor.Instance.DisplayValue);
    }

    [Fact]
    public void HighlightingEscapesMarkup()
    {
        var editor = RenderEditor(Context(Entry(BpmnFeelMode.Required, "=\"<script>alert(1)</script>\"")) with { IsReadOnly = true });
        Assert.Empty(editor.FindAll("script"));
        Assert.Contains("<script>", editor.Find("pre").TextContent, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", editor.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NativeSnapshotsRespectRevisionCompositionAndReadOnly()
    {
        var module = JSInterop.SetupModule(ModulePath);
        var bridge = module.SetupModule("attach", _ => true);
        bridge.SetupVoid("dispose", _ => true).SetVoidResult();
        string? submitted = null;
        var editor = RenderEditor(Context(Entry(BpmnFeelMode.Required, "=amo"), value => submitted = value) with
        {
            Variables = [new("amount")],
        });
        await editor.Instance.SnapshotAsync("amo", 3, 3, 1, 1, false, true);
        Assert.Single(editor.FindAll("[role=option]"));
        await editor.Instance.SnapshotAsync("bad", 3, 3, 0, 1, false, true);
        Assert.Single(editor.FindAll("[role=option]"));
        await editor.Instance.SnapshotAsync("amount", 6, 6, 2, 1, true, true);
        await editor.Instance.NativeKeyAsync("commit", 2, 1);
        Assert.Null(submitted);
        await editor.Instance.SnapshotAsync("amount", 6, 6, 3, 1, false, true);
        await editor.Instance.NativeKeyAsync("commit", 3, 1);
        Assert.Equal("=amount", submitted);
        editor.Render(p => p.Add(c => c.Context, Context(Entry(BpmnFeelMode.Required, "=reset")) with { IsReadOnly = true, Revision = 2 }));
        await editor.Instance.SnapshotAsync("stale", 5, 5, 4, 1, false, true);
        Assert.Equal("=reset", editor.Find("textarea").GetAttribute("value"));
        Assert.NotNull(editor.Find("textarea").GetAttribute("readonly"));
    }

    [Fact]
    public void ReadOnlyNeverImportsBridgeOrSubmits()
    {
        string? submitted = null;
        var editor = RenderEditor(Context(Entry(BpmnFeelMode.Optional, "=1"), value => submitted = value) with { IsReadOnly = true });
        editor.Find("textarea").Input("2");
        editor.Find("textarea").Change("2");
        Assert.Null(submitted);
        Assert.Empty(JSInterop.Invocations);
    }

    [Fact]
    public void OptionalTogglePreservesLiteralAndPersistencePrefix()
    {
        var module = JSInterop.SetupModule(ModulePath);
        var bridge = module.SetupModule("attach", _ => true);
        bridge.SetupVoid("dispose", _ => true).SetVoidResult();
        string? submitted = null;
        var editor = RenderEditor(Context(Entry(BpmnFeelMode.Optional, "amount"), value => submitted = value));
        editor.Find("button").Click();
        Assert.Equal("=amount", submitted);
        Assert.Equal("amount", editor.Find("textarea").GetAttribute("value"));
        editor.Find("button").Click();
        Assert.Equal("amount", submitted);
    }

    [Fact]
    public async Task DisposedEditorIgnoresNativeCallbacks()
    {
        var module = JSInterop.SetupModule(ModulePath);
        var bridge = module.SetupModule("attach", _ => true);
        bridge.SetupVoid("dispose", _ => true).SetVoidResult();
        string? submitted = null;
        var editor = RenderEditor(Context(Entry(BpmnFeelMode.Required, "=1"), value => submitted = value));
        await editor.InvokeAsync(() => editor.Instance.DisposeAsync().AsTask());
        await editor.Instance.SnapshotAsync("2", 1, 1, 1, 1, false, true);
        await editor.Instance.NativeKeyAsync("commit", 1, 1);
        Assert.Null(submitted);
    }

    [Fact]
    public async Task DiagnosticsPublishOncePerAnalysisAndClearAfterComposition()
    {
        var updates = new List<BpmnFeelSharpDiagnosticsUpdate>();
        var observed = new List<BpmnFeelSharpDiagnosticsUpdate>();
        Services.AddBpmnFeelSharpEditor(options =>
        {
            options.DiagnosticsObserver = update =>
            {
                observed.Add(update);
                return Task.CompletedTask;
            };
        });
        var module = JSInterop.SetupModule(ModulePath);
        var bridge = module.SetupModule("attach", _ => true);
        bridge.SetupVoid("dispose", _ => true).SetVoidResult();
        var callback = EventCallback.Factory.Create<BpmnFeelSharpDiagnosticsUpdate>(this, updates.Add);
        var editor = RenderEditor(Context(Entry(BpmnFeelMode.Required, "=1 + )")), diagnosticsChanged: callback);
        var diagnostic = Assert.Single(editor.Instance.Diagnostics);
        Assert.Equal("FEEL.Syntax", diagnostic.Code);
        Assert.Equal(BpmnFeelSharpDiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Null(diagnostic.Start);
        Assert.Null(diagnostic.Length);
        Assert.Single(updates);
        Assert.Single(observed);
        await editor.Instance.SnapshotAsync("1 + )", 1, 1, 1, 1, false, true);
        await editor.Instance.NativeKeyAsync("Escape", 1, 1);
        Assert.Single(updates);
        await editor.Instance.SnapshotAsync("1", 1, 1, 2, 1, true, true);
        Assert.Single(updates);
        await editor.Instance.SnapshotAsync("1", 1, 1, 3, 1, false, true);
        Assert.Empty(editor.Instance.Diagnostics);
        Assert.Equal(2, updates.Count);
        Assert.Equal(2, observed.Count);
        editor.Render(parameters => parameters.Add(component => component.Context,
            Context(Entry(BpmnFeelMode.Optional, "literal")) with { Revision = 2 }));
        Assert.Empty(Assert.Single(updates, update => update.Generation == 2).Diagnostics);
        await editor.Instance.SnapshotAsync("stale", 5, 5, 4, 1, false, true);
        Assert.Equal(3, updates.Count);
        await editor.InvokeAsync(() => editor.Instance.DisposeAsync().AsTask());
        await editor.Instance.SnapshotAsync("bad", 3, 3, 5, 2, false, true);
        Assert.Equal(3, updates.Count);
    }

    [Fact]
    public async Task DiagnosticCallbackResetCannotDeliverOldUpdateToObserver()
    {
        var observed = new List<BpmnFeelSharpDiagnosticsUpdate>();
        Services.Configure<BpmnFeelSharpEditorOptions>(options =>
            options.DiagnosticsObserver = update => { observed.Add(update); return Task.CompletedTask; });
        var module = JSInterop.SetupModule(ModulePath);
        var bridge = module.SetupModule("attach", _ => true);
        bridge.SetupVoid("dispose", _ => true).SetVoidResult();
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callback = EventCallback.Factory.Create<BpmnFeelSharpDiagnosticsUpdate>(this, async update =>
        {
            if (!update.Diagnostics.IsEmpty)
            {
                entered.SetResult();
                await pending.Task;
            }
        });
        var editor = RenderEditor(Context(Entry(BpmnFeelMode.Required, "=1")), diagnosticsChanged: callback);
        var snapshot = editor.Instance.SnapshotAsync("1 + )", 5, 5, 1, 1, false, true);
        await entered.Task.WaitAsync(Xunit.TestContext.Current.CancellationToken);
        editor.Render(parameters => parameters.Add(component => component.Context,
            Context(Entry(BpmnFeelMode.Required, "=2")) with { Revision = 2 }));
        pending.SetResult();
        await snapshot;
        Assert.All(observed, update => Assert.Empty(update.Diagnostics));
        Assert.Equal(2, observed.Count);
        Assert.Empty(editor.Instance.Diagnostics);
    }

    [Fact]
    public async Task ObserverFailuresPropagateInsteadOfHidingDiagnostics()
    {
        Services.Configure<BpmnFeelSharpEditorOptions>(options => options.DiagnosticsObserver = update =>
            update.Diagnostics.IsEmpty ? Task.CompletedTask : Task.FromException(new InvalidOperationException("Observer failed")));
        var module = JSInterop.SetupModule(ModulePath);
        var bridge = module.SetupModule("attach", _ => true);
        bridge.SetupVoid("dispose", _ => true).SetVoidResult();
        var editor = RenderEditor(Context(Entry(BpmnFeelMode.Required, "=1")));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => editor.Instance.SnapshotAsync("1 + )", 5, 5, 1, 1, false, true));
        Assert.Equal("Observer failed", error.Message);
        Assert.Single(editor.Instance.Diagnostics);
    }

    [Fact]
    public async Task ExplicitCompletionShowsEscapedHelpAndRequestsNativeCaret()
    {
        var module = JSInterop.SetupModule(ModulePath);
        var bridge = module.SetupModule("attach", _ => true);
        bridge.SetupVoid("dispose", _ => true).SetVoidResult();
        bridge.SetupVoid("complete", _ => true).SetVoidResult();
        var editor = RenderEditor(Context(Entry(BpmnFeelMode.Required, "=")) with
        { Variables = [new("abs", "Value", "<script>host documentation</script>")] });
        editor.Find("button[aria-keyshortcuts]").Click();
        Assert.Single(bridge.Invocations, invocation => invocation.Identifier == "complete");
        await editor.Instance.SnapshotAsync("", 0, 0, 1, 1, false, true);
        await editor.Instance.NativeKeyAsync("complete", 1, 1);
        Assert.NotEmpty(editor.FindAll("[role=option]"));
        Assert.Empty(editor.FindAll("script"));
        Assert.Contains("<script>host documentation</script>", editor.Find(".bpmn-feelsharp-help").TextContent, StringComparison.Ordinal);
        Assert.Contains(editor.Instance.HelpId, editor.Find("textarea").GetAttribute("aria-describedby"), StringComparison.Ordinal);
        Assert.Contains("suggestions available", editor.Find(".bpmn-feelsharp-announcement").TextContent, StringComparison.Ordinal);
        await editor.Instance.NativeKeyAsync("Escape", 1, 1);
        Assert.Empty(editor.FindAll(".bpmn-feelsharp-help"));
        await editor.Instance.SnapshotAsync("sum", 3, 3, 2, 1, false, true);
        Assert.Contains("sum(list)", editor.Find(".bpmn-feelsharp-help").TextContent, StringComparison.Ordinal);
        Assert.Equal("sum([1, 2, 3])", editor.Find(".bpmn-feelsharp-help code").TextContent);
    }

    [Fact]
    public async Task VariableRefreshPreservesExplicitEmptyCompletion()
    {
        var module = JSInterop.SetupModule(ModulePath);
        var bridge = module.SetupModule("attach", _ => true);
        bridge.SetupVoid("dispose", _ => true).SetVoidResult();
        var context = Context(Entry(BpmnFeelMode.Required, "=")) with { Variables = [new("amount")] };
        var editor = RenderEditor(context);
        await editor.Instance.SnapshotAsync("", 0, 0, 1, 1, false, true);
        await editor.Instance.NativeKeyAsync("complete", 1, 1);
        Assert.NotEmpty(editor.FindAll("[role=option]"));
        await editor.Instance.NativeKeyAsync("ArrowDown", 1, 1);

        editor.Render(parameters => parameters.Add(component => component.Context,
            context with { Variables = [new("account", "Updated type", "Updated documentation")] }));

        Assert.Contains(editor.FindAll("[role=option]"), option => option.TextContent.Contains("account", StringComparison.Ordinal));
        Assert.DoesNotContain(editor.FindAll("[role=option]"), option => option.TextContent.Contains("amount", StringComparison.Ordinal));
        Assert.Contains("Updated documentation", editor.Find(".bpmn-feelsharp-help").TextContent, StringComparison.Ordinal);
        await editor.Instance.NativeKeyAsync("Escape", 1, 1);
        editor.Render(parameters => parameters.Add(component => component.Context, context));
        Assert.Empty(editor.FindAll("[role=option]"));
    }

    private IRenderedComponent<BpmnFeelSharpCodeEditor> RenderEditor(BpmnPropertyEntryContext context, bool fluent = false,
        EventCallback<BpmnFeelSharpDiagnosticsUpdate> diagnosticsChanged = default)
    {
        var services = new ServiceCollection();
        if (fluent)
        {
            services.AddFluentBpmnEditor();
            Services.AddFluentUIComponents();
        }
        else
        {
            services.AddBpmnEditor();
        }
        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IOptions<BpmnOptions>>().Value.Properties.Editors;
        var scope = Render<BpmnPropertyScopeProvider>(parameters => parameters
            .Add(component => component.Editors, registry)
            .AddChildContent<BpmnFeelSharpCodeEditor>(child => child.Add(component => component.Context, context)
                .Add(component => component.PropertyInput, false)
                .Add(component => component.DiagnosticsChanged, diagnosticsChanged)));
        return scope.FindComponent<BpmnFeelSharpCodeEditor>();
    }

    private static BpmnPropertyEntry Entry(BpmnFeelMode mode, string? value) =>
        new("value", "Value", BpmnPropertyEntryKind.Text) { Feel = mode, Value = value };

    private static BpmnPropertyEntryContext Context(BpmnPropertyEntry entry, Action<string?>? changed = null) =>
        new(entry, EventCallback.Factory.Create<string?>(new object(), changed ?? (_ => { })), default, false, 0)
        { ElementId = "element" };

    private sealed class UnavailableRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            ValueTask.FromException<TValue>(new JSException("unavailable"));

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken,
            object?[]? args) => InvokeAsync<TValue>(identifier, args);
    }
}
