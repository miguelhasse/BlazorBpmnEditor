using Bunit;
using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;
using Hasseware.FluentUI.AspNetCore.Components.Bpmn;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Hasseware.AspNetCore.Components.Bpmn.FeelSharp.Tests;

public sealed class PropertyInputTests : BunitContext
{
    private const string ModulePath = "./_content/Hasseware.AspNetCore.Components.Bpmn.FeelSharp/feelsharp-editor.js";
    private const string Xml = """
        <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="Definitions">
          <process id="Process"><task id="element"/><task id="other"/></process>
        </definitions>
        """;

    [Theory]
    [InlineData(BpmnPropertyEntryKind.Text, false)]
    [InlineData(BpmnPropertyEntryKind.TextArea, false)]
    [InlineData(BpmnPropertyEntryKind.Text, true)]
    [InlineData(BpmnPropertyEntryKind.TextArea, true)]
    public void EnhancesRegisteredControlWithoutExtraEditorOrButtons(BpmnPropertyEntryKind kind, bool fluent)
    {
        var editor = RenderProperty(Context("=amount", kind: kind), fluent: fluent);
        Assert.Empty(editor.FindAll("button, pre, .bpmn-feelsharp-highlight"));
        Assert.Empty(editor.FindAll("select"));
        if (fluent)
        {
            Assert.Equal(kind == BpmnPropertyEntryKind.Text ? 1 : 0, editor.FindComponents<FluentTextInput>().Count);
            Assert.Equal(kind == BpmnPropertyEntryKind.TextArea ? 1 : 0, editor.FindComponents<FluentTextArea>().Count);
        }
        else
        {
            var input = Assert.Single(editor.FindAll("input, textarea"));
            Assert.Equal(kind == BpmnPropertyEntryKind.Text ? "INPUT" : "TEXTAREA", input.TagName);
            Assert.Equal("amount", input.GetAttribute("value"));
        }
        Assert.Contains(JSInterop.Invocations, invocation => invocation.Identifier == "attachProperty");
    }

    [Fact]
    public async Task PrefixOnlyUsesSameInputAndPreservesLiteralVersusFeelCommit()
    {
        var values = new List<string?>();
        var editor = RenderProperty(Context("literal", values.Add, BpmnFeelMode.Optional));
        Assert.Empty(editor.FindAll("select, button, textarea"));
        var generation = editor.Instance._generation;
        await editor.Instance.PropertySnapshotAsync("=amount", 7, 7, 1, generation, false, true);
        await editor.Instance.PropertyKeyAsync("commit", 1, generation);
        Assert.Equal(["=amount"], values);
        await editor.Instance.PropertySnapshotAsync("text", 4, 4, 2, generation, false, true);
        await editor.Instance.PropertyKeyAsync("commit", 2, generation);
        Assert.Equal(["=amount", "text"], values);
    }

    [Fact]
    public async Task OptionalFieldKeepsVisiblePrefixAndDraftWithoutPersisting()
    {
        var values = new List<string?>();
        var editor = RenderProperty(Context("=original", values.Add, BpmnFeelMode.Optional));
        Assert.Empty(editor.FindAll("select"));
        Assert.Equal("=original", editor.Find("input").GetAttribute("value"));
        await editor.Instance.PropertySnapshotAsync("=draft", 6, 6, 1, 1, false, true);
        editor.Render();
        Assert.Empty(editor.FindAll("select"));
        Assert.True(editor.Instance.FeelActive);
        Assert.Empty(values);
    }

    [Fact]
    public async Task PrefixChangesUpdateManagerEligibilityWithoutBlurOrPersistence()
    {
        Services.AddBpmnEditor();
        Services.Configure<BpmnFeelSharpEditorOptions>(options => options.ExpandedEditor = true);
        var scope = Render<BpmnEditorScope>(parameters => parameters.Add(component => component.Document, BpmnXmlReader.Read(Xml).Document));
        await scope.InvokeAsync(() => scope.Instance.Session.SetSelectionAsync(["element"]));
        var values = new List<string?>();
        var editor = RenderProperty(Context("literal", values.Add, BpmnFeelMode.Optional), session: scope.Instance.Session);
        await editor.Instance.ActivatePropertyAsync();
        Assert.False(scope.Instance.Session.PropertyAction!.Enabled);
        await editor.Instance.PropertySnapshotAsync("=amount", 7, 7, 1, 1, false, true);
        Assert.True(scope.Instance.Session.PropertyAction!.Enabled);
        await editor.Instance.PropertySnapshotAsync("amount", 6, 6, 2, 1, false, true);
        Assert.False(scope.Instance.Session.PropertyAction!.Enabled);
        Assert.Empty(values);
    }

    [Fact]
    public async Task RequiredFieldRemainsFeelWithoutVisiblePrefix()
    {
        var values = new List<string?>();
        var editor = RenderProperty(Context("=1", values.Add));
        await editor.Instance.PropertySnapshotAsync("amount", 6, 6, 1, 1, false, true);
        Assert.True(editor.Instance.FeelActive);
        await editor.Instance.PropertyKeyAsync("commit", 1, 1);
        Assert.Equal(["=amount"], values);
    }

