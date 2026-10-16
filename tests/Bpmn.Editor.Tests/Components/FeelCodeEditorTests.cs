using Bunit;
using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Feel;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Components;

public sealed class FeelCodeEditorTests : BunitContext
{
    private const string ModulePath = "./_content/Hasseware.AspNetCore.Components.Bpmn.Feel/feel-editor.js";

    [Theory]
    [InlineData(BpmnFeelMode.Required)]
    [InlineData(BpmnFeelMode.Optional)]
    public void RegistrationReplacesFeelDefaultOnly(BpmnFeelMode mode)
    {
        Services.AddBpmnEditor().AddBpmnFeelEditor();
        var registry = Services.GetRequiredService<IOptions<BpmnOptions>>().Value.Properties.Editors;
        Assert.Equal(typeof(BpmnFeelCodeEditor), registry.Resolve(Entry(mode, "=x")));
        Assert.Equal(typeof(BpmnTextEntry), registry.Resolve(Entry(BpmnFeelMode.None, "x")));
        registry.AddEditor<BpmnNumberEntry>(entry => entry.Id == "value");
        Assert.Equal(typeof(BpmnNumberEntry), registry.Resolve(Entry(mode, "=x")));
    }

    [Fact]
    public void FallbackRendersWhileImportIsPending()
    {
        Services.AddSingleton<IJSRuntime>(new ImportRuntime(false));
        var editor = Render<BpmnFeelCodeEditor>(p => p.AddCascadingValue(StandardPropertyScope.Default).Add(c => c.Context, Context(Entry(BpmnFeelMode.Required, "=x"))));
        Assert.Equal("x", editor.Find("textarea").GetAttribute("value"));
        Assert.Equal("FEEL", editor.Find(".bpmn-properties__feel-badge").TextContent);
    }

    [Fact]
    public void ImportFailureKeepsFallbackEditable()
    {
        Unavailable();
        string? submitted = null;
        var editor = Render<BpmnFeelCodeEditor>(p => p.AddCascadingValue(StandardPropertyScope.Default).Add(c => c.Context,
            Context(Entry(BpmnFeelMode.Required, "=original"), value => submitted = value)));
        editor.Find("textarea").Input("x + 1");
        Assert.Null(submitted);
        editor.Find("textarea").Change("x + 1");
        Assert.Equal("=x + 1", submitted);
        editor.Find("textarea").Change("");
        Assert.Null(submitted);
    }

    [Theory]
    [InlineData(BpmnPropertyEntryKind.Text, "text")]
    [InlineData(BpmnPropertyEntryKind.Number, "number")]
    [InlineData(BpmnPropertyEntryKind.Checkbox, "checkbox")]
    public void OptionalLiteralDelegatesToKindEditor(BpmnPropertyEntryKind kind, string inputType)
    {
        var editor = Render<BpmnFeelCodeEditor>(p => p.AddCascadingValue(StandardPropertyScope.Default).Add(c => c.Context,
            Context(Entry(BpmnFeelMode.Optional, "3") with { Kind = kind })));
        Assert.Equal(inputType, editor.Find("input").GetAttribute("type"));
        Assert.Empty(JSInterop.Invocations);
    }

    [Fact]
    public void OptionalLiteralHonorsCascadedRegistry()
    {
        var registry = new BpmnPropertyEditorRegistry().AddEditor<BpmnNumberEntry>(BpmnPropertyEntryKind.Text);
        var editor = Render<BpmnPropertyScopeProvider>(p => p
            .Add(c => c.Editors, registry)
            .AddChildContent<BpmnFeelCodeEditor>(child => child.Add(c => c.Context, Context(Entry(BpmnFeelMode.Optional, "3")))));
        Assert.Equal("number", editor.Find("input").GetAttribute("type"));
    }

    [Fact]
    public void ToggleCommitsAndStripsOnlyPrefix()
    {
        Unavailable();
        var submitted = new List<string?>();
        var editor = Render<BpmnFeelCodeEditor>(p => p.AddCascadingValue(StandardPropertyScope.Default).Add(c => c.Context,
            Context(Entry(BpmnFeelMode.Optional, "x + 1"), submitted.Add)));
        editor.Find("button").Click();
        Assert.Equal("=x + 1", submitted[^1]);
        Assert.Equal("true", editor.Find("button").GetAttribute("aria-pressed"));
        Assert.Equal("x + 1", editor.Find("textarea").GetAttribute("value"));
        editor.Find("button").Click();
        Assert.Equal("x + 1", submitted[^1]);
        Assert.Equal("false", editor.Find("button").GetAttribute("aria-pressed"));
        Assert.Equal("x + 1", editor.Find("input").GetAttribute("value"));
    }

