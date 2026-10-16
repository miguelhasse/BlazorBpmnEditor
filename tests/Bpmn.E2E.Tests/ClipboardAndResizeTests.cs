using Hasseware.AspNetCore.Components.Bpmn.E2E.Tests.Fixtures;

using Microsoft.Playwright;
using Microsoft.Playwright.Xunit.v3;

namespace Hasseware.AspNetCore.Components.Bpmn.E2E.Tests;

/// <summary>
/// The clipboard, rectangular selection and resizing, in a real browser.
/// </summary>
/// <remarks>
/// The commands are unit-tested against documents, which is where the reference rewiring and the
/// Lane arithmetic belong. What only a browser shows is the wiring: that a modifier reaches the
/// right gesture, that a preview is drawn and cleared, and that a committed resize is actually
/// re-rendered — the last of which was broken here and passed every unit test.
/// </remarks>
public sealed class ClipboardAndResizeTests(DemoAppFixture app) : PageTest, IClassFixture<DemoAppFixture>
{
    [Fact]
    public async Task CopyAndPasteAddsACopyAndSelectsIt()
    {
        await GoToEditorAsync();
        await SelectFirstTaskAsync();

        var before = await CellCountAsync();

        await Page.Keyboard.PressAsync("Control+c");
        await Page.Keyboard.PressAsync("Control+v");

        await Expect(Page.Locator(".bpmn-canvas .joint-cell")).ToHaveCountAsync(before + 1);

        // Selecting what arrived is what makes a second paste land somewhere sensible, and it is
        // what the user expects to be holding afterwards.
        await Expect(Page.Locator(".joint-highlight-mask")).ToHaveCountAsync(1);
    }

    [Fact]
    public async Task PastingTwiceAddsTwoCopiesAndLeavesTheOriginal()
    {
        await GoToEditorAsync();
        var original = await FirstTaskIdAsync();
        await SelectFirstTaskAsync();

        var before = await CellCountAsync();

        await Page.Keyboard.PressAsync("Control+c");
        await Page.Keyboard.PressAsync("Control+v");
        await Expect(Page.Locator(".bpmn-canvas .joint-cell")).ToHaveCountAsync(before + 1);

        await Page.Keyboard.PressAsync("Control+v");
        await Expect(Page.Locator(".bpmn-canvas .joint-cell")).ToHaveCountAsync(before + 2);

        // Section 2.7: each occurrence is a distinct, unrelated set of elements.
        await Expect(Page.Locator($".joint-cell[model-id='{original}']")).ToHaveCountAsync(1);
    }

    [Fact]
    public async Task CutRemovesTheSelectionAndPasteBringsItBack()
    {
        await GoToEditorAsync();
        await SelectFirstTaskAsync();

        var before = await CellCountAsync();

        await Page.Keyboard.PressAsync("Control+x");
        await Page.WaitForTimeoutAsync(800);

        /*
         * Asserted on the cell count rather than the status line. Deleting the selection also
         * empties it, so the demo's one status element ends up holding "Nothing selected" — two
         * sources writing the same line, which says nothing about whether the cut worked.
         *
         * The Task's two flows go with it, so the count drops by more than one.
         */
        var afterCut = await CellCountAsync();
        Assert.True(afterCut < before, $"cut removed nothing: still {afterCut} cells");

        await Page.Keyboard.PressAsync("Control+v");
        await Page.WaitForTimeoutAsync(900);

        // Back on the canvas, though its flows are not: they lost an endpoint when it was cut.
        Assert.True(await CellCountAsync() > afterCut, "paste brought nothing back");
    }

    [Fact]
    public async Task DuplicateAddsACopyWithoutTouchingTheClipboard()
    {
        await GoToEditorAsync();
        await SelectFirstTaskAsync();

        var before = await CellCountAsync();

        await Page.Keyboard.PressAsync("Control+d");
        await Expect(Page.Locator(".bpmn-canvas .joint-cell")).ToHaveCountAsync(before + 1);
    }

