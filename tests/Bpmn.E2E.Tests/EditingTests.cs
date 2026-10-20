using Hasseware.AspNetCore.Components.Bpmn.E2E.Tests.Fixtures;

using Microsoft.Playwright;
using Microsoft.Playwright.Xunit.v3;

namespace Hasseware.AspNetCore.Components.Bpmn.E2E.Tests;

/// <summary>
/// Editing, in a real browser.
/// </summary>
/// <remarks>
/// The command layer is unit-tested against documents; none of that reaches the pointer. These
/// cover the path from a gesture to a recorded edit, and the one thing a unit test cannot see at
/// all: that read-only actually is.
/// </remarks>
public sealed class EditingTests(DemoAppFixture app) : PageTest, IClassFixture<DemoAppFixture>
{
    /// <summary>
    /// Read-only keeps selection and refuses mutation.
    /// </summary>
    /// <remarks>
    /// This regressed once already. Turning off the paper's drag handling closes the pointer door
    /// and leaves the keyboard one open, so <c>Ctrl+A</c> then <c>Delete</c> emptied a read-only
    /// diagram — specification section 7 requires selection to survive and every mutation surface
    /// to be gone.
    /// </remarks>
    [Fact]
    public async Task ReadOnlyKeepsSelectionAndRefusesDeletion()
    {
        await Page.GotoAsync($"{app.BaseUrl}/gallery");
        await Expect(Page.Locator(".bpmn-canvas .joint-cell").First).ToBeVisibleAsync(new() { Timeout = 30_000 });

        var before = await Page.Locator(".bpmn-canvas .joint-cell").CountAsync();
        Assert.True(before > 0, "the gallery sheet drew nothing");

        await Page.Locator(".bpmn-canvas").FocusAsync();
        await Page.Keyboard.PressAsync("Control+a");

        // Selection is retained in read-only; it is not a mutation.
        await Expect(Page.Locator(".joint-highlight-mask").First).ToBeVisibleAsync();

        await Page.Keyboard.PressAsync("Delete");
        await Page.WaitForTimeoutAsync(600);

        await Expect(Page.Locator(".bpmn-canvas .joint-cell")).ToHaveCountAsync(before);
    }

    [Fact]
    public async Task ReadOnlyStillZoomsAndPans()
    {
        /*
         * The other half of section 7, and the half that is easy to break by locking the paper
         * wholesale: navigation is not mutation. A read-only diagram nobody can zoom into or move
         * around is a picture, and the mode is supposed to produce an editor with the editing
         * taken out.
         */
        await Page.GotoAsync($"{app.BaseUrl}/gallery");
        await Expect(Page.Locator(".bpmn-canvas .joint-cell").First).ToBeVisibleAsync(new() { Timeout = 30_000 });

        var start = await TransformAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Zoom in" }).ClickAsync();
        await Page.WaitForTimeoutAsync(600);

        var zoomed = await TransformAsync();
        Assert.NotEqual(start, zoomed);

        var canvas = await Page.Locator(".bpmn-canvas").BoundingBoxAsync()
            ?? throw new InvalidOperationException("the canvas has no bounding box");

        // Blank paper, so the drag is a pan and not a rubber band over some shape.
        await Page.Mouse.MoveAsync(canvas.X + 40, canvas.Y + canvas.Height - 40);
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(canvas.X + 140, canvas.Y + canvas.Height - 90, new() { Steps = 10 });
        await Page.Mouse.UpAsync();
        await Page.WaitForTimeoutAsync(600);

        Assert.NotEqual(zoomed, await TransformAsync());
    }

    [Fact]
    public async Task ClickingAnElementSelectsItWithAnOutline()
    {
        await GoToEditorAsync();

        await ClickFirstTaskAsync();

        await Expect(Page.Locator(".joint-highlight-mask")).ToHaveCountAsync(1);
        await Expect(Page.Locator(".demo-edit-status")).ToContainTextAsync("Selected Task_");
    }

    [Fact]
    public async Task EscapeClearsTheSelection()
    {
        await GoToEditorAsync();
        await ClickFirstTaskAsync();

        await Page.Keyboard.PressAsync("Escape");

        await Expect(Page.Locator(".joint-highlight-mask")).ToHaveCountAsync(0);
        await Expect(Page.Locator(".demo-edit-status")).ToContainTextAsync("Nothing selected");
    }

