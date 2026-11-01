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
        Assert.Contains("FEEL", editor.Find("[role=status]").TextContent, StringComparison.Ordinal);
        editor.Find("textarea").Input("2 + )");
        editor.Find("textarea").Change("2 + )");
        Assert.Equal("=2 + )", submitted);
        editor.Find("textarea").Change("");
        Assert.Null(submitted);
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
            editor.Instance.Input(new() { Value = "stale" }, oldGeneration);
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

    private IRenderedComponent<BpmnFeelSharpCodeEditor> RenderEditor(BpmnPropertyEntryContext context, bool fluent = false)
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
            .AddChildContent<BpmnFeelSharpCodeEditor>(child => child.Add(component => component.Context, context)));
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