    [Fact]
    public async Task ReadOnlyAllowsCopyingButNotCuttingOrPasting()
    {
        await Page.GotoAsync($"{app.BaseUrl}/gallery");
        await Expect(Page.Locator(".bpmn-canvas .joint-cell").First).ToBeVisibleAsync(new() { Timeout = 30_000 });

        var before = await CellCountAsync();

        await Page.Locator(".bpmn-canvas").FocusAsync();
        await Page.Keyboard.PressAsync("Control+a");

        // Copy changes nothing about the document, so section 7's "lose every mutation surface"
        // does not reach it; cut and paste are mutations and must not land.
        await Page.Keyboard.PressAsync("Control+c");
        await Page.Keyboard.PressAsync("Control+x");
        await Page.Keyboard.PressAsync("Control+v");
        await Page.WaitForTimeoutAsync(800);

        await Expect(Page.Locator(".bpmn-canvas .joint-cell")).ToHaveCountAsync(before);
    }

    // --- rectangular selection ------------------------------------------------

    [Fact]
    public async Task ShiftDraggingBlankPaperSelectsWhatItEncloses()
    {
        await GoToEditorAsync();
        var canvas = await Page.Locator(".bpmn-canvas").BoundingBoxAsync();

        await BandAsync(canvas!.X + 20, canvas.Y + 20, canvas.X + canvas.Width - 30, canvas.Y + canvas.Height - 30);

        await Expect(Page.Locator(".demo-edit-status")).ToContainTextAsync("Selected");
        Assert.True(await Page.Locator(".joint-highlight-mask").CountAsync() > 1, "the band selected nothing");

        // Transient interface: it must not be left on the canvas.
        await Expect(Page.Locator(".bpmn-rubber-band")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task TheBandDoesNotPanTheCanvas()
    {
        await GoToEditorAsync();
        var canvas = await Page.Locator(".bpmn-canvas").BoundingBoxAsync();
        var before = await TransformAsync();

        await BandAsync(canvas!.X + 20, canvas.Y + 20, canvas.X + 300, canvas.Y + 300);

        // Both gestures start on blank paper with the primary button, so one has to yield.
        Assert.Equal(before, await TransformAsync());
    }

    [Fact]
    public async Task APlainDragStillPans()
    {
        await GoToEditorAsync();
        var canvas = await Page.Locator(".bpmn-canvas").BoundingBoxAsync();
        var before = await TransformAsync();

        await Page.Mouse.MoveAsync(canvas!.X + canvas.Width - 80, canvas.Y + canvas.Height - 60);
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(canvas.X + canvas.Width - 160, canvas.Y + canvas.Height - 140, new() { Steps = 8 });
        await Page.Mouse.UpAsync();
        await Page.WaitForTimeoutAsync(400);

        Assert.NotEqual(before, await TransformAsync());
    }

    [Fact]
    public async Task ClickingBlankPaperClearsTheSelection()
    {
        await GoToEditorAsync();
        await SelectFirstTaskAsync();

        await Expect(Page.Locator(".joint-highlight-mask")).ToHaveCountAsync(1);

        // A press on empty canvas, not a drag: the corner of the canvas holds no shape.
        var canvas = await Page.Locator(".bpmn-canvas").BoundingBoxAsync();
        await Page.Mouse.ClickAsync(canvas!.X + 8, canvas.Y + 8);

        await Expect(Page.Locator(".joint-highlight-mask")).ToHaveCountAsync(0);
    }

    // --- resize ---------------------------------------------------------------

    [Fact]
    public async Task APoolOffersResizeHandlesAndAPlainTaskDoesNot()
    {
        await GoToEditorAsync();
        await SelectSampleAsync("collaboration.bpmn");

        var pool = await Page.Locator(".joint-cell[model-id^='Pool_']").First.GetAttributeAsync("model-id");
        await SelectPoolHeaderAsync(pool!);

        await Expect(Page.Locator(".bpmn-resize-handle")).ToHaveCountAsync(8);

        await SelectFirstTaskAsync();

        // BPMN gives Events and Gateways fixed notation sizes, and a resized Task is a layout
        // choice this milestone has no command for.
        await Expect(Page.Locator(".bpmn-resize-handle")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task DraggingAPoolHandleResizesItAndTheChangeIsRendered()
    {
        await GoToEditorAsync();
        await SelectSampleAsync("collaboration.bpmn");

        var pool = await Page.Locator(".joint-cell[model-id^='Pool_']").First.GetAttributeAsync("model-id");
        await SelectPoolHeaderAsync(pool!);

        var before = await PoolWidthAsync(pool!);

        // The east edge: the one handle whose drag changes only the width.
        var handle = await Page.Locator(".bpmn-resize-handle").Nth(3).BoundingBoxAsync();
        await Page.Mouse.MoveAsync(handle!.X + (handle.Width / 2), handle.Y + (handle.Height / 2));
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(handle.X + 80, handle.Y, new() { Steps = 8 });

        // A preview, not the shape: whether the change is legal is the host's to decide.
        await Expect(Page.Locator(".bpmn-resize-preview")).ToHaveCountAsync(1);

        await Page.Mouse.UpAsync();
        await Page.WaitForTimeoutAsync(900);

        // Re-rendered, which is the part that was broken: the canvas only drew a preview, so
        // telling it the new document was already on screen left the Pool at its old size.
        Assert.True(await PoolWidthAsync(pool!) > before + 20, "the pool did not grow");
        await Expect(Page.Locator(".bpmn-resize-preview")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task AGroupMovesFreelyOverOtherShapes()
    {
        await GoToEditorAsync();
        var (x, y) = await FreeSpotAsync();
        await DropToolAsync("Group", x, y);

        var group = Page.Locator(".bpmn-canvas .joint-element[model-id^='Group_']").Last;
        await Expect(group).ToBeVisibleAsync();
        var bounds = await group.Locator("[joint-selector='body']").BoundingBoxAsync()
            ?? throw new InvalidOperationException("the dropped Group has no body");

        var task = Page.Locator($".joint-cell[model-id='{await FirstTaskIdAsync()}']");
        var target = await task.Locator("[joint-selector='body']").First.BoundingBoxAsync()
            ?? throw new InvalidOperationException("the sample has no Task");

        // Grab the outline and lay it across the Task: overlap and containment rules do not apply to a Group.
        await Page.Mouse.MoveAsync(bounds.X + 2, bounds.Y + (bounds.Height / 2));
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(target.X + (target.Width / 2), target.Y + (target.Height / 2), new() { Steps = 12 });
        await Page.Mouse.UpAsync();
        await Page.WaitForTimeoutAsync(900);

        await Expect(Page.Locator(".demo-edit-status")).ToContainTextAsync("Edited");
        var moved = await group.Locator("[joint-selector='body']").BoundingBoxAsync()
            ?? throw new InvalidOperationException("the Group vanished");
        Assert.InRange(moved.X, target.X, target.X + target.Width);
    }

    [Fact]
    public async Task GroupCanBeSelectedResizedAndDeletedWithoutMenus()
    {
        await GoToEditorAsync();
        var (x, y) = await FreeSpotAsync();
        await DropToolAsync("Group", x, y);

        var group = Page.Locator(".bpmn-canvas .joint-element[model-id^='Group_']").Last;
        await Expect(group).ToBeVisibleAsync();

        var groupId = await group.GetAttributeAsync("model-id")
            ?? throw new InvalidOperationException("the dropped Group has no id");
        var bounds = await group.Locator("[joint-selector='body']").BoundingBoxAsync()
            ?? throw new InvalidOperationException("the dropped Group has no body");
        Assert.True(
            double.IsFinite(bounds.X) && double.IsFinite(bounds.Y)
                && double.IsFinite(bounds.Width) && double.IsFinite(bounds.Height),
            $"the Group bounds are not finite: {bounds}");

        await Page.Mouse.ClickAsync(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));
        await Expect(Page.Locator(".joint-highlight-mask")).ToHaveCountAsync(0);

        // Off the outline by a few pixels, inside the Group: the tolerance band, not a dash, takes the click.
        var nearOutline = (X: bounds.X + 4, Y: bounds.Y + (bounds.Height / 2));
        var hitSelector = await Page.EvaluateAsync<string?>(
            "point => document.elementFromPoint(point.x, point.y)?.getAttribute('joint-selector') ?? null",
            new { x = (double)nearOutline.X, y = (double)nearOutline.Y });
        Assert.Equal("hitArea", hitSelector);

        // The transparent interior remains click-through; a band around its border is the selection target.
        await Page.Mouse.ClickAsync(nearOutline.X, nearOutline.Y);

        await Expect(Page.Locator(".joint-highlight-mask")).ToHaveCountAsync(1);
        await Expect(Page.Locator(".bpmn-resize-handle")).ToHaveCountAsync(8);
        await Expect(Page.Locator(".bpmn-connect-handle")).ToHaveCountAsync(0);
        await Expect(Page.Locator(".bpmn-actions")).ToHaveCountAsync(0);

        var nativeMenuSuppressed = await Page.EvaluateAsync<bool>(
            """
            id => {
              const body = document.querySelector(`.joint-cell[model-id="${id}"] [joint-selector="body"]`);
              const box = body.getBoundingClientRect();
              const event = new MouseEvent('contextmenu', {
                bubbles: true, cancelable: true,
                clientX: box.x + box.width / 2, clientY: box.y + 1,
              });
              body.dispatchEvent(event);
              return event.defaultPrevented;
            }
            """,
            groupId);

        Assert.True(nativeMenuSuppressed);
        await Expect(Page.Locator(".bpmn-menu")).ToHaveCountAsync(0);

        await Page.Locator(".bpmn-canvas").FocusAsync();
        await Page.Keyboard.PressAsync("Shift+F10");
        await Expect(Page.Locator(".bpmn-menu")).ToHaveCountAsync(0);

        var before = await GroupWidthAsync(groupId);
        var east = await Page.Locator(".bpmn-resize-handle").Nth(3).BoundingBoxAsync()
            ?? throw new InvalidOperationException("the Group east resize handle is missing");
        await Page.Mouse.MoveAsync(east.X + (east.Width / 2), east.Y + (east.Height / 2));
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(east.X + 80, east.Y + (east.Height / 2), new() { Steps = 8 });
        await Page.Mouse.UpAsync();
        await Page.WaitForTimeoutAsync(900);

        Assert.True(await GroupWidthAsync(groupId) > before + 20, "the Group did not resize");

        await Page.Locator(".bpmn-canvas").FocusAsync();
        await Page.Keyboard.PressAsync("Delete");
        await Expect(group).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task MovingADataStorePastItsPoolGrowsTheRenderedPool()
    {
        await Page.SetViewportSizeAsync(1600, 2000);
        await GoToEditorAsync();
        await SelectSampleAsync("pizza-store.bpmn");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Fit" }).ClickAsync();
        await Page.WaitForTimeoutAsync(700);

        const string PoolId = "id-b4f2b221-f282-4363-88c6-9d058e0b1824";
        const string DataStoreId = "id-ee1f1e11-b138-4f5f-95ac-f28937782fb4";
        var pool = await Page.Locator($".joint-cell[model-id='{PoolId}'] [joint-selector='body']").BoundingBoxAsync()
            ?? throw new InvalidOperationException("the sample Pool has no body");
        var store = await Page.Locator($".joint-cell[model-id='{DataStoreId}'] [joint-selector='body']").BoundingBoxAsync()
            ?? throw new InvalidOperationException("the sample Data Store has no body");
        var before = pool.Height;

        await Page.Mouse.MoveAsync(store.X + (store.Width / 2), store.Y + (store.Height / 2));
        await Page.Mouse.DownAsync();
        // Past the top edge: below the Pool is the vendor Pool, and dropping there transfers the store.
        await Page.Mouse.MoveAsync(store.X + (store.Width / 2), pool.Y - 40, new() { Steps = 12 });
        await Page.Mouse.UpAsync();
        await Page.WaitForTimeoutAsync(1200);

        var after = await Page.Locator($".joint-cell[model-id='{PoolId}'] [joint-selector='body']").BoundingBoxAsync();
        var moved = await Page.Locator($".joint-cell[model-id='{DataStoreId}']").BoundingBoxAsync();
        var status = await Page.Locator(".demo-edit-status").InnerTextAsync();
        Assert.True(
            after?.Height > before + 30,
            $"the Pool did not grow around the moved Data Store (height {before} -> {after?.Height}, " +
            $"store {store.X},{store.Y} -> {moved?.X},{moved?.Y}, status '{status}')");
    }

    [Fact]
    public async Task DraggingALaneSplitterResizesBothLanesAndLeavesThePoolAlone()
    {
        // Tall enough that the sample's splitters are on screen rather than below the fold.
        await Page.SetViewportSizeAsync(1600, 1400);
        await GoToEditorAsync();
        await SelectSampleAsync("pizza-store.bpmn");

        var splitter = Page.Locator(".bpmn-lane-splitter").First;
        await Expect(splitter).ToBeAttachedAsync();

        var upper = await splitter.GetAttributeAsync("data-lane");
        const string Pool = "id-faefcc69-77a6-4248-9872-f9c33fce5055";

        var before = await LaneHeightAsync(upper!);
        var poolBefore = await LaneHeightAsync(Pool);

        var hit = await splitter.Locator(".bpmn-lane-splitter-hit").BoundingBoxAsync();
        await Page.Mouse.MoveAsync(hit!.X + (hit.Width / 2), hit.Y + (hit.Height / 2));
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(hit.X + (hit.Width / 2), hit.Y + (hit.Height / 2) + 20, new() { Steps = 8 });

        await Expect(Page.Locator(".bpmn-lane-splitter-preview")).ToHaveCountAsync(1);

        await Page.Mouse.UpAsync();
        await Page.WaitForTimeoutAsync(900);

        Assert.True(await LaneHeightAsync(upper!) > before + 5, "the upper lane did not grow");
        Assert.Equal(poolBefore, await LaneHeightAsync(Pool), 1);
    }

    [Fact]
    public async Task ALaneCannotBeDraggedOnItsOwn()
    {
        await Page.SetViewportSizeAsync(1600, 1400);
        await GoToEditorAsync();
        await SelectSampleAsync("pizza-store.bpmn");

        var lane = await Page.Locator(".bpmn-lane-splitter").First.GetAttributeAsync("data-lane");
        var body = Page.Locator($".joint-cell[model-id='{lane}'] [joint-selector='body']").First;
        var pool = Page.Locator(".joint-cell[model-id='id-faefcc69-77a6-4248-9872-f9c33fce5055'] [joint-selector='body']").First;
        var before = await body.BoundingBoxAsync();
        var poolBefore = await pool.BoundingBoxAsync();

        // The header band, where a lane is grabbed.
        await Page.Mouse.MoveAsync(before!.X + 8, before.Y + (before.Height / 2));
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(before.X + 8, before.Y + (before.Height / 2) + 60, new() { Steps = 8 });
        await Page.Mouse.UpAsync();
        await Page.WaitForTimeoutAsync(900);

        // Measured against the Pool: selecting the Lane opens the property panel, which resizes
        // the canvas and shifts everything on screen together.
        var after = await body.BoundingBoxAsync();
        var poolAfter = await pool.BoundingBoxAsync();
        Assert.Equal(before.Y - poolBefore!.Y, after!.Y - poolAfter!.Y, 1);
        Assert.Equal(before.X - poolBefore.X, after.X - poolAfter.X, 1);
    }

    [Fact]
    public async Task DraggingTheBackgroundWithTheMousePansWithoutSelectingOrDragging()
    {
        await GoToEditorAsync();
        var canvas = await Page.Locator(".bpmn-canvas").BoundingBoxAsync();
        var before = await TransformAsync();

        var dragged = await Page.EvaluateAsync<bool>(
            "() => { window.__nativeDrag = false; document.addEventListener('dragstart', () => { window.__nativeDrag = true; }, true); return true; }");
        Assert.True(dragged);

        await Page.Mouse.MoveAsync(canvas!.X + canvas.Width - 30, canvas.Y + canvas.Height - 30);
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(canvas.X + canvas.Width - 130, canvas.Y + canvas.Height - 90, new() { Steps = 8 });
        await Page.Mouse.UpAsync();
        await Page.WaitForTimeoutAsync(400);

        Assert.NotEqual(before, await TransformAsync());
        Assert.False(await Page.EvaluateAsync<bool>("() => window.__nativeDrag"), "a native drag started");
        Assert.Equal(string.Empty, await Page.EvaluateAsync<string>("() => String(getSelection())"));
    }

    private async Task<double> LaneHeightAsync(string laneId)
    {
        var box = await Page.Locator($".joint-cell[model-id='{laneId}'] [joint-selector='body']").First.BoundingBoxAsync();
        return box!.Height;
    }

    [Fact]
    public async Task DraggingContentPastAPoolEdgeGrowsThePoolOnScreen()
    {
        await GoToEditorAsync();
        await SelectSampleAsync("collaboration.bpmn");

        var pool = await Page.Locator(".joint-cell[model-id^='Pool_']").First.GetAttributeAsync("model-id");
        var before = await PoolWidthAsync(pool!);

        var task = await Page.Locator(".joint-cell[model-id^='Task_']").First.GetAttributeAsync("model-id");
        var body = await Page.Locator($".joint-cell[model-id='{task}'] [joint-selector='body']").First.BoundingBoxAsync();
        var poolBox = await Page.Locator($".joint-cell[model-id='{pool}'] [joint-selector='body']").First.BoundingBoxAsync();

        // Measured against the Pool's own right edge rather than a fixed pixel count: the fit
        // scale depends on the viewport, and a drag that clears the edge at one size does not at
        // another.
        var from = body!.X + (body.Width / 2);
        var to = poolBox!.X + poolBox.Width + body.Width;

        await Page.Mouse.MoveAsync(from, body.Y + (body.Height / 2));
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(to, body.Y + (body.Height / 2), new() { Steps = 12 });
        await Page.Mouse.UpAsync();
        await Page.WaitForTimeoutAsync(1100);

        /*
         * On screen, not merely in the document. The canvas dragged the element itself, so the
         * move result was being treated as already rendered — which froze the Pool at its old
         * size while the document underneath said otherwise.
         */
        Assert.True(await PoolWidthAsync(pool!) > before + 40, "the pool did not grow on screen");
    }

    [Fact]
    public async Task PastingACollapsedSubProcessDoesNotSpillItsInterior()
    {
        await GoToEditorAsync();
        await SelectSampleAsync("nested-subprocesses.bpmn");

        var sub = await Page.EvaluateAsync<string>(
            "() => [...document.querySelectorAll('.bpmn-canvas .joint-element[model-id]')].find((c) => (c.getAttribute('aria-label') ?? '').includes('Sub Process'))?.getAttribute('model-id') ?? ''");

        Assert.NotEqual(string.Empty, sub);

        var body = await Page.Locator($".joint-cell[model-id='{sub}'] [joint-selector='body']").First.BoundingBoxAsync();
        await Page.Mouse.ClickAsync(body!.X + (body.Width / 2), body.Y + 10);
        await Page.WaitForTimeoutAsync(500);

        var before = await CellCountAsync();

        await Page.Keyboard.PressAsync("Control+c");
        await Page.Keyboard.PressAsync("Control+v");
        await Page.WaitForTimeoutAsync(1200);

        // A collapsed Sub-Process draws its interior on a plane of its own. Flattening the copied
        // DI onto the active plane put that whole nested graph on the top level, undrawn by
        // nothing: seventeen cells became thirty-five.
        await Expect(Page.Locator(".bpmn-canvas .joint-cell")).ToHaveCountAsync(before + 1);
    }

    // --- helpers --------------------------------------------------------------

    private Task<int> CellCountAsync() => Page.Locator(".bpmn-canvas .joint-cell[model-id]").CountAsync();

    private async Task<(float X, float Y)> FreeSpotAsync()
    {
        var canvas = await Page.Locator(".bpmn-canvas").BoundingBoxAsync()
            ?? throw new InvalidOperationException("the canvas has no bounding box");

        return (canvas.X + canvas.Width - 200, canvas.Y + canvas.Height - 160);
    }

    private async Task DropToolAsync(string label, float x, float y)
    {
        await Page.Locator(".bpmn-stencil__tool", new() { HasTextString = label }).First.HoverAsync();
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(x, y, new() { Steps = 8 });
        await Page.Mouse.UpAsync();
        await Page.WaitForTimeoutAsync(900);
    }

    private Task<string> TransformAsync() => Page.EvaluateAsync<string>(
        "() => document.querySelector('.joint-paper .joint-layers')?.getAttribute('transform') ?? 'none'");

    private Task<double> PoolWidthAsync(string poolId) => Page.EvaluateAsync<double>(
        "id => document.querySelector(`.joint-cell[model-id=\"${id}\"] [joint-selector=\"body\"]`)?.getBoundingClientRect().width ?? 0",
        poolId);

    private Task<double> GroupWidthAsync(string groupId) => Page.EvaluateAsync<double>(
        "id => document.querySelector(`.joint-cell[model-id=\"${id}\"] [joint-selector=\"body\"]`)?.getBoundingClientRect().width ?? 0",
        groupId);

    private async Task BandAsync(float x1, float y1, float x2, float y2)
    {
        await Page.Keyboard.DownAsync("Shift");
        await Page.Mouse.MoveAsync(x1, y1);
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync((x1 + x2) / 2, (y1 + y2) / 2, new() { Steps = 4 });
        await Page.Mouse.MoveAsync(x2, y2, new() { Steps = 6 });
        await Page.Mouse.UpAsync();
        await Page.Keyboard.UpAsync("Shift");
        await Page.WaitForTimeoutAsync(500);
    }

    private async Task<string> FirstTaskIdAsync() =>
        await Page.Locator(".joint-cell[model-id^='Task_']").First.GetAttributeAsync("model-id")
        ?? throw new InvalidOperationException("no Task element on the canvas");

    private async Task SelectFirstTaskAsync()
    {
        var id = await FirstTaskIdAsync();
        var body = await Page.Locator($".joint-cell[model-id='{id}'] [joint-selector='body']").First.BoundingBoxAsync()
            ?? throw new InvalidOperationException($"no drawn body for '{id}'");

        await Page.Mouse.ClickAsync(body.X + (body.Width / 2), body.Y + (body.Height / 2));
        await Page.WaitForTimeoutAsync(400);
    }

    /// <summary>A Pool's centre is occupied by its contents, so its header band is the way in.</summary>
    private async Task SelectPoolHeaderAsync(string poolId)
    {
        var box = await Page.Locator($".joint-cell[model-id='{poolId}']").BoundingBoxAsync()
            ?? throw new InvalidOperationException($"no bounding box for '{poolId}'");

        await Page.Mouse.ClickAsync(box.X + 14, box.Y + (box.Height / 2));
        await Page.WaitForTimeoutAsync(500);
    }

    private async Task SelectSampleAsync(string fileName)
    {
        await Page.GetByRole(AriaRole.Combobox, new() { Name = "Sample diagram" }).ClickAsync();
        await Page.Locator("fluent-option", new() { HasTextString = fileName }).ClickAsync();
        await Page.WaitForTimeoutAsync(1500);
    }

    private async Task GoToEditorAsync()
    {
        await Page.GotoAsync($"{app.BaseUrl}/editor");

        await Expect(Page.Locator(".bpmn-canvas__paper > svg")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.Locator(".bpmn-canvas .joint-cell").First).ToBeVisibleAsync();
    }
}
