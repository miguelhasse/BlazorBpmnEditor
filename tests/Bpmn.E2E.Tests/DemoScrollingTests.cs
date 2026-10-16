using Hasseware.AspNetCore.Components.Bpmn.E2E.Tests.Fixtures;
using Microsoft.Playwright.Xunit.v3;

namespace Hasseware.AspNetCore.Components.Bpmn.E2E.Tests;

public sealed class DemoScrollingTests(DemoAppFixture app) : PageTest, IClassFixture<DemoAppFixture>
{
    [Theory]
    [InlineData("/icons", ".icons-grid")]
    [InlineData("/interchange", ".demo-table")]
    public async Task OverflowingReportContentScrollsWithinChrome(string route, string readySelector)
    {
        await Page.SetViewportSizeAsync(800, 400);
        await Page.GotoAsync($"{app.BaseUrl}{route}");
        await Expect(Page.Locator(readySelector).First).ToBeVisibleAsync(new() { Timeout = 30_000 });
        var scrollPage = Page.Locator(".demo-scroll-page");
        if (route == "/interchange")
        {
            await Page.Locator(".demo-mode details:has(.demo-xml) > summary").First.ClickAsync();
            await Expect(Page.Locator(".demo-xml").First).ToBeVisibleAsync();
        }

        Assert.True(await scrollPage.EvaluateAsync<bool>("element => element.scrollHeight > element.clientHeight"));
        Assert.True(await scrollPage.EvaluateAsync<bool>(
            """
            element => {
                const page = element.getBoundingClientRect();
                const body = document.querySelector('.demo-chrome__body').getBoundingClientRect();
                return page.top >= body.top && page.bottom <= body.bottom;
            }
            """));

        await scrollPage.HoverAsync(new() { Position = new() { X = 10, Y = 10 } });
        await Page.Mouse.WheelAsync(0, 600);
        await Page.WaitForFunctionAsync("document.querySelector('.demo-scroll-page').scrollTop > 0");
        await scrollPage.EvaluateAsync("element => element.scrollTop = element.scrollHeight");
        Assert.True(await scrollPage.EvaluateAsync<bool>(
            "element => Math.abs(element.scrollHeight - element.clientHeight - element.scrollTop) <= 1"));
        await Expect(Page.Locator(".demo-chrome__footer")).ToBeInViewportAsync();
    }
}
