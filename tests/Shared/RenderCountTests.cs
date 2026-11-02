using Bunit;
using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Interop;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Components;

/// <summary>Guards state-publication render budgets in both complete kit compositions.</summary>
public sealed class RenderCountTests : BunitContext
{
    private readonly ITestOutputHelper _output;
    private readonly RenderLog _renders = new();
    private readonly BunitJSModuleInterop _canvas;

    public RenderCountTests(ITestOutputHelper output)
    {
        _output = output;
        JSInterop.Mode = JSRuntimeMode.Loose;
        KitFixture.Register(Services);
        Services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Debug).AddProvider(_renders));
        var module = JSInterop.SetupModule(BpmnInteropContract.ModulePath);
        _canvas = module.SetupModule("initialize", _ => true);
        _canvas.Setup<CanvasInitResult>("getInitResult", _ => true)
            .SetResult(new("baseline", BpmnInteropContract.Version, 1, true, null));
    }

    [Theory]
    [InlineData("Viewport", false)]
    [InlineData("Selection", false)]
    [InlineData("UnchangedParameters", false)]
    [InlineData("SingleEdit", false)]
    [InlineData("Viewport", true)]
    [InlineData("Selection", true)]
    [InlineData("UnchangedParameters", true)]
    [InlineData("SingleEdit", true)]
    public async Task RecordFullCompositionRenderCounts(string scenario, bool showColorPicker)
    {
        var document = BpmnXmlReader.Read(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "large-virtualization-stress.bpmn"))).Document;
        var diagram = document.Diagrams[0];
        _canvas.Setup<DocumentLoaded?>("loadDocument", _ => true)
            .SetResult(new(diagram.Id, diagram.Plane.Shapes.Count, diagram.Plane.Edges.Count, []));
        _canvas.Setup<DocumentLoaded?>("patchDocument", _ => true)
            .SetResult(new(diagram.Id, diagram.Plane.Shapes.Count, diagram.Plane.Edges.Count, []));
        RenderFragment footer = builder =>
        {
            builder.OpenComponent<BpmnPart>(0);
            builder.AddComponentParameter(1, nameof(BpmnPart.Part), BpmnEditorPart.ValidationPanel);
            builder.CloseComponent();
            builder.OpenComponent<BpmnPart>(2);
            builder.AddComponentParameter(3, nameof(BpmnPart.Part), BpmnEditorPart.DiagramNavigator);
            builder.CloseComponent();
        };
        var parameters = new Dictionary<string, object>
        {
            ["Document"] = document,
            ["DiagramId"] = document.Diagrams[0].Id,
            ["ShowStencil"] = true,
            ["ShowColorPicker"] = showColorPicker,
            ["Footer"] = footer,
        };
        var editor = Render<DynamicComponent>(p => p.Add(c => c.Type, KitFixture.Composition)
            .Add(c => c.Parameters, parameters));
        var session = editor.FindComponent<BpmnEditorScope>().Instance.Session;
        var elements = document.FlowNodes.Take(2).Select(node => node.Id).ToArray();
        await editor.InvokeAsync(() => session.SetSelectionAsync([elements[0]]));
        await SettleAsync(editor);
        var before = _renders.Snapshot();
        Assert.NotEmpty(before);
        var notifications = 0;
        void Changed(object? sender, EventArgs args) => notifications++;
        session.StateChanged += Changed;
        try
        {
            switch (scenario)
            {
                case "Viewport":
                    await editor.InvokeAsync(() => session.HandleViewportChangedAsync(new(1.1, 0, 0)));
                    break;
                case "Selection":
                    await editor.InvokeAsync(() => session.SetSelectionAsync([elements[1]]));
                    break;
                case "UnchangedParameters":
                    editor.Render(p => p.Add(c => c.Type, KitFixture.Composition).Add(c => c.Parameters, parameters));
                    break;
                case "SingleEdit":
                    await editor.InvokeAsync(async () =>
                    {
                        var result = await session.ApplyAsync(new UpdatePropertiesCommand(elements[0], "name", "Baseline edit"));
                        Assert.True(result.IsApplied);
                    });
                    break;
            }
            await SettleAsync(editor);
            var after = _renders.Snapshot();
            var rows = after.GroupBy(component => component.Key.Type.Name).OrderBy(group => group.Key);
            _output.WriteLine($"Kit={KitFixture.Composition.Name}; Scenario={scenario}; Shapes={document.Diagrams[0].Plane.Shapes.Count}; Notifications={notifications}");
            foreach (var row in rows)
                _output.WriteLine($"{row.Key}: instances={row.Count()}, renders={row.Sum(component => component.Value - before.GetValueOrDefault(component.Key))}");
            var deltas = after.GroupBy(component => component.Key.Type.Name)
                .ToDictionary(group => group.Key, group => group.Sum(component => component.Value - before.GetValueOrDefault(component.Key)));
            var budget = scenario switch { "Viewport" => 6, "Selection" => showColorPicker ? 12 : 11, "SingleEdit" => 21, _ => 21 };
            Assert.InRange(deltas.Values.Sum(), 1, budget);
            // A rename's acknowledgement preserves loaded counts, so it must not publish a second state change.
            Assert.Equal(scenario == "UnchangedParameters" ? 0 : 1, notifications);
            if (scenario == "SingleEdit")
            {
                Assert.Single(_canvas.Invocations["patchDocument"]);
                Assert.Single(_canvas.Invocations["loadDocument"]);
            }
            Assert.Equal(0, deltas.GetValueOrDefault(nameof(BpmnPart)));
            if (scenario is "Viewport" or "Selection")
            {
                foreach (var part in new[] { "PropertyPanel", "ValidationPanel", "ReferenceBrowser", "ContextMenu", "LabelEditor", "ImportOffer", "ExternalChangeOffer", "AgentConfirmation", "CalledElementPicker", "ShortcutHelp" })
                    if (scenario == "Viewport" || part is not ("PropertyPanel" or "ReferenceBrowser"))
                        Assert.All(deltas.Where(row => row.Key.EndsWith(part, StringComparison.Ordinal)), row => Assert.Equal(0, row.Value));
            }
        }
        finally { session.StateChanged -= Changed; }
    }

    [Theory]
    [InlineData("SingleSelection")]
    [InlineData("AllSelection")]
    [InlineData("SingleEdit")]
    [InlineData("UnchangedParent")]
    public async Task HostCallbacksRetainStatusAndCompositionBudgets(string scenario)
    {
        var document = BpmnXmlReader.Read(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "large-virtualization-stress.bpmn"))).Document;
        var diagram = document.Diagrams[0];
        _canvas.Setup<DocumentLoaded?>("loadDocument", _ => true)
            .SetResult(new(diagram.Id, diagram.Plane.Shapes.Count, diagram.Plane.Edges.Count, []));
        _canvas.Setup<DocumentLoaded?>("patchDocument", _ => true)
            .SetResult(new(diagram.Id, diagram.Plane.Shapes.Count, diagram.Plane.Edges.Count, []));
        var host = Render<CallbackHost>(p => p.Add(c => c.Document, document));
        var editor = host.FindComponent<DynamicComponent>();
        var session = host.FindComponent<BpmnEditorScope>().Instance.Session;
        var ids = document.Diagrams[0].Plane.Shapes.Select(shape => shape.BpmnElement).ToArray();
        await host.InvokeAsync(() => session.HandleSelectionChangedAsync(new([ids[0]], null)));
        await SettleAsync(editor);
        var before = _renders.Total;
        var beforeComponents = _renders.Snapshot();
        var beforeHost = host.Instance.Renders;
        var beforeSelection = host.Instance.SelectionCallbacks;
        var beforeDocument = host.Instance.DocumentCallbacks;

        switch (scenario)
        {
            case "SingleSelection":
                await host.InvokeAsync(() => session.HandleSelectionChangedAsync(new([ids[1]], null)));
                break;
            case "AllSelection":
                await host.InvokeAsync(() => session.HandleSelectionChangedAsync(new(ids, null)));
                break;
            case "SingleEdit":
                await host.InvokeAsync(async () =>
                    Assert.True((await session.ApplyAsync(new UpdatePropertiesCommand(ids[0], "name", "Host edit"))).IsApplied));
                break;
            default:
                await host.InvokeAsync(host.Instance.RenderAgain);
                break;
        }
        await SettleAsync(editor);

        var selection = scenario is "SingleSelection" or "AllSelection";
        Assert.Equal(selection ? 1 : 0, host.Instance.SelectionCallbacks - beforeSelection);
        Assert.Equal(scenario == "SingleEdit" ? 1 : 0, host.Instance.DocumentCallbacks - beforeDocument);
        Assert.Equal(1, host.Instance.Renders - beforeHost);
        Assert.Equal(scenario == "AllSelection" ? ids.Length : 1, session.State.Selection.Length);
        Assert.Equal($"{session.State.Selection.Length} selected", host.Find("[data-host-status]").TextContent);
        Assert.Equal(diagram.Plane.Shapes.Count, host.FindAll(".bpmn-a11y-tree__item").Count);
        Assert.Single(host.FindAll("[aria-label='Colors'][aria-haspopup='dialog']"));
        var renders = _renders.Total - before;
        var componentRenders = _renders.Snapshot().Sum(component => component.Value - beforeComponents.GetValueOrDefault(component.Key));
        _output.WriteLine($"Kit={KitFixture.Composition.Name}; HostScenario={scenario}; AllComponentRenders={renders}; EditorComponentRenders={componentRenders}");
        var budget = scenario switch { "SingleSelection" => 28, "AllSelection" or "SingleEdit" => 24, _ => 18 };
        Assert.InRange(componentRenders, 1, budget);
    }

    public sealed class CallbackHost : ComponentBase
    {
        private BpmnDocument? _edited;
        private string _status = "0 selected";
        public int Renders { get; private set; }
        public int SelectionCallbacks { get; private set; }
        public int DocumentCallbacks { get; private set; }

        [Parameter]
        public BpmnDocument Document { get; set; } = default!;

        public void RenderAgain() => StateHasChanged();

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            Renders++;
            builder.OpenElement(0, "span");
            builder.AddAttribute(1, "data-host-status", true);
            builder.AddContent(2, _status);
            builder.CloseElement();
            builder.OpenComponent<DynamicComponent>(3);
            builder.AddComponentParameter(4, nameof(DynamicComponent.Type), KitFixture.Composition);
            builder.AddComponentParameter(5, nameof(DynamicComponent.Parameters), new Dictionary<string, object>
            {
                ["Document"] = _edited ?? Document,
                ["ShowColorPicker"] = true,
                ["OnSelectionChanged"] = EventCallback.Factory.Create<SelectionChanged>(this, payload =>
                {
                    SelectionCallbacks++;
                    _status = $"{payload.ElementIds.Count} selected";
                }),
                ["OnDocumentChanged"] = EventCallback.Factory.Create<BpmnDocument>(this, document =>
                {
                    DocumentCallbacks++;
                    _edited = document;
                }),
            });
            builder.CloseComponent();
        }
    }

    private async Task SettleAsync(IRenderedComponent<DynamicComponent> editor)
    {
        // Fluent controls can enqueue lifecycle continuations after the originating callback.
        // Require two quiet samples, not just completion of the session operation.
        var previous = -1;
        var quiet = 0;
        for (var sample = 0; sample < 100; sample++)
        {
            await Task.Delay(25);
            await editor.InvokeAsync(() => { });
            var current = _renders.Total;
            quiet = current == previous ? quiet + 1 : 0;
            if (quiet == 2) return;
            previous = current;
        }
        throw new TimeoutException("Editor render queue did not settle.");
    }

    // bUnit RenderCount includes descendant updates in ancestor wrappers. Renderer debug events
    // count each component's own render instead, including replaced/removed tracked components.
    private sealed class RenderLog : ILoggerProvider, ILogger
    {
        private readonly Dictionary<(int Id, Type Type), int> _counts = [];
        private readonly Lock _gate = new();
        private int _total;

        public int Total { get { lock (_gate) return _total; } }
        public Dictionary<(int Id, Type Type), int> Snapshot()
        {
            lock (_gate) return new(_counts);
        }

        public ILogger CreateLogger(string categoryName) => this;
        public bool IsEnabled(LogLevel logLevel) => logLevel == LogLevel.Debug;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Dispose() { }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (eventId.Name != "RenderingComponent"
                || state is not IEnumerable<KeyValuePair<string, object?>> fields) return;
            var values = fields.ToDictionary(field => field.Key, field => field.Value);
            if (values.GetValueOrDefault("ComponentId") is not int id
                || values.GetValueOrDefault("ComponentType") is not Type type) return;
            lock (_gate) _total++;
            if (!typeof(BpmnEditorComponentBase).IsAssignableFrom(type)
                && type != typeof(BpmnEditorScope) && type != typeof(BpmnEditorCanvas)
                && type != typeof(BpmnCanvas) && type != typeof(BpmnPart)
                && type != KitFixture.Composition) return;
            lock (_gate) _counts[(id, type)] = _counts.GetValueOrDefault((id, type)) + 1;
        }
    }
}
