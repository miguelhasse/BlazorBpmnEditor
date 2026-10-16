using Hasseware.AspNetCore.Components.Bpmn.E2E.Tests.Fixtures;

using Microsoft.Playwright;
using Microsoft.Playwright.Xunit.v3;

namespace Hasseware.AspNetCore.Components.Bpmn.E2E.Tests;

/// <summary>
/// The context menu, property editing, type switching, inline label editing and contextual actions.
/// </summary>
/// <remarks>
/// All five are browser behaviour in a way the unit tests cannot reach. The commands are exercised
/// against documents elsewhere; what only a browser can show is whether the native menu is really
/// unreachable, whether the overlay lands on the label it is replacing, and whether an edit made in
/// the menu comes back out on the canvas.
/// </remarks>
public class ContextMenuTests(DemoAppFixture app) : PageTest, IClassFixture<DemoAppFixture>
{
    [Fact]
    public async Task ContextualActionsKeepTheirInitialPositionAfterSelectionToolsRender()
    {
        await GoToEditorAsync();
        await Page.EvaluateAsync(
            """
            () => {
              window.actionPositions = [];
              window.actionObserver = new MutationObserver(() => {
                const strip = document.querySelector('.bpmn-actions');
                if (strip) window.actionPositions.push(strip.getAttribute('style'));
              });
              window.actionObserver.observe(document.body, {
                subtree: true, childList: true, attributes: true, attributeFilter: ['style']
              });
            }
            """);

        await Page.Locator(".joint-cell[model-id^='Task_'] [joint-selector='body']").First.ClickAsync();
        await Expect(Page.Locator(".bpmn-actions")).ToBeVisibleAsync();
        // The anchor reporter refreshes after pointer release and selection tools render.
        await Page.WaitForTimeoutAsync(500);

        var positions = await Page.EvaluateAsync<string[]>(
            """
            () => {
              window.actionObserver.disconnect();
              return window.actionPositions;
            }
            """);
        Assert.NotEmpty(positions);
        Assert.All(positions, position => Assert.Equal(positions[0], position));
        var aligned = await Page.EvaluateAsync<bool>(
            """
            () => {
              const strip = document.querySelector('.bpmn-actions').getBoundingClientRect();
              const shape = document.querySelector(".joint-cell[model-id^='Task_'] [joint-selector='body']")
                .getBoundingClientRect();
              return Math.abs(strip.top - shape.top) < 1 && Math.abs(strip.left - shape.right - 26) < 1;
            }
            """);
        Assert.True(aligned);
    }

    // --- suppression (section 7) ------------------------------------------------

    [Fact]
    public async Task RightClickingAShapeSuppressesTheBrowserMenuAndOpensTheCustomOne()
    {
        await GoToEditorAsync();

        var prevented = await RightClickAsync(await TaskIdAsync());

        Assert.True(prevented, "the browser's own context menu was not suppressed");
        await Expect(Page.Locator(".bpmn-menu")).ToHaveCountAsync(1);
        await Expect(Page.Locator(".bpmn-menu__title")).ToHaveTextAsync("Review Request");
    }

    [Fact]
    public async Task RightClickingBlankPaperStillOpensTheCanvasMenu()
    {
        // Section 7 forbids ever falling back to the browser menu, so a target with no
        // element-specific commands still gets one.
        await GoToEditorAsync();

        var prevented = await Page.EvaluateAsync<bool>(
            """
            () => {
              const canvas = document.querySelector('.bpmn-canvas');
              const box = canvas.getBoundingClientRect();
              const x = box.x + box.width - 40;
              const y = box.y + box.height - 40;
              const event = new MouseEvent('contextmenu', { bubbles: true, cancelable: true, clientX: x, clientY: y });
              (document.elementFromPoint(x, y) ?? canvas).dispatchEvent(event);
              return event.defaultPrevented;
            }
            """);

        Assert.True(prevented);
        await Expect(Page.Locator(".bpmn-menu__title")).ToHaveTextAsync("Canvas");
        await Expect(Page.Locator(".bpmn-menu__item", new() { HasText = "Fit to content" })).ToHaveCountAsync(1);
    }

