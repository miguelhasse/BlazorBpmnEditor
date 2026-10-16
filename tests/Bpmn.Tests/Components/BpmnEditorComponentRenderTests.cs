using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Components;

public sealed class BpmnEditorComponentRenderTests : BunitContext
{
    public BpmnEditorComponentRenderTests() => Services.AddBpmnComponents();

    [Fact]
    public async Task DefaultPredicateRendersEveryNewState()
    {
        var scope = Render<BpmnEditorScope>(p => p.AddChildContent<ProbeHost<DefaultProbe>>());
        var probe = scope.FindComponent<DefaultProbe>();
        var before = probe.Instance.Renders;
        await scope.InvokeAsync(scope.Instance.Session.ShowShortcutHelp);
        Assert.True(probe.Instance.Renders > before);
        Assert.Contains("True", probe.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SuppressionIsOneShotAndLocalAndParameterChangesStillRender()
    {
        var scope = Render<BpmnEditorScope>(p => p.AddChildContent<ProbeHost<SelectiveProbe>>());
        var probe = scope.FindComponent<SelectiveProbe>();
        Assert.Equal(1, probe.Instance.Renders);
        await scope.InvokeAsync(() => scope.Instance.Session.HandleViewportChangedAsync(new(1.1, 0, 0)));
        Assert.Equal(1, probe.Instance.Renders);
        await probe.InvokeAsync(probe.Instance.RenderLocally);
        Assert.Equal(2, probe.Instance.Renders);
        Assert.Contains("local:1", probe.Markup, StringComparison.Ordinal);
        await scope.InvokeAsync(() => scope.Instance.Session.HandleViewportChangedAsync(new(1.2, 0, 0)));
        Assert.Equal(2, probe.Instance.Renders);
        probe.Render(p => p.Add(c => c.Class, "changed"));
        Assert.Equal(3, probe.Instance.Renders);
        Assert.Contains("changed", probe.Markup, StringComparison.Ordinal);
        await scope.InvokeAsync(scope.Instance.Session.ShowShortcutHelp);
        Assert.Contains("True", probe.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LocalRenderDuringStateOnlyParameterAssignmentIsNotSuppressed()
    {
        var scope = Render<BpmnEditorScope>(p => p.AddChildContent<ProbeHost<SelectiveProbe>>());
        var probe = scope.FindComponent<SelectiveProbe>();
        probe.Instance.ForceDuringParameters = true;
        await scope.InvokeAsync(() => scope.Instance.Session.HandleViewportChangedAsync(new(1.1, 0, 0)));
        Assert.True(probe.Instance.Renders > 1);
        Assert.Contains("local:1", probe.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChangedStateAndOtherParameterRenderTogether()
    {
        var scope = Render<BpmnEditorScope>(p => p.AddChildContent<ProbeHost<SelectiveProbe>>());
        var probe = scope.FindComponent<SelectiveProbe>();
        var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            ["CascadedState"] = scope.Instance.Session.State with { Zoom = 2 },
            ["CascadedSession"] = scope.Instance.Session,
            [nameof(SelectiveProbe.Class)] = "new-parameter",
        });
        await probe.InvokeAsync(() => probe.Instance.SetParametersAsync(parameters));
        Assert.True(probe.Instance.Renders > 1);
        Assert.Contains("new-parameter", probe.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AsynchronousParameterWorkRendersItsCompletion()
    {
        var scope = Render<BpmnEditorScope>(p => p.AddChildContent<ProbeHost<SelectiveProbe>>());
        var probe = scope.FindComponent<SelectiveProbe>();
        probe.Instance.Work = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await scope.InvokeAsync(() => scope.Instance.Session.HandleViewportChangedAsync(new(1.1, 0, 0)));
        Assert.Equal(1, probe.Instance.Renders);
        probe.Instance.Work.SetResult();
        probe.WaitForAssertion(() => Assert.Contains("local:1", probe.Markup, StringComparison.Ordinal));
    }

    // Hold direct child parameters steady; the state cascade still notifies the child directly.
    public sealed class ProbeHost<T> : ComponentBase where T : IComponent
    {
        protected override bool ShouldRender() => false;
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<T>(0);
            builder.CloseComponent();
        }
    }

    public class DefaultProbe : BpmnEditorComponentBase
    {
        public int Renders { get; private set; }
        protected int Local { get; set; }
        public void RenderLocally() { Local++; StateHasChanged(); }
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            Renders++;
            builder.AddContent(0, $"{State.ShortcutHelpVisible};local:{Local};{Class}");
        }
    }

    public sealed class SelectiveProbe : DefaultProbe
    {
        public bool ForceDuringParameters { get; set; }
        public TaskCompletionSource? Work { get; set; }
        protected override bool ShouldRenderOnStateChange(BpmnEditorState previous, BpmnEditorState current) =>
            previous.ShortcutHelpVisible != current.ShortcutHelpVisible;
        protected override void OnParametersSet()
        {
            base.OnParametersSet();
            if (ForceDuringParameters) { ForceDuringParameters = false; RenderLocally(); }
        }
        protected override async Task OnParametersSetAsync()
        {
            if (Work is not { } work) return;
            await work.Task;
            Work = null;
            Local++;
        }
    }
}