    [Theory]
    [InlineData(BpmnFeelSharpLiteralManagerBehavior.RequireFeelMode, false)]
    [InlineData(BpmnFeelSharpLiteralManagerBehavior.AllowConversionOnApply, true)]
    public async Task LiteralManagerConvertsOnlyOnApply(BpmnFeelSharpLiteralManagerBehavior behavior, bool enabled)
    {
        Services.AddBpmnEditor();
        Services.Configure<BpmnFeelSharpEditorOptions>(options =>
        {
            options.ExpandedEditor = true;
            options.LiteralManagerBehavior = behavior;
        });
        var values = new List<string?>();
        var scope = Render<BpmnEditorScope>(parameters => parameters.Add(component => component.Document, BpmnXmlReader.Read(Xml).Document));
        var session = scope.Instance.Session;
        await scope.InvokeAsync(() => session.SetSelectionAsync(["element"]));
        var editor = RenderProperty(Context("literal", values.Add, BpmnFeelMode.Optional), session: session, draft: "literal");
        await editor.Instance.ActivatePropertyAsync();
        Assert.Equal(enabled, session.PropertyAction!.Enabled);
        await editor.InvokeAsync(session.ExecutePropertyActionAsync);
        Assert.Equal(enabled, editor.Instance._expanded);
        Assert.Empty(values);
        if (!enabled) return;
        await editor.InvokeAsync(editor.Instance.CancelExpanded);
        Assert.Empty(values);
        await Assert.ThrowsAsync<InvalidOperationException>(() => editor.InvokeAsync(() => editor.Instance.ApplyExpandedAsync("stale")));
        await editor.InvokeAsync(session.ExecutePropertyActionAsync);
        await editor.InvokeAsync(() => editor.Instance.ApplyExpandedAsync("42"));
        Assert.Equal(["=42"], values);
    }

    [Fact]
    public async Task TargetRetainsOwnershipAcrossBlurButNotSelectionChangesOrDisposal()
    {
        Services.AddBpmnEditor();
        Services.Configure<BpmnFeelSharpEditorOptions>(options => options.ExpandedEditor = true);
        var scope = Render<BpmnEditorScope>(parameters => parameters.Add(component => component.Document, BpmnXmlReader.Read(Xml).Document));
        var session = scope.Instance.Session;
        await scope.InvokeAsync(() => session.SetSelectionAsync(["element"]));
        var editor = RenderProperty(Context("=1"), session: session);
        await editor.Instance.ActivatePropertyAsync();
        var action = session.PropertyAction;
        Assert.NotNull(action);
        Assert.Equal("FEEL", action.Label);
        await editor.Instance.PropertySnapshotAsync("2", 1, 1, 1, 1, false, false);
        await editor.Instance.PropertyKeyAsync("commit", 1, 1);
        Assert.Equal(action, session.PropertyAction);
        await scope.InvokeAsync(() => session.SetSelectionAsync(["other"]));
        Assert.Null(session.PropertyAction);
        editor.Render();
        Assert.Null(session.PropertyAction);
        await scope.InvokeAsync(() => session.SetSelectionAsync(["element"]));
        Assert.Null(session.PropertyAction);
        await editor.Instance.ActivatePropertyAsync();
        Assert.NotNull(session.PropertyAction);
        await editor.InvokeAsync(() => editor.Instance.DisposeAsync().AsTask());
        Assert.Null(session.PropertyAction);
    }

    [Fact]
    public async Task EditorSessionsNeverShareTargetsAndReadOnlyOrDocumentReplacementClearsTarget()
    {
        Services.AddBpmnEditor();
        var document = BpmnXmlReader.Read(Xml).Document;
        var first = Render<BpmnEditorScope>(parameters => parameters.Add(component => component.Document, document));
        var second = Render<BpmnEditorScope>(parameters => parameters.Add(component => component.Document, document));
        await first.InvokeAsync(() => first.Instance.Session.SetSelectionAsync(["element"]));
        await second.InvokeAsync(() => second.Instance.Session.SetSelectionAsync(["element"]));
        var editor = RenderProperty(Context("=1"), session: first.Instance.Session);
        await editor.Instance.ActivatePropertyAsync();
        Assert.Null(second.Instance.Session.PropertyAction);
        first.Render(parameters => parameters.Add(component => component.IsReadOnly, true));
        Assert.Null(first.Instance.Session.PropertyAction);
        first.Render(parameters => parameters.Add(component => component.IsReadOnly, false));
        await editor.Instance.ActivatePropertyAsync();
        Assert.NotNull(first.Instance.Session.PropertyAction);
        first.Render(parameters => parameters.Add(component => component.Document, BpmnXmlReader.Read(Xml).Document));
        Assert.Null(first.Instance.Session.PropertyAction);
    }

    [Fact]
    public void FallbackPropertyCommitsWithKeyboardAndRetainsAdvisoryWarnings()
    {
        Services.AddSingleton<IJSRuntime>(new UnavailableRuntime());
        var values = new List<string?>();
        var editor = RenderProperty(Context("=1", values.Add));
        editor.Find("input").Input("2 + )");
        Assert.Single(editor.Instance.Diagnostics);
        editor.Find("input").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter", CtrlKey = true });
        Assert.Equal(["=2 + )"], values);
        Assert.Empty(editor.FindAll("textarea, button"));
    }