    [Fact]
    public async Task TheBrowserMenuIsSuppressedAcrossTheEditorExceptInEditableFields()
    {
        await GoToEditorAsync();

        var prevented = await Page.EvaluateAsync<bool[]>(
            """
            () => {
              const fire = (target) => {
                const event = new MouseEvent('contextmenu', { bubbles: true, cancelable: true });
                target.dispatchEvent(event);
                return event.defaultPrevented;
              };

              const field = document.createElement('input');
              document.querySelector('.bpmn-editor__stencil').appendChild(field);

              return [
                fire(document.querySelector('.bpmn-editor__stencil')),
                fire(document.querySelector('.bpmn-editor__toolbar')),
                fire(field),
              ];
            }
            """);

        Assert.True(prevented[0], "the stencil showed the browser menu");
        Assert.True(prevented[1], "the toolbar showed the browser menu");
        Assert.False(prevented[2], "a text field lost its browser menu");
    }

    [Fact]
    public async Task NothingOnTheEditorIsSelectableText()
    {
        await GoToEditorAsync();

        var selectable = await Page.EvaluateAsync<string[]>(
            """
            () => ['.bpmn-editor', '.bpmn-editor__toolbar', '.bpmn-editor__stencil', '.bpmn-canvas']
              .map((selector) => getComputedStyle(document.querySelector(selector)).userSelect)
            """);

        Assert.All(selectable, value => Assert.Equal("none", value));
    }

    [Theory]
    [InlineData("F10", true)]
    [InlineData("ContextMenu", false)]
    public async Task TheKeyboardOpensTheSameMenu(string key, bool shift)
    {
        await GoToEditorAsync();

        var prevented = await Page.EvaluateAsync<bool>(
            """
            ([key, shift]) => {
              const host = document.querySelector('.bpmn-canvas');
              host.focus();
              const event = new KeyboardEvent('keydown', { key, shiftKey: shift, bubbles: true, cancelable: true });
              host.dispatchEvent(event);
              return event.defaultPrevented;
            }
            """,
            new object[] { key, shift });

        Assert.True(prevented);
        await Expect(Page.Locator(".bpmn-menu")).ToHaveCountAsync(1);
    }

    [Fact]
    public async Task EscapeClosesTheMenu()
    {
        await GoToEditorAsync();
        await RightClickAsync(await TaskIdAsync());
        await Expect(Page.Locator(".bpmn-menu")).ToHaveCountAsync(1);

        await Page.Keyboard.PressAsync("Escape");

        await Expect(Page.Locator(".bpmn-menu")).ToHaveCountAsync(0);
    }

    // --- property editing and type switching -------------------------------------

    [Fact]
    public async Task EditingANameInTheMenuChangesTheCanvas()
    {
        await GoToEditorAsync();
        var id = await TaskIdAsync();
        await RightClickAsync(id);

        await Page.Locator(".bpmn-menu__property", new() { HasText = "Name" })
            .Locator("input").FillAsync("Renamed By Menu");
        await Page.Locator(".bpmn-menu__title").ClickAsync();

        await Expect(Page.Locator($".joint-cell[model-id='{id}']"))
            .ToHaveAttributeAsync("aria-label", "User Task: Renamed By Menu");
    }

    [Fact]
    public async Task SwitchingTypeKeepsTheElementAndItsLabel()
    {
        await GoToEditorAsync();
        var id = await TaskIdAsync();
        await RightClickAsync(id);

        // Section 7: eight Task kinds, with the current one marked rather than hidden.
        await Expect(Page.Locator(".bpmn-menu__item--type")).ToHaveCountAsync(8);
        await Expect(Page.Locator(".bpmn-menu__item--type[aria-checked='true']")).ToHaveTextAsync("User Task");

        await Page.Locator(".bpmn-menu__item--type", new() { HasTextString = "Service Task" }).ClickAsync();

        // Same id, same label, new type — and the menu now marks the new one.
        await Expect(Page.Locator($".joint-cell[model-id='{id}']"))
            .ToHaveAttributeAsync("aria-label", "Service Task: Review Request");
        await Expect(Page.Locator(".bpmn-menu__item--type[aria-checked='true']")).ToHaveTextAsync("Service Task");
    }