    [Fact]
    public void ReadOnlyShowsRawValueAndBlocksAllWrites()
    {
        var submitted = new List<string?>();
        var editor = Render<BpmnFeelCodeEditor>(p => p.AddCascadingValue(StandardPropertyScope.Default).Add(c => c.Context,
            Context(Entry(BpmnFeelMode.Optional, "=x"), submitted.Add) with { IsReadOnly = true }));
        Assert.Equal("=x", editor.Find("textarea").GetAttribute("value"));
        Assert.NotNull(editor.Find("textarea").GetAttribute("readonly"));
        Assert.NotNull(editor.Find("button").GetAttribute("disabled"));
        editor.Find("textarea").Change("y");
        editor.Find("button").Click();
        Assert.Empty(submitted);
        Assert.Empty(JSInterop.Invocations);
    }

    [Theory]
    [InlineData("revision")]
    [InlineData("entry")]
    [InlineData("element")]
    [InlineData("value")]
    public void ContextChangesResetFallbackState(string change)
    {
        Unavailable();
        var context = Context(Entry(BpmnFeelMode.Required, "=original"));
        var editor = Render<BpmnFeelCodeEditor>(p => p.AddCascadingValue(StandardPropertyScope.Default).Add(c => c.Context, context));
        editor.Find("textarea").Input("draft");
        context = change switch
        {
            "revision" => context with { Revision = 1 },
            "entry" => context with { Entry = context.Entry with { Id = "other" } },
            "element" => context with { ElementId = "Task_2" },
            _ => context with { Entry = context.Entry with { Value = "=new" } },
        };
        editor.Render(p => p.Add(c => c.Context, context));
        Assert.Equal(change == "value" ? "new" : "original", editor.Find("textarea").GetAttribute("value"));
    }

    [Fact]
    public void FallbackSupportsControlEnterAndServerErrors()
    {
        Unavailable();
        string? submitted = null;
        var editor = Render<BpmnFeelCodeEditor>(p => p.AddCascadingValue(StandardPropertyScope.Default).Add(c => c.Context,
            Context(Entry(BpmnFeelMode.Required, "=x") with { Error = "Server error" }, value => submitted = value)));
        Assert.Equal("Server error", editor.Find("[role=alert]").TextContent);
        Assert.Equal("true", editor.Find("textarea").GetAttribute("aria-invalid"));
        editor.Find("textarea").Input("y");
        editor.Find("textarea").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter", CtrlKey = true });
        Assert.Equal("=y", submitted);
    }

    [Fact]
    public async Task RichEditorSynchronizesRevisionsAndVariableReferences()
    {
        var handle = RichInterop();
        var context = Context(Entry(BpmnFeelMode.Required, "=x"));
        var editor = Render<BpmnFeelCodeEditor>(p => p.AddCascadingValue(StandardPropertyScope.Default).Add(c => c.Context, context));
        Assert.Empty(editor.FindAll("textarea"));
        Assert.Single(editor.FindAll(".bpmn-feel-code"));
        context = context with { Revision = 1, Variables = [new("customer") { Entries = [new("name")] }] };
        editor.Render(p => p.Add(c => c.Context, context));
        Assert.Equal("x", handle.Invocations["setValue"][^1].Arguments[0]);
        Assert.Equal(2, handle.Invocations["setValue"][^1].Arguments[1]);
        Assert.Same(context.Variables, handle.Invocations["setVariables"][^1].Arguments[0]);
        await editor.Instance.ReportLintAsync(["Unbalanced brackets"], 2);
        Assert.Contains("Unbalanced brackets", editor.Find("[role=status]").TextContent, StringComparison.Ordinal);
        Assert.Equal(editor.Find("[role=status]").Id, handle.Invocations["setAttributes"][^1].Arguments[1]);
    }

    [Fact]
    public async Task StaleRichCallbacksAreIgnoredAndDisposeIsGuarded()
    {
        var handle = RichInterop();
        handle.SetupVoid("dispose", _ => true).SetException(new JSDisconnectedException("Disconnected"));
        var submitted = new List<string?>();
        var context = Context(Entry(BpmnFeelMode.Required, "=x"), submitted.Add);
        var editor = Render<BpmnFeelCodeEditor>(p => p.AddCascadingValue(StandardPropertyScope.Default).Add(c => c.Context, context));
        editor.Render(p => p.Add(c => c.Context, context with { Revision = 1 }));
        await editor.Instance.CommitExpressionAsync("stale", 1);
        Assert.Empty(submitted);
        await editor.Instance.CommitExpressionAsync("fresh", 2);
        Assert.Equal("=fresh", Assert.Single(submitted));
        await editor.Instance.DisposeAsync();
        await editor.Instance.DisposeAsync();
    }

