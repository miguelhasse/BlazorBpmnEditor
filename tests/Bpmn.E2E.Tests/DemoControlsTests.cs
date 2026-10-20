using Hasseware.AspNetCore.Components.Bpmn.E2E.Tests.Fixtures;

using Microsoft.Playwright;
using Microsoft.Playwright.Xunit.v3;

namespace Hasseware.AspNetCore.Components.Bpmn.E2E.Tests;

public class DemoControlsTests(DemoAppFixture app) : PageTest, IClassFixture<DemoAppFixture>
{
    [Fact]
    public async Task ControlsInitiallyKeepFrequentActionsVisibleAndSecondaryActionsGrouped()
    {
        await GoToEditorAsync();

        await Expect(FilesButton).ToBeVisibleAsync();
        await Expect(SettingsButton).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Switch, new() { Name = "Properties", Exact = true })).Not.ToBeCheckedAsync();
        await Expect(Page.Locator("fluent-button.demo-external-change")).ToBeVisibleAsync();
        await Page.Locator(".demo-external-change").ExpectDisabledAsync(false);
        await Expect(FilesPopover).ToBeHiddenAsync();
        await Expect(SettingsPopover).ToBeHiddenAsync();
        await Expect(Page.Locator(".demo-editor-controls input[type=checkbox]")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task PopoversRestoreFocusAndExpandedStateAfterKeyboardAndOutsideDismissal()
    {
        await GoToEditorAsync();
        await FilesButton.FocusAsync();

        await Page.Keyboard.PressAsync("Enter");
        await Expect(FilesPopover).ToBeVisibleAsync();
        await Expect(FilesPopover.GetByRole(AriaRole.Button, new() { Name = "Open file", Exact = true })).ToBeFocusedAsync();
        await Page.Keyboard.PressAsync("Escape");
        await Expect(FilesPopover).ToBeHiddenAsync();
        await Expect(FilesButton).ToHaveAttributeAsync("aria-expanded", "false");
        await Expect(FilesButton).ToBeFocusedAsync();

        await SettingsButton.ClickAsync();
        await Expect(SettingsPopover.GetByRole(AriaRole.Switch, new() { Name = "High contrast", Exact = true })).ToBeFocusedAsync();
        await FilesButton.ClickAsync();
        await Expect(SettingsPopover).ToBeHiddenAsync();
        await Expect(SettingsButton).ToHaveAttributeAsync("aria-expanded", "false");
        await Expect(FilesPopover).ToBeVisibleAsync();

        await Page.Locator(".demo-chrome__header").ClickAsync();
        await Expect(FilesPopover).ToBeHiddenAsync();
        await Expect(FilesButton).ToHaveAttributeAsync("aria-expanded", "false");
    }

    [Fact]
    public async Task SettingsSwitchesApplyImmediatelyAndPersistAcrossReopening()
    {
        await GoToEditorAsync();
        await SettingsButton.ClickAsync();
        await Expect(SettingsPopover.GetByRole(AriaRole.Switch)).ToHaveCountAsync(6);
        await Expect(SettingsPopover.Locator("fluent-switch[checked]")).ToHaveCountAsync(0);

        var contrast = SettingsPopover.GetByRole(AriaRole.Switch, new() { Name = "High contrast", Exact = true });
        await contrast.CheckAsync();
        await Expect(contrast).ToBeCheckedAsync();
        await Expect(SettingsPopover).ToBeVisibleAsync();
        await Page.SetDemoSwitchAsync("Read only");
        await Expect(Page.Locator(".bpmn-stencil")).ToHaveCountAsync(0);

        await SettingsButton.ClickAsync();
        await Expect(contrast).ToBeCheckedAsync();
        await Expect(SettingsPopover.GetByRole(AriaRole.Switch, new() { Name = "Read only", Exact = true })).ToBeCheckedAsync();
        await Page.Keyboard.PressAsync("Escape");

        await Page.GetByRole(AriaRole.Switch, new() { Name = "Properties", Exact = true }).CheckAsync();
        await Expect(Page.Locator(".bpmn-properties")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task OpeningAndExpandingSettingsDoesNotMoveCanvas()
    {
        await GoToEditorAsync();
        var canvas = Page.Locator(".bpmn-canvas");
        var before = await canvas.BoundingBoxAsync();
        Assert.NotNull(before);

        await SettingsButton.ClickAsync();
        await SettingsPopover.GetByRole(AriaRole.Switch, new() { Name = "Offer to browser agents (WebMCP)", Exact = true }).CheckAsync();
        await Expect(SettingsPopover.GetByRole(AriaRole.Switch)).ToHaveCountAsync(7);

        var after = await canvas.BoundingBoxAsync();
        Assert.NotNull(after);
        Assert.Equal(before.Y, after.Y);
        Assert.Equal(before.Height, after.Height);
    }

    [Fact]
    public async Task SelectingASampleLoadsDiagramAndDismissesFilesPopover()
    {
        await GoToEditorAsync();
        await Page.OpenDemoFilesAsync();

        await Page.GetByRole(AriaRole.Combobox, new() { Name = "Sample diagram" }).ClickAsync();
        await Page.Locator("fluent-option", new() { HasTextString = "order-process.bpmn" }).ClickAsync();

        await Expect(FilesPopover).ToBeHiddenAsync();
        await Expect(Page.Locator(".joint-cell[model-id='Task_ValidateOrder']")).ToHaveCountAsync(1);
        await Expect(Page.Locator(".demo-status")).ToContainTextAsync("Rendered");
    }

    [Theory]
    [InlineData("Export XML", "<bpmn:definitions")]
    [InlineData("Export JSON", "\"flowNodes\"")]
    public async Task ExportPreservesFormatAndDismissesFilesPopover(string action, string content)
    {
        await GoToEditorAsync();
        await Page.OpenDemoFilesAsync();
        await FilesPopover.Locator(".demo-save").ExpectDisabledAsync();
        await FilesPopover.GetByRole(AriaRole.Button, new() { Name = action, Exact = true }).ExpectDisabledAsync(false);

        await FilesPopover.GetByRole(AriaRole.Button, new() { Name = action, Exact = true }).ClickAsync();

        await Expect(FilesPopover).ToBeHiddenAsync();
        await Expect(Page.Locator(".demo-export-xml")).ToContainTextAsync(content);
    }

    [Fact]
    public async Task OpeningAFileOffersImportWithoutReplacingCurrentDocument()
    {
        await GoToEditorAsync();
        var before = await Page.Locator(".bpmn-canvas .joint-cell").CountAsync();
        var response = await Page.APIRequest.GetAsync($"{app.BaseUrl}/samples/order-process.bpmn");
        Assert.True(response.Ok);
        await Page.OpenDemoFilesAsync();

        var chooser = await Page.RunAndWaitForFileChooserAsync(
            () => FilesPopover.GetByRole(AriaRole.Button, new() { Name = "Open file", Exact = true }).ClickAsync());
        await chooser.SetFilesAsync(new FilePayload
        {
            Name = "opened.bpmn",
            MimeType = "application/xml",
            Buffer = await response.BodyAsync(),
        });

        await Expect(FilesPopover).ToBeHiddenAsync();
        await Expect(Page.Locator(".bpmn-import__title")).ToContainTextAsync("opened.bpmn");
        await Expect(Page.Locator(".bpmn-canvas .joint-cell")).ToHaveCountAsync(before);
        await Page.Locator(".bpmn-import__cancel").ClickAsync();
        await Expect(Page.Locator(".bpmn-canvas .joint-cell")).ToHaveCountAsync(before);
    }

    [Fact]
    public async Task SaveRespectsDirtyAndReadOnlyState()
    {
        await GoToEditorAsync();
        await Page.Locator(".bpmn-canvas .joint-element[model-id^='Task_']").ClickAsync();
        await Page.Locator(".bpmn-canvas").FocusAsync();
        await Page.Keyboard.PressAsync("Delete");
        await Expect(Page.Locator(".demo-save-status")).ToContainTextAsync("Unsaved");
        await Page.OpenDemoFilesAsync();
        await FilesPopover.Locator(".demo-save").ExpectDisabledAsync(false);

        await Page.SetDemoSwitchAsync("Read only");
        await Page.OpenDemoFilesAsync();
        await FilesPopover.Locator(".demo-save").ExpectDisabledAsync();

        await Page.SetDemoSwitchAsync("Read only", false);
        await Page.OpenDemoFilesAsync();
        await FilesPopover.Locator(".demo-save").ClickAsync();

        await Expect(FilesPopover).ToBeHiddenAsync();
        await Expect(Page.Locator(".demo-save-status")).ToContainTextAsync("Saved");
        await Page.OpenDemoFilesAsync();
        await FilesPopover.Locator(".demo-save").ExpectDisabledAsync();
    }

    [Theory]
    [InlineData(320)]
    [InlineData(390)]
    public async Task ControlsKeepActionsAndPopoverWithinNarrowViewport(int width)
    {
        await Page.SetViewportSizeAsync(width, 800);
        await GoToEditorAsync();

        await Expect(FilesButton).ToBeInViewportAsync();
        await Expect(SettingsButton).ToBeInViewportAsync();
        await Expect(Page.GetByRole(AriaRole.Switch, new() { Name = "Properties", Exact = true })).ToBeInViewportAsync();
        await Expect(Page.Locator(".demo-external-change")).ToBeInViewportAsync();
        await SettingsButton.ClickAsync();

        var fits = await SettingsPopover.Locator("[part=dialog]").EvaluateAsync<bool>(
            """
            element => {
              const bounds = element.getBoundingClientRect();
              return bounds.left >= 0 && bounds.right <= innerWidth &&
                bounds.top >= 0 && bounds.bottom <= innerHeight &&
                element.scrollWidth <= element.clientWidth;
            }
            """);
        Assert.True(fits, "Settings must fit the viewport without horizontal clipping.");
        await Expect(SettingsPopover.GetByRole(AriaRole.Switch, new() { Name = "Animate tokens", Exact = true })).ToBeInViewportAsync();
    }

    private ILocator FilesButton => Page.GetByRole(AriaRole.Button, new() { Name = "Files", Exact = true });

    private ILocator SettingsButton => Page.GetByRole(AriaRole.Button, new() { Name = "Settings", Exact = true });

    private ILocator FilesPopover => Page.Locator(".demo-control-popover").Filter(new() { Has = Page.Locator("[data-controls=file-actions]") });

    private ILocator SettingsPopover => Page.Locator(".demo-control-popover").Filter(new() { Has = Page.Locator("[data-controls=settings-group]") });

    private async Task GoToEditorAsync()
    {
        await Page.GotoAsync(app.Url(this));
        await Expect(Page.Locator(".bpmn-canvas__paper > svg")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.Locator(".demo-status")).ToContainTextAsync("Rendered");
    }
}