    [Fact]
    public async Task ReadonlyShowsTheSnapshotAndOffersNoEdits()
    {
        // Section 7: "Readonly mode exposes the snapshot and inspection controls only".
        await Page.GotoAsync($"{app.BaseUrl}/gallery");
        await Expect(Page.Locator(".bpmn-canvas .joint-cell").First).ToBeVisibleAsync(new() { Timeout = 30_000 });

        var prevented = await Page.EvaluateAsync<bool>(
            """
            () => {
              const cell = document.querySelector('.bpmn-canvas .joint-element[model-id]');
              const box = cell.getBoundingClientRect();
              const event = new MouseEvent('contextmenu', {
                bubbles: true, cancelable: true,
                clientX: box.x + box.width / 2, clientY: box.y + box.height / 2,
              });
              cell.dispatchEvent(event);
              return event.defaultPrevented;
            }
            """);

        Assert.True(prevented, "read-only did not suppress the browser menu");
        await Expect(Page.Locator(".bpmn-menu")).ToHaveCountAsync(1);
        await Expect(Page.Locator(".bpmn-menu__item--type")).ToHaveCountAsync(0);
        await Expect(Page.Locator(".bpmn-menu__item", new() { HasText = "Delete" })).ToHaveCountAsync(0);
        foreach (var field in await Page.Locator(".bpmn-menu").GetByRole(AriaRole.Textbox).AllAsync())
            await Expect(field).Not.ToBeEditableAsync();
    }

    // --- inline label editing (section 4.5) ----------------------------------------

    [Fact]
    public async Task EditingALabelFromTheMenuUsesTheDoubleClickPosition()
    {
        await GoToEditorAsync();
        var id = await TaskIdAsync();
        var body = Page.Locator($".joint-cell[model-id='{id}'] [joint-selector='body']").First;
        var input = Page.Locator(".bpmn-label-editor__input");

        await body.DblClickAsync();
        await Expect(input).ToHaveValueAsync("Review Request");
        var doubleClickBox = await input.BoundingBoxAsync();
        Assert.NotNull(doubleClickBox);
        await Page.Keyboard.PressAsync("Escape");
        await Expect(input).ToHaveCountAsync(0);

        await RightClickAsync(id);
        await Page.Locator(".bpmn-menu__item", new() { HasTextString = "Edit label" }).ClickAsync();
        await Expect(input).ToHaveValueAsync("Review Request");
        var menuBox = await input.BoundingBoxAsync();
        Assert.NotNull(menuBox);

        Assert.InRange(Math.Abs(menuBox.X - doubleClickBox.X), 0, 1);
        Assert.InRange(Math.Abs(menuBox.Y - doubleClickBox.Y), 0, 1);
        Assert.InRange(Math.Abs(menuBox.Width - doubleClickBox.Width), 0, 1);
        Assert.InRange(Math.Abs(menuBox.Height - doubleClickBox.Height), 0, 1);
    }

    [Fact]
    public async Task DoubleClickingOpensAnInlineEditorOverTheLabel()
    {
        await GoToEditorAsync();
        var id = await TaskIdAsync();

        await Page.Locator($".joint-cell[model-id='{id}'] [joint-selector='body']").First.DblClickAsync();

        // Section 4.5: in place, never a popup, dialog or modal.
        await Expect(Page.Locator(".bpmn-label-editor__input")).ToHaveValueAsync("Review Request");
        await Expect(Page.Locator(".bpmn-canvas [role='dialog']")).ToHaveCountAsync(0);

        var offset = await Page.EvaluateAsync<int>(
            """
            id => {
              const editor = document.querySelector('.bpmn-label-editor__input').getBoundingClientRect();
              const label = document.querySelector(`.joint-cell[model-id="${id}"] [joint-selector="label"]`)
                .getBoundingClientRect();
              return Math.round(Math.abs((editor.y + editor.height / 2) - (label.y + label.height / 2)));
            }
            """,
            id);

        Assert.True(offset <= 2, $"the editor sat {offset}px off the label it replaced");
    }