    [Fact]
    public async Task DeletingTheSelectionCascadesAndUndoRestoresIt()
    {
        await GoToEditorAsync();
        var before = await Page.Locator(".bpmn-canvas .joint-cell").CountAsync();

        await Page.Locator(".bpmn-canvas").FocusAsync();
        await Page.Keyboard.PressAsync("Control+a");
        await Page.Keyboard.PressAsync("Delete");

        // Deleting the three nodes takes both connectors with them, so nothing is left.
        await Expect(Page.Locator(".bpmn-canvas .joint-cell")).ToHaveCountAsync(0);

        await Page.Keyboard.PressAsync("Control+z");

        await Expect(Page.Locator(".bpmn-canvas .joint-cell")).ToHaveCountAsync(before);
    }

    [Fact]
    public async Task DraggingAnElementCommitsAndUndoRestoresTheExactPosition()
    {
        await GoToEditorAsync();
        var task = await FirstTaskIdAsync();
        var before = await PositionAsync(task);

        await DragAsync(task, 0, 260);
        var after = await PositionAsync(task);
        Assert.NotEqual(before, after);
        await Expect(Page.Locator(".demo-edit-status")).ToContainTextAsync("Edited");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Undo" }).ClickAsync();

        // Exactly, not approximately: the history restores a snapshot rather than replaying a
        // reverse move.
        await EventuallyAsync(async () => await PositionAsync(task) == before, $"the task returned to {before}");
    }

    [Fact]
    public async Task DraggingOntoAPeerIsRefusedAndRolledBack()
    {
        await GoToEditorAsync();
        var task = await FirstTaskIdAsync();
        var start = await PositionAsync(task);

        // The Start Event sits up and to the left; dropping the Task on it collides.
        var taskBox = await Page.Locator($".joint-cell[model-id='{task}']").BoundingBoxAsync();
        var eventBox = await Page.Locator(".joint-cell[model-id^='Event_']").First.BoundingBoxAsync();

        await DragAsync(task, eventBox!.X - taskBox!.X, eventBox.Y - taskBox.Y);

        Assert.Equal(start, await PositionAsync(task));
        await Expect(Page.Locator(".demo-edit-status")).ToContainTextAsync("DI-011");
    }

    [Fact]
    public async Task DraggingAContainerMovesItsContentsAndUndoRestoresThemAll()
    {
        await GoToEditorAsync();
        await SelectSampleAsync("collaboration.bpmn");
        await Expect(Page.Locator(".joint-cell[model-id^='Pool_']").First).ToBeVisibleAsync();

        var pool = await Page.Locator(".joint-cell[model-id^='Pool_']").First.GetAttributeAsync("model-id");
        var child = await Page.Locator(".joint-cell[model-id^='Task_']").First.GetAttributeAsync("model-id");

        var poolBefore = await PositionAsync(pool!);
        var childBefore = await PositionAsync(child!);
        await DragAsync(pool!, 60, 0, fromContainerBody: true);

        // Deep container movement changes each descendant exactly once.
        var poolAfter = await PositionAsync(pool!);
        var childAfter = await PositionAsync(child!);
        var status = await Page.Locator(".demo-edit-status").InnerTextAsync();

        Assert.True(poolBefore != poolAfter, $"Pool stayed at {poolBefore}; status: {status}");
        Assert.True(childBefore != childAfter, $"Child stayed at {childBefore}; status: {status}");
        Assert.Contains("Edited", status, StringComparison.Ordinal);

        await Page.GetByRole(AriaRole.Button, new() { Name = "Undo" }).ClickAsync();

        // The whole point: the reported command carried every descendant, so undo puts them all
        // back. Had it carried only the container, the children would stay displaced.
        await EventuallyAsync(
            async () => await PositionAsync(pool!) == poolBefore && await PositionAsync(child!) == childBefore,
            "the pool and its contents both returned");
    }

    // --- helpers --------------------------------------------------------------

