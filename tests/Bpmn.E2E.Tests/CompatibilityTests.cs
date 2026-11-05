using Hasseware.AspNetCore.Components.Bpmn.E2E.Tests.Fixtures;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit.v3;

namespace Hasseware.AspNetCore.Components.Bpmn.E2E.Tests;

public sealed class CompatibilityTests(DemoAppFixture app) : PageTest, IClassFixture<DemoAppFixture>
{
    [Fact]
    public async Task CustomRegistryRetainsParametersInjectionAndCallbacksInBothKits()
    {
        await Page.GotoAsync($"{app.BaseUrl}/compatibility");
        var standard = Page.Locator("#compatibility-standard").GetByRole(AriaRole.Textbox);
        var fluent = Page.Locator("#compatibility-fluent").GetByRole(AriaRole.Textbox);
        await Expect(standard).ToHaveValueAsync("Preserved parameter", new() { Timeout = 30_000 });
        await Expect(fluent).ToHaveValueAsync("Preserved parameter");
        foreach (var section in new[] { "#compatibility-standard", "#compatibility-fluent" })
            await Expect(Page.Locator($"{section} [data-injected-navigation]")).ToHaveTextAsync($"{app.BaseUrl}/compatibility");

        await standard.FillAsync("Standard callback");
        await standard.PressAsync("Tab");
        await Expect(fluent).ToHaveValueAsync("Standard callback");
        await fluent.FillAsync("Fluent callback");
        await fluent.PressAsync("Tab");
        await Expect(Page.Locator("#compatibility-value")).ToHaveTextAsync("Fluent callback");
        await Expect(standard).ToHaveValueAsync("Fluent callback");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Toggle read-only" }).ClickAsync();
        await Expect(standard).ToHaveAttributeAsync("readonly", "");
        await Expect(fluent).ToHaveAttributeAsync("readonly", "");
    }
}
