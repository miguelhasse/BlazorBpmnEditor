using Hasseware.AspNetCore.Components.Bpmn.E2E.Tests.Fixtures;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit.v3;

namespace Hasseware.AspNetCore.Components.Bpmn.E2E.Tests;

public sealed class FeelAuthoringTests(DemoAppFixture app) : PageTest, IClassFixture<DemoAppFixture>
{
    [Theory]
    [InlineData("/feel-authoring", "fluent-authoring", "amount + 1", "=amount + 1")]
    [InlineData("/feel-authoring?plain=true", "native-authoring", "sum([1, 2, 3])", "=sum([1, 2, 3])")]
    [InlineData("/feel-authoring-server", "fluent-authoring", "amount + 1", "=amount + 1")]
    [InlineData("/feel-authoring-server?plain=true", "native-authoring", "sum([1, 2, 3])", "=sum([1, 2, 3])")]
    public async Task PageControlsInBothKitsPreserveEditingAndAuthorization(
        string route, string entryId, string initialDraft, string initialValue)
    {
        await GoToAuthoringAsync(route);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Managed FEEL authoring", Exact = true })).ToBeVisibleAsync();
        await Expect(Page.Locator("fluent-button#feel-reset")).ToBeVisibleAsync();
        await Expect(Page.Locator("fluent-button#feel-readonly")).ToBeVisibleAsync();
        await Expect(Page.Locator("fluent-checkbox#feel-authorize")).ToBeVisibleAsync();

        var entry = Page.Locator($"#{entryId}");
        var input = entry.GetByRole(AriaRole.Textbox);
        await Expect(input).ToHaveValueAsync(initialDraft);
        await input.FillAsync("1 + 2");
        await input.PressAsync("Control+Enter");
        await Expect(entry.Locator("output")).ToHaveTextAsync("=1 + 2");
        await Page.Locator("#feel-readonly").ClickAsync();
        await Expect(input).ToHaveAttributeAsync("readonly", "");
        await Expect(Page.Locator("#feel-readonly")).ToHaveAttributeAsync("aria-pressed", "true");
        await Page.Locator("#feel-readonly").ClickAsync();
        await Expect(input).Not.ToHaveAttributeAsync("readonly", "");
        await Page.Locator("#feel-reset").ClickAsync();
        await Expect(entry.GetByRole(AriaRole.Textbox)).ToHaveValueAsync(initialDraft);
        await Expect(entry.Locator("output")).ToHaveTextAsync(initialValue);

        await Page.GetByRole(AriaRole.Tab, new() { Name = "Evaluation", Exact = true }).ClickAsync();
        var run = Page.Locator("[data-action=run]");
        await run.ExpectDisabledAsync();
        var authorize = Page.GetByRole(AriaRole.Checkbox, new() { Name = "Authorize trusted sample interpretation" });
        await Expect(authorize).Not.ToBeCheckedAsync();
        await authorize.ClickAsync();
        await Expect(authorize).ToBeCheckedAsync();
        await run.ExpectDisabledAsync(false);
        await Expect(Page.GetByRole(AriaRole.Tabpanel).Locator("output")).ToHaveCountAsync(0);
        await run.ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Tabpanel).Locator("output pre")).ToHaveTextAsync("false");
        await authorize.ClickAsync();
        await Expect(authorize).Not.ToBeCheckedAsync();
        await run.ExpectDisabledAsync();
        await Expect(Page.GetByRole(AriaRole.Tabpanel).Locator("output")).ToHaveCountAsync(0);
    }

    [Theory]
    [InlineData("/feel-authoring")]
    [InlineData("/feel-authoring?plain=true")]
    [InlineData("/feel-authoring-server")]
    [InlineData("/feel-authoring-server?plain=true")]
    public async Task WorkspaceApplyAndCancelPreserveTransactionalBehavior(string route)
    {
        await GoToAuthoringAsync(route);
        var workspace = Page.GetByRole(AriaRole.Region, new() { Name = "Standalone FEEL workspace", Exact = true });
        var input = workspace.GetByRole(AriaRole.Textbox);
        var result = Page.Locator("#feel-workspace-result");
        await Expect(input).ToHaveValueAsync("false");
        await input.FillAsync("true");
        await Expect(result).ToHaveTextAsync("false");
        await workspace.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
        await Expect(workspace.GetByRole(AriaRole.Textbox)).ToHaveValueAsync("false");
        await Expect(result).ToHaveTextAsync("false");
        await Expect(Page.Locator(".demo-feel-authoring > .demo-shell > [role=status]"))
            .ToHaveTextAsync("Workspace draft cancelled; persisted value unchanged.");

        await workspace.GetByRole(AriaRole.Textbox).FillAsync("true");
        await workspace.GetByRole(AriaRole.Button, new() { Name = "Apply", Exact = true }).ClickAsync();
        await Expect(result).ToHaveTextAsync("true");
        await Page.Locator("#feel-readonly").ClickAsync();
        await workspace.GetByRole(AriaRole.Button, new() { Name = "Apply", Exact = true }).ExpectDisabledAsync();
        await Expect(workspace.GetByRole(AriaRole.Textbox)).ToHaveAttributeAsync("readonly", "");
        await Page.Locator("#feel-reset").ClickAsync();
        await Expect(result).ToHaveTextAsync("false");
    }

    [Theory]
    [InlineData("/feel-authoring", ColorScheme.Light)]
    [InlineData("/feel-authoring?plain=true", ColorScheme.Dark)]
    [InlineData("/feel-authoring-server", ColorScheme.Dark)]
    [InlineData("/feel-authoring-server?plain=true", ColorScheme.Light)]
    public async Task PageLayoutInSmallViewportScrollsWithoutHorizontalOverflow(string route, ColorScheme colorScheme)
    {
        await Page.SetViewportSizeAsync(390, 400);
        await Page.EmulateMediaAsync(new() { ColorScheme = colorScheme });
        await GoToAuthoringAsync(route);

        var scrollPage = Page.Locator(".demo-scroll-page");
        Assert.True(await scrollPage.EvaluateAsync<bool>("element => element.scrollHeight > element.clientHeight"));
        Assert.True(await scrollPage.EvaluateAsync<bool>("element => element.scrollWidth <= element.clientWidth + 1"));
        Assert.True(await scrollPage.EvaluateAsync<bool>(
            """
            element => {
                const page = element.getBoundingClientRect();
                const body = document.querySelector('.demo-chrome__body').getBoundingClientRect();
                return page.top >= body.top && page.bottom <= body.bottom;
            }
            """));
        var expectedBackground = colorScheme == ColorScheme.Dark ? "rgb(22, 24, 27)" : "rgb(255, 255, 255)";
        await Expect(Page.Locator(".demo-chrome")).ToHaveCSSAsync("background-color", expectedBackground);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Apply", Exact = true }).ScrollIntoViewIfNeededAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Apply", Exact = true })).ToBeInViewportAsync();
        await Page.Locator("#feel-workspace-result").ScrollIntoViewIfNeededAsync();
        await Expect(Page.Locator("#feel-workspace-result")).ToBeInViewportAsync();
        Assert.True(await scrollPage.EvaluateAsync<bool>("element => element.scrollTop > 0"));
        await Expect(Page.Locator(".demo-chrome__footer")).ToBeInViewportAsync();
    }

    private async Task GoToAuthoringAsync(string route)
    {
        await Page.GotoAsync($"{app.BaseUrl}{route}");
        await Expect(Page.Locator(".demo-feel-authoring [data-enhanced=true]").First)
            .ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Page.WaitForFunctionAsync("customElements.get('fluent-checkbox') !== undefined");
    }
}