    [Fact]
    public async Task EnterCommitsTheInlineEditAndEscapeAbandonsIt()
    {
        await GoToEditorAsync();
        var id = await TaskIdAsync();
        var body = Page.Locator($".joint-cell[model-id='{id}'] [joint-selector='body']").First;

        await body.DblClickAsync();
        await Page.Locator(".bpmn-label-editor__input").FillAsync("Typed Inline");
        await Page.Keyboard.PressAsync("Enter");

        await Expect(Page.Locator(".bpmn-label-editor__input")).ToHaveCountAsync(0);
        await Expect(Page.Locator($".joint-cell[model-id='{id}']"))
            .ToHaveAttributeAsync("aria-label", "User Task: Typed Inline");

        await body.DblClickAsync();
        await Page.Locator(".bpmn-label-editor__input").FillAsync("Discarded");
        await Page.Keyboard.PressAsync("Escape");

        await Expect(Page.Locator(".bpmn-label-editor__input")).ToHaveCountAsync(0);
        await Expect(Page.Locator($".joint-cell[model-id='{id}']"))
            .ToHaveAttributeAsync("aria-label", "User Task: Typed Inline");
    }

    // --- contextual actions ----------------------------------------------------------

    [Fact]
    public async Task SelectingOneElementOffersQuickActionsBesideIt()
    {
        await GoToEditorAsync();
        await ClickBodyAsync(await TaskIdAsync());

        await Expect(Page.Locator(".bpmn-actions")).ToHaveCountAsync(1);
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Append Task" })).ToBeVisibleAsync();

        var rightOfShape = await Page.EvaluateAsync<bool>(
            """
            () => {
              const strip = document.querySelector('.bpmn-actions').getBoundingClientRect();
              const shape = document.querySelector('.joint-cell[model-id^="Task_"] [joint-selector="body"]')
                .getBoundingClientRect();
              return Math.round(strip.x) >= Math.round(shape.x + shape.width);
            }
            """);

        Assert.True(rightOfShape, "the quick actions did not sit beside the element they act on");
    }

    [Fact]
    public async Task AppendingCreatesAnElementAndTheFlowIntoIt()
    {
        await GoToEditorAsync();
        var before = await Page.Locator(".bpmn-canvas .joint-cell[model-id]").CountAsync();

        await ClickBodyAsync(await TaskIdAsync());
        await Page.GetByRole(AriaRole.Button, new() { Name = "Append Task" }).ClickAsync();

        // One compound command: the element and the Sequence Flow arrive together, or neither does.
        await Expect(Page.Locator(".bpmn-canvas .joint-cell[model-id]")).ToHaveCountAsync(before + 2);
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Undo" })).ToBeEnabledAsync();
    }

    [Fact]
    public async Task AppendingUndoesAsOneStep()
    {
        // Section 9.3 makes creation one transaction, and an append is a creation plus its flow.
        // A Task left behind with no flow into it is not what the user asked to keep.
        await GoToEditorAsync();
        var before = await Page.Locator(".bpmn-canvas .joint-cell[model-id]").CountAsync();

        await ClickBodyAsync(await TaskIdAsync());
        await Page.GetByRole(AriaRole.Button, new() { Name = "Append Task" }).ClickAsync();
        await Expect(Page.Locator(".bpmn-canvas .joint-cell[model-id]")).ToHaveCountAsync(before + 2);

        await Page.GetByRole(AriaRole.Button, new() { Name = "Undo" }).ClickAsync();

        await Expect(Page.Locator(".bpmn-canvas .joint-cell[model-id]")).ToHaveCountAsync(before);
    }

