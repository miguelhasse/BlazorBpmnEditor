using Hasseware.AspNetCore.Components.Bpmn.E2E.Tests.Fixtures;

using Microsoft.Playwright;
using Microsoft.Playwright.Xunit.v3;

namespace Hasseware.AspNetCore.Components.Bpmn.E2E.Tests;

/// <summary>
/// Editing an existing connector, in a real browser.
/// </summary>
/// <remarks>
/// <para>
/// The commands are unit-tested against documents. What only a browser shows is whether the
/// gestures reach them, and here they very nearly did not: JointJS's bend tools take the pointer
/// on the document and never let the link view see either event, so the reroute had to be hung off
/// the graph batch instead — and then off the batch's *start*, because a listener registered when
/// the batch closes is waiting for the pointer-up that is already being dispatched.
/// </para>
/// <para>
/// JointJS 4 gives link tools no per-tool class beyond the arrowheads, so the handles are found
/// here the way a user finds them: by where they are drawn.
/// </para>
/// </remarks>
public sealed class ConnectorEditTests(DemoAppFixture app) : PageTest, IClassFixture<DemoAppFixture>
{
    [Fact]
    public async Task SelectingAConnectorOffersItsTools()
    {
        await GoToEditorAsync();
        var flow = await FirstLinkAsync();
        await SelectLinkAsync(flow);

        await Expect(Page.Locator(".joint-highlight-mask")).ToHaveCountAsync(1);

        // Both arrowheads and Remove. There are no bend handles: bends are the router's.
        await Expect(Page.Locator(".joint-tools > .joint-tool")).ToHaveCountAsync(3);
        await Expect(Page.Locator(".joint-tool.source-arrowhead, .joint-tool.target-arrowhead")).ToHaveCountAsync(2);
    }

    [Fact]
    public async Task NoBendHandleIsDrawnOnASelectedConnector()
    {
        await GoToEditorAsync();
        await SelectSampleAsync("order-process.bpmn");

        // A flow with a bend in it.
        await SelectLinkAsync("Flow_3");

        // No wide segment handle and no vertex handle: the line itself is what is taken hold of.
        var handles = await Page.EvaluateAsync<int>(
            """
            () => [...document.querySelectorAll('.joint-tools path')]
              .filter((n) => { const b = n.getBoundingClientRect(); return b.width > 40 && b.height < 14; }).length
            """);
        Assert.Equal(0, handles);
    }

    [Fact]
    public async Task DraggingAnArrowheadReconnectsWithoutReplacingTheConnector()
    {
        await GoToEditorAsync();
        await SelectSampleAsync("order-process.bpmn");

        var flow = await FirstLinkAsync();
        await SelectLinkAsync(flow);

        var links = await LinkCountAsync();
        var target = await Page.Locator(".joint-cell[model-id='Task_ShipOrder']").First.BoundingBoxAsync();
        var head = await Page.Locator(".joint-tool.target-arrowhead").First.BoundingBoxAsync();

        await Page.Mouse.MoveAsync(head!.X + (head.Width / 2), head.Y + (head.Height / 2));
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(head.X + 20, head.Y + 20, new() { Steps = 3 });
        await Page.Mouse.MoveAsync(target!.X + (target.Width / 2), target.Y + (target.Height / 2), new() { Steps = 10 });
        await Page.Mouse.UpAsync();
        await Page.WaitForTimeoutAsync(1200);

        // Edited in place: the connector keeps its id, so nothing was created or destroyed. That
        // is the whole reason reconnect is a command rather than delete-then-create.
        await Expect(Page.Locator(".demo-edit-status")).ToContainTextAsync("Edited");
        await Expect(Page.Locator(".bpmn-canvas .joint-link")).ToHaveCountAsync(links);
        await Expect(Page.Locator($".joint-link[model-id='{flow}']")).ToHaveCountAsync(1);
    }

    [Fact]
    public async Task TheRemoveToolDeletesTheConnector()
    {
        await GoToEditorAsync();
        await SelectLinkAsync(await FirstLinkAsync());

        var before = await LinkCountAsync();
        // By name, not by "the first circle in the tool layer": the endpoint handles are circles
        // too, and which one comes first is an accident of the tool order.
        var button = await Page.Locator(".bpmn-remove-tool").First.BoundingBoxAsync();

        await Page.Mouse.ClickAsync(button!.X + (button.Width / 2), button.Y + (button.Height / 2));
        await Page.WaitForTimeoutAsync(1000);

        await Expect(Page.Locator(".bpmn-canvas .joint-link")).ToHaveCountAsync(before - 1);
    }