    [Fact]
    public async Task DisposeCancelsPendingModuleImport()
    {
        var runtime = new DeferredRuntime(cancelImport: true);
        Services.AddSingleton<IJSRuntime>(runtime);
        var editor = Render<BpmnFeelCodeEditor>(p => p.AddCascadingValue(StandardPropertyScope.Default).Add(c => c.Context,
            Context(Entry(BpmnFeelMode.Required, "=x"))));
        Assert.Equal(ModulePath, Assert.Single(runtime.Arguments!));
        Assert.True(runtime.Token.CanBeCanceled);
        await editor.Instance.DisposeAsync();
        Assert.True(runtime.Token.IsCancellationRequested);
        Assert.True(runtime.ImportTask.IsCanceled);
    }

    [Fact]
    public async Task LateModuleImportIsReleasedAfterDisposal()
    {
        var runtime = new DeferredRuntime();
        var module = new DeferredModule();
        Services.AddSingleton<IJSRuntime>(runtime);
        var editor = Render<BpmnFeelCodeEditor>(p => p.AddCascadingValue(StandardPropertyScope.Default).Add(c => c.Context,
            Context(Entry(BpmnFeelMode.Required, "=x"))));
        await editor.Instance.DisposeAsync();
        await editor.InvokeAsync(() => runtime.Import.SetResult(module));
        editor.WaitForAssertion(() => Assert.Equal(1, module.DisposeCount));
        Assert.False(module.CreateToken.CanBeCanceled);
        await editor.Instance.DisposeAsync();
        Assert.Equal(1, module.DisposeCount);
    }

    [Fact]
    public async Task LateEditorCreationIsReleasedAfterDisposal()
    {
        var runtime = new DeferredRuntime();
        var module = new DeferredModule();
        var handle = new DeferredModule();
        runtime.Import.SetResult(module);
        Services.AddSingleton<IJSRuntime>(runtime);
        var editor = Render<BpmnFeelCodeEditor>(p => p.AddCascadingValue(StandardPropertyScope.Default).Add(c => c.Context,
            Context(Entry(BpmnFeelMode.Required, "=x"))));
        Assert.True(module.CreateToken.CanBeCanceled);
        await editor.Instance.DisposeAsync();
        Assert.True(module.CreateToken.IsCancellationRequested);
        Assert.Equal(1, module.DisposeCount);
        await editor.InvokeAsync(() => module.Create.SetResult(handle));
        editor.WaitForAssertion(() => Assert.Equal(1, handle.DisposeCount));
        Assert.Equal(1, handle.TeardownCount);
    }

    private sealed class DeferredRuntime(bool cancelImport = false) : IJSRuntime
    {
        public TaskCompletionSource<IJSObjectReference> Import { get; } = new();
        public CancellationToken Token { get; private set; }
        public object?[]? Arguments { get; private set; }
        public Task<IJSObjectReference> ImportTask { get; private set; } = null!;

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken,
            object?[]? args)
        {
            Assert.Equal("import", identifier);
            Token = cancellationToken;
            Arguments = args;
            ImportTask = cancelImport ? Import.Task.WaitAsync(cancellationToken) : Import.Task;
            return (TValue)(object)await ImportTask;
        }
    }

    private sealed class DeferredModule : IJSObjectReference
    {
        public TaskCompletionSource<IJSObjectReference> Create { get; } = new();
        public CancellationToken CreateToken { get; private set; }
        public int DisposeCount { get; private set; }
        public int TeardownCount { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken,
            object?[]? args)
        {
            if (identifier == "create")
            {
                CreateToken = cancellationToken;
                return (TValue)(object)await Create.Task;
            }
            if (identifier == "dispose") TeardownCount++;
            return default!;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }

    private void Unavailable() => Services.AddSingleton<IJSRuntime>(new ImportRuntime(true));

    private sealed class ImportRuntime(bool fail) : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => fail
            ? ValueTask.FromException<TValue>(new JSException("Module unavailable"))
            : new ValueTask<TValue>(new TaskCompletionSource<TValue>().Task);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken,
            object?[]? args) => InvokeAsync<TValue>(identifier, args);
    }

    private BunitJSModuleInterop RichInterop()
    {
        var module = JSInterop.SetupModule(ModulePath);
        var handle = module.SetupModule("create", _ => true);
        foreach (var name in new[] { "setValue", "setVariables", "setReadOnly", "setAttributes", "dispose" })
            handle.SetupVoid(name, _ => true).SetVoidResult();
        return handle;
    }

    private static BpmnPropertyEntry Entry(BpmnFeelMode mode, string? value) => new("value", "Value", BpmnPropertyEntryKind.Text, value) { Feel = mode };
    private BpmnPropertyEntryContext Context(BpmnPropertyEntry entry, Action<string?>? changed = null) => new(entry,
        EventCallback.Factory.Create<string?>(this, value => changed?.Invoke(value)), default, false, 0)
    { ElementId = "Task_1" };
}