    /// <summary>
    /// Polls until a condition holds.
    /// </summary>
    /// <remarks>
    /// Undo travels canvas to Blazor and back as a re-render, so the DOM catches up a frame or two
    /// later. Polling states the expectation once instead of guessing at a sleep long enough to
    /// cover it.
    /// </remarks>
    private static async Task EventuallyAsync(Func<Task<bool>> condition, string expectation)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition().ConfigureAwait(true)) return;
            await Task.Delay(100).ConfigureAwait(true);
        }

        Assert.Fail($"Timed out waiting until {expectation}.");
    }

    private async Task<string> FirstTaskIdAsync() =>
        await Page.Locator(".joint-cell[model-id^='Task_']").First.GetAttributeAsync("model-id")
        ?? throw new InvalidOperationException("no Task element on the canvas");

    private async Task ClickFirstTaskAsync()
    {
        var box = await Page.Locator($".joint-cell[model-id='{await FirstTaskIdAsync()}']").BoundingBoxAsync();
        await Page.Mouse.ClickAsync(box!.X + 6f, box.Y + 6f);
    }

    /// <summary>The element's position, read from its rendered transform.</summary>
    /// <summary>The paper's transform, which both zoom and pan move.</summary>
    private Task<string> TransformAsync() => Page.EvaluateAsync<string>(
        "() => document.querySelector('.bpmn-canvas svg g')?.getAttribute('transform') ?? '(none)'");

    [Fact]
    public async Task ABoundaryEventKeepsItsPlaceOnAHostThatMoves()
    {
        /*
         * The offset, not the position. A Boundary Event is carried by its host as an embedded
         * child, so moving the host must not change where on the border the user put it.
         *
         * What broke this was the alignment snapper: it corrected the host's position without
         * translating what was embedded in it, so every snapped frame slid the host out from under
         * the Event, and the pin then re-projected the Event to the *nearest* point of the new
         * outline — which is a different point. Measured on this fixture the offset jumped five
         * pixels mid-drag. A drag across a peer is what makes the snapper fire, which is why this
         * one travels diagonally rather than straight.
         */
        await GoToEditorAsync();
        await SelectSampleAsync("order-process.bpmn");

        var boundary = await AttachBoundaryEventAsync("Task_ValidateOrder");
        var before = await OffsetAsync("Task_ValidateOrder", boundary);

        await DragAsync("Task_ValidateOrder", 108, -84);

        Assert.Equal(before, await OffsetAsync("Task_ValidateOrder", boundary));
    }

    [Fact]
    public async Task AContainerKeepsItsContentsWhereTheyWere()
    {
        // The same defect, on the other thing embedding carries: a Pool snapped mid-drag left its
        // tasks behind by the snap delta.
        await GoToEditorAsync();
        await SelectSampleAsync("pizza-store.bpmn");

        // Found by what the canvas says each cell is, not by id: this fixture's ids are GUIDs.
        var pair = await Page.EvaluateAsync<string[]>(
            """
            () => {
              const cells = [...document.querySelectorAll('.bpmn-canvas .joint-element[model-id]')];
              const pool = cells.find((c) => (c.getAttribute('aria-label') ?? '').startsWith('Pool'));
              if (pool == null) return ['', ''];

              const box = pool.getBoundingClientRect();
              const child = cells.find((c) => {
                if (c === pool) return false;
                const b = c.getBoundingClientRect();
                return b.left > box.left && b.right < box.right && b.top > box.top && b.bottom < box.bottom;
              });

              return [pool.getAttribute('model-id') ?? '', child?.getAttribute('model-id') ?? ''];
            }
            """);

        var pool = pair[0];
        var inside = pair[1];

        Assert.NotEmpty(pool);
        Assert.NotEmpty(inside);

        var before = await OffsetAsync(pool, inside);

        await DragAsync(pool, 70, -50, fromContainerBody: true);

        Assert.Equal(before, await OffsetAsync(pool, inside));
    }

    // --- helpers for the two above ---------------------------------------------------

    /// <summary>One element's position relative to another, which is what must not change.</summary>
    private Task<string> OffsetAsync(string hostId, string childId) => Page.EvaluateAsync<string>(
        """
        ids => {
          const at = (m) => {
            const el = document.querySelector(`.joint-cell[model-id="${m}"]`);
            const t = el?.getAttribute('transform')?.match(/translate\(\s*([-\d.]+)[ ,]+([-\d.]+)/);
            return t == null ? null : { x: Number(t[1]), y: Number(t[2]) };
          };
          const host = at(ids[0]);
          const child = at(ids[1]);
          if (host == null || child == null) return 'missing';
          return `${(child.x - host.x).toFixed(1)},${(child.y - host.y).toFixed(1)}`;
        }
        """,
        new[] { hostId, childId });

    private Task<string> FirstIdAsync(string prefix) => Page.EvaluateAsync<string>(
        """
        p => [...document.querySelectorAll('.bpmn-canvas .joint-element[model-id]')]
          .map((c) => c.getAttribute('model-id') ?? '')
          .find((id) => id.startsWith(p)) ?? ''
        """,
        prefix);

    /// <summary>Drops a Boundary Event on an Activity and returns its id.</summary>
    private async Task<string> AttachBoundaryEventAsync(string hostId)
    {
        var host = await Page.Locator($".joint-cell[model-id='{hostId}'] [joint-selector='body']").First.BoundingBoxAsync()
            ?? throw new InvalidOperationException($"'{hostId}' has no drawn body");

        await Page.Locator(".bpmn-stencil__tool", new() { HasTextString = "Intermediate / Boundary Event" }).First.HoverAsync();
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(host.X + (host.Width / 2), host.Y + (host.Height / 2), new() { Steps = 8 });
        await Page.Mouse.UpAsync();
        await Page.WaitForTimeoutAsync(1000);

        var id = await FirstIdAsync("boundaryEvent");
        Assert.NotEmpty(id);

        return id;
    }

    private async Task<string> PositionAsync(string modelId) =>
        await Page.EvaluateAsync<string>(
            """
            id => {
              const el = document.querySelector(`.joint-cell[model-id="${id}"]`);
              const m = el?.getAttribute('transform')?.match(/translate\(\s*([-\d.]+)[ ,]+([-\d.]+)/);
              return m == null ? 'none' : `${m[1]},${m[2]}`;
            }
            """,
            modelId);

    /// <summary>
    /// A real mouse drag.
    /// </summary>
    /// <remarks>
    /// Synthetic pointer events reach the listeners but not JointJS's move machinery, which only
    /// runs for trusted events — a script-dispatched drag reports success and moves nothing.
    /// </remarks>
    private async Task DragAsync(string modelId, float dx, float dy, bool fromContainerBody = false)
    {
        var box = await Page.Locator($".joint-cell[model-id='{modelId}']").BoundingBoxAsync()
            ?? throw new InvalidOperationException($"no bounding box for '{modelId}'");

        // A container's centre is occupied by its contents. Use the empty strip just inside the
        // Pool body, beyond its header, so the grab is the Pool and stays clear of the stencil.
        var header = fromContainerBody
            ? await Page.Locator($".joint-cell[model-id='{modelId}'] [joint-selector='header']").BoundingBoxAsync()
            : null;
        var x = header is null
            ? box.X + (box.Width / 2)
            : Math.Min(box.X + (box.Width / 2), header.X + header.Width + 20);
        var y = fromContainerBody ? box.Y + 10 : box.Y + (box.Height / 2);

        await Page.Mouse.MoveAsync(x, y);
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(x + (dx / 2), y + (dy / 2), new() { Steps = 4 });
        await Page.Mouse.MoveAsync(x + dx, y + dy, new() { Steps = 4 });
        await Page.Mouse.UpAsync();
        await Page.WaitForTimeoutAsync(500);
    }

    private async Task SelectSampleAsync(string fileName)
    {
        await Page.OpenDemoFilesAsync();
        await Page.GetByRole(AriaRole.Combobox, new() { Name = "Sample diagram" }).ClickAsync();
        await Page.Locator("fluent-option", new() { HasTextString = fileName }).ClickAsync();
        await Page.WaitForTimeoutAsync(1200);
    }

    private async Task GoToEditorAsync()
    {
        await Page.GotoAsync($"{app.BaseUrl}/editor");

        await Expect(Page.Locator(".bpmn-canvas__paper > svg")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.Locator(".bpmn-canvas .joint-cell").First).ToBeVisibleAsync();
    }
}
