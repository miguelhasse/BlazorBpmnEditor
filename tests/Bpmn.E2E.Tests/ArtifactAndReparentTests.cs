using Hasseware.AspNetCore.Components.Bpmn.E2E.Tests.Fixtures;

using Microsoft.Playwright;
using Microsoft.Playwright.Xunit.v3;

namespace Hasseware.AspNetCore.Components.Bpmn.E2E.Tests;

/// <summary>
/// The artifact and data-reference gestures, plus group movement and reparenting.
/// </summary>
/// <remarks>
/// The commands are unit-tested against documents; what a browser shows is whether the palette
/// reaches them.
/// </remarks>
public sealed class ArtifactAndReparentTests(DemoAppFixture app) : PageTest, IClassFixture<DemoAppFixture>
{
    [Theory]
    [InlineData("Text Annotation")]
    [InlineData("Group")]
    [InlineData("Data Object Reference")]
    [InlineData("Data Store Reference")]
    public async Task DroppingAnArtifactOrDataToolCreatesIt(string tool)
    {
        await GoToEditorAsync();
        var before = await CellCountAsync();

        var (x, y) = await FreeSpotAsync();
        await DropToolAsync(tool, x, y);

        await Expect(Page.Locator(".bpmn-canvas .joint-cell")).ToHaveCountAsync(before + 1);
        await Expect(Page.Locator(".demo-edit-status")).ToContainTextAsync("Edited");
    }

    [Fact]
    public async Task AnArtifactMayOverlapAFlowNode()
    {
        // DI-011 governs Events, Activities and Gateways. A Group is meant to enclose things.
        await GoToEditorAsync();
        var before = await CellCountAsync();

        var task = await Page.Locator(".joint-cell[model-id^='Task_'] [joint-selector='body']").First.BoundingBoxAsync();
        await DropToolAsync("Group", task!.X + (task.Width / 2), task.Y + (task.Height / 2));

        await Expect(Page.Locator(".bpmn-canvas .joint-cell")).ToHaveCountAsync(before + 1);
    }

    // --- group movement --------------------------------------------------------

    [Fact]
    public async Task DraggingOneOfTwoSelectedElementsMovesBoth()
    {
        await GoToEditorAsync();
        await SelectSampleAsync("order-process.bpmn");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Fit" }).ClickAsync();
        await Page.WaitForTimeoutAsync(700);

        await ClickBodyAsync("Task_ValidateOrder", shift: false);
        await ClickBodyAsync("Task_ShipOrder", shift: true);
        await Expect(Page.Locator(".joint-highlight-mask")).ToHaveCountAsync(2);

        var leaderBefore = await PositionAsync("Task_ValidateOrder");
        var companionBefore = await PositionAsync("Task_ShipOrder");

        var grip = await Page.Locator(".joint-cell[model-id='Task_ValidateOrder'] [joint-selector='body']")
            .First.BoundingBoxAsync();

        /*
         * The drag is stated in model units and converted through the paper's own scale, rather
         * than in screen pixels.
         *
         * Fit takes whatever scale the viewport allows, so a fixed pixel drag covers a different
         * distance on the diagram whenever the demo's chrome changes height. In this fixture
         * Task_ShipOrder sits at y=100 and Task_RejectOrder at y=290, so a downward move of 105
         * clears it and one of 110 overlaps it: the test used to live inside that five-unit margin
         * and failed with a DI-011 refusal when a toolbar row grew by fourteen pixels. Nothing sits
         * above either task, so up 100 is free at any scale.
         */
        var scale = await ScaleAsync();
        var startX = grip!.X + (grip.Width / 2);
        var startY = grip.Y + (grip.Height / 2);

        await Page.Mouse.MoveAsync(startX, startY);
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(startX, startY - (100 * scale), new() { Steps = 10 });
        await Page.Mouse.UpAsync();
        await Page.WaitForTimeoutAsync(1000);

        // The leader too, not only the companion: a refused move leaves everything exactly where it
        // was, and asserting the companion alone reports that as "the group did not follow".
        Assert.NotEqual(leaderBefore, await PositionAsync("Task_ValidateOrder"));

        // JointJS moves what is under the pointer; the selection is the editor's idea, so carrying
        // the rest of it along is the editor's job.
        Assert.NotEqual(companionBefore, await PositionAsync("Task_ShipOrder"));
    }

    // --- reparenting -----------------------------------------------------------

