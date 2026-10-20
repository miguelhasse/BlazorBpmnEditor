using Hasseware.AspNetCore.Components.Bpmn.E2E.Tests.Fixtures;

using Microsoft.Playwright;
using Microsoft.Playwright.Xunit.v3;

namespace Hasseware.AspNetCore.Components.Bpmn.E2E.Tests;

/// <summary>
/// Walking the diagram with the keyboard alone (implementation plan M7).
/// </summary>
/// <remarks>
/// The milestone's exit criterion is that the whole diagram is navigable without a mouse, and that
/// is only true in a browser: the tree is rendered by Blazor, the keys are delivered by the DOM,
/// the selection is drawn by JointJS, and the thing that broke the first time was none of those —
/// it was the canvas's own pan shortcuts consuming every arrow key before the tree saw one.
/// </remarks>
public class KeyboardNavigationTests(DemoAppFixture app) : PageTest, IClassFixture<DemoAppFixture>
{
    [Fact]
    public async Task TheDiagramIsATreeWithOneItemPerElement()
    {
        await GoToEditorAsync("order-process.bpmn");

        var tree = Page.Locator(".bpmn-a11y-tree");
        await Expect(tree).ToHaveAttributeAsync("role", "tree");
        await Expect(Page.Locator(".bpmn-a11y-tree__item")).ToHaveCountAsync(7);
    }

    [Fact]
    public async Task TheDrawnShapesAreNotTheAccessibleRepresentation()
    {
        /*
         * Both would say everything twice. The SVG keeps its names — a tool reading the DOM gets
         * the same words a reader hears — but it is hidden from assistive technology, because in
         * paint order it says nothing about what connects to what and, once the paper culls what
         * is off screen, it is not even all there.
         */
        await GoToEditorAsync("order-process.bpmn");

        await Expect(Page.Locator(".bpmn-canvas__paper")).ToHaveAttributeAsync("aria-hidden", "true");
    }

    [Fact]
    public async Task ExactlyOneItemIsInTheTabOrder()
    {
        await GoToEditorAsync("large-virtualization-stress.bpmn");

        // Six hundred elements, one tab stop. The arrow keys move within.
        await Expect(Page.Locator(".bpmn-a11y-tree__item")).ToHaveCountAsync(602);
        await Expect(Page.Locator(".bpmn-a11y-tree__item[tabindex='0']")).ToHaveCountAsync(1);
    }

    [Fact]
    public async Task ArrowKeysWalkTheFlowAndSelectAsTheyGo()
    {
        await GoToEditorAsync("order-process.bpmn");
        await EnterTheTreeAsync();

        Assert.Equal("StartEvent_1", await FocusedElementAsync());

        await PressAsync("ArrowRight");
        Assert.Equal("Task_ValidateOrder", await FocusedElementAsync());

        await PressAsync("ArrowRight");
        Assert.Equal("Gateway_IsValid", await FocusedElementAsync());

        await PressAsync("ArrowLeft");
        Assert.Equal("Task_ValidateOrder", await FocusedElementAsync());

        // Moving is selecting: a sighted keyboard user has to see where the announcements are.
        await Expect(Page.Locator(".joint-highlight-mask")).ToHaveCountAsync(1);
        await Expect(Page.Locator(".demo-edit-status")).ToContainTextAsync("Task_ValidateOrder");
    }

    [Fact]
    public async Task HomeAndEndAreTheEndsOfTheDiagram()
    {
        await GoToEditorAsync("order-process.bpmn");
        await EnterTheTreeAsync();

        await PressAsync("End");
        Assert.Equal("EndEvent_Rejected", await FocusedElementAsync());

        await PressAsync("Home");
        Assert.Equal("StartEvent_1", await FocusedElementAsync());
    }

    [Fact]
    public async Task TheCanvasFollowsTheReader()
    {
        /*
         * The other half of "navigable": a reader stepping into a part of the diagram that is off
         * screen must end up looking at it. Measured on the six-hundred-shape fixture, where
         * twenty-five steps is far past the right edge.
         */
        await GoToEditorAsync("large-virtualization-stress.bpmn");
        await EnterTheTreeAsync();

        var before = await TransformAsync();

        for (var step = 0; step < 25; step++) await PressAsync("ArrowRight", settle: 120);

        Assert.NotEqual(before, await TransformAsync());

        var visible = await Page.EvaluateAsync<bool>(
            """
            () => {
              const mask = document.querySelector('.joint-highlight-mask');
              const host = document.querySelector('.bpmn-canvas');
              if (mask == null || host == null) return false;
              const m = mask.getBoundingClientRect();
              const h = host.getBoundingClientRect();
              return m.left >= h.left - 1 && m.right <= h.right + 1 && m.top >= h.top - 1 && m.bottom <= h.bottom + 1;
            }
            """);

        Assert.True(visible, "the element the reader moved to was left off screen");
    }

