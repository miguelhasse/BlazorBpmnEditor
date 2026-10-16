using Hasseware.AspNetCore.Components.Bpmn.E2E.Tests.Fixtures;

using Microsoft.Playwright;
using Microsoft.Playwright.Xunit.v3;

namespace Hasseware.AspNetCore.Components.Bpmn.E2E.Tests;

/// <summary>
/// Reports what the editor page actually rendered.
/// </summary>
/// <remarks>
/// A failing canvas assertion says only that a selector did not match, which is the same symptom
/// whether WebAssembly never booted, the component threw, or the paper was created somewhere
/// unexpected. These assert on the page's own structure and echo the browser's log, so a failure
/// names the stage that broke instead of just the selector that missed.
/// </remarks>
public sealed class RenderDiagnosticsTests(DemoAppFixture app) : PageTest, IClassFixture<DemoAppFixture>
{
    [Fact]
    public async Task BootsWebAssemblyAndRendersTheEditorShell()
    {
        var log = new List<string>();
        Page.Console += (_, message) => log.Add($"[{message.Type}] {message.Text}");
        Page.PageError += (_, error) => log.Add($"[pageerror] {error}");
        Page.RequestFailed += (_, request) => log.Add($"[requestfailed] {request.Url} {request.Failure}");

        await Page.GotoAsync($"{app.BaseUrl}/editor");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var appeared = await Page.Locator(".demo-shell").IsVisibleAsync();
        if (!appeared)
        {
            // Wait once more: a cold WebAssembly boot downloads the whole runtime and every
            // referenced assembly, which can exceed the default expectation timeout.
            await Page.WaitForTimeoutAsync(20_000);
            appeared = await Page.Locator(".demo-shell").IsVisibleAsync();
        }

        if (!appeared)
        {
            var body = await Page.Locator("body").InnerTextAsync();
            Assert.Fail(
                $"The editor page never rendered.{Environment.NewLine}"
                + $"Body text: {body}{Environment.NewLine}"
                + $"Browser log:{Environment.NewLine}{string.Join(Environment.NewLine, log)}");
        }

        // Each stage is asserted separately so the first failure identifies where it stopped.
        await Expect(Page.Locator(".bpmn-editor")).ToBeVisibleAsync();
        await Expect(Page.Locator(".bpmn-canvas")).ToBeAttachedAsync();
        await Expect(Page.Locator(".bpmn-canvas__paper > svg")).ToBeAttachedAsync();
    }
}
