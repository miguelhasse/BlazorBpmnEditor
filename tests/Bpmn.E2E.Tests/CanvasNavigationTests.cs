using System.Globalization;
using System.Text.RegularExpressions;

using Hasseware.AspNetCore.Components.Bpmn.E2E.Tests.Fixtures;

using Microsoft.Playwright;
using Microsoft.Playwright.Xunit.v3;

namespace Hasseware.AspNetCore.Components.Bpmn.E2E.Tests;

/// <summary>
/// The navigation matrix: wheel, keyboard, touch, alignment guides and the accessibility skeleton.
/// </summary>
/// <remarks>
/// All of it belongs in a browser. Wheel deltas arrive in device-dependent units, touch reaches
/// the canvas as pointer events with real capture semantics, and alignment guides are SVG in a
/// live layer — none of which a unit runner can honestly stand in for. The pure arithmetic each of
/// these depends on is unit-tested in the TypeScript suite; this asserts it is actually wired up.
/// </remarks>
public partial class CanvasNavigationTests(DemoAppFixture app) : PageTest, IClassFixture<DemoAppFixture>
{
    private const string CanvasSelector = ".bpmn-canvas__paper > svg";

    // --- keyboard -------------------------------------------------------------

    [Fact]
    public async Task PansWithTheArrowKeys()
    {
        await GoToEditorAsync();
        await PressAsync("0");

        await PressAsync("ArrowRight");
        var right = await ReadTransformAsync();

        // Panning the view right translates the content left, by the configured 50-unit step.
        Assert.Equal(-50d, right.TranslateX);
        Assert.Equal(0d, right.TranslateY);

        await PressAsync("ArrowDown");
        Assert.Equal(-50d, (await ReadTransformAsync()).TranslateY);
    }

    [Fact]
    public async Task ZoomsAndResetsFromTheKeyboard()
    {
        await GoToEditorAsync();
        await PressAsync("0");

        await PressAsync("+");
        Assert.True((await ReadTransformAsync()).Scale > 1d);

        await PressAsync("-");
        await PressAsync("-");
        Assert.True((await ReadTransformAsync()).Scale < 1d);

        await PressAsync("0");
        var reset = await ReadTransformAsync();
        Assert.Equal(1d, reset.Scale);
        Assert.Equal(0d, reset.TranslateX);
    }

    [Fact]
    public async Task FitsFromTheKeyboard()
    {
        await GoToEditorAsync();
        await PressAsync("0");

        await PressAsync("f");

        Assert.NotEqual(1d, (await ReadTransformAsync()).Scale);
    }

    [Fact]
    public async Task LeavesUnboundKeysAlone()
    {
        // The handler only calls preventDefault once a binding matched, so tabbing out of the
        // canvas and typing anywhere else keeps working.
        await GoToEditorAsync();
        await PressAsync("0");
        var before = await ReadTransformAsync();

        await PressAsync("q");

        Assert.Equal(before, await ReadTransformAsync());
    }

    // --- wheel ----------------------------------------------------------------

    [Fact]
    public async Task ScrollsWithAnUnmodifiedWheel()
    {
        await GoToEditorAsync();
        await PressAsync("0");

        await HoverCanvasAsync();
        await Page.Mouse.WheelAsync(0, 120);

        var after = await ReadTransformAsync();
        Assert.Equal(1d, after.Scale);
        Assert.True(after.TranslateY < 0d, $"expected a vertical pan, got {after.TranslateY}");
    }

    [Fact]
    public async Task ZoomsWithCtrlAndTheWheel()
    {
        // What a trackpad pinch reports, and what every browser already treats as zoom.
        await GoToEditorAsync();
        await PressAsync("0");

        await HoverCanvasAsync();
        await Page.Keyboard.DownAsync("Control");
        await Page.Mouse.WheelAsync(0, -120);
        await Page.Keyboard.UpAsync("Control");

        Assert.True((await ReadTransformAsync()).Scale > 1d);
    }

    // --- shortcut help --------------------------------------------------------

    [Fact]
    public async Task OpensShortcutHelpFromItsBoundKeyAndClosesOnEscape()
    {
        await GoToEditorAsync();
        var dialog = Page.ShortcutDialog();

        await PressAsync("?", shift: true);
        await Expect(dialog).ToBeVisibleAsync();

        // Focus must land inside the overlay (a Fluent dialog may hold it on its own surface), or a keyboard user cannot reach Close.
        await Page.WaitForFunctionAsync(
            """
            () => {
                return !!document.activeElement?.closest(".bpmn-shortcut-help");
            }
            """);

        await Page.Keyboard.PressAsync("Escape");
        await Expect(dialog).ToBeHiddenAsync();
    }

