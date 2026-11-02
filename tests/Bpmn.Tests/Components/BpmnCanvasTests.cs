using Bunit;

using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Interop;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Resources;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
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

    [Fact]
    public async Task SendsOneLoadWhenTheCanvasIsRenderedAgainWhileALoadIsStillInFlight()
    {
        SetupInitialize(Accepted());
        var component = Render<BpmnCanvas>(parameters => parameters.Add(p => p.Document, Document()));
        var load = _canvas.Setup<DocumentLoaded?>("loadDocument", _ => true);
        var changed = Document();

        component.Render(parameters => parameters.Add(p => p.Document, changed));
        component.Render();
        component.Render();
        load.SetResult(new DocumentLoaded("Diagram_1", 1, 0, []));
        await component.WaitForStateAsync(() => load.Invocations.Count >= 1);
        await Task.Delay(100, Xunit.TestContext.Current.CancellationToken);

        // Every render reaches OnAfterRenderAsync, and each saw a document that was not yet recorded as drawn: all sent it.
        Assert.Single(load.Invocations);
    }

    [Fact]
    public async Task LoadsTheDocumentItWasGivenWhileTheCanvasWasStillStarting()
    {
        var instance = _module.SetupModule("initialize", _ => true);
        _canvas = instance;
        var initResult = _canvas.Setup<CanvasInitResult>("getInitResult", _ => true);
        var load = _canvas.Setup<DocumentLoaded?>("loadDocument", _ => true).SetResult(new DocumentLoaded("Diagram_1", 1, 0, []));

        var component = Render<BpmnCanvas>(parameters => parameters.Add(p => p.Document, Document()));
        var changed = Document();
        component.Render(parameters => parameters.Add(p => p.Document, changed));
        initResult.SetResult(Accepted());
        await component.WaitForStateAsync(() => load.Invocations.Count >= 1, TimeSpan.FromSeconds(5));

        // The canvas started with the first document; it must not mark the second as drawn just because that was the parameter when it finished.
        var sent = Assert.Single(load.Invocations);
        Assert.NotNull(sent.Arguments[0]);
    }

    [Fact]
    public async Task ReleasesTheCanvasItWasStartingWhenTheComponentIsDisposedFirst()
    {
        var instance = _module.SetupModule("initialize", _ => true);
        _canvas = instance;
        var initResult = _canvas.Setup<CanvasInitResult>("getInitResult", _ => true);
        var dispose = _canvas.SetupVoid("dispose", _ => true).SetVoidResult();
        var component = Render<BpmnCanvas>();

        await component.Instance.DisposeAsync();
        initResult.SetResult(Accepted());
        await Task.Delay(300, Xunit.TestContext.Current.CancellationToken);

        // initialize had returned a canvas, with its window listeners and paper, and nothing else would ever dispose it.
        Assert.Single(dispose.Invocations);
    }

    [Fact]
    public async Task ResyncDrawsTheCurrentDocumentAgainEvenThoughItWasAcceptedAsDrawn()
    {
        SetupInitialize(Accepted());
        var load = SetupLoadDocument();
        var document = Document();
        var component = Render<BpmnCanvas>(parameters => parameters.Add(p => p.Document, document));

        // An optimistic move marks the document as drawn; a refusal then has to undo what the canvas did ahead of it.
        component.Instance.AcceptRenderedDocument(document);
        await component.InvokeAsync(component.Instance.ResyncAsync);

        Assert.Single(load.Invocations);
    }

    [Fact]
    public void InitializesAnExplicitDiagramThroughTheAuthoritativeLoadPath()
    {
        SetupInitialize(Accepted());
        var load = SetupLoadDocument();

        Render<BpmnCanvas>(parameters => parameters
            .Add(p => p.Document, DiagramDocument())
            .Add(p => p.DiagramId, "Diagram_1"));

        Assert.Equal("Diagram_1", Assert.Single(load.Invocations).Arguments[1]);
    }

    [Fact]
    public async Task PureDeletionRetainsTheCompactFastPathAndAcknowledgesItsRevision()
    {
        SetupInitialize(Accepted());
        var delete = _canvas.Setup<bool>("deleteElements", _ => true).SetResult(true);
        var before = DiagramDocument();
        var diagram = before.Diagrams[0];
        var after = before with { FlowNodes = [], Diagrams = [diagram with { Plane = diagram.Plane with { Shapes = [] } }] };
        var component = Render<BpmnCanvas>(parameters => parameters.Add(p => p.Document, before));

        var applied = await component.Instance.DeleteElementsAsync(before, after, ["Task_1"]);
        component.Render(parameters => parameters.Add(p => p.Document, after));

        Assert.True(applied);
        Assert.Equal(1L, Assert.Single(delete.Invocations).Arguments[1]);
        Assert.Empty(JSInterop.Invocations["patchDocument"]);
        Assert.Empty(JSInterop.Invocations["loadDocument"]);
    }

    [Fact]
    public async Task DeletionWithSurvivorGeometryUsesTheCompletePatchInsteadOfPruning()
    {
        SetupInitialize(Accepted());
        var patch = _canvas.Setup<DocumentLoaded?>("patchDocument", _ => true)
            .SetResult(new DocumentLoaded("Diagram_1", 1, 0, []));
        var original = DiagramDocument();
        var diagram = original.Diagrams[0];
        var survivor = new BpmnFlowNode("Task_2", BpmnFlowNodeType.Task, "Process_1");
        var survivorShape = new BpmnShape("Task_2_di", "Task_2", new Bounds(200, 0, 100, 80));
        var before = original with
        {
            FlowNodes = [.. original.FlowNodes, survivor],
            Diagrams = [diagram with { Plane = diagram.Plane with { Shapes = [.. diagram.Plane.Shapes, survivorShape] } }],
        };
        var after = before with
        {
            FlowNodes = [survivor],
            Diagrams = [diagram with { Plane = diagram.Plane with { Shapes = [survivorShape with { Bounds = survivorShape.Bounds with { X = 300 } }] } }],
        };
        var component = Render<BpmnCanvas>(parameters => parameters.Add(p => p.Document, before));

        Assert.True(await component.Instance.DeleteElementsAsync(before, after, ["Task_1"]));

        Assert.Single(patch.Invocations);
        Assert.Empty(JSInterop.Invocations["deleteElements"]);
    }

    [Fact]
    public async Task InFlightPatchCannotAdvanceTheBaseOfANewerEditPrematurely()
    {
        SetupInitialize(Accepted());
        var patch = _canvas.Setup<DocumentLoaded?>("patchDocument", _ => true);
        var before = DiagramDocument();
        var component = Render<BpmnCanvas>(parameters => parameters.Add(p => p.Document, before));
        var first = before with { FlowNodes = [before.FlowNodes[0] with { Name = "First" }] };
        var second = before with { FlowNodes = [before.FlowNodes[0] with { Name = "Second" }] };
        component.Render(parameters => parameters.Add(p => p.Document, first));
        component.Render(parameters => parameters.Add(p => p.Document, second));

        patch.SetResult(new DocumentLoaded("Diagram_1", 1, 0, []));
        await component.WaitForStateAsync(() => patch.Invocations.Count == 2);

        var requests = patch.Invocations.Select(call => Assert.IsType<CanvasDocumentPatch>(call.Arguments[0])).ToArray();
        Assert.Equal(0, requests[0].BaseRevision);
        Assert.Equal(1, requests[1].BaseRevision);
        Assert.Equal("Second", Assert.Single(Assert.Single(requests[1].Collections).Delta.Upserts).GetProperty("name").GetString());
    }

    [Fact]
    public void SendsProjectedPatchesFromTheLastAcknowledgedResidentDocument()
    {
        SetupInitialize(Accepted());
        var patchCall = _canvas.Setup<DocumentLoaded?>("patchDocument", _ => true)
            .SetResult(new DocumentLoaded("Diagram_1", 1, 0, []));
        var document = DiagramDocument();
        var component = Render<BpmnCanvas>(parameters => parameters.Add(p => p.Document, document));
        var moved = document with { FlowNodes = [document.FlowNodes[0] with { Name = "Moved" }] };
        component.Instance.AcceptRenderedDocument(moved);

        component.Render(parameters => parameters.Add(p => p.Document, moved));
        component.Render();

        var first = Assert.IsType<CanvasDocumentPatch>(Assert.Single(patchCall.Invocations).Arguments[0]);
        Assert.Equal(0, first.BaseRevision);
        Assert.Equal(1, first.Revision);
        Assert.Equal("Moved", Assert.Single(Assert.Single(first.Collections).Delta.Upserts).GetProperty("name").GetString());
        var renamed = moved with { FlowNodes = [moved.FlowNodes[0] with { Name = "Again" }] };
        component.Render(parameters => parameters.Add(p => p.Document, renamed));
        Assert.Equal(2, patchCall.Invocations.Count);
        Assert.Equal(1, Assert.IsType<CanvasDocumentPatch>(patchCall.Invocations.Last().Arguments[0]).BaseRevision);
    }

    [Fact]
    public void RejectedPatchFallsBackToFullLoadAndAdvancesItsBase()
    {
        SetupInitialize(Accepted());
        var patchCall = _canvas.Setup<DocumentLoaded?>("patchDocument", _ => true).SetResult(null);
        var load = SetupLoadDocument();
        var document = DiagramDocument();
        var component = Render<BpmnCanvas>(parameters => parameters.Add(p => p.Document, document));
        var changed = document with { FlowNodes = [document.FlowNodes[0] with { Name = "Updated" }] };

        component.Render(parameters => parameters.Add(p => p.Document, changed));
        component.Render();

        Assert.Single(patchCall.Invocations);
        Assert.Equal(1L, Assert.Single(load.Invocations).Arguments[2]);
    }

    [Fact]
    public async Task ResyncBypassesPatchesEvenWhenTheirBaseMatches()
    {
        SetupInitialize(Accepted());
        var load = SetupLoadDocument();
        var component = Render<BpmnCanvas>(parameters => parameters.Add(p => p.Document, DiagramDocument()));

        await component.InvokeAsync(component.Instance.ResyncAsync);

        Assert.Single(load.Invocations);
        Assert.Empty(JSInterop.Invocations["patchDocument"]);
    }

    [Fact]
    public async Task LogsAHostHandlerThatThrowsInsteadOfLeavingAFaultedTaskNobodyObserves()
    {
        var logs = new CapturingLoggerProvider();
        Services.AddLogging(builder => builder.AddProvider(logs));
        SetupInitialize(Accepted());
        var component = Render<BpmnCanvas>(parameters => parameters
            .Add(p => p.OnViewportChanged, Microsoft.AspNetCore.Components.EventCallback.Factory.Create<ViewportChanged>(this, _ => throw new InvalidOperationException("host bug"))));
        var bridge = ((DotNetObjectReference<BpmnCanvasEventBridge>)JSInterop.Invocations["initialize"].Single().Arguments[4]!).Value;

        await component.InvokeAsync(() => bridge.ViewportChanged(new ViewportChanged(1, 0, 0)));
        await Task.Delay(200, Xunit.TestContext.Current.CancellationToken);

        Assert.Contains(logs.Entries, entry => entry.Level == Microsoft.Extensions.Logging.LogLevel.Error && entry.Exception is InvalidOperationException);
    }

    private sealed class CapturingLoggerProvider : Microsoft.Extensions.Logging.ILoggerProvider
    {
        public List<(Microsoft.Extensions.Logging.LogLevel Level, Exception? Exception)> Entries { get; } = [];

        public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) => new Logger(this);

        public void Dispose()
        {
        }

        private sealed class Logger(CapturingLoggerProvider owner) : Microsoft.Extensions.Logging.ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

            public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (owner.Entries) owner.Entries.Add((logLevel, exception));
            }
        }
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

    private static BpmnDocument DiagramDocument() => Document() with
    {
        FlowNodes = [new("Task_1", BpmnFlowNodeType.Task, "Process_1", "Original")],
        Diagrams = [new("Diagram_1", new BpmnPlane("Plane_1", "Process_1", [new("Task_1_di", "Task_1", new Bounds(0, 0, 100, 80))], []))],
    };

    private sealed class TranslatedCanvasStrings : IStringLocalizer<BpmnCanvasStrings>
    {
        public LocalizedString this[string name] => new(name, $"localized:{name}");

        public LocalizedString this[string name, params object[] arguments] => this[name];

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
