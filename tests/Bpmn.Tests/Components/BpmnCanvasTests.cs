using Bunit;

using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Interop;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Resources;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Components;

/// <summary>
/// Canvas lifecycle, asserted through bUnit's strict JS interop.
/// </summary>
/// <remarks>
/// Strict mode means an unplanned interop call fails the test, so these also prove the canvas
/// makes no calls beyond the ones described here.
/// </remarks>
public sealed class BpmnCanvasTests : BunitContext
{
    private const string InstanceId = "instance-1";

    /// <summary>
    /// The canvas module, on which every interop call after the import is made.
    /// </summary>
    /// <remarks>
    /// Resolved once: each SetupModule call returns a fresh interop, so re-resolving it would
    /// discard the setups registered against the previous one.
    /// </remarks>
    private readonly BunitJSModuleInterop _module;

    /// <summary>The canvas <c>initialize</c> hands back, on which every later call is made.</summary>
    private BunitJSModuleInterop _canvas = null!;

    public BpmnCanvasTests()
    {
        JSInterop.Mode = JSRuntimeMode.Strict;
        Services.AddOptions<BpmnOptions>();
        Services.AddLocalization();
        _module = JSInterop.SetupModule(BpmnInteropContract.ModulePath);
    }

    [Fact]
    public void ImportsTheModuleFromTheContractPath()
    {
        SetupInitialize(Accepted());

        Render<BpmnCanvas>();

        // The path is part of the contract: a consuming app's stale copy of the static web
        // assets is otherwise indistinguishable from the component simply not working.
        var import = Assert.Single(JSInterop.Invocations["import"]);
        Assert.Equal(BpmnInteropContract.ModulePath, import.Arguments[0]);
    }

    [Fact]
    public void PassesADotNetObjectReferenceToTheEventBridge()
    {
        SetupInitialize(Accepted());

        Render<BpmnCanvas>();

        /*
         * Five: the canvas root, the element JointJS mounts into, the document, the configuration
         * and the bridge. The first two are separate because Blazor and JointJS cannot both manage
         * one element's children.
         */
        var arguments = JSInterop.Invocations["initialize"].Single().Arguments;
        Assert.Equal(5, arguments.Count);
        Assert.IsType<DotNetObjectReference<BpmnCanvasEventBridge>>(arguments[4]);
    }

    [Fact]
    public void PassesResourceBackedDisplayStringsWithoutChangingHostConfiguration()
    {
        SetupInitialize(Accepted());
        var configuration = EditorConfig.CreateDefault();

        Render<BpmnCanvas>(parameters => parameters.Add(p => p.Configuration, configuration));

        var applied = Assert.IsType<EditorConfig>(JSInterop.Invocations["initialize"].Single().Arguments[3]);
        Assert.NotNull(applied.Strings);
        Assert.Equal("Expand sub-process {0}", applied.Strings.Values["ExpandNamed"]);
        Assert.Equal("User Task", applied.Strings.Values["Type.userTask"]);
        Assert.Equal("Source must not be an End Event.", applied.Strings.Values["Rule.SF-002"]);
        Assert.Null(configuration.Strings);
    }

    [Fact]
    public void CustomLocalizerControlsHostAndScriptTextWithoutEnumeratingTranslations()
    {
        SetupInitialize(Accepted());
        Services.AddSingleton<IStringLocalizer<BpmnCanvasStrings>, TranslatedCanvasStrings>();

        var component = Render<BpmnCanvas>();

        Assert.Equal("localized:AriaLabel", component.Find("div.bpmn-canvas").GetAttribute("aria-label"));
        var configuration = Assert.IsType<EditorConfig>(JSInterop.Invocations["initialize"].Single().Arguments[3]);
        Assert.NotNull(configuration.Strings);
        Assert.Equal("localized:ExpandNamed", configuration.Strings.Values["ExpandNamed"]);
        Assert.Equal("localized:Type.userTask", configuration.Strings.Values["Type.userTask"]);
        Assert.Equal("localized:Rule.SF-002", configuration.Strings.Values["Rule.SF-002"]);
    }

    [Fact]
    public void ThrowsWhenTheModuleImplementsADifferentContractVersion()
    {
        SetupInitialize(new CanvasInitResult(InstanceId, "0.9", 1, true, null));

        // A version skew must fail loudly at initialisation; its later symptoms look like data
        // bugs and cost far more to diagnose.
        var exception = Assert.Throws<BpmnInteropVersionException>(() => Render<BpmnCanvas>());

        Assert.Equal(BpmnInteropContract.Version, exception.Expected);
        Assert.Equal("0.9", exception.Actual);
    }