    [Fact]
    public async Task ListsTheBoundKeysItActuallyHas()
    {
        await GoToEditorAsync();

        await PressAsync("?", shift: true);

        // Generated from the same binding list the canvas was configured with, so this is the one
        // place a rebinding and its documentation can be seen to agree.
        await Expect(Page.Locator(".bpmn-shortcut-help kbd", new() { HasTextString = "ArrowLeft" })).ToBeVisibleAsync();
        await Expect(Page.Locator(".bpmn-shortcut-help kbd", new() { HasTextString = "Shift + ?" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task OpensShortcutHelpFromTheToolbar()
    {
        await GoToEditorAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Show keyboard shortcuts" }).ClickAsync();

        await Expect(Page.ShortcutDialog()).ToBeVisibleAsync();
    }

    // --- touch ----------------------------------------------------------------

    [Fact]
    public async Task PansWithOneFingerAndPinchZoomsWithTwo()
    {
        await GoToEditorAsync();
        await PressAsync("0");

        // Dispatched as pointer events because that is how touch reaches the canvas; Playwright's
        // touchscreen API only produces taps.
        await Page.EvaluateAsync(
            """
            () => {
              // The paper mount, not the canvas root: JointJS binds its pointer handlers to the
              // element it renders into, and an event dispatched on the ancestor bubbles upward
              // past it rather than down to it.
              const canvas = document.querySelector('.bpmn-canvas__paper');
              canvas.setPointerCapture = () => {};
              canvas.hasPointerCapture = () => false;
              canvas.releasePointerCapture = () => {};
              const send = (type, id, x, y) => canvas.dispatchEvent(new PointerEvent(type, {
                pointerId: id, pointerType: 'touch', isPrimary: id === 1,
                clientX: x, clientY: y, bubbles: true, cancelable: true,
              }));
              send('pointerdown', 1, 300, 300);
              send('pointermove', 1, 360, 340);
              send('pointerup', 1, 360, 340);
            }
            """);

        var panned = await ReadTransformAsync();
        Assert.Equal(60d, panned.TranslateX);
        Assert.Equal(40d, panned.TranslateY);

        await PressAsync("0");
        await Page.EvaluateAsync(
            """
            () => {
              const canvas = document.querySelector('.bpmn-canvas__paper');
              const send = (type, id, x, y) => canvas.dispatchEvent(new PointerEvent(type, {
                pointerId: id, pointerType: 'touch', isPrimary: id === 1,
                clientX: x, clientY: y, bubbles: true, cancelable: true,
              }));
              send('pointerdown', 1, 200, 300);
              send('pointerdown', 2, 400, 300);
              send('pointermove', 1, 100, 300);
              send('pointermove', 2, 500, 300);
              send('pointerup', 1, 100, 300);
              send('pointerup', 2, 500, 300);
            }
            """);

        // Fingers 200 apart spread to 400: exactly twice the distance, so exactly twice the scale.
        Assert.Equal(2d, (await ReadTransformAsync()).Scale, 3);
        await Expect(Page.Locator(".bpmn-canvas--panning")).ToHaveCountAsync(0);
    }

    // --- alignment guides -----------------------------------------------------

    [Fact]
    public async Task SnapsAMovedShapeToItsNeighbourAndDrawsAGuide()
    {
        await GoToEditorAsync();

        // The Task sits at x=220 in simple-process.bpmn. Moving the Start Event to 216 is 4 units
        // off its left edge, inside the 6-unit tolerance.
        var landed = await MoveStartEventAsync(216);

        Assert.Equal(220d, landed);
        await Expect(Page.Locator(".bpmn-snapline")).ToHaveCountAsync(1);
    }

    [Fact]
    public async Task LeavesAShapeAloneWhenNothingIsInRange()
    {
        await GoToEditorAsync();

        var landed = await MoveStartEventAsync(150);

        Assert.Equal(150d, landed);
        await Expect(Page.Locator(".bpmn-snapline")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task ClearsGuidesOnceMovementStops()
    {
        await GoToEditorAsync();

        await MoveStartEventAsync(216);
        await Expect(Page.Locator(".bpmn-snapline")).ToHaveCountAsync(1);

        // Cleared on an idle timer rather than on pointer-up, so the rule holds for every way a
        // shape can move.
        await Expect(Page.Locator(".bpmn-snapline")).ToHaveCountAsync(0, new() { Timeout = 5_000 });
    }

    // --- accessibility skeleton ----------------------------------------------

    [Fact]
    public async Task ExposesLandmarkRegions()
    {
        await GoToEditorAsync();

        await Expect(Page.GetByRole(AriaRole.Region, new() { Name = "BPMN editor" })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Toolbar, new() { Name = "Diagram tools" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task ExposesAFocusableCanvasWithAnAccessibleName()
    {
        await GoToEditorAsync();

        var canvas = Page.Locator(".bpmn-canvas");

        await Expect(canvas).ToHaveAttributeAsync("tabindex", "0");
        await Expect(canvas).ToHaveAttributeAsync("aria-label", "BPMN diagram canvas");

        await canvas.FocusAsync();
        await Expect(canvas).ToBeFocusedAsync();
    }

    [Fact]
    public async Task AnnouncesWhatTheCanvasIsShowing()
    {
        await GoToEditorAsync();

        var summary = Page.Locator(".bpmn-canvas__summary");

        await Expect(summary).ToHaveAttributeAsync("aria-live", "polite");
        await Expect(summary).ToContainTextAsync("3 shapes, 2 connectors");
    }

    [Fact]
    public async Task NamesEveryDrawnCellByItsBpmnType()
    {
        await GoToEditorAsync();

        // Read as "User Task: Review Request" — type first, because many BPMN elements
        // legitimately have no name and the type is what identifies them.
        await Expect(Page.Locator("[aria-label='Start Event: Start']")).ToHaveCountAsync(1);
        await Expect(Page.Locator("[aria-label='User Task: Review Request']")).ToHaveCountAsync(1);
        await Expect(Page.Locator("[aria-label='Sequence Flow']")).ToHaveCountAsync(2);

        var roles = await Page.Locator(".joint-cell[aria-label]").EvaluateAllAsync<string[]>(
            "nodes => [...new Set(nodes.map(n => n.getAttribute('role')))]");

        Assert.Equal(["graphics-symbol"], roles);
    }

    // --- helpers --------------------------------------------------------------

    [GeneratedRegex(@"matrix\(([-\d.e]+),[-\d.e]+,[-\d.e]+,[-\d.e]+,([-\d.e]+),([-\d.e]+)\)")]
    private static partial Regex MatrixPattern { get; }

    /// <summary>Puts the pointer over the canvas, since a wheel event goes wherever it already is.</summary>
    private async Task HoverCanvasAsync()
    {
        var box = await Page.Locator(".bpmn-canvas").BoundingBoxAsync().ConfigureAwait(true);
        Assert.NotNull(box);
        await Page.Mouse.MoveAsync(box.X + (box.Width / 2), box.Y + (box.Height / 2)).ConfigureAwait(true);
    }

    private async Task PressAsync(string key, bool shift = false)
    {
        await Page.Locator(".bpmn-canvas").FocusAsync();
        await Page.Keyboard.PressAsync(shift ? $"Shift+{key}" : key);
    }

    /// <summary>
    /// Moves the Start Event through the module's debug-only hook and returns where it landed.
    /// </summary>
    /// <remarks>
    /// Element dragging arrives with the editing interactions. Snapping listens to position
    /// changes rather than to a drag handler, so this drives exactly the path a pointer will.
    /// </remarks>
    private async Task<double> MoveStartEventAsync(double x)
    {
        await SkipUnlessMoveHookAvailableAsync();

        return await Page.EvaluateAsync<double>(
            """
            async (x) => {
              const canvas = document.querySelector('.bpmn-canvas');
              const mod = await import('/_content/Hasseware.AspNetCore.Components.Bpmn/Hasseware.AspNetCore.Components.Bpmn.lib.module.js');
              const id = [...document.querySelectorAll('.bpmn-canvas .joint-cell[model-id]')]
                .map((c) => c.getAttribute('model-id'))
                .find((i) => i.startsWith('Event_'));
              mod.__moveElement(canvas.dataset.bpmnInstance, id, x, 400);
              await new Promise((r) => setTimeout(r, 150));
              const el = document.querySelector(`.joint-cell[model-id="${id}"]`);
              return Number(el.getAttribute('transform').match(/translate\(\s*([-\d.]+)/)[1]);
            }
            """,
            x);
    }

    /// <summary>
    /// Skips when the loaded bundle has no move hook.
    /// </summary>
    /// <remarks>
    /// The hook is excluded from Release bundles, and the bundle's build mode is whichever
    /// configuration was built last — not which host is running. Skipping says so out loud; the
    /// alternative is a test that passes without asserting anything. These become ordinary drag
    /// tests once the editing interactions exist.
    /// </remarks>
    private async Task SkipUnlessMoveHookAvailableAsync()
    {
        var available = await Page.EvaluateAsync<bool>(
            """
            async () => {
              const mod = await import('/_content/Hasseware.AspNetCore.Components.Bpmn/Hasseware.AspNetCore.Components.Bpmn.lib.module.js');
              return typeof mod.__moveElement === 'function' && mod.__instanceCount() >= 0;
            }
            """).ConfigureAwait(true);

        Assert.SkipUnless(available, "The loaded canvas bundle is a Release build, which excludes the move hook.");
    }

    private async Task<(double Scale, double TranslateX, double TranslateY)> ReadTransformAsync()
    {
        var transform = await Page.Locator(".bpmn-canvas__paper > svg > g").First
            .GetAttributeAsync("transform")
            .ConfigureAwait(true);

        // An untransformed paper has no attribute at all, which is identity.
        if (transform is null) return (1d, 0d, 0d);

        var match = MatrixPattern.Match(transform);
        Assert.True(match.Success, $"unrecognised transform '{transform}'");

        return (
            double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
            double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
            double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture));
    }

    private async Task GoToEditorAsync()
    {
        await Page.GotoAsync(app.Url(this));

        await Expect(Page.Locator(CanvasSelector)).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.Locator(".bpmn-canvas .joint-cell").First).ToBeVisibleAsync();
    }
}
