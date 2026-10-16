using System.Collections.Concurrent;

using Hasseware.AspNetCore.Components.Bpmn.E2E.Tests.Fixtures;

using Microsoft.Playwright;
using Microsoft.Playwright.Xunit.v3;

namespace Hasseware.AspNetCore.Components.Bpmn.E2E.Tests;

public sealed class GalleryTests(DemoAppFixture app) : PageTest, IClassFixture<DemoAppFixture>
{
    [Fact]
    public async Task EventsOmitUnsupportedCellsAndShowRealHostsAndContainers()
    {
        var errors = CaptureErrors();
        await GoToGalleryAsync();

        var ids = await Page.Locator(".bpmn-a11y-tree__item[id]").EvaluateAllAsync<string[]>(
            "items => items.map(item => item.id.replace('bpmn-a11y-', ''))");
        int[][] allowedColumns =
        [
            [0, 1, 2, 3, 4, 5, 6],
            [1, 2, 3, 4, 5, 6, 9, 11],
            [1, 2, 3, 4, 5, 6, 7],
            [1, 2, 3, 4, 5, 6, 8, 9, 11],
            [1, 2, 3, 4, 5, 6, 9],
            [0, 1, 4, 5, 7, 9, 11],
            [0, 1, 4, 5, 8, 9, 11, 12],
        ];
        var expectedEvents = allowedColumns.SelectMany((columns, row) =>
            columns.Select(column => $"Event_{row}_{column}")).Order(StringComparer.Ordinal);
        Assert.Equal(expectedEvents, ids.Where(id => id.StartsWith("Event_", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal));
        Assert.Equal(84, ids.Length);
        foreach (var id in new[]
        {
            "Event_0_8", "Event_0_10", "Event_0_12", "Event_1_0", "Event_1_8",
            "Event_2_0", "Event_2_8", "Event_2_10", "Event_2_12", "Event_3_0",
            "Event_3_7", "Event_3_10", "Event_4_8", "Event_4_10", "Event_4_11",
            "Event_5_2", "Event_5_3", "Event_5_6", "Event_5_8", "Event_5_10",
            "Event_5_12", "Event_6_2", "Event_6_3", "Event_6_6", "Event_6_7", "Event_6_10",
        })
        {
            Assert.DoesNotContain(id, ids);
        }

        foreach (var id in new[] { "Event_0_0", "Event_1_1", "Event_2_7", "Event_3_8", "Event_5_7", "Event_6_12" })
        {
            Assert.Contains(id, ids);
        }

        await Expect(Page.Locator("#bpmn-a11y-Event_1_1")).ToHaveAttributeAsync("aria-level", "2");
        await Expect(Page.Locator("#bpmn-a11y-EventSub_1")).ToHaveAttributeAsync("aria-expanded", "true");
        await Expect(Page.Locator(".joint-element[model-id='Event_1_1'] [joint-selector='outerRing']"))
            .ToHaveAttributeAsync("stroke-dasharray", "4 3");
        await Expect(Page.Locator(".joint-element[model-id='Host_3_8']")).ToHaveCountAsync(1);
        await Expect(Page.Locator(".joint-element[model-id='Event_3_8'] [joint-selector='innerRing']")).ToHaveCountAsync(1);
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.Empty(errors);
    }

    [Fact]
    public async Task SwimlanesSwitchRepeatedlyWithoutDuplicateKeysOrLostElements()
    {
        var errors = CaptureErrors();
        await GoToGalleryAsync();

        for (var iteration = 0; iteration < 3; iteration++)
        {
            await SelectSheetAsync("Pools and Lanes: both orientations");
            await Expect(Page.Locator("#bpmn-a11y-S_TaskE")).ToHaveCountAsync(1);
            await Expect(Page.Locator(".bpmn-a11y-tree__item[id]")).ToHaveCountAsync(13);
            await Expect(Page.Locator(".bpmn-canvas .joint-element[model-id]")).ToHaveCountAsync(13);
            var ids = await Page.Locator(".bpmn-a11y-tree__item[id]").EvaluateAllAsync<string[]>(
                "items => items.map(item => item.id)");
            Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
            foreach (var lane in new[] { "S_LaneA", "S_LaneB", "S_LaneC", "S_LaneD" })
            {
                await Expect(Page.Locator($"#bpmn-a11y-{lane}")).ToHaveCountAsync(1);
            }

            var horizontal = await HeaderBoundsAsync("S_Horizontal");
            var vertical = await HeaderBoundsAsync("S_Vertical");
            Assert.True(horizontal[1] > horizontal[0], "Horizontal Pool header must run down the left edge.");
            Assert.True(vertical[0] > vertical[1], "Vertical Pool header must run across the top edge.");

            await SelectSheetAsync("Events: all triggers by context");
            await Expect(Page.Locator("#bpmn-a11y-Event_0_0")).ToHaveCountAsync(1);
            await Expect(Page.Locator("#bpmn-a11y-S_LaneA")).ToHaveCountAsync(0);
        }

        Assert.Empty(errors);
    }

    private ConcurrentQueue<string> CaptureErrors()
    {
        var errors = new ConcurrentQueue<string>();
        Page.PageError += (_, error) => errors.Enqueue(error);
        Page.Console += (_, message) =>
        {
            if (message.Type == "error" || message.Text.Contains("Unhandled exception", StringComparison.Ordinal))
            {
                errors.Enqueue(message.Text);
            }
        };
        return errors;
    }

    private Task<double[]> HeaderBoundsAsync(string id) => Page.EvaluateAsync<double[]>(
        """
        id => {
          const header = document.querySelector(`.joint-element[model-id="${id}"] .bpmn-swimlane-header`);
          const bounds = header.getBBox();
          return [bounds.width, bounds.height];
        }
        """, id);

    private async Task SelectSheetAsync(string title)
    {
        await Page.GetByRole(AriaRole.Combobox, new() { Name = "Notation sheet" }).ClickAsync();
        await Page.Locator("fluent-option").Filter(new() { HasTextString = title }).ClickAsync();
        var sentinel = title == "Pools and Lanes: both orientations" ? "S_TaskE" : "Event_0_0";
        await Expect(Page.Locator($".joint-element[model-id='{sentinel}']")).ToHaveCountAsync(1);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Fit to content", Exact = true }).ClickAsync();
    }

    private async Task GoToGalleryAsync()
    {
        await Page.GotoAsync($"{app.BaseUrl}/gallery");
        await Expect(Page.Locator(".bpmn-canvas__paper > svg")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.Locator("#bpmn-a11y-Event_0_0")).ToHaveCountAsync(1);
        await Expect(Page.Locator(".joint-element[model-id='Event_0_0']")).ToHaveCountAsync(1);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Fit to content", Exact = true }).ClickAsync();
    }
}