    [Fact]
    public async Task ReadOnlyOffersNoConnectorTools()
    {
        await Page.GotoAsync($"{app.BaseUrl}/editor");
        await Expect(Page.Locator(".bpmn-canvas .joint-cell").First).ToBeVisibleAsync(new() { Timeout = 30_000 });

        // The gallery is the read-only surface, but its sheets are shape matrices; the editor's
        // own read-only state is what this needs, so the assertion is made against a page that
        // has connectors and then again where editing is off.
        await SelectLinkAsync(await FirstLinkAsync());
        Assert.True(await Page.Locator(".joint-tools > .joint-tool").CountAsync() > 0, "the editor offered no tools");

        await Page.GotoAsync($"{app.BaseUrl}/gallery");
        await Expect(Page.Locator(".bpmn-canvas .joint-cell").First).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Page.Locator(".bpmn-canvas").FocusAsync();
        await Page.Keyboard.PressAsync("Control+a");
        await Page.WaitForTimeoutAsync(600);

        await Expect(Page.Locator(".joint-tools > .joint-tool")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task ARefusedReconnectionNamesTheRuleThatStoppedIt()
    {
        await GoToEditorAsync();
        await Page.SetViewportSizeAsync(1600, 1400);
        await SelectSampleAsync("order-process.bpmn");

        var flow = await Page.Locator(".bpmn-canvas .joint-link[model-id]").Last.GetAttributeAsync("model-id");
        await SelectLinkAsync(flow!);

        var links = await LinkCountAsync();
        var start = await Page.Locator(".joint-cell[model-id='StartEvent_1'] [joint-selector='outerRing']")
            .First.BoundingBoxAsync();
        var head = await Page.Locator(".joint-tool.target-arrowhead").First.BoundingBoxAsync();

        await Page.Mouse.MoveAsync(head!.X + (head.Width / 2), head.Y + (head.Height / 2));
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(head.X + 20, head.Y + 20, new() { Steps = 3 });
        await Page.Mouse.MoveAsync(start!.X + (start.Width / 2), start.Y + (start.Height / 2), new() { Steps = 12 });
        await Page.Mouse.UpAsync();
        await Page.WaitForTimeoutAsync(1000);

        /*
         * SF-003: nothing may flow into a Start Event. The end snaps back either way — what this
         * asserts is that the user is told why, because a refusal that says nothing reads as the
         * editor having missed the gesture entirely.
         */
        await Expect(Page.Locator(".demo-edit-status")).ToContainTextAsync("SF-003");
        await Expect(Page.Locator(".bpmn-canvas .joint-link")).ToHaveCountAsync(links);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DraggingAShapeStepsByTheGridOnlyWhenSnappingIsOn(bool snap)
    {
        await GoToEditorAsync();
        if (snap)
        {
            await Page.SetDemoSwitchAsync("Snap to grid");
            await Page.WaitForTimeoutAsync(800);
        }

        const string TaskSelector = ".joint-cell[model-id^='Task_']";
        var body = await Page.Locator($"{TaskSelector} [joint-selector='body']").First.BoundingBoxAsync()
            ?? throw new InvalidOperationException("no task");
        var startX = body.X + (body.Width / 2);
        var startY = body.Y + (body.Height / 2);
        await Page.Mouse.MoveAsync(startX, startY);
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(startX + 13, startY + 7, new() { Steps = 6 });
        await Page.Mouse.UpAsync();
        await Expect(Page.Locator(".demo-edit-status")).ToContainTextAsync("Edited");

        // The sample places the task at (220, 100) on a 10-unit grid at 100% zoom.
        var position = await Page.EvaluateAsync<double[]>(
            """
            selector => {
              const cell = document.querySelector(selector);
              const m = /translate\(([-\d.]+)[ ,]+([-\d.]+)\)/.exec(cell?.getAttribute('transform') ?? '');
              return m === null ? [] : [Number(m[1]), Number(m[2])];
            }
            """,
            TaskSelector);
        Assert.Equal(2, position.Length);
        Assert.Equal(snap, position[0] % 10 == 0 && position[1] % 10 == 0);
    }

    [Fact]
    public async Task WithTheGridOnACreatedEndpointStaysExactlyOnTheOutline()
    {
        await GoToEditorAsync();

        // Snapping is off by default, so without this the grid-enabled half of section 4.6 has no
        // surface to be exercised on at all.
        await Page.SetDemoSwitchAsync("Snap to grid");
        await Page.WaitForTimeoutAsync(800);

        var source = await Page.Locator(".joint-cell[model-id^='Event_'] [joint-selector='outerRing']")
            .First.BoundingBoxAsync();
        await Page.Mouse.ClickAsync(source!.X + (source.Width / 2), source.Y + (source.Height / 2));
        await Page.WaitForTimeoutAsync(500);

        var handle = await Page.Locator(".bpmn-connect-handle").First.BoundingBoxAsync();
        var target = await Page.Locator(".joint-cell[model-id^='Event_'] [joint-selector='outerRing']")
            .Last.BoundingBoxAsync();

        // Released deliberately off-grid, near the bottom of the ring.
        await Page.Mouse.MoveAsync(handle!.X + (handle.Width / 2), handle.Y + (handle.Height / 2));
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(handle.X + 30, handle.Y, new() { Steps = 3 });
        await Page.Mouse.MoveAsync(target!.X + (target.Width / 2) + 7, target.Y + target.Height - 4, new() { Steps = 12 });
        await Page.Mouse.UpAsync();
        await Page.WaitForTimeoutAsync(1200);

        await Expect(Page.Locator(".demo-edit-status")).ToContainTextAsync("Edited");

        /*
         * Snap the pointer, then project. The committed endpoint must lie on the circle even
         * though the snapped pointer does not — projecting first and snapping second would leave
         * it floating off the outline, which is what section 4.6 forbids.
         */
        var offOutline = await Page.EvaluateAsync<double>(
            """
            () => {
              const ring = [...document.querySelectorAll('.bpmn-canvas .joint-element[model-id] [joint-selector="outerRing"]')].pop();
              if (ring === undefined) return -1;
              const b = ring.getBoundingClientRect();
              const cx = b.x + b.width / 2;
              const cy = b.y + b.height / 2;
              const radius = b.width / 2;

              const ends = [...document.querySelectorAll('.bpmn-canvas .joint-link[model-id] [joint-selector="line"]')]
                .map((l) => {
                  const d = l.getAttribute('d') ?? '';
                  const pts = [...d.matchAll(/[ML]\s*(-?[\d.]+)[ ,]+(-?[\d.]+)/g)];
                  const last = pts[pts.length - 1];
                  if (last === undefined) return null;
                  const m = l.getScreenCTM();
                  return m === null ? null : { x: +last[1] * m.a + m.e, y: +last[2] * m.d + m.f };
                })
                .filter((p) => p !== null);

              const best = ends.map((p) => Math.abs(Math.hypot(p.x - cx, p.y - cy) - radius)).sort((a, b2) => a - b2)[0];
              return best === undefined ? -1 : best;
            }
            """);

        Assert.True(offOutline >= 0 && offOutline < 1.5, $"the endpoint sits {offOutline}px off the outline");
    }

    [Fact]
    public async Task AnArrowheadFollowsTheOutlineWhileItIsDragged()
    {
        await GoToEditorAsync();
        await SelectSampleAsync("order-process.bpmn");

        var flow = await FirstLinkAsync();
        await SelectLinkAsync(flow);

        var target = (await Page.Locator(".joint-cell[model-id='Task_ShipOrder'] [joint-selector='body']")
            .First.BoundingBoxAsync())!;
        var head = (await Page.Locator(".joint-tool.target-arrowhead").First.BoundingBoxAsync())!;

        await Page.Mouse.MoveAsync(head.X + (head.Width / 2), head.Y + (head.Height / 2));
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(head.X + 20, head.Y + 20, new() { Steps = 3 });

        /*
         * Well inside the shape, where a free-following end would sit on the pointer: the end has to
         * be on the border every time, and follow the pointer along it.
         */
        foreach (var (fx, fy) in new[] { (0.5, 0.5), (0.8, 0.4), (0.3, 0.7), (0.5, 0.9) })
        {
            await Page.Mouse.MoveAsync(
                (float)(target.X + (target.Width * fx)),
                (float)(target.Y + (target.Height * fy)),
                new() { Steps = 6 });

            var off = await EndOffTheBoxEdgeAsync(flow, target);

            Assert.True(off < 3.5, $"the end is {off}px from the task's border with the pointer at ({fx}, {fy})");
        }

        await Page.Mouse.UpAsync();
    }

    [Fact]
    public async Task AnArrowheadNearACompassPointSnapsOntoIt()
    {
        await GoToEditorAsync();
        await SelectSampleAsync("order-process.bpmn");

        var flow = await FirstLinkAsync();
        await SelectLinkAsync(flow);

        var target = (await Page.Locator(".joint-cell[model-id='Task_ShipOrder'] [joint-selector='body']")
            .First.BoundingBoxAsync())!;
        var head = (await Page.Locator(".joint-tool.target-arrowhead").First.BoundingBoxAsync())!;

        await Page.Mouse.MoveAsync(head.X + (head.Width / 2), head.Y + (head.Height / 2));
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(head.X + 20, head.Y + 20, new() { Steps = 3 });

        // A few pixels above the top side's midpoint, inside the snap radius (measured in the
        // diagram's units, so the screen distance depends on the zoom).
        await Page.Mouse.MoveAsync(
            (float)(target.X + (target.Width / 2) + 2),
            (float)(target.Y + 3),
            new() { Steps = 8 });

        var (X, Y) = await EndPointAsync(flow);

        Assert.True(Math.Abs(X - (target.X + (target.Width / 2))) < 1.5, $"the end is at x={X}, not on the side's midpoint");
        Assert.True(Math.Abs(Y - target.Y) < 1.5, $"the end is at y={Y}, not on the top side");

        await Page.Mouse.UpAsync();
    }

    /// <summary>How far a connector's last point is from the nearest side of a screen box.</summary>
    private async Task<double> EndOffTheBoxEdgeAsync(string connectorId, Microsoft.Playwright.LocatorBoundingBoxResult box)
    {
        var (X, Y) = await EndPointAsync(connectorId);

        return new[]
        {
            Math.Abs(X - box.X),
            Math.Abs(X - (box.X + box.Width)),
            Math.Abs(Y - box.Y),
            Math.Abs(Y - (box.Y + box.Height)),
        }.Min();
    }

    /// <summary>A connector's last drawn point, in screen coordinates.</summary>
    private async Task<(double X, double Y)> EndPointAsync(string connectorId)
    {
        var found = await Page.EvaluateAsync<double[]>(
            """
            id => {
              const line = document.querySelector(`.joint-link[model-id="${id}"] [joint-selector="line"]`);
              const d = line?.getAttribute('d') ?? '';
              const pts = [...d.matchAll(/[ML]\s*(-?[\d.]+)[ ,]+(-?[\d.]+)/g)];
              const last = pts[pts.length - 1];
              const m = line?.getScreenCTM();
              if (last === undefined || m === null || m === undefined) return [NaN, NaN];
              return [+last[1] * m.a + m.e, +last[2] * m.d + m.f];
            }
            """,
            connectorId);

        return (found[0], found[1]);
    }

    // --- helpers --------------------------------------------------------------

    private Task<int> LinkCountAsync() => Page.Locator(".bpmn-canvas .joint-link[model-id]").CountAsync();

    private async Task<string> FirstLinkAsync() =>
        await Page.Locator(".bpmn-canvas .joint-link[model-id]").First.GetAttributeAsync("model-id")
        ?? throw new InvalidOperationException("no connector on the canvas");

    private async Task SelectLinkAsync(string connectorId)
    {
        var line = await Page.Locator($".joint-link[model-id='{connectorId}'] [joint-selector=\"line\"]")
            .First.BoundingBoxAsync() ?? throw new InvalidOperationException($"'{connectorId}' is not drawn");

        await Page.Mouse.ClickAsync(line.X + (line.Width / 2), line.Y + (line.Height / 2));
        await Page.WaitForTimeoutAsync(500);
    }

    private async Task SelectSampleAsync(string fileName)
    {
        await Page.OpenDemoFilesAsync();
        await Page.GetByRole(AriaRole.Combobox, new() { Name = "Sample diagram" }).ClickAsync();
        await Page.Locator("fluent-option", new() { HasTextString = fileName }).ClickAsync();
        await Page.WaitForTimeoutAsync(1500);
    }

    private async Task GoToEditorAsync()
    {
        await Page.GotoAsync($"{app.BaseUrl}/editor");

        await Expect(Page.Locator(".bpmn-canvas__paper > svg")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.Locator(".bpmn-canvas .joint-cell").First).ToBeVisibleAsync();
    }
}
