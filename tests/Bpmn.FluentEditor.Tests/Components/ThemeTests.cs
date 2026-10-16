using Bunit;
using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Interop;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.FluentUI.AspNetCore.Components.Bpmn;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Components;

/// <summary>
/// Themes and contrast (implementation plan M6).
/// </summary>
/// <remarks>
/// Colour and typography are a host's to change; notation geometry is not. Everything here is
/// therefore about attributes and custom properties on the root, and nothing about the shapes —
/// a Gateway is the same diamond at every contrast.
/// </remarks>
public sealed class ThemeTests : FluentTestContext
{
    private const string InstanceId = "instance-1";

    private readonly BunitJSModuleInterop _module;

    /// <summary>The canvas that <c>initialize</c> returns, on which every later call is made.</summary>
    private readonly BunitJSModuleInterop _canvas;

    public ThemeTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentBpmnEditor();
        _module = JSInterop.SetupModule(BpmnInteropContract.ModulePath);
        _canvas = _module.SetupModule("initialize", _ => true);
        _canvas.Setup<CanvasInitResult>("getInitResult", _ => true)
            .SetResult(new CanvasInitResult(InstanceId, BpmnInteropContract.Version, 1, true, null));
    }

    [Fact]
    public void SaysNothingWhenTheHostSaysNothing()
    {
        // Absent rather than "system": the stylesheet's own media query decides, and a third
        // attribute value would be a state the CSS then has to agree with.
        var root = RenderEditor(null).Find("section.bpmn-editor");

        Assert.Null(root.GetAttribute("data-bpmn-theme"));
        Assert.Null(root.GetAttribute("data-bpmn-contrast"));
    }

    [Theory]
    [InlineData(BpmnThemeMode.Light, "light")]
    [InlineData(BpmnThemeMode.Dark, "dark")]
    public void NamesTheChosenMode(BpmnThemeMode mode, string expected)
    {
        var root = RenderEditor(new ThemeOptions(mode)).Find("section.bpmn-editor");

        Assert.Equal(expected, root.GetAttribute("data-bpmn-theme"));
    }

    [Fact]
    public void ContrastIsItsOwnAxis()
    {
        // A user can want dark, high contrast, both or neither, so one does not imply the other.
        var root = RenderEditor(new ThemeOptions(BpmnThemeMode.Dark, BpmnContrast.High)).Find("section.bpmn-editor");

        Assert.Equal("dark", root.GetAttribute("data-bpmn-theme"));
        Assert.Equal("high", root.GetAttribute("data-bpmn-contrast"));
    }

    [Fact]
    public void NormalContrastSaysNothingEither()
    {
        var root = RenderEditor(new ThemeOptions(Contrast: BpmnContrast.Normal)).Find("section.bpmn-editor");

        Assert.Null(root.GetAttribute("data-bpmn-contrast"));
    }

    // --- overrides -----------------------------------------------------------------------

    [Fact]
    public void AppliesAHostsOwnTokens()
    {
        var root = RenderEditor(new ThemeOptions(Variables: new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["--bpmn-selection-color"] = "#ff6600",
        })).Find("section.bpmn-editor");

        Assert.Contains("--bpmn-selection-color:#ff6600", root.GetAttribute("style")!, StringComparison.Ordinal);
    }

    [Fact]
    public void PutsAHostsTokensOnTheCanvasAsWell()
    {
        /*
         * Where they have to be, not merely above it. The stylesheet declares the base tokens on
         * `.bpmn-canvas` itself, and a declaration on an element beats a value inherited from an
         * ancestor however that value was set — specificity does not come into it. Set only on the
         * root, every override reached the chrome and stopped at the diagram: a host asking for an
         * orange stroke got black shapes and no indication why.
         *
         * Asserted against the canvas element rather than the parameter, because the parameter was
         * being set correctly the whole time it did not work.
         */
        var editor = RenderEditor(new ThemeOptions(Variables: new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["--bpmn-stroke"] = "#ff6600",
        }));

        var canvas = editor.Find("div.bpmn-canvas");

        Assert.Contains("--bpmn-stroke:#ff6600", canvas.GetAttribute("style")!, StringComparison.Ordinal);
    }

    [Fact]
    public void LeavesTheCanvasAloneWhenThereIsNothingToOverride()
    {
        // No style attribute rather than an empty one: the canvas has its own layout rules, and an
        // inline style that says nothing is still something a reader has to check.
        var canvas = RenderEditor(new ThemeOptions(BpmnThemeMode.Dark)).Find("div.bpmn-canvas");

        Assert.True(canvas.GetAttribute("style") is null or "");
    }

    [Fact]
    public void AcceptsATokenNameWithoutTheDashes()
    {
        // A host writing configuration should not have to remember which half of the name the
        // library wants; both spellings mean the same custom property.
        var root = RenderEditor(new ThemeOptions(Variables: new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["bpmn-stroke"] = "#123456",
        })).Find("section.bpmn-editor");

        Assert.Contains("--bpmn-stroke:#123456", root.GetAttribute("style")!, StringComparison.Ordinal);
    }

    [Fact]
    public void KeepsTheHostsOwnStyleAndPutsTheThemeAfterIt()
    {
        // Last wins in an inline style, and the theme is the more specific statement of the two.
        SetupCanvas();

        var editor = Render<FluentBpmnEditor>(parameters => parameters
            .Add(p => p.Document, Document())
            .Add(p => p.Style, "outline:1px solid red;")
            .Add(p => p.Theming, new ThemeOptions(Variables: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["--bpmn-fill"] = "#eeeeee",
            })));

        var style = editor.Find("section.bpmn-editor").GetAttribute("style")!;

        Assert.StartsWith("outline:1px solid red;", style, StringComparison.Ordinal);
        Assert.Contains("--bpmn-fill:#eeeeee", style, StringComparison.Ordinal);
    }

    [Fact]
    public void ThemingDoesNotTouchTheCanvasConfiguration()
    {
        /*
         * Colour is not the canvas's business. If theming rode on the configuration snapshot, every
         * accent change would advance its revision and make the paper re-apply a configuration that
         * had not changed — a repaint for a repaint's sake.
         */
        var plain = SnapshotFor(null);
        var themed = SnapshotFor(new ThemeOptions(BpmnThemeMode.Dark, BpmnContrast.High));

        // Same snapshot, theme or no theme: the canvas is told nothing about colour. Compared as
        // the canvas was given it, not as a parameter on the editor — the parameter is null in
        // both renders, so comparing that would pass however much theming leaked into the config.
        Assert.Equal(plain, themed);
    }

    // --- helpers ------------------------------------------------------------------

    /// <summary>The configuration snapshot the canvas was handed, with the revision normalised.</summary>
    /// <remarks>
    /// The revision counts renders rather than describing content, so two snapshots that differ
    /// only in it are the same configuration and should compare equal here.
    /// </remarks>
    private EditorConfig SnapshotFor(ThemeOptions? theme)
    {
        _canvas.Setup<DocumentLoaded?>("loadDocument", _ => true)
            .SetResult(new DocumentLoaded("Diagram_1", 1, 0, []));

        var editor = Render<FluentBpmnEditor>(parameters => parameters
            .Add(p => p.Document, Document())
            .Add(p => p.Theming, theme));

        Assert.Equal(theme?.ThemeAttribute, editor.Find("section.bpmn-editor").GetAttribute("data-bpmn-theme"));

        return JSInterop.Invocations["initialize"][^1].Arguments.OfType<EditorConfig>().Single() with { Revision = 0 };
    }

    private void SetupCanvas()
    {
        _module.Setup<CanvasInitResult>("initialize", _ => true)
            .SetResult(new CanvasInitResult(InstanceId, BpmnInteropContract.Version, 1, true, null));
        _canvas.Setup<DocumentLoaded?>("loadDocument", _ => true)
            .SetResult(new DocumentLoaded("Diagram_1", 1, 0, []));
    }

    private IRenderedComponent<FluentBpmnEditor> RenderEditor(ThemeOptions? theme)
    {
        SetupCanvas();

        return Render<FluentBpmnEditor>(parameters => parameters
            .Add(p => p.Document, Document())
            .Add(p => p.Theming, theme));
    }

    private static BpmnDocument Document() => new BpmnDocumentBuilder()
        .Node("Task_1", BpmnFlowNodeType.Task, new Bounds(200, 120, 100, 80), "Work")
        .Build();
}