    [Fact]
    public async Task EnterOpensTheLabelForEditing()
    {
        // Editing without a pointer, which is the rest of what "operable by keyboard" means.
        await GoToEditorAsync("order-process.bpmn");
        await EnterTheTreeAsync();

        await PressAsync("ArrowRight");
        await PressAsync("Enter", settle: 700);

        await Expect(Page.Locator(".bpmn-label-editor")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task TheTreeIsNotAKeyboardTrap()
    {
        /*
         * The one thing a keyboard surface must never be. Tab is not handled by the tree at all,
         * so focus leaves it the way it leaves any other control — which also means the canvas's
         * own shortcut handler must not swallow it on the way past.
         */
        await GoToEditorAsync("order-process.bpmn");
        await EnterTheTreeAsync();

        await PressAsync("Tab");

        var inside = await Page.EvaluateAsync<bool>(
            "() => document.activeElement?.closest('.bpmn-a11y-tree') != null");

        Assert.False(inside, "Tab did not move focus out of the tree");
    }

    [Fact]
    public async Task TheCanvasStillPansWhenItIsTheThingWithFocus()
    {
        /*
         * The fix for the arrow keys was to let a composite widget own its unmodified keys. That
         * must not have taken panning away from the canvas itself, which is not inside the tree.
         */
        await GoToEditorAsync("order-process.bpmn");

        await Page.Locator(".bpmn-canvas").FocusAsync();
        var before = await TransformAsync();

        await PressAsync("ArrowRight", settle: 500);

        Assert.NotEqual(before, await TransformAsync());
    }

    // --- virtualization ---------------------------------------------------------------

    [Fact]
    public async Task OnlyWhatIsNearTheViewportIsDrawn()
    {
        /*
         * The budget in docs/performance.md. Six hundred shapes is 8,433 SVG nodes if all of them
         * are in the document, and it is the browser's layout and paint over that tree that costs.
         *
         * Safe only because the tree beside it lists every element whatever the paper has mounted:
         * culling shapes while the drawn cells were still the accessible representation would have
         * hidden most of the diagram from anyone using a screen reader. That is why this assertion
         * sits next to the one below it.
         */
        await GoToEditorAsync("large-virtualization-stress.bpmn");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Reset" }).ClickAsync();
        await Page.WaitForTimeoutAsync(1200);

        var drawn = await Page.EvaluateAsync<int>(
            "() => document.querySelectorAll('.bpmn-canvas__paper svg *').length");

        Assert.InRange(drawn, 1, 2500);
    }

    [Fact]
    public async Task CullingTakesNothingFromTheReader()
    {
        await GoToEditorAsync("large-virtualization-stress.bpmn");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Reset" }).ClickAsync();
        await Page.WaitForTimeoutAsync(1200);

        // Every element, drawn or not.
        await Expect(Page.Locator(".bpmn-a11y-tree__item")).ToHaveCountAsync(602);
    }

    [Fact]
    public async Task ADiagramThatFitsIsDrawnEntirely()
    {
        // Culling must only ever remove what is off screen. Fitted, nothing is.
        await GoToEditorAsync("pizza-store.bpmn");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Fit" }).ClickAsync();
        await Page.WaitForTimeoutAsync(1000);

        var offScreen = await Page.EvaluateAsync<int>(
            """
            () => {
              const host = document.querySelector('.bpmn-canvas');
              if (host == null) return -1;
              const view = host.getBoundingClientRect();
              return [...document.querySelectorAll('.bpmn-canvas .joint-element[model-id]')].filter((c) => {
                const b = c.getBoundingClientRect();
                return b.right <= view.left || b.left >= view.right || b.bottom <= view.top || b.top >= view.bottom;
              }).length;
            }
            """);

        await Expect(Page.Locator(".bpmn-canvas .joint-element[model-id]")).ToHaveCountAsync(24);
        Assert.Equal(0, offScreen);
    }

    // --- helpers ------------------------------------------------------------------

    private Task<string> TransformAsync() => Page.EvaluateAsync<string>(
        "() => document.querySelector('.bpmn-canvas svg g')?.getAttribute('transform') ?? '(none)'");

    /// <summary>The element id behind the focused tree item, or what has focus instead.</summary>
    private Task<string> FocusedElementAsync() => Page.EvaluateAsync<string>(
        """
        () => {
          const active = document.activeElement;
          const id = active?.getAttribute('id') ?? '';
          return id.startsWith('bpmn-a11y-') ? id.slice('bpmn-a11y-'.length) : `(focus on ${active?.tagName ?? 'nothing'})`;
        }
        """);

    private async Task PressAsync(string key, int settle = 400)
    {
        await Page.Keyboard.PressAsync(key);
        await Page.WaitForTimeoutAsync(settle);
    }

    /// <summary>Reaches the diagram the way a keyboard user does: focus the canvas, then Tab in.</summary>
    private async Task EnterTheTreeAsync()
    {
        await Page.Locator(".bpmn-canvas").FocusAsync();
        await Page.Keyboard.PressAsync("Tab");
        await Page.WaitForTimeoutAsync(600);
    }

    private async Task GoToEditorAsync(string sample)
    {
        await Page.GotoAsync(app.Url(this));

        await Expect(Page.Locator(".bpmn-canvas__paper > svg")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.Locator(".bpmn-canvas .joint-cell").First).ToBeVisibleAsync();

        await Page.OpenDemoFilesAsync();
        await Page.GetByRole(AriaRole.Combobox, new() { Name = "Sample diagram" }).ClickAsync();
        await Page.Locator("fluent-option", new() { HasTextString = sample }).ClickAsync();
        await Page.WaitForTimeoutAsync(2000);
    }
}
