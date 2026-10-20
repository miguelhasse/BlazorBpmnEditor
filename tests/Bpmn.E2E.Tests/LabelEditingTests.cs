using Hasseware.AspNetCore.Components.Bpmn.E2E.Tests.Fixtures;

using Microsoft.Playwright;
using Microsoft.Playwright.Xunit.v3;

namespace Hasseware.AspNetCore.Components.Bpmn.E2E.Tests;

/// <summary>Inline editor geometry, content sizing, and viewport tracking in both kits.</summary>
public class LabelEditingTests(DemoAppFixture app) : PageTest, IClassFixture<DemoAppFixture>
{
    private ILocator Input => Page.Locator(".bpmn-label-editor__input");

    [Theory]
    [InlineData("Task_ValidateOrder", "body", false)]
    [InlineData("StartEvent_1", "outerRing", true)]
    [InlineData("Gateway_IsValid", "body", true)]
    public async Task EmptyShapeLabelOpensAtCompactDefaultPosition(string id, string selector, bool external)
    {
        await GoToEditorAsync();
        var body = Page.Locator($".joint-cell[model-id='{id}'] [joint-selector='{selector}']");
        await body.DblClickAsync();
        await Expect(Input).ToBeFocusedAsync();
        await Input.FillAsync(string.Empty);
        await Input.PressAsync("Enter");
        await Expect(Input).ToHaveCountAsync(0);
        await Expect(Page.Locator($".joint-cell[model-id='{id}'] [joint-selector='label']")).ToBeHiddenAsync();

        await body.DblClickAsync();
        await Expect(Input).ToHaveValueAsync(string.Empty);
        await AssertSingleLineAsync();

        var shape = await body.BoundingBoxAsync();
        var editor = await Input.BoundingBoxAsync();
        Assert.NotNull(shape);
        Assert.NotNull(editor);
        Assert.InRange(Math.Abs(editor.X + editor.Width / 2 - shape.X - shape.Width / 2), 0, 2);
        Assert.True(editor.Width <= 100, $"Empty editor width was {editor.Width}.");
        Assert.True(external ? editor.Y >= shape.Y + shape.Height : editor.Y > shape.Y);
    }

    [Fact]
    public async Task EmptyConnectorLabelOpensAtPathMidpoint()
    {
        await GoToEditorAsync();
        var line = Page.Locator(".joint-link[model-id='Flow_4'] [joint-selector='line']");
        await line.DispatchEventAsync("dblclick", new { bubbles = true, cancelable = true });
        await Expect(Input).ToBeFocusedAsync();
        await Input.FillAsync(string.Empty);
        await Input.PressAsync("Enter");
        await Expect(Input).ToHaveCountAsync(0);
        await line.DispatchEventAsync("dblclick", new { bubbles = true, cancelable = true });
        await Expect(Input).ToHaveValueAsync(string.Empty);
        await AssertSingleLineAsync();

        var geometry = await line.EvaluateAsync<double[]>(
            """
            line => {
                const point = line.getPointAtLength(line.getTotalLength() / 2);
                const middle = new DOMPoint(point.x, point.y).matrixTransform(line.getScreenCTM());
                const editor = document.querySelector('.bpmn-label-editor__input').getBoundingClientRect();
                return [editor.x + editor.width / 2 - middle.x, editor.y + editor.height / 2 - middle.y,
                    editor.width, editor.height, parseFloat(getComputedStyle(document.querySelector('.bpmn-label-editor__input')).lineHeight)];
            }
            """);

        Assert.InRange(Math.Abs(geometry[0]), 0, 2);
        Assert.InRange(Math.Abs(geometry[1]), 0, 2);
        Assert.InRange(geometry[2], 80, 100);
        Assert.InRange(Math.Abs(geometry[3] - geometry[4]), 0, 1);
    }