    [Fact]
    public async Task ARefusedPropertyEditLeavesTheFormShowingTheDocumentsValue()
    {
        /*
         * The failure this guards against is subtle and was real: a checkbox the document refused
         * kept the state the browser toggled it to, because the rendered value never changed and
         * so Blazor's diff had nothing to send. Section 7 forbids the menu showing a stale
         * standard value, and a value the document rejected is the stalest kind.
         */
        await GoToEditorAsync();

        var flow = await Page.EvaluateAsync<string>(
            "() => document.querySelector('.bpmn-canvas .joint-link[model-id]')?.getAttribute('model-id') ?? ''");

        Assert.NotEqual(string.Empty, flow);
        await RightClickAsync(flow);

        // A condition first, so marking it the default breaks SF-006.
        await Page.Locator(".bpmn-menu__property", new() { HasText = "Condition" })
            .First.Locator("textarea").FillAsync("${amount > 0}");
        await Page.Locator(".bpmn-menu__title").ClickAsync();

        var isDefault = Page.Locator(".bpmn-menu__property", new() { HasText = "Default flow" })
            .Locator("input[type=checkbox], fluent-checkbox");

        // Clicked, not CheckAsync: that helper retries until the box is checked, and the whole
        // point here is that it does not stay checked.
        await isDefault.ClickAsync();

        await Expect(Page.Locator(".demo-edit-status")).ToContainTextAsync("SF-006");
        Assert.False(await isDefault.EvaluateAsync<bool>("e => !!e.checked"));
    }

    [Fact]
    public async Task AnEndEventOffersNothingToAppend()
    {
        // Nothing follows an End Event, so offering it would offer a Sequence Flow section 5.1
        // refuses.
        await GoToEditorAsync();

        var endEvent = await Page.EvaluateAsync<string>(
            """
            () => [...document.querySelectorAll('.bpmn-canvas .joint-element[model-id]')]
              .find((c) => (c.getAttribute('aria-label') ?? '').startsWith('End Event'))
              ?.getAttribute('model-id') ?? ''
            """);

        Assert.NotEqual(string.Empty, endEvent);
        await ClickBodyAsync(endEvent);

        await Expect(Page.Locator(".bpmn-actions")).ToHaveCountAsync(1);
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Append Task" })).ToHaveCountAsync(0);
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Rename" })).ToBeVisibleAsync();
    }

    // --- helpers ------------------------------------------------------------------------

    private Task<string> TaskIdAsync() => Page.EvaluateAsync<string>(
        """
        () => [...document.querySelectorAll('.bpmn-canvas .joint-element[model-id]')]
          .find((c) => (c.getAttribute('model-id') ?? '').startsWith('Task_'))
          ?.getAttribute('model-id') ?? ''
        """);

    /// <summary>
    /// Right-clicks a cell and reports whether the browser's menu was suppressed.
    /// </summary>
    /// <remarks>
    /// Dispatched rather than driven through <c>Mouse.ClickAsync</c> because the assertion is about
    /// <c>defaultPrevented</c>, which only the event object can answer — and a real right-click in a
    /// headless browser would open a menu the driver cannot see either way.
    /// </remarks>
    private async Task<bool> RightClickAsync(string modelId)
    {
        var prevented = await Page.EvaluateAsync<bool>(
            """
            id => {
              const cell = document.querySelector(`.joint-cell[model-id="${id}"]`);
              const body = cell.querySelector('[joint-selector="body"]') ?? cell;
              const box = body.getBoundingClientRect();
              const event = new MouseEvent('contextmenu', {
                bubbles: true, cancelable: true,
                clientX: box.x + box.width / 2, clientY: box.y + box.height / 2,
              });
              body.dispatchEvent(event);
              return event.defaultPrevented;
            }
            """,
            modelId);

        await Expect(Page.Locator(".bpmn-menu")).ToHaveCountAsync(1);
        return prevented;
    }

    private async Task ClickBodyAsync(string modelId)
    {
        await Page
            .Locator($".joint-cell[model-id='{modelId}'] [joint-selector='body'], "
                + $".joint-cell[model-id='{modelId}'] [joint-selector='outerRing']")
            .First.ClickAsync();

        await Expect(Page.Locator(".joint-highlight-mask")).ToHaveCountAsync(1);
    }

    private async Task GoToEditorAsync()
    {
        await Page.GotoAsync(app.Url(this));

        await Expect(Page.Locator(".bpmn-canvas__paper > svg")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.Locator(".bpmn-canvas .joint-cell").First).ToBeVisibleAsync();
    }
}
