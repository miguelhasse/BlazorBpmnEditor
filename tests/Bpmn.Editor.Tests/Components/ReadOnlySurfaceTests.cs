using Bunit;
using Hasseware.AspNetCore.Components.Bpmn.Interop;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;

using Microsoft.AspNetCore.Components;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Components;

/// <summary>
/// What a read-only editor offers, and what it must not.
/// </summary>
/// <remarks>
/// Section 7: read-only "keeps selection, inspection and navigation and loses every mutation". A
/// surface that is still on screen but refuses everything is not read-only, it is a trap — the
/// reader has no way to tell which of the things in front of them will work.
/// </remarks>
public sealed class ReadOnlySurfaceTests : BunitContext
{
    private const string InstanceId = "instance-1";

    private readonly BunitJSModuleInterop _module;

    /// <summary>The canvas that <c>initialize</c> returns, on which every later call is made.</summary>
    private readonly BunitJSModuleInterop _canvas;

    public ReadOnlySurfaceTests()
    {
        JSInterop.Mode = JSRuntimeMode.Strict;
        Services.AddBpmnEditor();
        _module = JSInterop.SetupModule(BpmnInteropContract.ModulePath);
        _canvas = _module.SetupModule("initialize", _ => true);
        _canvas.Setup<CanvasInitResult>("getInitResult", _ => true)
            .SetResult(new CanvasInitResult(InstanceId, BpmnInteropContract.Version, 1, true, null));
    }

    [Fact]
    public void AnEditorOffersItsPaletteWhenItCanBeUsed()
    {
        var editor = RenderEditor(readOnly: false);

        Assert.NotEmpty(editor.FindAll(".bpmn-stencil__tool"));
    }

    [Fact]
    public void AndTakesItAwayInReadOnly()
    {
        /*
         * Every tool in a palette creates something, so a palette is nothing but a mutation
         * surface. Offering thirty-two of them to someone who cannot use any is worse than
         * offering none: each one is a refusal waiting to happen.
         */
        var editor = RenderEditor(readOnly: true);

        Assert.Empty(editor.FindAll(".bpmn-stencil__tool"));
        Assert.Empty(editor.FindAll(".bpmn-editor__stencil"));
    }

    [Fact]
    public void AndPutsItBackWhenTheModeIsTurnedOffAgain()
    {
        var editor = RenderEditor(readOnly: true);

        editor.Render(parameters => Configure(parameters, readOnly: false));

        Assert.NotEmpty(editor.FindAll(".bpmn-stencil__tool"));
    }

    [Fact]
    public void AHostsOwnPaletteIsLeftAlone()
    {
        // StencilContent is the host's, and a host that supplies a palette and then turns
        // read-only on is describing something this component cannot see inside.
        var editor = Render<BpmnEditor>(parameters => Configure(parameters, readOnly: true)
            .Add(p => p.StencilContent, (RenderFragment)(builder =>
            {
                builder.OpenElement(0, "div");
                builder.AddAttribute(1, "class", "host-palette");
                builder.CloseElement();
            })));

        Assert.NotEmpty(editor.FindAll(".host-palette"));
    }

    // --- and the canvas, which is configured once ---------------------------------------

    [Fact]
    public void TheCanvasIsRebuiltWhenTheConfigurationChanges()
    {
        /*
         * A configuration is read once, when the instance is built: the paper's interactivity and
         * every controller that only exists in edit mode are decided then. Without this, turning
         * read-only on left an editor that refused mutations while its canvas still offered the
         * gestures that make them.
         */
        var editor = RenderEditor(readOnly: false);

        Assert.Equal(1, Initialisations());

        editor.Render(parameters => Configure(parameters, readOnly: true));

        Assert.Equal(2, Initialisations());
        Assert.Equal(1, Disposals());
    }

    [Fact]
    public void AndIsNotRebuiltWhenNothingAboutItChanged()
    {
        // A re-render caused by an unrelated parameter must not throw the canvas away: the
        // revision only advances when the configuration really differs.
        var editor = RenderEditor(readOnly: false);

        editor.Render(parameters => Configure(parameters, readOnly: false).Add(p => p.AriaLabel, "Something else"));

        Assert.Equal(1, Initialisations());
        Assert.Equal(0, Disposals());
    }

    [Fact]
    public void AndAskedForNoViewItWasNeverShowing()
    {
        /*
         * A rebuilt canvas is put back where the reader left it — but only if it ever said where
         * that was. Nothing here has reported a viewport, so there is nothing to restore, and
         * asking for one would move a canvas that had not moved.
         *
         * That it *is* restored when there is something to restore is a browser fact: it needs a
         * real paper reporting a real transform, which is exactly what bUnit does not have.
         */
        var editor = RenderEditor(readOnly: false);

        editor.Render(parameters => Configure(parameters, readOnly: true));

        Assert.Empty(JSInterop.Invocations["setViewport"]);
    }

    // --- fixtures -------------------------------------------------------------------------

    private int Initialisations() => JSInterop.Invocations["initialize"].Count;

    private int Disposals() => JSInterop.Invocations["dispose"].Count;

    private IRenderedComponent<BpmnEditor> RenderEditor(bool readOnly)
    {
        var component = Render<BpmnEditor>(parameters => Configure(parameters, readOnly));

        // The canvas only reports a viewport once it is running; this is what a real one sends.
        component.Find(".bpmn-canvas");

        return component;
    }

    private ComponentParameterCollectionBuilder<BpmnEditor> Configure(
        ComponentParameterCollectionBuilder<BpmnEditor> parameters,
        bool readOnly)
    {
        _canvas.Setup<DocumentLoaded?>("loadDocument", _ => true)
            .SetResult(new DocumentLoaded("Diagram_1", 1, 0, []));
        _canvas.SetupVoid("dispose", _ => true).SetVoidResult();
        _canvas.SetupVoid("setViewport", _ => true).SetVoidResult();
        _canvas.SetupVoid("setSelection", _ => true).SetVoidResult();

        return parameters
            .Add(p => p.Document, Document())
            .Add(p => p.ShowStencil, true)
            .Add(p => p.IsReadOnly, readOnly);
    }

    private static BpmnDocument Document() => new BpmnDocumentBuilder("Process_1")
        .Node("Task_1", BpmnFlowNodeType.UserTask, new Bounds(200, 120, 100, 80), "Work")
        .Build();
}