    [Fact]
    public async Task DraggingANodeIntoAnotherLaneReportsAReparent()
    {
        await GoToEditorAsync();
        await SelectSampleAsync("pizza-store.bpmn");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Fit" }).ClickAsync();
        await Page.WaitForTimeoutAsync(900);

        var task = await Page.EvaluateAsync<string>(
            "() => [...document.querySelectorAll('.bpmn-canvas .joint-element[model-id]')].find((c) => (c.getAttribute('aria-label') ?? '').startsWith('Task: Bake'))?.getAttribute('model-id') ?? ''");

        var lane = await Page.EvaluateAsync<string>(
            "() => [...document.querySelectorAll('.bpmn-canvas .joint-element[model-id]')].find((c) => (c.getAttribute('aria-label') ?? '').startsWith('Lane'))?.getAttribute('model-id') ?? ''");

        Assert.NotEqual(string.Empty, task);
        Assert.NotEqual(string.Empty, lane);

        var laneBox = await Page.Locator($".joint-cell[model-id='{lane}']").BoundingBoxAsync();
        var taskBox = await Page.Locator($".joint-cell[model-id='{task}'] [joint-selector='body']").First.BoundingBoxAsync();

        await Page.Mouse.MoveAsync(taskBox!.X + (taskBox.Width / 2), taskBox.Y + (taskBox.Height / 2));
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(laneBox!.X + laneBox.Width - 220, laneBox.Y + (laneBox.Height / 2), new() { Steps = 12 });
        await Page.Mouse.UpAsync();
        await Page.WaitForTimeoutAsync(1200);

        // The lane membership itself is asserted in the unit tests, which can read the document.
        // What a browser proves is that the drop became a reparent rather than a bare move.
        await Expect(Page.Locator(".demo-edit-status")).ToContainTextAsync("Edited");
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Undo" })).ToBeEnabledAsync();
    }

    [Fact]
    public async Task DraggingANodeIntoAnotherPoolTransfersItInsteadOfGrowingItsOwn()
    {
        const string customer = "id-b4f2b221-f282-4363-88c6-9d058e0b1824";
        const string vendor = "id-faefcc69-77a6-4248-9872-f9c33fce5055";

        await GoToEditorAsync();
        await SelectSampleAsync("pizza-store.bpmn");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Fit" }).ClickAsync();
        await Page.WaitForTimeoutAsync(900);

        var task = await Page.EvaluateAsync<string>(
            "() => [...document.querySelectorAll('.bpmn-canvas .joint-element[model-id]')].find((c) => (c.getAttribute('aria-label') ?? '').startsWith('Task: Select a pizza'))?.getAttribute('model-id') ?? ''");

        Assert.NotEqual(string.Empty, task);

        var scale = await ScaleAsync();
        var customerBefore = await Page.Locator($".joint-cell[model-id='{customer}']").BoundingBoxAsync();
        var vendorBox = await Page.Locator($".joint-cell[model-id='{vendor}']").BoundingBoxAsync();
        var taskBox = await Page.Locator($".joint-cell[model-id='{task}'] [joint-selector='body']").First.BoundingBoxAsync();

        await Page.Mouse.MoveAsync(taskBox!.X + (taskBox.Width / 2), taskBox.Y + (taskBox.Height / 2));
        await Page.Mouse.DownAsync();
        // Model point (1100, 740): the vendor's middle Lane, clear of its shapes. The Pool's origin is (40, 522).
        await Page.Mouse.MoveAsync(vendorBox!.X + ((1100 - 40) * scale), vendorBox.Y + ((740 - 522) * scale), new() { Steps = 12 });
        await Page.Mouse.UpAsync();
        await Page.WaitForTimeoutAsync(1200);

        await Expect(Page.Locator(".demo-edit-status")).ToContainTextAsync("Edited");

        // The source Pool kept its size: the node left it rather than stretching it.
        var customerAfter = await Page.Locator($".joint-cell[model-id='{customer}']").BoundingBoxAsync();
        Assert.Equal(customerBefore!.Height / scale, customerAfter!.Height / await ScaleAsync(), 1);

        // And the node now sits inside the vendor Pool, which owns it.
        vendorBox = await Page.Locator($".joint-cell[model-id='{vendor}']").BoundingBoxAsync();
        taskBox = await Page.Locator($".joint-cell[model-id='{task}'] [joint-selector='body']").First.BoundingBoxAsync();
        var cx = taskBox!.X + (taskBox.Width / 2);
        var cy = taskBox.Y + (taskBox.Height / 2);
        Assert.InRange(cx, vendorBox!.X, vendorBox.X + vendorBox.Width);
        Assert.InRange(cy, vendorBox.Y, vendorBox.Y + vendorBox.Height);
    }