    [Fact]
    public void PropertyRetainsExplicitUnderlyingKindOverride()
    {
        Services.Configure<BpmnOptions>(options => options.Properties.Editors.AddEditor<BpmnTextAreaEntry>(BpmnPropertyEntryKind.Text));
        var editor = RenderProperty(Context("=1"));
        Assert.Single(editor.FindComponents<BpmnTextAreaEntry>());
        Assert.Empty(editor.FindAll("input"));
    }

    [Fact]
    public async Task RemovingListRowDisposesItsManagerTarget()
    {
        Services.AddBpmnEditor().AddBpmnFeelSharpEditor(options => options.ExpandedEditor = true);
        JSInterop.Mode = JSRuntimeMode.Loose;
        var module = JSInterop.SetupModule(ModulePath);
        var bridge = module.SetupModule("attachProperty", _ => true);
        bridge.Setup<BpmnFeelSharpDraft>("read", _ => true).SetResult(new("first", 5, 5));
        var scope = Render<BpmnEditorScope>(parameters => parameters.Add(component => component.Document, BpmnXmlReader.Read(Xml).Document));
        var session = scope.Instance.Session;
        await scope.InvokeAsync(() => session.SetSelectionAsync(["element"]));
        var entry = new BpmnPropertyEntry("rows", "Rows", BpmnPropertyEntryKind.List)
        {
            Columns = [new("expression", "Expression") { Feel = BpmnFeelMode.Required }],
            Rows = [new Dictionary<string, string?> { ["expression"] = "=first" },
                new Dictionary<string, string?> { ["expression"] = "=second" }],
        };
        var list = Render<BpmnPropertyScopeProvider>(parameters => parameters.AddCascadingValue(session)
            .AddChildContent<BpmnPropertyEntryView>(child => child.Add(component => component.Context, Context(null) with { Entry = entry })));
        var first = list.FindComponents<BpmnFeelSharpCodeEditor>()[0].Instance;
        await first.ActivatePropertyAsync();
        Assert.NotNull(session.PropertyAction);
        list.Find("tbody tr button").Click();
        list.WaitForAssertion(() => Assert.Null(session.PropertyAction));
        Assert.Single(list.FindComponents<BpmnFeelSharpCodeEditor>());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => first.CaptureAsync());
    }

    [Fact]
    public async Task StaleFallbackCallbackCannotEditResetProperty()
    {
        var values = new List<string?>();
        var editor = RenderProperty(Context("=1", values.Add));
        var old = (BpmnPropertyEntryContext)editor.Instance.PropertyParameters["Context"];
        editor.Render(parameters => parameters.Add(component => component.Context, Context("=reset", values.Add) with { Revision = 2 }));
        await editor.InvokeAsync(() => old.OnValueChanged.InvokeAsync("stale"));
        Assert.Empty(values);
        Assert.Equal("reset", editor.Find("input").GetAttribute("value"));
    }

    private IRenderedComponent<BpmnFeelSharpCodeEditor> RenderProperty(BpmnPropertyEntryContext context,
        bool fluent = false, BpmnEditorSession? session = null, string draft = "1")
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        if (!Services.IsProviderInitialized && fluent)
        {
            Services.AddFluentUIComponents();
            Services.AddFluentBpmnEditor();
        }
        else if (!Services.IsProviderInitialized) Services.AddBpmnEditor();
        var module = JSInterop.SetupModule(ModulePath);
        var bridge = module.SetupModule("attachProperty", _ => true);
        bridge.Setup<BpmnFeelSharpDraft>("read", _ => true).SetResult(new(draft, draft.Length, draft.Length));
        var registry = Services.GetRequiredService<IOptions<BpmnOptions>>().Value.Properties.Editors;
        var rendered = Render<BpmnPropertyScopeProvider>(parameters =>
        {
            parameters.Add(component => component.Editors, registry);
            if (session is not null) parameters.AddCascadingValue(session);
            parameters.AddChildContent<BpmnFeelSharpCodeEditor>(child => child.Add(component => component.Context, context));
        });
        return rendered.FindComponent<BpmnFeelSharpCodeEditor>();
    }

    private static BpmnPropertyEntryContext Context(string? value, Action<string?>? changed = null,
        BpmnFeelMode mode = BpmnFeelMode.Required, BpmnPropertyEntryKind kind = BpmnPropertyEntryKind.Text) =>
        new(new("value", "Value", kind) { Feel = mode, Value = value },
            EventCallback.Factory.Create<string?>(new object(), changed ?? (_ => { })), default, false, 0)
        { ElementId = "element" };

    private sealed class UnavailableRuntime : IJSRuntime
    {
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) =>
            ValueTask.FromException<T>(new JSException("Native enhancement unavailable"));
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken token, object?[]? args) =>
            InvokeAsync<T>(identifier, args);
    }
}
