using Bunit;
using Hasseware.AspNetCore.Components.Bpmn.Accessibility;
using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Validation;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Components;

/// <summary>A component rendered inside a <see cref="BpmnEditorScope"/>, with the pieces tests drive.</summary>
internal sealed record TestScope<T>(IRenderedComponent<BpmnEditorScope> Scope, IRenderedComponent<T> Component)
    where T : IComponent
{
    public BpmnEditorSession Session => Scope.Instance.Session;

    public string Markup => Component.Markup;

    public IRenderedComponent<T> Cut => Component;
}

internal static partial class TestScope
{
    static partial void ConfigureKit(IServiceCollection services);

    /// <summary>
    /// Renders <typeparamref name="T"/> inside a scope, and inside a canvas surface when
    /// <paramref name="canvas"/> is set.
    /// </summary>
    public static TestScope<T> Render<T>(
        this BunitContext context,
        Action<ComponentParameterCollectionBuilder<T>>? parameters = null,
        Action<ComponentParameterCollectionBuilder<BpmnEditorScope>>? scope = null,
        bool canvas = false)
        where T : IComponent
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Services.Any(descriptor => descriptor.ServiceType == typeof(BpmnDiagnosticLocalizer)))
        {
            context.Services.AddBpmnComponents();
            ConfigureKit(context.Services);
        }

        RenderFragment inner = builder =>
        {
            builder.OpenComponent<T>(0);
            builder.CloseComponent();
        };
        if (parameters is not null)
        {
            var collectionBuilder = new ComponentParameterCollectionBuilder<T>(parameters);
            var build = typeof(ComponentParameterCollectionBuilder<T>).GetMethod("Build", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var collection = build.Invoke(collectionBuilder, null)!;
            inner = (RenderFragment)collection.GetType().GetMethod("ToRenderFragment")!.MakeGenericMethod(typeof(T)).Invoke(collection, null)!;
        }
        RenderFragment content = inner;
        if (canvas)
        {
            content = builder =>
            {
                builder.OpenComponent<CascadingValue<BpmnCanvasSurface>>(0);
                builder.AddComponentParameter(1, "Value", new BpmnCanvasSurface(new BpmnCanvas()));
                builder.AddComponentParameter(2, "IsFixed", true);
                builder.AddComponentParameter(3, "ChildContent", inner);
                builder.CloseComponent();
            };
        }

        var rendered = context.Render<BpmnEditorScope>(p =>
        {
            scope?.Invoke(p);
            p.Add(s => s.ChildContent, content);
        });
        return new TestScope<T>(rendered, rendered.FindComponent<T>());
    }

    /// <summary>
    /// Feeds the session what a canvas and a document would: the described selection, its
    /// diagnostics and usages, the diagrams, and observers for what the component asks the
    /// session to do.
    /// </summary>
    public static TestScope<T> Seed<T>(this TestScope<T> target, SeedBuilder seed)
        where T : IComponent
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(seed);

        var session = target.Session;
        var byId = seed.SnapshotList.ToDictionary(snapshot => snapshot.ElementId);
        session.DescribeOverride = id => byId.GetValueOrDefault(id);
        session.DiagnosticsOverride = seed.DiagnosticList ?? [];
        session.UsagesOverride = seed.UsageList ?? [];
        session.DiagramsOverride = seed.DiagramList;
        if (seed.DrillTarget is { } drill) session.DrillTargetOverride = _ => drill;
        if (seed.CommandObserver is { } onCommand)
        {
            session.PropertyCommandOverride = command =>
            {
                onCommand(command);
                return Task.FromResult<BpmnEditResult?>(null);
            };
        }

        if (seed.UsageObserver is { } onUsage)
        {
            session.GoToUsageOverride = usage =>
            {
                onUsage(usage);
                return Task.CompletedTask;
            };
        }

        if (seed.GraphNodes is { } graph) session.AccessibleGraphOverride = graph;
        if (seed.ActivationObserver is { } onActivate)
        {
            session.ActivateOverride = id =>
            {
                onActivate(id);
                return Task.CompletedTask;
            };
        }

        if (seed.ActivateToolObserver is { } onTool)
            session.ActivateToolOverride = entry =>
            {
                onTool(entry);
                return Task.CompletedTask;
            };

        if (seed.ToggleObserver is { } onToggle)
        {
            session.ToggleOverride = id =>
            {
                onToggle(id);
                return Task.CompletedTask;
            };
        }

        if (seed.NavigationObserver is { } onNavigate)
        {
            session.NavigateOverride = id =>
            {
                onNavigate(id);
                return Task.CompletedTask;
            };
        }

        seed.SessionObserver?.Invoke(session);
        if (seed.HelpVisible) target.Scope.InvokeAsync(session.ShowShortcutHelp).GetAwaiter().GetResult();
        var selection = seed.SnapshotList.Select(snapshot => snapshot.ElementId).Concat(seed.ExtraSelection).ToArray();
        target.Scope.InvokeAsync(() => session.SetSelectionAsync(selection)).GetAwaiter().GetResult();
        return target;
    }

    /// <summary>Renders <typeparamref name="T"/> in a scope seeded through <paramref name="seed"/>.</summary>
    public static IRenderedComponent<T> RenderSeeded<T>(
        this BunitContext context,
        Action<SeedBuilder> seed,
        Action<ComponentParameterCollectionBuilder<T>>? parameters = null,
        bool canvas = false)
        where T : IComponent
    {
        ArgumentNullException.ThrowIfNull(seed);
        var builder = new SeedBuilder();
        seed(builder);

        var target = context.Render(
            parameters,
            scope: p =>
            {
                p.Add(s => s.IsReadOnly, builder.IsReadOnly).Add(s => s.DiagramId, builder.ActiveDiagram).Add(s => s.Document, builder.Doc);
                if (builder.BindingList is { } bindings)
                    p.Add(s => s.Configuration, EditorConfig.CreateDefault() with { Keyboard = KeyboardOptions.Default with { Bindings = bindings } });
            },
            canvas: canvas);
        return target.Seed(builder).Component;
    }
}