    // --- hit areas and the quick-action strip ----------------------------------

    [Fact]
    public async Task ATextAnnotationIsGrabbedNearItsBracket()
    {
        await GoToEditorAsync();
        var (x, y) = await FreeSpotAsync();
        await DropToolAsync("Text Annotation", x, y);

        var annotation = Page.Locator(".bpmn-canvas .joint-element[model-id^='TextAnnotation_']").Last;
        await Expect(annotation).ToBeVisibleAsync();
        var id = await annotation.GetAttributeAsync("model-id")
            ?? throw new InvalidOperationException("the dropped annotation has no id");
        var bracket = await annotation.Locator("[joint-selector='bracket']").BoundingBoxAsync()
            ?? throw new InvalidOperationException("the annotation has no bracket");

        // A few pixels right of the 1px bracket: the tolerance band, not the stroke, takes the press.
        var near = (X: bracket.X + 4, Y: bracket.Y + (bracket.Height / 2));
        var hit = await Page.EvaluateAsync<string?>(
            "p => document.elementFromPoint(p.x, p.y)?.getAttribute('joint-selector') ?? null",
            new { x = (double)near.X, y = (double)near.Y });
        Assert.Equal("hitArea", hit);

        // The text block is hit as a whole; the empty box right of it, past the band, stays click-through.
        var right = bracket.X + (100 * await ScaleAsync());
        var text = await annotation.Locator("[joint-selector='textHitArea']").BoundingBoxAsync()
            ?? throw new InvalidOperationException("the annotation has no text hit area");
        var textRight = text.X + text.Width;
        Assert.True(right - textRight > 12, $"no empty room right of the text: right={right} text={text.X},{text.Width}");

        var onText = await Page.EvaluateAsync<string?>(
            "p => document.elementFromPoint(p.x, p.y)?.getAttribute('joint-selector') ?? null",
            new { x = (double)(text.X + (text.Width / 2)), y = (double)(text.Y + (text.Height / 2)) });
        Assert.Equal("textHitArea", onText);

        var interior = await Page.EvaluateAsync<string?>(
            "p => document.elementFromPoint(p.x, p.y)?.closest('.joint-cell')?.getAttribute('model-id') ?? null",
            new { x = (double)((textRight + right) / 2), y = (double)(text.Y + (text.Height / 2)) });
        Assert.NotEqual(id, interior);

        var before = await PositionAsync(id);
        await Page.Mouse.MoveAsync(near.X, near.Y);
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(near.X - 60, near.Y - 30, new() { Steps = 10 });
        await Page.Mouse.UpAsync();
        await Page.WaitForTimeoutAsync(900);

        await Expect(Page.Locator(".joint-highlight-mask")).ToHaveCountAsync(1);
        Assert.NotEqual(before, await PositionAsync(id));
    }

    [Fact]
    public async Task TheQuickActionStripFollowsAMovedElement()
    {
        await GoToEditorAsync();
        await SelectSampleAsync("order-process.bpmn");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Fit" }).ClickAsync();
        await Page.WaitForTimeoutAsync(700);

        await ClickBodyAsync("Task_ValidateOrder", shift: false);
        await Expect(Page.Locator(".bpmn-actions")).ToHaveCountAsync(1);
        await AssertStripBesideAsync("Task_ValidateOrder", "after selecting");

        // Up 100 model units is free in this fixture at any scale (see the group-move test above).
        var scale = await ScaleAsync();
        var grip = await Page.Locator(".joint-cell[model-id='Task_ValidateOrder'] [joint-selector='body']")
            .First.BoundingBoxAsync() ?? throw new InvalidOperationException("no Task body");
        var startX = grip.X + (grip.Width / 2);
        var startY = grip.Y + (grip.Height / 2);

        await Page.Mouse.MoveAsync(startX, startY);
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(startX, startY - (100 * scale), new() { Steps = 12 });

        // Hidden while the gesture lasts: it would only be in the way, and stale.
        await Expect(Page.Locator(".bpmn-actions")).ToHaveCountAsync(0);
        await Page.Mouse.UpAsync();

        await Expect(Page.Locator(".bpmn-actions")).ToHaveCountAsync(1);
        await Page.WaitForTimeoutAsync(500);
        await AssertStripBesideAsync("Task_ValidateOrder", "after a move");

        // And when the view changes under it.
        await Page.GetByRole(AriaRole.Button, new() { Name = "Zoom in" }).ClickAsync();
        await Page.WaitForTimeoutAsync(700);
        await Expect(Page.Locator(".bpmn-actions")).ToHaveCountAsync(1);
        await AssertStripBesideAsync("Task_ValidateOrder", "after a zoom");
    }

