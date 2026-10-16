using Bunit;
using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Interop;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Components;

public sealed class BpmnEditorScopeTests : BunitContext
{
    public BpmnEditorScopeTests() => Services.AddBpmnComponents();

    [Fact]
    public void ScopeAddsNoDom()
    {
        var scope = Render<BpmnEditorScope>();
        Assert.Empty(scope.Markup.Trim());
        Assert.NotNull(scope.Instance.Session);
    }

    [Fact]
    public async Task SessionIsFixedWhileStateCascadeRefreshes()
    {
        var scope = Render<BpmnEditorScope>(parameters => parameters.AddChildContent<StateProbe>());
        var probe = scope.FindComponent<StateProbe>();
        var session = probe.Instance.Session;
        var before = probe.Instance.State;
        await scope.InvokeAsync(session.ShowShortcutHelp);
        scope.WaitForAssertion(() => Assert.True(probe.Instance.State.ShortcutHelpVisible));
        Assert.Same(session, probe.Instance.Session);
        Assert.Same(scope.Instance.Session.State, probe.Instance.State);
        Assert.NotSame(before, probe.Instance.State);
        Assert.False(before.ShortcutHelpVisible);
    }

    [Fact]
    public void CanvasRequiresScope()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Render<BpmnEditorCanvas>());
        Assert.Contains("BpmnEditorScope", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ScopeResolvesOptionsAndExplicitOverrides()
    {
        Services.Configure<BpmnOptions>(options => options.Grid = options.Grid with { IsVisible = false });
        var scope = Render<BpmnEditorScope>(parameters => parameters.Add(p => p.IsReadOnly, true));
        Assert.True(scope.Instance.Session.State.IsReadOnly);
        Assert.True(scope.Instance.Session.State.Configuration.IsReadOnly);
        Assert.False(scope.Instance.Session.State.Configuration.Grid.IsVisible);
        var revision = scope.Instance.Session.State.Configuration.Revision;
        scope.Render();
        Assert.Equal(revision, scope.Instance.Session.State.Configuration.Revision);
        scope.Render(parameters => parameters.Add(p => p.Configuration,
            EditorConfig.CreateDefault() with { Grid = GridOptions.Default with { IsVisible = true } }));
        Assert.True(scope.Instance.Session.State.Configuration.Grid.IsVisible);
        Assert.True(scope.Instance.Session.State.Configuration.Revision > revision);
    }

    [Fact]
    public async Task CanvasOverlaysConsumeNewStateInsideHost()
    {
        Services.Configure<BpmnOptions>(options => options.Components.Use<MenuProbe>(BpmnEditorPart.ContextMenu));
        JSInterop.Mode = JSRuntimeMode.Loose;
        var module = JSInterop.SetupModule(BpmnInteropContract.ModulePath);
        var canvas = module.SetupModule("initialize", _ => true);
        canvas.Setup<CanvasInitResult>("getInitResult", _ => true)
            .SetResult(new CanvasInitResult("instance", BpmnInteropContract.Version, 1, true, null));
        var scope = Render<BpmnEditorScope>(parameters => parameters.AddChildContent(builder =>
        {
            builder.OpenComponent<BpmnEditorCanvas>(0);
            builder.AddAttribute(1, nameof(BpmnEditorCanvas.ChildContent), (RenderFragment)(overlay =>
                overlay.AddMarkupContent(0, "<span class='custom-overlay'>Overlay</span>")));
            builder.CloseComponent();
        }));
        Assert.NotNull(scope.Find(".bpmn-canvas .custom-overlay"));
        Assert.Empty(scope.FindAll(".bpmn-menu"));
        var bounds = new Bounds(0, 0, 1, 1);
        await scope.InvokeAsync(() => scope.Instance.Session.HandleContextMenuRequestedAsync(
            new ContextMenuRequested("canvas", null, new Anchor(bounds, bounds), "pointer", [])));
        scope.WaitForAssertion(() => Assert.Single(scope.FindAll(".bpmn-canvas .bpmn-menu")));
        await scope.InvokeAsync(scope.Instance.Session.CloseContextMenu);
        scope.WaitForAssertion(() => Assert.Empty(scope.FindAll(".bpmn-menu")));
    }

    [Fact]
    public void CanvasCascadesItsSurfaceToOverlays()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var module = JSInterop.SetupModule(BpmnInteropContract.ModulePath);
        module.SetupModule("initialize", _ => true)
            .Setup<CanvasInitResult>("getInitResult", _ => true)
            .SetResult(new CanvasInitResult("instance", BpmnInteropContract.Version, 1, true, null));
        var canvas = Render<BpmnCanvas>(parameters => parameters
            .Add(p => p.DiagramId, "Diagram_1")
            .AddChildContent<SurfaceProbe>());
        var surface = canvas.FindComponent<SurfaceProbe>().Instance.Surface;
        Assert.NotNull(surface);
        Assert.Equal("Diagram_1", surface.DiagramId);
        Assert.Null(Render<SurfaceProbe>().Instance.Surface);
    }

    [Fact]
    public void PropertyScopeProviderInheritsWhatItDoesNotSet()
    {
        var registry = new BpmnPropertyEditorRegistry();
        var outer = Render<BpmnPropertyScopeProvider>(parameters => parameters
            .Add(p => p.Block, "outer")
            .Add(p => p.Editors, registry)
            .Add(p => p.IsReadOnly, true)
            .Add(p => p.Revision, 4)
            .AddChildContent<BpmnPropertyScopeProvider>(inner => inner
                .Add(p => p.MultilineRows, 7)
                .Add(p => p.IsReadOnly, false)
                .AddChildContent<ScopeProbe>()));
        var scope = outer.FindComponent<ScopeProbe>().Instance.Scope!;
        Assert.Equal("outer", scope.Block);
        Assert.Equal(7, scope.MultilineRows);
        Assert.Same(registry, scope.Editors);
        Assert.True(scope.IsReadOnly);
        Assert.Equal(4, scope.Revision);
        Assert.Null(scope.Document);
    }

    [Fact]
    public void PropertyScopeProviderDefaultsWithoutAParent()
    {
        var scope = Render<BpmnPropertyScopeProvider>(parameters => parameters.AddChildContent<ScopeProbe>())
            .FindComponent<ScopeProbe>().Instance.Scope!;
        Assert.Equal(BpmnPropertyScope.DefaultBlock, scope.Block);
        Assert.Equal(BpmnPropertyScope.DefaultMultilineRows, scope.MultilineRows);
        Assert.False(scope.IsReadOnly);
    }

    public sealed class SurfaceProbe : ComponentBase
    {
        [CascadingParameter] public BpmnCanvasSurface? Surface { get; set; }
    }

    public sealed class ScopeProbe : ComponentBase
    {
        [CascadingParameter] public BpmnPropertyScope? Scope { get; set; }
    }

    public sealed class StateProbe : ComponentBase
    {
        [CascadingParameter] public BpmnEditorSession Session { get; set; } = default!;
        [CascadingParameter] public BpmnEditorState State { get; set; } = default!;
        protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, State.ShortcutHelpVisible);
    }
    private sealed class MenuProbe : BpmnContextMenuBase
    {
        protected override bool FocusOnOpen => false;
        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
        {
            if (IsOpen) builder.AddMarkupContent(0, "<div class='bpmn-menu'></div>");
        }
    }

}