/// <summary>What a test says about the session a component is rendered in.</summary>
internal sealed class SeedBuilder
{
    internal IReadOnlyList<BpmnElementSnapshot> SnapshotList { get; private set; } = [];

    internal IReadOnlyList<BpmnDiagnostic>? DiagnosticList { get; private set; }

    internal IReadOnlyList<BpmnReferenceSite>? UsageList { get; private set; }

    internal IReadOnlyList<BpmnDiagramTarget>? DiagramList { get; private set; }

    internal BpmnDrillTarget? DrillTarget { get; private set; }

    internal string? ActiveDiagram { get; private set; }

    internal List<string> ExtraSelection { get; } = [];

    internal Action<BpmnEditCommand>? CommandObserver { get; private set; }

    internal Action<BpmnReferenceSite>? UsageObserver { get; private set; }

    internal Action<string>? NavigationObserver { get; private set; }

    internal IReadOnlyList<BpmnGraphNode>? GraphNodes { get; private set; }

    internal Action<string>? ActivationObserver { get; private set; }

    internal Action<string>? ToggleObserver { get; private set; }

    internal Action<BpmnStencilEntry>? ActivateToolObserver { get; private set; }

    internal Action<BpmnEditorSession>? SessionObserver { get; private set; }

    internal IReadOnlyList<ShortcutBinding>? BindingList { get; private set; }

    internal Model.BpmnDocument? Doc { get; private set; }

    internal bool HelpVisible { get; private set; }

    internal bool IsReadOnly { get; private set; }

    public SeedBuilder Snapshots(IReadOnlyList<BpmnElementSnapshot> value) { SnapshotList = value; return this; }

    public SeedBuilder Diagnostics(IReadOnlyList<BpmnDiagnostic> value) { DiagnosticList = value; return this; }

    public SeedBuilder Usages(IReadOnlyList<BpmnReferenceSite> value) { UsageList = value; return this; }

    public SeedBuilder Diagrams(IReadOnlyList<BpmnDiagramTarget> value) { DiagramList = value; return this; }

    public SeedBuilder Active(string diagramId) { ActiveDiagram = diagramId; return this; }

    public SeedBuilder Drill(BpmnDrillTarget value)
    {
        DrillTarget = value;
        ExtraSelection.Add("Selected_1");
        return this;
    }

    public SeedBuilder OnCommand(Action<BpmnEditCommand> value) { CommandObserver = value; return this; }

    public SeedBuilder OnUsage(Action<BpmnReferenceSite> value) { UsageObserver = value; return this; }

    public SeedBuilder OnNavigate(Action<string> value) { NavigationObserver = value; return this; }

    public SeedBuilder Graph(IReadOnlyList<BpmnGraphNode> value) { GraphNodes = value; return this; }

    public SeedBuilder Current(string? id)
    {
        if (id is not null) ExtraSelection.Add(id);
        return this;
    }

    public SeedBuilder OnActivate(Action<string> value) { ActivationObserver = value; return this; }

    public SeedBuilder OnActivateTool(Action<BpmnStencilEntry> value) { ActivateToolObserver = value; return this; }

    public SeedBuilder OnToggle(Action<string> value) { ToggleObserver = value; return this; }

    public SeedBuilder Session(Action<BpmnEditorSession> value) { SessionObserver = value; return this; }

    public SeedBuilder Bindings(IReadOnlyList<ShortcutBinding> value) { BindingList = value; return this; }

    public SeedBuilder Document(Model.BpmnDocument value) { Doc = value; return this; }

    public SeedBuilder ShowHelp() { HelpVisible = true; return this; }

    public SeedBuilder ReadOnly() { IsReadOnly = true; return this; }
}
