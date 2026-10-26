using Bunit;
using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Interop;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Components;

public sealed class ComponentRegistryTests : BunitContext
{
    public ComponentRegistryTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var module = JSInterop.SetupModule(BpmnInteropContract.ModulePath);
        module.Mode = JSRuntimeMode.Loose;
        var canvas = module.SetupModule("initialize", _ => true);
        canvas.Mode = JSRuntimeMode.Loose;
        canvas.Setup<CanvasInitResult>("getInitResult", _ => true).SetResult(new("instance-1", BpmnInteropContract.Version, 1, true, null));
    }

    [Fact]
    public void UseOverridesDefaultInEitherOrder()
    {
        var first = new BpmnEditorComponentOptions();
        first.UseDefault<BpmnToolbar>(BpmnEditorPart.Toolbar).Use<CustomToolbar>(BpmnEditorPart.Toolbar);
        var second = new BpmnEditorComponentOptions();
        second.Use<CustomToolbar>(BpmnEditorPart.Toolbar).UseDefault<BpmnToolbar>(BpmnEditorPart.Toolbar);
        Assert.Equal(typeof(CustomToolbar), first.Get(BpmnEditorPart.Toolbar));
        Assert.Equal(typeof(CustomToolbar), second.Get(BpmnEditorPart.Toolbar));
    }

    [Fact]
    public void FirstDefaultWinsAndUnsetIsNull()
    {
        var options = new BpmnEditorComponentOptions();
        Assert.Null(options.Get(BpmnEditorPart.Toolbar));
        options.UseDefault<CustomToolbar>(BpmnEditorPart.Toolbar).UseDefault<BpmnToolbar>(BpmnEditorPart.Toolbar);
        Assert.Equal(typeof(CustomToolbar), options.Get(BpmnEditorPart.Toolbar));
    }

    [Fact]
    public void RemoveSurvivesLaterDefaultsAndUseRevivesIt()
    {
        var options = new BpmnEditorComponentOptions();
        options.Use<CustomToolbar>(BpmnEditorPart.Toolbar).Remove(BpmnEditorPart.Toolbar).UseDefault<BpmnToolbar>(BpmnEditorPart.Toolbar);
        Assert.Null(options.Get(BpmnEditorPart.Toolbar));
        options.Use<CustomToolbar>(BpmnEditorPart.Toolbar);
        Assert.Equal(typeof(CustomToolbar), options.Get(BpmnEditorPart.Toolbar));
    }

    [Fact]
    public void WrongBaseOrAbstractTypeThrows()
    {
        var options = new BpmnEditorComponentOptions();
        Assert.Throws<ArgumentException>(() => options.Use<BpmnToolbar>(BpmnEditorPart.ContextMenu));
        Assert.Throws<ArgumentException>(() => options.UseDefault<BpmnToolbar>(BpmnEditorPart.Stencil));
        Assert.Throws<ArgumentException>(() => options.Use<BpmnContextMenuBase>(BpmnEditorPart.ContextMenu));
        Assert.Throws<ArgumentException>(() => options.Use<Plain>(BpmnEditorPart.Toolbar));
    }

    [Fact]
    public void EveryPartHasARequiredBaseThatIsAnEditorComponent()
    {
        foreach (var part in Enum.GetValues<BpmnEditorPart>())
            Assert.True(typeof(BpmnEditorComponentBase).IsAssignableFrom(BpmnEditorComponentOptions.RequiredBase(part)), part.ToString());
    }

    [Fact]
    public void CoreOwnsLabelEditorAndAccessibleTreeDefaults()
    {
        var options = new BpmnOptions();
        Assert.Equal(typeof(BpmnLabelEditor), options.Components.Get(BpmnEditorPart.LabelEditor));
        Assert.Equal(typeof(BpmnAccessibleTree), options.Components.Get(BpmnEditorPart.AccessibleTree));
        Assert.Null(options.Components.Get(BpmnEditorPart.ContextMenu));
    }

    [Fact]
    public void AddBpmnEditorRegistersDefaultsWithoutOverridingHostInEitherOrder()
    {
        var before = new ServiceCollection();
        before.Configure<BpmnOptions>(o => o.Components.Use<CustomMenu>(BpmnEditorPart.ContextMenu));
        before.AddBpmnEditor();
        var after = new ServiceCollection();
        after.AddBpmnEditor();
        after.Configure<BpmnOptions>(o => o.Components.Use<CustomMenu>(BpmnEditorPart.ContextMenu));
        foreach (var services in new[] { before, after })
        {
            var options = services.BuildServiceProvider().GetRequiredService<Microsoft.Extensions.Options.IOptions<BpmnOptions>>().Value;
            Assert.Equal(typeof(CustomMenu), options.Components.Get(BpmnEditorPart.ContextMenu));
            Assert.Equal(typeof(BpmnStencilPanel), options.Components.Get(BpmnEditorPart.Stencil));
        }
    }

    [Fact]
    public void PartRendersConfiguredComponent()
    {
        Services.AddBpmnEditor(o => o.Components.Use<CustomToolbar>(BpmnEditorPart.Toolbar));
        var cut = Render<BpmnEditorScope>(p => p.AddChildContent<BpmnPart>(part => part.Add(x => x.Part, BpmnEditorPart.Toolbar)));
        Assert.Contains("custom-toolbar", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void PartRendersNothingWhenUnsetAndFallbackWhenGiven()
    {
        Services.AddBpmnComponents();
        var empty = Render<BpmnEditorScope>(p => p.AddChildContent<BpmnPart>(part => part.Add(x => x.Part, BpmnEditorPart.Toolbar)));
        Assert.Empty(empty.Markup.Trim());
        var fallback = Render<BpmnEditorScope>(p => p.AddChildContent<BpmnPart>(part => part
            .Add(x => x.Part, BpmnEditorPart.Toolbar)
            .Add(x => x.Fallback, (RenderFragment)(b => b.AddMarkupContent(0, "<i>none</i>")))));
        Assert.Contains("<i>none</i>", fallback.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void PartRequiresScope()
    {
        Services.AddBpmnEditor();
        var error = Assert.Throws<InvalidOperationException>(() => Render<BpmnPart>(p => p.Add(x => x.Part, BpmnEditorPart.Toolbar)));
        Assert.Contains("BpmnEditorScope", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SlotOutranksRegistry()
    {
        Services.AddBpmnEditor(o => o.Components.Use<CustomToolbar>(BpmnEditorPart.Toolbar));
        var cut = Render<BpmnEditor>(p => p.Add(x => x.ToolbarContent, (RenderFragment)(b => b.AddMarkupContent(0, "<b id=\"slot\">slot</b>"))));
        Assert.Contains("id=\"slot\"", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("custom-toolbar", cut.Markup, StringComparison.Ordinal);
        var registryOnly = Render<BpmnEditor>();
        Assert.Contains("custom-toolbar", registryOnly.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void HostContextMenuOverrideRendersInCanvas()
    {
        Services.AddBpmnEditor(o => o.Components.Use<CustomMenu>(BpmnEditorPart.ContextMenu));
        var cut = Render<BpmnEditor>();
        Assert.Single(cut.FindComponents<CustomMenu>());
        Assert.Empty(cut.FindComponents<BpmnContextMenu>());
        Assert.NotNull(cut.Find(".bpmn-editor__canvas #custom-menu"));
    }

    [Fact]
    public void CanvasOverlaysAndDialogsSlotsRender()
    {
        Services.AddBpmnEditor();
        var cut = Render<BpmnEditor>(p => p
            .Add(x => x.CanvasOverlays, (RenderFragment)(b => b.AddMarkupContent(0, "<i id=\"overlay\"></i>")))
            .Add(x => x.Dialogs, (RenderFragment)(b => b.AddMarkupContent(0, "<i id=\"dialog\"></i>"))));
        Assert.NotNull(cut.Find(".bpmn-editor__canvas #overlay"));
        Assert.NotNull(cut.Find("#dialog"));
    }

    [Fact]
    public void FeelEditorStillResolvesAndHostOverridesWin()
    {
        var options = new BpmnOptions();
        BpmnStandardDefaults.Register(options);
        options.Properties.Editors.AddFeelEditor<BpmnTextAreaEntry>();
        var entry = new BpmnPropertyEntry("x", "x", BpmnPropertyEntryKind.Text) { Feel = BpmnFeelMode.Optional };
        Assert.Equal(typeof(BpmnTextAreaEntry), options.Properties.Editors.Resolve(entry));
        Assert.Equal(typeof(BpmnTextEntry), options.Properties.Editors.Resolve(entry with { Feel = BpmnFeelMode.None }));
    }

    [Fact]
    public void EmptyRegistryFallsBackToReadOnlyEntry()
    {
        var entry = new BpmnPropertyEntry("x", "x", BpmnPropertyEntryKind.Text);
        Assert.Equal(typeof(BpmnReadOnlyEntry), new BpmnPropertyEditorRegistry().Resolve(entry));
    }

    [Fact]
    public void StandardRegistrationProvidesEveryPartAndCoreRegistrationOnlyIntrinsicParts()
    {
        var coreServices = new ServiceCollection().AddBpmnComponents();
        var standardServices = new ServiceCollection().AddBpmnEditor();
        var core = coreServices.BuildServiceProvider().GetRequiredService<Microsoft.Extensions.Options.IOptions<BpmnOptions>>().Value;
        var standard = standardServices.BuildServiceProvider().GetRequiredService<Microsoft.Extensions.Options.IOptions<BpmnOptions>>().Value;
        foreach (var part in Enum.GetValues<BpmnEditorPart>())
        {
            Assert.NotNull(standard.Components.Get(part));
            if (part is not BpmnEditorPart.LabelEditor and not BpmnEditorPart.AccessibleTree)
                Assert.Null(core.Components.Get(part));
        }
    }

    [Fact]
    public void StandardToolbarAndPanelRenderWithoutSlots()
    {
        Services.AddBpmnEditor();
        var editor = Render<BpmnEditor>();
        Assert.Single(editor.FindComponents<BpmnToolbar>());
        Assert.Single(editor.FindComponents<BpmnPropertyPanel>());
        Assert.Equal(8, editor.FindAll(".bpmn-toolbar button[type=button]").Count);
        Assert.Equal("Zoom out", editor.Find(".bpmn-toolbar button").GetAttribute("aria-label"));
    }

    private sealed class Plain : ComponentBase;

    private sealed class CustomToolbar : BpmnToolbarBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddMarkupContent(0, "<div class=\"custom-toolbar\"></div>");
    }

    private sealed class CustomMenu : BpmnContextMenuBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddMarkupContent(0, "<div id=\"custom-menu\"></div>");
    }
}
