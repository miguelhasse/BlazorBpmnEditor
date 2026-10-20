using Hasseware.AspNetCore.Components.Bpmn.E2E.Tests.Fixtures;
using Microsoft.Playwright.Xunit.v3;

namespace Hasseware.AspNetCore.Components.Bpmn.E2E.Tests;

/// <summary>Both kits ship Core's stylesheet; each also ships its own, served and applied on its page.</summary>
public sealed class StandardPackageTests(DemoAppFixture app) : PageTest, IClassFixture<DemoAppFixture>
{
    [Theory]
    [InlineData("/editor", false)]
    [InlineData("/editor?plain=false", false)]
    [InlineData("/editor?plain=true", true)]
    public async Task QuerySelectsEditorKit(string route, bool plain)
    {
        await Page.GotoAsync($"{app.BaseUrl}{route}");
        await Expect(Page.Locator(".bpmn-canvas .joint-cell").First).ToBeVisibleAsync(new() { Timeout = 30_000 });

        await Expect(Page.Locator(".fluent-bpmn-editor")).ToHaveCountAsync(plain ? 0 : 1);
        await Expect(Page.Locator(".bpmn-editor:not(.fluent-bpmn-editor)")).ToHaveCountAsync(plain ? 1 : 0);
    }

    [Fact]
    public async Task CoreAndStandardStylesheetsAreServedAndApplied()
    {
        await Page.GotoAsync($"{app.BaseUrl}{KitRoute.Plain}");
        await Expect(Page.Locator(".bpmn-canvas .joint-cell").First).ToBeVisibleAsync(new() { Timeout = 30_000 });
        foreach (var path in new[]
        {
            "_content/Hasseware.AspNetCore.Components.Bpmn/Hasseware.AspNetCore.Components.Bpmn.bundle.scp.css",
            "_content/Hasseware.AspNetCore.Components.BpmnEditor/css/BpmnEditor.css",
            "_content/Hasseware.AspNetCore.Components.BpmnEditor/css/BpmnOverlays.css",
        })
        {
            var response = await Page.APIRequest.GetAsync($"{app.BaseUrl}/{path}");
            Assert.Equal(200, response.Status);
            Assert.Contains("text/css", response.Headers["content-type"], StringComparison.Ordinal);
        }
        var styles = await Page.EvaluateAsync<string[]>("""
            () => [
                getComputedStyle(document.querySelector('.bpmn-editor')).display,
                getComputedStyle(document.querySelector('.bpmn-toolbar')).display,
                getComputedStyle(document.querySelector('.bpmn-canvas')).getPropertyValue('--bpmn-stroke').trim(),
                document.querySelector('.bpmn-toolbar button').tagName
            ]
            """);
        Assert.Equal("grid", styles[0]);
        Assert.Equal("flex", styles[1]);
        Assert.NotEmpty(styles[2]);
        Assert.Equal("BUTTON", styles[3]);
    }

    [Fact]
    public async Task FluentStylesheetIsServedAndScopedToTheFluentEditor()
    {
        await Page.GotoAsync($"{app.BaseUrl}{KitRoute.Fluent}");
        await Expect(Page.Locator(".bpmn-canvas .joint-cell").First).ToBeVisibleAsync(new() { Timeout = 30_000 });

        var response = await Page.APIRequest.GetAsync(
            $"{app.BaseUrl}/_content/Hasseware.FluentUI.AspNetCore.Components.BpmnEditor/css/FluentBpmnEditor.css");
        Assert.Equal(200, response.Status);
        Assert.Contains("text/css", response.Headers["content-type"], StringComparison.Ordinal);

        var root = Page.Locator(".fluent-bpmn-editor");
        await Expect(root).ToHaveCountAsync(1);
        await Expect(root).ToHaveClassAsync(new System.Text.RegularExpressions.Regex(@"\bbpmn-editor\b"));
        await Expect(Page.Locator(".bpmn-editor:not(.fluent-bpmn-editor)")).ToHaveCountAsync(0);
    }
}
