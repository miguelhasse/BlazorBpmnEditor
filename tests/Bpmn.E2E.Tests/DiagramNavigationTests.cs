using System.Text.RegularExpressions;

using Hasseware.AspNetCore.Components.Bpmn.E2E.Tests.Fixtures;

using Microsoft.Playwright;
using Microsoft.Playwright.Xunit.v3;

namespace Hasseware.AspNetCore.Components.Bpmn.E2E.Tests;

/// <summary>
/// Moving between the diagrams one file declares (implementation plan M6).
/// </summary>
/// <remarks>
/// What only a browser shows: that the canvas actually repaints the other plane, that the path the
/// editor reports is one a host can render and click, and — the exit criterion — that an unsaved
/// edit and its undo survive the trip.
/// </remarks>
public class DiagramNavigationTests(DemoAppFixture app) : PageTest, IClassFixture<DemoAppFixture>
{
    [Fact]
    public async Task PickingADiagramSynthesizesTheRootToSelectedBreadcrumb()
    {
        await GoToLinkedProcessesAsync();

        await Expect(Page.Locator(".demo-breadcrumb")).ToHaveCountAsync(0);

        await Page.PickAsync(Page.DiagramSelect(), "Handle Shipment Diagram");

        await Expect(Page.SelectedDiagram()).ToHaveAttributeAsync("value", "Diagram_HandleShipment");
        await Expect(Page.Locator(".demo-breadcrumb__step")).ToHaveTextAsync(
            ["Main Process Diagram", "Handle Shipment Diagram"]);
        await Expect(Page.Locator(".demo-breadcrumb__step").First).ToBeEnabledAsync();
        await Expect(Page.Locator(".demo-breadcrumb__step").Last).ToBeDisabledAsync();
        await Expect(Page.Locator(".joint-cell[model-id='CallActivity_HandleShipment']")).ToHaveCountAsync(0);

        await Page.PickAsync(Page.DiagramSelect(), "Main Process Diagram");

        await Expect(Page.SelectedDiagram()).ToHaveAttributeAsync("value", "Diagram_Main");
        await Expect(Page.Locator(".demo-breadcrumb")).ToHaveCountAsync(0);
        await Expect(Page.Locator(".joint-cell[model-id='CallActivity_HandleShipment']")).ToHaveCountAsync(1);

        await Page.PickAsync(Page.DiagramSelect(), "Handle Shipment Diagram");
        await Expect(Page.Locator(".demo-breadcrumb__step")).ToHaveCountAsync(2);
        await Page.Locator(".demo-breadcrumb__step").First.ClickAsync();

        await Expect(Page.SelectedDiagram()).ToHaveAttributeAsync("value", "Diagram_Main");
        await Expect(Page.Locator(".demo-breadcrumb")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task PickingADiagramReplacesTheDrillDownBreadcrumb()
    {
        await GoToLinkedProcessesAsync();
        await SelectAsync("CallActivity_HandleShipment");
        await Page.Locator(".bpmn-diagrams__open").ClickAsync();
        await Expect(Page.Locator(".demo-breadcrumb__step")).ToHaveTextAsync(
            ["Main Process Diagram", "Handle Shipment Diagram"]);

        await Page.PickAsync(Page.DiagramSelect(), "Main Process Diagram");

        await Expect(Page.SelectedDiagram()).ToHaveAttributeAsync("value", "Diagram_Main");
        await Expect(Page.Locator(".demo-breadcrumb")).ToHaveCountAsync(0);

        await Page.PickAsync(Page.DiagramSelect(), "Handle Shipment Diagram");

        await Expect(Page.Locator(".demo-breadcrumb__step")).ToHaveTextAsync(
            ["Main Process Diagram", "Handle Shipment Diagram"]);
        await Expect(Page.Locator(".demo-breadcrumb__step").Last).ToBeDisabledAsync();
    }

    [Fact]
    public async Task ACallActivityOpensTheProcessItCalls()
    {
        await GoToLinkedProcessesAsync();

        var before = await Page.Locator(".bpmn-canvas .joint-cell").CountAsync();

        await SelectAsync("CallActivity_HandleShipment");
        await Page.OpenButton().ExpectDisabledAsync(false);

        await Page.Locator(".bpmn-diagrams__open").ClickAsync();
        await Page.WaitForTimeoutAsync(1500);

        // A different plane, with different content on it.
        await Expect(Page.SelectedDiagram()).ToHaveAttributeAsync("value", "Diagram_HandleShipment");
        Assert.NotEqual(before, await Page.Locator(".bpmn-canvas .joint-cell").CountAsync());
    }

    [Fact]
    public async Task TheHostGetsAPathItCanRender()
    {
        // `diagramNavigated` carries the whole route so the application draws its own breadcrumb.
        // The one in this demo is the host's, not the editor's, which is the point of the event.
        await GoToLinkedProcessesAsync();
        await SelectAsync("CallActivity_HandleShipment");
        await Page.Locator(".bpmn-diagrams__open").ClickAsync();
        await Page.WaitForTimeoutAsync(1500);

        await Expect(Page.Locator(".demo-breadcrumb__step")).ToHaveTextAsync(
            ["Main Process Diagram", "Handle Shipment Diagram"]);

        // The last step is where you are, so there is nowhere to go from it.
        await Expect(Page.Locator(".demo-breadcrumb__step").Last).ToBeDisabledAsync();
    }

    [Fact]
    public async Task TheBreadcrumbGoesBackUp()
    {
        await GoToLinkedProcessesAsync();
        await SelectAsync("CallActivity_HandleShipment");
        await Page.Locator(".bpmn-diagrams__open").ClickAsync();
        await Page.WaitForTimeoutAsync(1500);

        await Page.Locator(".demo-breadcrumb__step").First.ClickAsync();
        await Page.WaitForTimeoutAsync(1500);

        await Expect(Page.SelectedDiagram()).ToHaveAttributeAsync("value", "Diagram_Main");
        await Expect(Page.Locator(".joint-cell[model-id='CallActivity_HandleShipment']")).ToHaveCountAsync(1);
    }

    [Fact]
    public async Task TheOpenButtonIsDisabledWithAReasonForAnElementThatOpensNothing()
    {
        await GoToLinkedProcessesAsync();
        await SelectAsync("Event_MainStart");

        var button = Page.OpenButton();

        await button.ExpectDisabledAsync();
        await Expect(Page.Locator(".bpmn-diagrams__open")).ToHaveAttributeAsync("title", new Regex("Call Activity"));
    }

    [Fact]
    public async Task AnUnsavedEditSurvivesATripToAnotherDiagram()
    {
        /*
         * The exit criterion: switching diagrams "preserves undo/redo history, unsaved edits, and
         * execution overlays on inactive planes". The document and its history belong to the
         * editor, not to the plane being looked at, so a move between them can neither lose an edit
         * nor commit one.
         */
        await GoToLinkedProcessesAsync();

        await MoveAsync("CallActivity_HandleShipment", 40, 60);
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Undo" })).ToBeEnabledAsync();

        await Page.PickAsync(Page.DiagramSelect(), "Handle Shipment Diagram");
        await Page.WaitForTimeoutAsync(1600);
        await Page.PickAsync(Page.DiagramSelect(), "Main Process Diagram");
        await Page.WaitForTimeoutAsync(1600);

        // Still undoable, and undoing it still puts the element back.
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Undo" })).ToBeEnabledAsync();

        var moved = await PositionAsync("CallActivity_HandleShipment");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Undo" }).ClickAsync();
        await Page.WaitForTimeoutAsync(1400);

        Assert.NotEqual(moved, await PositionAsync("CallActivity_HandleShipment"));
    }

    // --- helpers ------------------------------------------------------------------

    private Task<string> PositionAsync(string modelId) => Page.EvaluateAsync<string>(
        $$"""
        () => {
          const cell = document.querySelector(".joint-cell[model-id='{{modelId}}'] [joint-selector='body']");
          if (cell === null) return '(absent)';
          const b = cell.getBoundingClientRect();
          return Math.round(b.x) + ',' + Math.round(b.y);
        }
        """);

    private async Task MoveAsync(string modelId, int dx, int dy)
    {
        var box = await Page.Locator($".joint-cell[model-id='{modelId}'] [joint-selector='body']").First.BoundingBoxAsync();

        await Page.Mouse.MoveAsync(box!.X + (box.Width / 2), box.Y + (box.Height / 2));
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(box.X + (box.Width / 2) + dx, box.Y + (box.Height / 2) + dy, new() { Steps = 10 });
        await Page.Mouse.UpAsync();
        await Page.WaitForTimeoutAsync(1300);
    }

    private async Task SelectAsync(string modelId)
    {
        await Page.Locator($".joint-cell[model-id='{modelId}']").First.ClickAsync();
        await Page.WaitForTimeoutAsync(800);
    }

    private async Task GoToLinkedProcessesAsync()
    {
        await Page.GotoAsync(app.Url(this));

        await Expect(Page.Locator(".bpmn-canvas__paper > svg")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.Locator(".bpmn-canvas .joint-cell").First).ToBeVisibleAsync();

        await Page.GetByRole(AriaRole.Combobox, new() { Name = "Sample diagram" }).ClickAsync();
        await Page.Locator("fluent-option", new() { HasTextString = "linked-processes.bpmn" }).ClickAsync();
        await Page.WaitForTimeoutAsync(2000);
    }
}