    [Fact]
    public async Task DisposesTheInstanceAndTheModule()
    {
        SetupInitialize(Accepted());
        // dispose is called on the canvas reference, not on the module or the runtime root.
        var dispose = _canvas.SetupVoid("dispose", _ => true);
        dispose.SetVoidResult();

        var component = Render<BpmnCanvas>();
        await component.Instance.DisposeAsync();

        Assert.Single(dispose.Invocations);
    }

    [Fact]
    public async Task SwallowsADisconnectedCircuitDuringDisposal()
    {
        SetupInitialize(Accepted());
        _canvas.SetupVoid("dispose", _ => true).SetException(new JSDisconnectedException("circuit gone"));

        var component = Render<BpmnCanvas>();

        // Navigating away from a Blazor Server page routinely disposes a component whose circuit
        // has already dropped; letting that surface would log an exception on every navigation.
        await component.Instance.DisposeAsync();
    }

    [Fact]
    public void ReportsARejectedConfigurationWithoutImportingTheModule()
    {
        ConfigRejected? rejection = null;

        var invalid = EditorConfig.CreateDefault() with { Grid = new GridOptions(0d, true, false) };

        Render<BpmnCanvas>(parameters => parameters
            .Add(p => p.Configuration, invalid)
            .Add(p => p.OnConfigRejected, payload => rejection = payload));

        // Validated before the boundary, so an invalid snapshot never even loads the bundle.
        Assert.NotNull(rejection);
        Assert.Equal("CFG-010", rejection.Code);
    }

    [Fact]
    public void IsNotInitializedWhenTheModuleRefusesTheConfiguration()
    {
        SetupInitialize(new CanvasInitResult(
            string.Empty,
            BpmnInteropContract.Version,
            1,
            false,
            new ConfigRejected(1, "CFG-020", "viewport.minZoom", "rejected")));

        var component = Render<BpmnCanvas>();

        Assert.False(component.Instance.IsInitialized);
    }

    [Fact]
    public void RendersAnAccessibleCanvasHost()
    {
        SetupInitialize(Accepted());

        var component = Render<BpmnCanvas>(parameters => parameters
            .Add(p => p.AriaLabel, "Order process"));

        var host = component.Find("div.bpmn-canvas");
        Assert.Equal("application", host.GetAttribute("role"));
        Assert.Equal("Order process", host.GetAttribute("aria-label"));
        Assert.Equal("0", host.GetAttribute("tabindex"));
    }

    [Fact]
    public void UsesResourceBackedDefaultCanvasName()
    {
        SetupInitialize(Accepted());

        var component = Render<BpmnCanvas>();

        Assert.Equal("BPMN diagram canvas", component.Find("div.bpmn-canvas").GetAttribute("aria-label"));
    }

    [Fact]
    public void RendersADocumentPresentAtStartupThroughInitializeAlone()
    {
        SetupInitialize(Accepted());
        var load = SetupLoadDocument();

        Render<BpmnCanvas>(parameters => parameters.Add(p => p.Document, Document()));

        // The document is part of the initialize call precisely so the canvas is never briefly
        // visible and empty; a second load would redraw what is already on screen.
        Assert.NotNull(JSInterop.Invocations["initialize"].Single().Arguments[2]);
        Assert.Empty(load.Invocations);
    }

    [Fact]
    public void DoesNotReloadTheDocumentOnEveryRender()
    {
        SetupInitialize(Accepted());
        var load = SetupLoadDocument();

        var component = Render<BpmnCanvas>(parameters => parameters.Add(p => p.Document, Document()));

        // Every re-render reaches OnAfterRenderAsync, and the host re-renders whenever it
        // reacts to a canvas event. The module resolves a null DiagramId to the first diagram,
        // so comparing against that resolved value rather than the request would make each
        // render trigger another load — a loop that ends in a crashed browser tab.
        component.Render();
        component.Render();

        Assert.Empty(load.Invocations);
    }

    private static CanvasInitResult Accepted() =>
        new(InstanceId, BpmnInteropContract.Version, 1, true, null);

    private void SetupInitialize(CanvasInitResult result)
    {
        _canvas = _module.SetupModule("initialize", _ => true);
        _canvas.Setup<CanvasInitResult>("getInitResult", _ => true).SetResult(result);
    }

    private JSRuntimeInvocationHandler<DocumentLoaded?> SetupLoadDocument() =>
        _canvas.Setup<DocumentLoaded?>("loadDocument", _ => true)
            .SetResult(new DocumentLoaded("Diagram_1", 1, 0, []));

    private static BpmnDocument Document() => new()
    {
        Definitions = new BpmnDefinitions("Definitions_1", "http://example.test/bpmn"),
    };

    private sealed class TranslatedCanvasStrings : IStringLocalizer<BpmnCanvasStrings>
    {
        public LocalizedString this[string name] => new(name, $"localized:{name}");

        public LocalizedString this[string name, params object[] arguments] => this[name];

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
