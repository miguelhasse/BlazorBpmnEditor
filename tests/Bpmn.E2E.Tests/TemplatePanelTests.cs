using Hasseware.AspNetCore.Components.Bpmn.E2E.Tests.Fixtures;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit.v3;

namespace Hasseware.AspNetCore.Components.Bpmn.E2E.Tests;

/// <summary>Local demo templates, conditional fields and history on the real WASM property panel.</summary>
/// <remarks>Driven against the standard kit; the Fluent kit's template chooser is markup of its own.</remarks>
public sealed class TemplatePanelTests(DemoAppFixture app) : PageTest, IClassFixture<DemoAppFixture>
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreatesTemplateFromStencilEditsUndoAndExports(bool drag)
    {
        await Page.GotoAsync($"{app.BaseUrl}{KitRoute.Plain}");
        await Expect(Page.Locator(".bpmn-canvas__paper > svg")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Page.Locator(".demo-show-properties").CheckAsync();
        var before = await Page.Locator(".bpmn-canvas .joint-element").CountAsync();
        var tool = Page.Locator(".bpmn-stencil__template-tool", new() { HasTextString = "HTTP request" });
        if (drag)
        {
            var paper = await Page.Locator(".bpmn-canvas__paper").BoundingBoxAsync();
            Assert.NotNull(paper);
            await tool.DragToAsync(Page.Locator(".bpmn-canvas__paper"), new()
            {
                TargetPosition = new() { X = paper.Width * .75f, Y = paper.Height * .75f }
            });
        }
        else await tool.ClickAsync();
        await Expect(Page.Locator(".bpmn-canvas .joint-element")).ToHaveCountAsync(before + 1);
        await Page.Locator(".bpmn-canvas .joint-element", new() { HasTextString = "HTTP request" })
            .Locator("[joint-selector='body']").First.ClickAsync();
        await Expect(Page.Locator(".bpmn-templates__row .bpmn-templates__name")).ToHaveTextAsync("HTTP request");
        await Expect(Page.Locator("details[data-group-id='template:request']")).ToBeVisibleAsync();
        var url = Page.Locator("details[data-group-id='template:request'] input");
        await Expect(url).ToHaveValueAsync("https://example.invalid");
        await url.FillAsync("https://changed.invalid");
        await Page.Keyboard.PressAsync("Tab");
        await Expect(url).ToHaveValueAsync("https://changed.invalid");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Undo", Exact = true }).ClickAsync();
        await Expect(url).ToHaveValueAsync("https://example.invalid");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Export XML", Exact = true }).ClickAsync();
        var xml = await Page.Locator(".demo-export-xml").InnerTextAsync();
        Assert.Contains("zeebe:modelerTemplate=\"demo.http\"", xml, StringComparison.Ordinal);
        Assert.Contains("zeebe:taskDefinition", xml, StringComparison.Ordinal);
        Assert.Contains("type=\"demo-http\"", xml, StringComparison.Ordinal);
        Assert.Contains("zeebe:input", xml, StringComparison.Ordinal);
        Assert.Contains("target=\"url\"", xml, StringComparison.Ordinal);
        Assert.Contains("https://example.invalid", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("https://changed.invalid", xml, StringComparison.Ordinal);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Undo", Exact = true }).ClickAsync();
        await Expect(Page.Locator(".bpmn-canvas .joint-element")).ToHaveCountAsync(before);
    }

    [Fact]
    public async Task AppliesEditsUndoUnlinksAndRemovesSampleTemplate()
    {
        await Page.GotoAsync($"{app.BaseUrl}{KitRoute.Plain}");
        await Expect(Page.Locator(".bpmn-canvas__paper > svg")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Page.Locator(".demo-show-properties").CheckAsync();
        var taskId = await Page.EvaluateAsync<string>("""
            () => [...document.querySelectorAll('.bpmn-canvas .joint-element[model-id]')]
              .find(cell => (cell.getAttribute('model-id') ?? '').startsWith('Task_'))?.getAttribute('model-id') ?? ''
            """);
        Assert.NotEmpty(taskId);
        await Page.Locator($".joint-cell[model-id='{taskId}'] [joint-selector='body']").First.ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Select template", Exact = true }).ClickAsync();
        await Expect(Page.Locator(".bpmn-templates__option")).ToHaveCountAsync(2);
        await Page.Locator(".bpmn-templates__list").GetByRole(AriaRole.Option, new() { Name = "HTTP request", Exact = false }).ClickAsync();
        await Expect(Page.Locator(".bpmn-templates__row .bpmn-templates__name")).ToHaveTextAsync("HTTP request");
        await Expect(Page.Locator(".bpmn-properties__subtitle")).ToHaveTextAsync("Service Task");
        await Page.Locator("details[data-group-id='template:auth'] > summary").ClickAsync();
        await Page.Locator("details[data-group-id='template:auth'] select").SelectOptionAsync("key");
        var apiKey = Page.Locator(".bpmn-properties__property", new() { HasTextString = "API key" }).Locator("input");
        await Expect(apiKey).ToBeVisibleAsync();
        await apiKey.FillAsync("demo-key");
        await Page.Keyboard.PressAsync("Tab");
        await Expect(apiKey).ToHaveValueAsync("demo-key");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Undo", Exact = true }).ClickAsync();
        await Expect(apiKey).ToHaveValueAsync("");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Undo", Exact = true }).ClickAsync();
        await Expect(apiKey).ToHaveCountAsync(0);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Undo", Exact = true }).ClickAsync();
        await Expect(Page.Locator(".bpmn-templates__select")).ToBeVisibleAsync();
        await Page.Locator(".bpmn-templates__select").ClickAsync();
        await Page.Locator(".bpmn-templates__list").GetByRole(AriaRole.Option, new() { Name = "HTTP request", Exact = false }).ClickAsync();
        await Page.Locator(".bpmn-templates__unlink").ClickAsync();
        await Expect(Page.Locator(".bpmn-templates__select")).ToBeVisibleAsync();
        var workerType = Page.Locator("details[data-group-id='task-definition'] input").First;
        await Expect(workerType).ToHaveValueAsync("demo-http");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Undo", Exact = true }).ClickAsync();
        await Page.Locator(".bpmn-templates__remove").ClickAsync();
        await Expect(Page.Locator(".bpmn-templates__select")).ToBeVisibleAsync();
        await Expect(workerType).ToHaveCountAsync(0);
    }
}