    [Fact]
    public async Task LineBreaksAndWrappingGrowAndShrinkTheEditorWithoutClipping()
    {
        await GoToEditorAsync();
        await OpenTaskAsync();
        await Input.FillAsync("First");
        await Input.PressAsync("End");
        await Input.PressAsync("Shift+Enter");
        await Input.PressSequentiallyAsync("Second");
        await Input.PressAsync("Shift+Enter");
        await Input.PressAsync("Shift+Enter");
        await Expect(Input).ToHaveValueAsync("First\nSecond\n\n");

        await Page.WaitForFunctionAsync(
            """
            () => {
                const input = document.querySelector('.bpmn-label-editor__input');
                return input.clientHeight >= parseFloat(getComputedStyle(input).lineHeight) * 4 - 1
                    && input.scrollHeight <= input.clientHeight + 1;
            }
            """);

        await Input.FillAsync("A long label with enough words to wrap across multiple lines without explicit line breaks");
        await Page.WaitForFunctionAsync(
            """
            () => {
                const input = document.querySelector('.bpmn-label-editor__input');
                return input.clientHeight > parseFloat(getComputedStyle(input).lineHeight) * 2
                    && input.scrollHeight <= input.clientHeight + 1;
            }
            """);
        await Input.FillAsync(string.Empty);
        await AssertSingleLineAsync();
        await Expect(Input).ToBeFocusedAsync();
    }

    [Fact]
    public async Task WheelPanAndZoomKeepTheEditorAlignedAndPreserveDraftAndCaret()
    {
        await GoToEditorAsync();
        await OpenTaskAsync();
        await Input.FillAsync("Unsaved\nmultiline draft");
        await Input.EvaluateAsync("input => input.setSelectionRange(3, 8)");
        var before = await LabelCenterAsync();

        await WheelAsync(120, false);
        await Page.WaitForFunctionAsync(
            "before => document.querySelector(\".joint-cell[model-id='Task_ValidateOrder'] [joint-selector='label']\").getBoundingClientRect().y < before - 10",
            before[1]);
        await AssertAlignedAsync();
        await WheelAsync(-120, true);
        await Page.WaitForFunctionAsync(
            """
            () => parseFloat(getComputedStyle(document.querySelector('.bpmn-label-editor__input')).fontSize) > 12
            """);
        await AssertAlignedAsync();

        await Expect(Input).ToHaveValueAsync("Unsaved\nmultiline draft");
        await Expect(Input).ToBeFocusedAsync();
        var selection = await Input.EvaluateAsync<int[]>("input => [input.selectionStart, input.selectionEnd]");
        Assert.Equal([3, 8], selection);
    }

    [Fact]
    public async Task AncestorScrollingKeepsTheEditorAlignedWithoutCommitting()
    {
        await GoToEditorAsync();
        await OpenTaskAsync();
        await Input.FillAsync("Still editing");
        var before = await LabelCenterAsync();

        await Page.EvaluateAsync(
            """
            () => {
                const page = document.querySelector('.demo-shell').parentElement;
                page.style.height = '400px';
                page.style.maxHeight = '400px';
                page.style.overflowY = 'auto';
                page.scrollTop = 80;
            }
            """);
        await Page.WaitForFunctionAsync("document.querySelector('.demo-shell').parentElement.scrollTop > 0");
        var after = await LabelCenterAsync();
        Assert.True(after[1] < before[1]);
        await AssertAlignedAsync();
        await Expect(Input).ToHaveValueAsync("Still editing");
        await Expect(Input).ToBeFocusedAsync();
    }

