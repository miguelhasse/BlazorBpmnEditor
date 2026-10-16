using Bunit;
using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Execution;
using Hasseware.AspNetCore.Components.Bpmn.Interop;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.FluentUI.AspNetCore.Components.Bpmn;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Components;

/// <summary>
/// Execution overlays (implementation plan M6.5).
/// </summary>
/// <remarks>
/// Strict interop, so an unplanned call fails: these therefore also prove what the editor does
/// <em>not</em> do with an execution state — it never loads a document, never patches the
/// configuration, and never touches history, because an engine's observation about a running
/// instance is not a change to the model.
/// </remarks>
public sealed class ExecutionOverlayTests : FluentTestContext
{
    private const string InstanceId = "instance-1";

    private readonly BunitJSModuleInterop _module;

    /// <summary>The canvas that <c>initialize</c> returns, on which every later call is made.</summary>
    private readonly BunitJSModuleInterop _canvas;

    public ExecutionOverlayTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentBpmnEditor();
        _module = JSInterop.SetupModule(BpmnInteropContract.ModulePath);
        _canvas = _module.SetupModule("initialize", _ => true);
        _canvas.Setup<CanvasInitResult>("getInitResult", _ => true)
            .SetResult(new CanvasInitResult(InstanceId, BpmnInteropContract.Version, 1, true, null));
    }

    // --- pushing state --------------------------------------------------------------

    [Fact]
    public async Task SendsTheWholeStateToTheCanvas()
    {
        var setState = _canvas.SetupVoid("setExecutionState", _ => true);
        setState.SetVoidResult();
        var editor = RenderEditor();

        await editor.InvokeAsync(() => editor.Instance.SetExecutionStateAsync(Running()));

        var call = Assert.Single(setState.Invocations);
        var sent = Assert.IsType<BpmnExecutionState>(Assert.Single(call.Arguments));
        Assert.Equal("instance-42", sent.InstanceId);
        Assert.Equal(2, sent.Elements.Count);
        Assert.Single(sent.Flows);
    }

    [Fact]
    public async Task RemembersWhatItDrew()
    {
        _canvas.SetupVoid("setExecutionState", _ => true).SetVoidResult();
        var editor = RenderEditor();

        await editor.InvokeAsync(() => editor.Instance.SetExecutionStateAsync(Running()));

        Assert.Equal("instance-42", editor.Instance.ExecutionState?.InstanceId);
    }

    [Fact]
    public async Task ClearingForgetsIt()
    {
        _canvas.SetupVoid("setExecutionState", _ => true).SetVoidResult();
        var clear = _canvas.SetupVoid("clearExecutionState", _ => true);
        clear.SetVoidResult();
        var editor = RenderEditor();

        await editor.InvokeAsync(() => editor.Instance.SetExecutionStateAsync(Running()));
        await editor.InvokeAsync(() => editor.Instance.ClearExecutionStateAsync());

        Assert.Single(clear.Invocations);
        Assert.Null(editor.Instance.ExecutionState);
    }

    [Fact]
    public async Task WorksInReadOnly()
    {
        /*
         * The case the mode exists for. Section 7 asks read-only to keep everything except
         * mutation, and watching a process run mutates nothing — an editor that refused an
         * execution state unless it was editable would have the rule exactly backwards.
         */
        var setState = _canvas.SetupVoid("setExecutionState", _ => true);
        setState.SetVoidResult();
        var editor = RenderEditor(readOnly: true);

        await editor.InvokeAsync(() => editor.Instance.SetExecutionStateAsync(Running()));

        Assert.Single(setState.Invocations);
    }

    [Fact]
    public async Task RefusesNothingAndRaisesNoRejection()
    {
        // Nothing is validated against the document on purpose: an engine may well be reporting on
        // an element this diagram no longer contains, and that is a stale diagram, not a bad state.
        var setState = _canvas.SetupVoid("setExecutionState", _ => true);
        setState.SetVoidResult();
        var editor = RenderEditor();

        var unknown = new BpmnExecutionState("instance-42", [new BpmnElementExecution("Nothing_Here", BpmnActivityStatus.Active)]);
        await editor.InvokeAsync(() => editor.Instance.SetExecutionStateAsync(unknown));

        Assert.Single(setState.Invocations);
    }

    // --- configuration --------------------------------------------------------------

    [Fact]
    public void AnimationIsOffUnlessAsked()
    {
        // Absent rather than "disabled", matching the theme attributes: the stylesheet animates on
        // the presence of the opt-in, so there is no second state for it to have to agree with.
        Assert.Null(AnimationAttribute());
    }

    [Fact]
    public void TheParameterReachesTheRoot()
    {
        Assert.Equal("tokens", AnimationAttribute(BpmnExecutionAnimation.Enabled));
    }

    [Fact]
    public void ChangingItTakesEffect()
    {
        /*
         * The reason it is an attribute on the root rather than a section of the configuration
         * snapshot. That snapshot reaches the canvas once, at initialisation, and nothing applies
         * a later revision to a live paper — so an animation setting carried in it would have
         * silently done nothing every time it changed, which is exactly what it did.
         */
        var editor = RenderEditor();
        Assert.Null(editor.Find("section.bpmn-editor").GetAttribute("data-bpmn-animation"));

        editor.Render(parameters => parameters
            .Add(p => p.Document, Document())
            .Add(p => p.ExecutionAnimation, BpmnExecutionAnimation.Enabled));

        Assert.Equal("tokens", editor.Find("section.bpmn-editor").GetAttribute("data-bpmn-animation"));
    }

    [Fact]
    public void LeavingTheParameterAloneKeepsTheHostsOwnChoice()
    {
        /*
         * The reason the parameter is nullable. A non-nullable one defaulting to Disabled would
         * silently overrule an application that had turned animation on in BpmnOptions, and the
         * override would be invisible at the call site precisely because nothing was written there.
         */
        Services.Configure<BpmnOptions>(options =>
            options.Execution = new ExecutionOptions(BpmnExecutionAnimation.Enabled));

        Assert.Equal("tokens", AnimationAttribute());
    }

    [Fact]
    public void TheParameterStillWinsOverTheHostsChoice()
    {
        Services.Configure<BpmnOptions>(options =>
            options.Execution = new ExecutionOptions(BpmnExecutionAnimation.Enabled));

        Assert.Null(AnimationAttribute(BpmnExecutionAnimation.Disabled));
    }

    // --- the model ------------------------------------------------------------------

    [Fact]
    public void CollectionsAreNeverNull()
    {
        // The overlay iterates both on every draw, and a host reporting only element status is the
        // ordinary case rather than an incomplete one.
        var state = new BpmnExecutionState("instance-42");

        Assert.Empty(state.Elements);
        Assert.Empty(state.Flows);
        Assert.True(state.IsEmpty);
        Assert.Empty(new BpmnElementExecution("Task_1").Incidents);
    }

    // --- helpers --------------------------------------------------------------------

    private static BpmnExecutionState Running() => new(
        "instance-42",
        [
            new BpmnElementExecution("Task_1", BpmnActivityStatus.Active, 2),
            new BpmnElementExecution("Task_2", BpmnActivityStatus.Failed, 0,
                [new BpmnIncident("i-1", "The service did not answer.", BpmnIncidentSeverity.Error)]),
        ],
        [new BpmnFlowExecution("Flow_1", Traversed: true, TraversalCount: 3)]);

    /// <summary>The root's <c>data-bpmn-animation</c>, which is what the stylesheet reads.</summary>
    private string? AnimationAttribute(BpmnExecutionAnimation? animation = null) =>
        RenderEditor(animation: animation).Find("section.bpmn-editor").GetAttribute("data-bpmn-animation");

    private IRenderedComponent<FluentBpmnEditor> RenderEditor(
        bool readOnly = false,
        BpmnExecutionAnimation? animation = null)
    {
        _canvas.Setup<DocumentLoaded?>("loadDocument", _ => true)
            .SetResult(new DocumentLoaded("Diagram_1", 1, 0, []));

        return Render<FluentBpmnEditor>(parameters => parameters
            .Add(p => p.Document, Document())
            .Add(p => p.IsReadOnly, readOnly)
            .Add(p => p.ExecutionAnimation, animation));
    }

    private static BpmnDocument Document() => new BpmnDocumentBuilder()
        .Node("Task_1", BpmnFlowNodeType.Task, new Bounds(200, 120, 100, 80), "Work")
        .Node("Task_2", BpmnFlowNodeType.Task, new Bounds(400, 120, 100, 80), "More work")
        .Build();
}
