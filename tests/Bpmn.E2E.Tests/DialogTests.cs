using Hasseware.AspNetCore.Components.Bpmn.E2E.Tests.Fixtures;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit.v3;

namespace Hasseware.AspNetCore.Components.Bpmn.E2E.Tests;

public sealed class DialogTests(DemoAppFixture app) : PageTest, IClassFixture<DemoAppFixture>
{
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
            await dialog.GetByRole(AriaRole.Textbox, new() { Name = "Message name (optional)" }).FillAsync($"Message {index}");
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
