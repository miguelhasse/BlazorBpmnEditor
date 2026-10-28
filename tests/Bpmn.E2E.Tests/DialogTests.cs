using Hasseware.AspNetCore.Components.Bpmn.E2E.Tests.Fixtures;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit.v3;

namespace Hasseware.AspNetCore.Components.Bpmn.E2E.Tests;

public sealed class DialogTests(DemoAppFixture app) : PageTest, IClassFixture<DemoAppFixture>
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MessageListScrollsVerticallyWithoutHorizontalOverflow(bool plain)
    {
        await OpenEditorAsync(plain);
        Assert.True(Page.Context.Browser?.IsConnected);
        await Page.SetViewportSizeAsync(700, 800);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Messages", Exact = true }).ClickAsync();
        var dialog = Page.Locator(".bpmn-message-catalog");
        for (var index = 0; index < 9; index++)
        {
            await dialog.GetByRole(AriaRole.Button, new() { Name = "New message", Exact = true }).ClickAsync();
            await dialog.GetByRole(AriaRole.Textbox, new() { Name = "Message name (optional)" })
                .FillAsync(index == 0 ? new string('M', 120) : "");
            await dialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
            await Expect(dialog.Locator("[data-message-id]")).ToHaveCountAsync(index + 1);
        }

        var list = dialog.Locator(".bpmn-message-catalog__list");
        var layout = await list.EvaluateAsync<double[]>(
            """
            list => {
              const bounds = list.getBoundingClientRect();
              const buttons = [...list.querySelectorAll('[data-message-heading]')];
              return [
                list.scrollWidth - list.clientWidth,
                list.scrollHeight - list.clientHeight,
                Math.max(...buttons.map(button => button.getBoundingClientRect().right - bounds.right)),
              ];
            }
            """);
        Assert.True(layout[0] <= 1, $"Message list has {layout[0]}px horizontal overflow.");
        Assert.True(layout[1] > 0, "Message list must scroll vertically.");
        Assert.True(layout[2] <= 1, $"Message button exceeds list bounds by {layout[2]}px.");
        await list.Locator("[data-message-heading]").Last.FocusAsync();
        await Expect(list.Locator("[data-message-details]").Last).ToBeVisibleAsync();
        await list.Locator("[data-message-heading]").First.FocusAsync();
        await Expect(list.Locator("[data-message-details]").First).ToBeVisibleAsync();
        foreach (var width in new[] { 700, 380 })
        {
            await Page.SetViewportSizeAsync(width, 800);
            Assert.True(await list.EvaluateAsync<bool>(
                """
                list => list.scrollWidth <= list.clientWidth + 1 &&
                  [...list.querySelectorAll('[data-message-details]')].filter(e => e.getClientRects().length).every(details => {
                    const bounds = list.getBoundingClientRect();
                    return [...details.querySelectorAll('*')].every(element => {
                      const rect = element.getBoundingClientRect();
                      return !rect.width || (rect.left >= bounds.left - 1 && rect.right <= bounds.right + 1);
                    });
                  })
                """), "Expanded message rows and actions must fit the scrollable list.");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MessageCatalogContainsFocusAndDiscardsDraftOnEscape(bool plain)
    {
        await OpenEditorAsync(plain);
        var trigger = Page.GetByRole(AriaRole.Button, new() { Name = "Messages", Exact = true });
        await trigger.ClickAsync();
        var dialog = Page.Locator(".bpmn-message-catalog");
        await Expect(dialog).ToBeVisibleAsync();
        await Expect(dialog.GetByRole(AriaRole.Button, new() { Name = "Close", Exact = true })).ToHaveCountAsync(1);
        await Expect(dialog.Locator("span[tabindex]")).ToHaveCountAsync(0);
        await dialog.GetByRole(AriaRole.Button, new() { Name = "New message", Exact = true }).ClickAsync();
        await Expect(dialog.Locator(".bpmn-dialog__content")).ToBeFocusedAsync();
        await dialog.GetByRole(AriaRole.Textbox, new() { Name = "Message name (optional)" }).FillAsync("Discarded draft");
        for (var index = 0; index < 24; index++)
        {
            await Page.Keyboard.PressAsync(index < 12 ? "Tab" : "Shift+Tab");
            // Native modals may Tab into browser chrome, but not into the inert page.
            Assert.True(await dialog.EvaluateAsync<bool>("host => !document.hasFocus() || host === document.activeElement || host.contains(document.activeElement)"),
                await Page.EvaluateAsync<string>("() => document.activeElement?.outerHTML.slice(0, 500) ?? 'none'"));
        }
        await Page.Keyboard.PressAsync("Escape");
        await Expect(dialog).ToHaveCountAsync(0);
        await Expect(trigger).ToBeFocusedAsync();
        await trigger.ClickAsync();
        await Expect(dialog).ToBeVisibleAsync();
        await Expect(dialog.Locator("[data-message-id]")).ToHaveCountAsync(0);
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Close", Exact = true }).ClickAsync();
        await Expect(dialog).ToHaveCountAsync(0);
        await Expect(trigger).ToBeFocusedAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MessageCatalogSubmitsWithEnterAndFooterSave(bool plain)
    {
        await OpenEditorAsync(plain);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Messages", Exact = true }).ClickAsync();
        var dialog = Page.Locator(".bpmn-message-catalog");
        for (var index = 0; index < 2; index++)
        {
            await dialog.GetByRole(AriaRole.Button, new() { Name = "New message", Exact = true }).ClickAsync();
            await Expect(dialog.Locator(".bpmn-dialog__content")).ToBeFocusedAsync();
            var messageName = dialog.GetByRole(AriaRole.Textbox, new() { Name = "Message name (optional)" });
            await Expect(messageName).ToHaveAttributeAsync("autocomplete", "off");
            await messageName.FillAsync($"Message {index}");
            await Expect(dialog.GetByRole(AriaRole.Textbox, new() { Name = "Message name (optional)" })).ToBeFocusedAsync();
            if (index == 0)
                await Page.Keyboard.PressAsync("Enter");
            else
                await dialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
            await Expect(dialog.Locator("[data-message-id]")).ToHaveCountAsync(index + 1);
            await Expect(dialog.Locator(".bpmn-dialog__content")).ToBeFocusedAsync();
            await Page.Keyboard.PressAsync("Tab");
            Assert.True(await dialog.EvaluateAsync<bool>("host => host === document.activeElement || host.contains(document.activeElement)"),
                await Page.EvaluateAsync<string>("() => document.activeElement?.outerHTML.slice(0, 500) ?? 'none'"));
        }
        var newMessage = dialog.GetByRole(AriaRole.Button, new() { Name = "New message", Exact = true });
        await Page.Mouse.MoveAsync(0, 0);
        await newMessage.FocusAsync();
        await Expect(dialog.Locator("[data-message-details]:visible")).ToHaveCountAsync(0);
        var list = dialog.Locator(".bpmn-message-catalog__list");
        await Expect(list).ToHaveCSSAsync("overflow-y", "auto");
        var firstMessageId = await dialog.Locator("[data-message-id]").Nth(0).GetAttributeAsync("data-message-id")
            ?? throw new InvalidOperationException("The first message id was not rendered.");
        var secondMessageId = await dialog.Locator("[data-message-id]").Nth(1).GetAttributeAsync("data-message-id")
            ?? throw new InvalidOperationException("The second message id was not rendered.");
        var firstRow = dialog.Locator($"[data-message-id='{firstMessageId}']");
        var secondRow = dialog.Locator($"[data-message-id='{secondMessageId}']");
        await firstRow.Locator("[data-message-heading]").HoverAsync();
        await Expect(firstRow.Locator("[data-message-details]")).ToBeVisibleAsync();
        await Expect(secondRow.Locator("[data-message-details]")).ToBeHiddenAsync();
        await Page.Mouse.MoveAsync(0, 0);
        await Expect(firstRow.Locator("[data-message-details]")).ToBeHiddenAsync();
        await firstRow.Locator("[data-message-heading]").FocusAsync();
        await Expect(firstRow.Locator("[data-message-details]")).ToBeVisibleAsync();
        await Page.Keyboard.PressAsync("Tab");
        await Expect(firstRow.Locator("[data-message-action=edit]")).ToBeFocusedAsync();
        await Page.Keyboard.PressAsync("Tab");
        await Expect(firstRow.Locator("[data-message-action=delete]")).ToBeFocusedAsync();
        await Page.Keyboard.PressAsync("Tab");
        await Expect(secondRow.Locator("[data-message-heading]")).ToBeFocusedAsync();
        await Expect(firstRow.Locator("[data-message-details]")).ToBeHiddenAsync();
        await Expect(secondRow.Locator("[data-message-details]")).ToBeVisibleAsync();
        await firstRow.Locator("[data-message-heading]").ClickAsync();
        await Expect(firstRow.Locator("[data-message-details]")).ToBeVisibleAsync();
        await Page.Mouse.MoveAsync(0, 0);
        await newMessage.FocusAsync();
        await Expect(firstRow.Locator("[data-message-details]")).ToBeHiddenAsync();
        await Expect(secondRow.Locator("[data-message-details]")).ToBeHiddenAsync();
        await secondRow.Locator("[data-message-heading]").HoverAsync();
        await Expect(firstRow.Locator("[data-message-details]")).ToBeHiddenAsync();
        await Expect(secondRow.Locator("[data-message-details]")).ToBeVisibleAsync();
        var edit = secondRow.GetByRole(AriaRole.Button, new() { Name = "Edit name", Exact = true });
        var delete = secondRow.GetByRole(AriaRole.Button, new() { Name = "Delete", Exact = true });
        if (!plain)
        {
            await Expect(edit.Locator("svg")).ToHaveCountAsync(1);
            await Expect(delete.Locator("svg")).ToHaveCountAsync(1);
            Assert.Equal("", (await edit.TextContentAsync())?.Trim());
            Assert.Equal("", (await delete.TextContentAsync())?.Trim());
        }
        await edit.ClickAsync();
        await Expect(dialog.Locator("form code")).ToHaveTextAsync(secondMessageId);
        await Expect(dialog.GetByRole(AriaRole.Textbox, new() { Name = "Message name (optional)" })).ToHaveValueAsync("Message 1");
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
        await secondRow.Locator("[data-message-heading]").HoverAsync();
        await delete.ClickAsync();
        await Expect(dialog.Locator(".bpmn-message-catalog__panel")).ToContainTextAsync(secondMessageId);
        await Expect(dialog.Locator("[data-message-action=confirm-delete]")).ToBeVisibleAsync();
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
        await secondRow.Locator("[data-message-heading]").ClickAsync();
        await Page.Mouse.MoveAsync(0, 0);
        await newMessage.FocusAsync();
        await Expect(dialog.Locator("[data-message-details]:visible")).ToHaveCountAsync(0);
        await Page.Keyboard.PressAsync("Escape");
        await Expect(dialog).ToHaveCountAsync(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShortcutHeaderCloseAndEscapeRestoreTriggerFocus(bool plain)
    {
        await OpenEditorAsync(plain);
        var trigger = Page.GetByRole(AriaRole.Button, new() { Name = "Show keyboard shortcuts" });
        await trigger.ClickAsync();
        var dialog = Page.ShortcutDialog();
        var close = dialog.GetByRole(AriaRole.Button, new() { Name = "Close", Exact = true });
        await Expect(close).ToHaveCountAsync(1);
        var titleBounds = await dialog.Locator("h2").BoundingBoxAsync();
        var closeBounds = await close.BoundingBoxAsync();
        Assert.NotNull(titleBounds);
        Assert.NotNull(closeBounds);
        Assert.InRange(Math.Abs(titleBounds.Y - closeBounds.Y), 0, 24);
        await close.ClickAsync();
        await Expect(dialog).ToHaveCountAsync(0);
        await Expect(trigger).ToBeFocusedAsync();
        await trigger.ClickAsync();
        await Expect(dialog).ToBeVisibleAsync();
        await Page.Keyboard.PressAsync("Escape");
        await Expect(dialog).ToHaveCountAsync(0);
        await Expect(trigger).ToBeFocusedAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImportCloseAndEscapeNeverReplaceDocument(bool plain)
    {
        await OpenEditorAsync(plain);
        var before = await Page.Locator(".bpmn-canvas .joint-cell").CountAsync();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await using var transfer = await Page.EvaluateHandleAsync("""
                () => {
                  const transfer = new DataTransfer();
                  transfer.items.add(new File(
                    ['<definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" targetNamespace="urn:test" id="Defs"><process id="Other"><task id="OtherTask"/></process></definitions>'],
                    'other.bpmn', { type: 'application/xml' }));
                  return transfer;
                }
                """);
            await Page.Locator(".bpmn-canvas").DispatchEventAsync("drop", new Dictionary<string, object> { ["dataTransfer"] = transfer });
            var dialog = Page.Locator(".bpmn-import");
            await Expect(dialog).ToBeVisibleAsync();
            await Expect(dialog.GetByRole(AriaRole.Button, new() { Name = "Close", Exact = true })).ToHaveCountAsync(1);
            await Expect(Page.ImportAccept()).ToBeVisibleAsync();
            if (attempt == 0)
                await Page.Keyboard.PressAsync("Escape");
            else
                await dialog.GetByRole(AriaRole.Button, new() { Name = "Close", Exact = true }).ClickAsync();
            await Expect(dialog).ToHaveCountAsync(0);
            await Expect(Page.Locator(".bpmn-canvas .joint-cell")).ToHaveCountAsync(before);
        }
    }

    private async Task OpenEditorAsync(bool plain)
    {
        Assert.True(Page.Context.Browser?.IsConnected);
        await Page.GotoAsync($"{app.BaseUrl}{(plain ? KitRoute.Plain : KitRoute.Fluent)}");
        await Expect(Page.Locator(".demo-status")).ToContainTextAsync("Rendered", new() { Timeout = 30_000 });
    }
}