    private async Task AssertStripBesideAsync(string modelId, string when)
    {
        // Exclude selection tools from the shape bounds (BpmnContextualActions.Gap).
        var offsets = await Page.EvaluateAsync<double[]>(
            """
            id => {
              const strip = document.querySelector('.bpmn-actions').getBoundingClientRect();
              const shape = document.querySelector(`.joint-cell[model-id="${id}"] [joint-selector="body"]`)
                .getBoundingClientRect();
              return [strip.x - shape.right, strip.y - shape.y];
            }
            """,
            modelId);

        Assert.True(
            Math.Abs(offsets[0] - 26) <= 3 && Math.Abs(offsets[1]) <= 3,
            $"{when}: the strip was {offsets[0]:0.#},{offsets[1]:0.#} from the element instead of beside it");
    }

    // --- helpers --------------------------------------------------------------

    private Task<int> CellCountAsync() => Page.Locator(".bpmn-canvas .joint-cell[model-id]").CountAsync();

    /// <summary>
    /// The paper's current zoom, so a drag can be stated in model units instead of screen pixels.
    /// </summary>
    private Task<float> ScaleAsync() => Page.EvaluateAsync<float>(
        """
        () => {
          const layer = document.querySelector('.bpmn-canvas svg g');
          return layer == null ? 1 : new DOMMatrix(layer.getAttribute('transform') ?? '').a;
        }
        """);

    private Task<string> PositionAsync(string modelId) => Page.EvaluateAsync<string>(
        """
        id => {
          const el = document.querySelector(`.joint-cell[model-id="${id}"]`);
          const m = el?.getAttribute('transform')?.match(/translate\(\s*([-\d.]+)[ ,]+([-\d.]+)/);
          return m == null ? 'none' : `${m[1]},${m[2]}`;
        }
        """,
        modelId);

    private async Task<(float X, float Y)> FreeSpotAsync()
    {
        var canvas = await Page.Locator(".bpmn-canvas").BoundingBoxAsync()
            ?? throw new InvalidOperationException("the canvas has no bounding box");

        return (canvas.X + canvas.Width - 170, canvas.Y + canvas.Height - 140);
    }

    private async Task DropToolAsync(string label, float x, float y)
    {
        await Page.Locator(".bpmn-stencil__tool", new() { HasText = label }).First.HoverAsync();
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(x, y, new() { Steps = 8 });
        await Page.Mouse.UpAsync();
        await Page.WaitForTimeoutAsync(700);
    }

    private async Task ClickBodyAsync(string modelId, bool shift)
    {
        var body = await Page
            .Locator($".joint-cell[model-id='{modelId}'] [joint-selector='body'], .joint-cell[model-id='{modelId}'] [joint-selector='outerRing']")
            .First.BoundingBoxAsync() ?? throw new InvalidOperationException($"no drawn body for '{modelId}'");

        if (shift) await Page.Keyboard.DownAsync("Shift");
        await Page.Mouse.ClickAsync(body.X + (body.Width / 2), body.Y + (body.Height / 2));
        if (shift) await Page.Keyboard.UpAsync("Shift");

        await Page.WaitForTimeoutAsync(400);
    }

    private async Task SelectSampleAsync(string fileName)
    {
        await Page.OpenDemoFilesAsync();
        await Page.GetByRole(AriaRole.Combobox, new() { Name = "Sample diagram" }).ClickAsync();
        await Page.Locator("fluent-option", new() { HasTextString = fileName }).ClickAsync();
        await Page.WaitForTimeoutAsync(1500);
    }

    private async Task GoToEditorAsync()
    {
        await Page.GotoAsync($"{app.BaseUrl}/editor");

        await Expect(Page.Locator(".bpmn-canvas__paper > svg")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.Locator(".bpmn-canvas .joint-cell").First).ToBeVisibleAsync();
        await Expect(Page.Locator(".bpmn-stencil__tool").First).ToBeVisibleAsync();
    }
}
