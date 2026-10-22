using Hasseware.AspNetCore.Components.Bpmn.E2E.Tests.Fixtures;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;

using Microsoft.Playwright;
using Microsoft.Playwright.Xunit.v3;

namespace Hasseware.AspNetCore.Components.Bpmn.E2E.Tests;

public sealed class ColorTests(DemoAppFixture app) : PageTest, IClassFixture<DemoAppFixture>
{
    [Theory]
    [InlineData(KitRoute.Plain)]
    [InlineData(KitRoute.Fluent)]
    public async Task ToolbarColorsRenderUndoResetAndSurviveExport(string route)
    {
        var page = Page;
        try
        {
            Assert.Equal(2, await page.EvaluateAsync<int>("() => 1 + 1"));
        }
        catch (PlaywrightException)
        {
            await page.CloseAsync();
            page = await Context.NewPageAsync();
            Assert.Equal(2, await page.EvaluateAsync<int>("() => 1 + 1"));
        }

        await page.GotoAsync($"{app.BaseUrl}{route}");
        var task = page.Locator(".joint-cell[data-type='bpmn.Activity']").First;
        await Expect(task).ToBeVisibleAsync(new() { Timeout = 30_000 });
        var id = await task.GetAttributeAsync("model-id")
            ?? throw new InvalidOperationException("The task has no model id.");
        await task.Locator(".bpmn-activity-body").ClickAsync();
        if (route == KitRoute.Fluent)
        {
            var colors = page.GetByRole(AriaRole.Button, new() { Name = "Colors", Exact = true });
            await Expect(colors).ToHaveAttributeAsync("aria-expanded", "false");
            await OpenColorsAsync(page);
            var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Colors", Exact = true });
            await Expect(dialog).ToBeVisibleAsync();
            await page.GetByRole(AriaRole.Textbox, new() { Name = "Fill color", Exact = true }).PressAsync("Escape");
            await Expect(dialog).Not.ToBeVisibleAsync();
            await Expect(colors).ToBeFocusedAsync();
            await OpenColorsAsync(page);
        }
        var colorInputs = page.Locator(".bpmn-toolbar__color input");
        await Expect(colorInputs).ToHaveCountAsync(3);
        foreach (var input in await colorInputs.AllAsync())
            await Expect(input).ToHaveAttributeAsync("autocomplete", "off");
        var body = task.Locator(".bpmn-activity-body");
        var before = await body.EvaluateAsync<string>("element => getComputedStyle(element).fill");

        if (route == KitRoute.Fluent)
        {
            await page.Locator("[data-color-channel='Background'] [slot='swatch']").ClickAsync();
            var swatch = page.Locator(".fluent-color-picker:visible polygon").Nth(20);
            await Expect(swatch).ToBeVisibleAsync();
            var fill = await swatch.EvaluateAsync<string>("element => getComputedStyle(element).fill");
            await swatch.ClickAsync();
            await Expect(body).ToHaveCSSAsync("fill", fill);
            await Expect(page.GetByRole(AriaRole.Dialog, new() { Name = "Colors", Exact = true })).ToBeVisibleAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Undo", Exact = true }).ClickAsync();
            await Expect(page.GetByRole(AriaRole.Dialog, new() { Name = "Colors", Exact = true })).Not.ToBeVisibleAsync();
            await Expect(body).ToHaveCSSAsync("fill", before);
            await page.GetByRole(AriaRole.Button, new() { Name = "Undo", Exact = true }).ExpectDisabledAsync();
        }

        await SetColorAsync(page, "Background", "Fill color", "#abcdef");
        await Expect(body).ToHaveCSSAsync("fill", "rgb(171, 205, 239)");
        await SetColorAsync(page, "Border", "Border / line color", "#123456");
        await Expect(body).ToHaveCSSAsync("stroke", "rgb(18, 52, 86)");
        await SetColorAsync(page, "Text", "Text color", "#654321");
        await Expect(task.Locator(".bpmn-label")).ToHaveCSSAsync("fill", "rgb(101, 67, 33)");

        await page.GetByRole(AriaRole.Button, new() { Name = "Undo", Exact = true }).ClickAsync();
        await Expect(task.Locator(".bpmn-label")).Not.ToHaveCSSAsync("fill", "rgb(101, 67, 33)");
        await page.GetByRole(AriaRole.Button, new() { Name = "Redo", Exact = true }).ClickAsync();
        await Expect(task.Locator(".bpmn-label")).ToHaveCSSAsync("fill", "rgb(101, 67, 33)");

        await page.OpenDemoFilesAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Export XML", Exact = true }).ClickAsync();
        var xml = await page.Locator(".demo-export-xml").TextContentAsync();
        var document = BpmnXmlReader.Read(xml!).Document;
        var stored = document.Diagrams.SelectMany(diagram => diagram.Plane.Shapes)
            .Single(shape => shape.BpmnElement == id);
        Assert.Equal("#abcdef", stored.BackgroundColor);
        Assert.Equal("#123456", stored.BorderColor);
        Assert.Equal("#654321", stored.Label!.Color);

        await OpenColorsAsync(page);
        await page.GetByRole(AriaRole.Button, new() { Name = "Reset Fill color", Exact = true }).ClickAsync();
        await Expect(body).ToHaveCSSAsync("fill", before);
        await Expect(body).ToHaveCSSAsync("stroke", "rgb(18, 52, 86)");

        // Reopen the exported file through the real import path.
        var transfer = await page.EvaluateHandleAsync(
            """
            text => {
              const transfer = new DataTransfer();
              transfer.items.add(new File([text], 'colors.bpmn', { type: 'application/xml' }));
              return transfer;
            }
            """, xml);
        await page.Locator(".bpmn-canvas").First.DispatchEventAsync("drop",
            new Dictionary<string, object> { ["dataTransfer"] = transfer });
        await page.ImportAccept().ClickAsync();
        var reopened = page.Locator($".joint-cell[model-id='{id}']");
        await Expect(reopened.Locator(".bpmn-activity-body")).ToHaveCSSAsync("fill", "rgb(171, 205, 239)");
        await Expect(reopened.Locator(".bpmn-label")).ToHaveCSSAsync("fill", "rgb(101, 67, 33)");

        var line = page.Locator(".bpmn-connector-line").First;
        await line.ClickAsync(new() { Force = true });
        await OpenColorsAsync(page);
        await page.GetByRole(AriaRole.Button, new() { Name = "Reset Fill color", Exact = true }).ExpectDisabledAsync();
        await SetColorAsync(page, "Border", "Border / line color", "#224466");
        await Expect(line).ToHaveCSSAsync("stroke", "rgb(34, 68, 102)");
        var marker = await line.GetAttributeAsync("marker-end")
            ?? throw new InvalidOperationException("The sequence flow has no arrowhead.");
        var markerId = marker[(marker.LastIndexOf('#') + 1)..].TrimEnd(')', '"', '\'');
        await Expect(page.Locator($"marker[id='{markerId}'] path")).ToHaveCSSAsync("fill", "rgb(34, 68, 102)");

        await page.EmulateMediaAsync(new() { ForcedColors = ForcedColors.Active });
        await Expect(reopened.Locator(".bpmn-activity-body")).Not.ToHaveCSSAsync("fill", "rgb(171, 205, 239)");

        await page.SetViewportSizeAsync(900, 900);
        var toolbar = page.Locator(".bpmn-toolbar");
        Assert.True(await toolbar.EvaluateAsync<bool>("element => element.scrollWidth <= element.clientWidth + 1"),
            "Color controls must wrap instead of overflowing the toolbar.");
        if (route == KitRoute.Fluent)
        {
            await OpenColorsAsync(page);
            var popover = await page.GetByRole(AriaRole.Dialog, new() { Name = "Colors", Exact = true })
                .Locator("[part='dialog']").First.BoundingBoxAsync();
            Assert.NotNull(popover);
            Assert.InRange(popover.X, 0, 900 - popover.Width);
        }
    }

    private static async Task OpenColorsAsync(IPage page)
    {
        var trigger = page.GetByRole(AriaRole.Button, new() { Name = "Colors", Exact = true });
        if (await trigger.CountAsync() > 0 && await trigger.GetAttributeAsync("aria-expanded") == "false")
            await trigger.ClickAsync();
    }

    private static async Task SetColorAsync(IPage page, string channel, string label, string value)
    {
        await OpenColorsAsync(page);
        var native = page.Locator($"input[type='color'][data-color-channel='{channel}']");
        if (await native.CountAsync() > 0)
        {
            await native.EvaluateAsync(
                "(input, color) => { input.value = color; input.dispatchEvent(new Event('change', { bubbles: true })); }",
                value);
            return;
        }

        var input = page.GetByRole(AriaRole.Textbox, new() { Name = label, Exact = true });
        await input.FillAsync(value);
        await input.PressAsync("Tab");
    }
}
