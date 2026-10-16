using Hasseware.AspNetCore.Components.Bpmn.E2E.Tests.Fixtures;

using Microsoft.Playwright.Xunit.v3;

namespace Hasseware.AspNetCore.Components.Bpmn.E2E.Tests;

/// <summary>
/// The Server-Sent Events change feed in a real browser, with the client running as WebAssembly: the streaming fetch, the parser and
/// the reconnection the design leaves to the transport.
/// </summary>
public class LiveUpdatesTests(DemoAppFixture app) : PageTest, IClassFixture<DemoAppFixture>
{
    [Fact]
    public async Task ReportsAChangeMadeOnTheServerAndReconnectsWhenTheServerClosesTheStream()
    {
        await Page.GotoAsync($"{app.BaseUrl}/live-updates");
        await Expect(Page.Locator(".live-watch")).ToBeEnabledAsync(new() { Timeout = 60_000 });

        await Page.Locator(".live-watch").ClickAsync();
        await Expect(Page.Locator(".live-state")).ToHaveTextAsync("Live");

        // The playground ends each stream after a few seconds, so a change made in the moment between one stream and the next reaches nobody:
        // make it again if nothing arrived, which is what a person watching would do.
        for (var attempt = 0; attempt < 4 && await Page.Locator(".live-events li").CountAsync() == 0; attempt++)
        {
            await Page.Locator(".live-change").ClickAsync();
            await Page.Locator(".live-events li").First.WaitForAsync(new() { Timeout = 4_000 }).ContinueWith(_ => { }, TaskScheduler.Default);
        }

        await Expect(Page.Locator(".live-events li").First).ToContainTextAsync("updated");

        // The playground's server ends each stream after a few seconds; the client reopens it and says so.
        await Expect(Page.Locator(".live-gaps")).Not.ToHaveTextAsync("0", new() { Timeout = 30_000 });
        await Expect(Page.Locator(".live-state")).ToHaveTextAsync("Live", new() { Timeout = 30_000 });

        var before = await Page.Locator(".live-events li").CountAsync();
        await Page.Locator(".live-change").ClickAsync();
        await Expect(Page.Locator(".live-events li")).ToHaveCountAsync(before + 1);
    }
}