    [Fact]
    public async Task ReopeningACommittedConnectorLabelUsesItsRenderedTextPosition()
    {
        await GoToEditorAsync();
        var line = Page.Locator(".joint-link [joint-selector='line']").Last;
        await line.DispatchEventAsync("dblclick", new { bubbles = true, cancelable = true });
        await Expect(Input).ToBeFocusedAsync();
        await Input.FillAsync("Connector\nname");
        await Input.PressAsync("Enter");
        await Expect(Input).ToHaveCountAsync(0);
        await Expect(Page.Locator("[label-idx] [joint-selector='text']").Last).ToContainTextAsync("Connector");

        await line.DispatchEventAsync("dblclick", new { bubbles = true, cancelable = true });
        await Expect(Input).ToHaveValueAsync("Connector\nname");
        await Page.WaitForFunctionAsync(
            """
            () => {
                const label = [...document.querySelectorAll('[label-idx] [joint-selector="text"]')].at(-1).getBoundingClientRect();
                const editor = document.querySelector('.bpmn-label-editor__input').getBoundingClientRect();
                return Math.abs(label.x + label.width / 2 - editor.x - editor.width / 2) < 2
                    && Math.abs(label.y + label.height / 2 - editor.y - editor.height / 2) < 2;
            }
            """);
        await Input.FillAsync("Discarded");
        await Input.PressAsync("Escape");
        await Expect(Input).ToHaveCountAsync(0);
        await line.DispatchEventAsync("dblclick", new { bubbles = true, cancelable = true });
        await Expect(Input).ToHaveValueAsync("Connector\nname");
    }

    private async Task GoToEditorAsync()
    {
        await Page.SetViewportSizeAsync(1600, 1000);
        await Page.GotoAsync(app.Url(this));
        await Expect(Page.Locator(".bpmn-canvas__paper > svg")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Page.OpenDemoFilesAsync();
        await Page.GetByRole(AriaRole.Combobox, new() { Name = "Sample diagram" }).ClickAsync();
        await Page.Locator("fluent-option", new() { HasTextString = "order-process.bpmn" }).ClickAsync();
        await Page.Keyboard.PressAsync("Escape");
        await Expect(Page.Locator(".joint-cell[model-id='Task_ValidateOrder']")).ToBeVisibleAsync();
    }

    private async Task OpenTaskAsync()
    {
        await Page.Locator(".joint-cell[model-id='Task_ValidateOrder'] [joint-selector='body']").DblClickAsync();
        await Expect(Input).ToBeFocusedAsync();
    }

    private Task<IJSHandle> AssertSingleLineAsync() => Page.WaitForFunctionAsync(
        """
        () => {
            const input = document.querySelector('.bpmn-label-editor__input');
            return Math.abs(input.getBoundingClientRect().height - parseFloat(getComputedStyle(input).lineHeight)) <= 1;
        }
        """);

    private Task<double[]> LabelCenterAsync() => Page.Locator(".joint-cell[model-id='Task_ValidateOrder'] [joint-selector='label']")
        .EvaluateAsync<double[]>("label => { const box = label.getBoundingClientRect(); return [box.x + box.width / 2, box.y + box.height / 2]; }");

    private Task<IJSHandle> AssertAlignedAsync() => Page.WaitForFunctionAsync(
        """
        () => {
            const label = document.querySelector(".joint-cell[model-id='Task_ValidateOrder'] [joint-selector='label']").getBoundingClientRect();
            const editor = document.querySelector('.bpmn-label-editor__input').getBoundingClientRect();
            return Math.abs(label.x + label.width / 2 - editor.x - editor.width / 2) < 2
                && Math.abs(label.y + label.height / 2 - editor.y - editor.height / 2) < 2;
        }
        """);

    private Task<System.Text.Json.JsonElement?> WheelAsync(double delta, bool zoom) => Page.Locator(".bpmn-canvas__paper")
        .EvaluateAsync(
            """
            (paper, args) => {
                const box = paper.getBoundingClientRect();
                paper.dispatchEvent(new WheelEvent('wheel', {
                    deltaY: args.delta, ctrlKey: args.zoom, bubbles: true, cancelable: true,
                    clientX: box.x + box.width / 2, clientY: box.y + box.height / 2
                }));
            }
            """, new { delta, zoom });
}
