using Hasseware.AspNetCore.Components.Bpmn.E2E.Tests.Fixtures;

using Microsoft.Playwright;
using Microsoft.Playwright.Xunit.v3;

namespace Hasseware.AspNetCore.Components.Bpmn.E2E.Tests;

/// <summary>
/// Connector routing (implementation plan, M5).
/// </summary>
/// <remarks>
/// <para>
/// Routing only tells the truth through a real pointer. A connector's route is computed by the
/// JointJS Manhattan router while the gesture is in flight, read off the provisional link at
/// release, and stored — so a synthetic event that never drives JointJS's drag machinery would test
/// nothing at all.
/// </para>
/// <para>
/// The other half of what is asserted here is what routing must <em>not</em> do. Sections 10.6 and 11 keep
/// imported geometry as the document has it, so an imported diagram's routes are drawn as its file states them;
/// a router that re-shaped every diagram on load would be the fidelity failure BPMNDI exists to
/// prevent.
/// </para>
/// </remarks>
public partial class RoutingTests(DemoAppFixture app) : PageTest, IClassFixture<DemoAppFixture>
{
    // --- imported routes are not re-shaped ----------------------------------------

    [Fact]
    public async Task ImportedRoutesAreDrawnExactlyAsTheFileStatesThem()
    {
        await GoToEditorAsync();

        var drift = await Page.EvaluateAsync<int>(
            """
            () => {
              const shown = [...document.querySelectorAll('.bpmn-canvas .joint-link[model-id]')];
              // Sections 10.6 and 11: what is drawn is what is stored. A router that bent these on load
              // would add points to routes the file declares as two.
              return shown.filter((link) => {
                const d = link.querySelector('[joint-selector="line"], path')?.getAttribute('d') ?? '';
                return [...d.matchAll(/[ML]/g)].length !== 2;
              }).length;
            }
            """);

        Assert.Equal(0, drift);
    }

    [Fact]
    public async Task ACollaborationKeepsItsImportedBends()
    {
        // Tall enough that every route is inside the culling margin: a bend that is not drawn is
        // not a bend that was lost.
        await Page.SetViewportSizeAsync(1280, 1100);
        await GoToEditorAsync();
        await SelectSampleAsync("pizza-store.bpmn");

        // Eight of this fixture's edges are drawn with bends. They are the document's, so they must
        // survive a load untouched rather than be recomputed into whatever the router prefers.
        var bent = await Page.EvaluateAsync<int>(
            """
            () => [...document.querySelectorAll('.bpmn-canvas .joint-link[model-id]')]
              .filter((link) => {
                const d = link.querySelector('[joint-selector="line"], path')?.getAttribute('d') ?? '';
                return [...d.matchAll(/[ML]/g)].length > 2;
              }).length
            """);

        Assert.True(bent >= 8, $"only {bent} of the eight bent routes survived the load");
    }

    // --- a drawn connector is routed ------------------------------------------------

    [Fact]
    public async Task ADrawnConnectorIsRoutedOrthogonally()
    {
        await GoToEditorAsync();

        // Two shapes that do not line up, so a straight line between them would be diagonal and a
        // routed one cannot be.
        var (x, y) = await FreeSpotAsync();
        await DropToolAsync("Task", x, y);
        var target = await Page.Locator(".joint-cell[model-id^='task_']").First.GetAttributeAsync("model-id");

        var source = await Page.EvaluateAsync<string>(
            """
            () => [...document.querySelectorAll('.bpmn-canvas .joint-element[model-id]')]
              .find((c) => (c.getAttribute('model-id') ?? '').startsWith('Task_'))?.getAttribute('model-id') ?? ''
            """);

        var before = await LinkIdsAsync();
        await DragHandleToAsync(source, target!);
        await Expect(Page.Locator(".bpmn-canvas .joint-link[model-id]")).ToHaveCountAsync(before.Count + 1);

        var route = await RouteOfAsync(await LinkAddedSinceAsync(before));

        Assert.True(route.Count > 2, $"the route was not bent at all: {Describe(route)}");
        AssertOrthogonal(route);
    }

    [Fact]
    public async Task ADrawnConnectorsRouteSurvivesAReload()
    {
        // The route the router computed has to be in the document, not only on the canvas: a route
        // held only by the canvas would be redrawn as a straight line the moment anything reloads.
        await GoToEditorAsync();

        var (x, y) = await FreeSpotAsync();
        await DropToolAsync("Task", x, y);
        var target = await Page.Locator(".joint-cell[model-id^='task_']").First.GetAttributeAsync("model-id");

        var source = await Page.EvaluateAsync<string>(
            """
            () => [...document.querySelectorAll('.bpmn-canvas .joint-element[model-id]')]
              .find((c) => (c.getAttribute('model-id') ?? '').startsWith('Task_'))?.getAttribute('model-id') ?? ''
            """);

        var existing = await LinkIdsAsync();
        await DragHandleToAsync(source, target!);

        var created = await LinkAddedSinceAsync(existing);
        var before = await RouteOfAsync(created);
        Assert.True(before.Count > 2, "nothing was routed, so there is nothing to preserve");

        // Zooming re-renders from the document without touching it.
        await Page.GetByRole(AriaRole.Button, new() { Name = "Fit" }).ClickAsync();
        await Page.WaitForTimeoutAsync(900);

        var after = await RouteOfAsync(created);
        Assert.Equal(before.Count, after.Count);
    }

    // --- parallel connectors --------------------------------------------------------

    [Fact]
    public async Task ASecondConnectorBetweenAPairRunsInItsOwnCorridor()
    {
        /*
         * An orthogonal route between two fixed attachment points has one degree of freedom: the
         * corridor its interior runs along. The ends cannot move — section 4.6 puts them on the
         * outline — so what must differ between two connectors over the same pair is the middle.
         */
        await GoToEditorAsync();

        var source = await Page.EvaluateAsync<string>(
            """
            () => [...document.querySelectorAll('.bpmn-canvas .joint-element[model-id]')]
              .find((c) => (c.getAttribute('model-id') ?? '').startsWith('Task_'))?.getAttribute('model-id') ?? ''
            """);

        var (x, y) = await ClearBelowAsync(source);
        await DropToolAsync("Task", x, y);
        var target = await Page.Locator(".joint-cell[model-id^='task_']").First.GetAttributeAsync("model-id");

        var before = await LinkIdsAsync();
        await DragHandleToAsync(source, target!);
        var first = await LinkAddedSinceAsync(before);

        var between = await LinkIdsAsync();
        await DragHandleToAsync(source, target!);
        var second = await LinkAddedSinceAsync(between);

        var one = await RouteOfAsync(first);
        var two = await RouteOfAsync(second);

        AssertOrthogonal(one);
        AssertOrthogonal(two);

        Assert.True(one.Count >= 4, $"the first route has no corridor to separate from: {Describe(one)}");
        Assert.Equal(one.Count, two.Count);

        // Every interior bend moved; the two endpoints did not.
        Assert.Equal(one[0], two[0]);
        Assert.Equal(one[^1], two[^1]);

        for (var index = 1; index < one.Count - 1; index++)
        {
            var moved = Math.Abs(one[index].X - two[index].X) + Math.Abs(one[index].Y - two[index].Y);
            Assert.True(moved > 1d, $"interior bend {index} did not move: {Describe(one)} vs {Describe(two)}");
        }
    }

    // --- dragging a segment ---------------------------------------------------------

    // There are no bend handles, so there is no segment drag to test; see ConnectorEditTests.

    [Fact]
    public async Task AnAssociationIsDrawnStraight()
    {
        // An Association is a loose tie to an annotation. Right angles would make it read as
        // process flow, which is the one thing it is not.
        await GoToEditorAsync();
        await SelectSampleAsync("global-catalog.bpmn");

        var association = await Page.EvaluateAsync<string>(
            """
            () => [...document.querySelectorAll('.bpmn-canvas .joint-link[model-id]')]
              .find((l) => (l.getAttribute('aria-label') ?? '').startsWith('Association'))
              ?.getAttribute('model-id') ?? ''
            """);

        Assert.NotEqual(string.Empty, association);

        var route = await RouteOfAsync(association);

        Assert.Equal(2, route.Count);
        Assert.True(
            Math.Abs(route[0].X - route[1].X) > 1.5 && Math.Abs(route[0].Y - route[1].Y) > 1.5,
            $"the Association was routed orthogonally: {Describe(route)}");
    }

    /// <summary>Paper coordinates to client coordinates, through the paper's own transform.</summary>
    private const string CTM_EXPRESSION = "p => { const m = document.querySelector('.bpmn-canvas__paper > svg > g').getScreenCTM(); return [m.a * p[0] + m.c * p[1] + m.e, m.b * p[0] + m.d * p[1] + m.f]; }";

    // --- helpers ---------------------------------------------------------------------

    private static string Describe(IReadOnlyList<(double X, double Y)> route) =>
        string.Join(" -> ", route.Select(point => $"{point.X:0},{point.Y:0}"));

    /// <summary>Every segment runs along one axis, which is what orthogonal means.</summary>
    // --- routing while the shape is still moving -------------------------------------

    [Fact]
    public async Task ConnectorsStayOrthogonalWhileTheShapeIsBeingDragged()
    {
        /*
         * Not only once it has been dropped. Re-anchoring an endpoint without re-squaring leaves
         * the first and last segments at whatever angle the new position implies, so a Task dragged
         * away trailed two diagonals behind it for the whole gesture and snapped square on release.
         *
         * Measured mid-drag, which no other routing test does — every one of them asserts a settled
         * state, and a settled state is exactly what was already correct.
         */
        await GoToSampleAsync("order-process.bpmn");

        var box = await Page.Locator(".joint-cell[model-id='Task_ValidateOrder'] [joint-selector='body']")
            .First.BoundingBoxAsync() ?? throw new InvalidOperationException("no body");

        var x = box.X + (box.Width / 2);
        var y = box.Y + (box.Height / 2);

        await Page.Mouse.MoveAsync(x, y);
        await Page.Mouse.DownAsync();

        try
        {
            for (var step = 1; step <= 6; step++)
            {
                await Page.Mouse.MoveAsync(x + (step * 12), y - (step * 10));
                await Page.WaitForTimeoutAsync(90);

                Assert.Equal(0, await DiagonalSegmentsAsync());
            }
        }
        finally
        {
            await Page.Mouse.UpAsync();
            await Page.WaitForTimeoutAsync(900);
        }

        // And nothing moves on release: what was drawn during the drag is what gets stored, so the
        // gesture ends without the correction the user would otherwise see.
        Assert.Equal(0, await DiagonalSegmentsAsync());
    }

    [Fact]
    public async Task AGroupMoveKeepsEveryFlowOrthogonalAtEveryStep()
    {
        // Flows with both ends among the moving shapes travel with them; the rest are re-routed.
        // Neither may show a diagonal while the pointer is down.
        await GoToSampleAsync("order-process.bpmn");
        await Page.Locator(".bpmn-canvas").ClickAsync(new() { Position = new() { X = 8, Y = 8 } });
        await Page.Keyboard.PressAsync("Control+a");
        await Page.WaitForTimeoutAsync(300);

        var box = await Page.Locator(".joint-cell[model-id='Task_ValidateOrder'] [joint-selector='body']")
            .First.BoundingBoxAsync() ?? throw new InvalidOperationException("no body");

        var x = box.X + (box.Width / 2);
        var y = box.Y + (box.Height / 2);

        await Page.Mouse.MoveAsync(x, y);
        await Page.Mouse.DownAsync();

        try
        {
            for (var step = 1; step <= 5; step++)
            {
                await Page.Mouse.MoveAsync(x + (step * 14), y + (step * 9));
                await Page.WaitForTimeoutAsync(90);

                Assert.Equal(0, await DiagonalSegmentsAsync());
            }
        }
        finally
        {
            await Page.Mouse.UpAsync();
            await Page.WaitForTimeoutAsync(900);
        }

        Assert.Equal(0, await DiagonalSegmentsAsync());
    }

    [Fact]
    public async Task DraggingAnEndpointKeepsTheFlowOrthogonalAtEveryStep()
    {
        await GoToSampleAsync("order-process.bpmn");

        var flow = (await LinkIdsAsync())[0];
        var line = await Page.Locator($".joint-link[model-id='{flow}'] [joint-selector='line']")
            .First.BoundingBoxAsync() ?? throw new InvalidOperationException("no line");

        await Page.Mouse.ClickAsync(line.X + (line.Width / 2), line.Y + (line.Height / 2));
        await Page.WaitForTimeoutAsync(500);

        var head = await Page.Locator(".joint-tool.target-arrowhead").First.BoundingBoxAsync()
            ?? throw new InvalidOperationException("no arrowhead");
        var target = await Page.Locator(".joint-cell[model-id='Task_ShipOrder'] [joint-selector='body']")
            .First.BoundingBoxAsync() ?? throw new InvalidOperationException("no target");

        await Page.Mouse.MoveAsync(head.X + (head.Width / 2), head.Y + (head.Height / 2));
        await Page.Mouse.DownAsync();

        try
        {
            // Around the target, where a free-following end would draw a diagonal to the old bend.
            foreach (var (fx, fy) in new[] { (0.9, 0.2), (0.5, -0.15), (0.1, 0.5), (0.5, 1.2), (0.85, 0.8) })
            {
                await Page.Mouse.MoveAsync(
                    (float)(target.X + (target.Width * fx)),
                    (float)(target.Y + (target.Height * fy)),
                    new() { Steps = 4 });
                await Page.WaitForTimeoutAsync(90);

                Assert.Equal(0, await DiagonalSegmentsAsync());
            }
        }
        finally
        {
            await Page.Mouse.UpAsync();
            await Page.WaitForTimeoutAsync(1000);
        }

        Assert.Equal(0, await DiagonalSegmentsAsync());
    }

    [Fact]
    public async Task ResizingAPoolKeepsItsMessageFlowsOrthogonal()
    {
        await GoToSampleAsync("self-serve-restaurant.bpmn");
        await Page.GetByText("Fit", new() { Exact = true }).First.ClickAsync();
        await Page.WaitForTimeoutAsync(800);

        const string Pool = "id-6b414d17-b5b4-41a0-a9ea-a96c679f3972";
        var body = await Page.Locator($".joint-cell[model-id='{Pool}'] [joint-selector='body']")
            .First.BoundingBoxAsync() ?? throw new InvalidOperationException("no pool");

        await Page.Mouse.ClickAsync(body.X + 14, body.Y + (body.Height / 2));
        await Page.WaitForTimeoutAsync(500);

        // The west handle, dragged left.
        var west = await Page.Locator(".bpmn-resize-handle").Nth(7).BoundingBoxAsync()
            ?? throw new InvalidOperationException("no handle");

        await Page.Mouse.MoveAsync(west.X + (west.Width / 2), west.Y + (west.Height / 2));
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(west.X - 40, west.Y + (west.Height / 2), new() { Steps = 6 });
        await Page.Mouse.UpAsync();
        await Page.WaitForTimeoutAsync(1400);

        await Expect(Page.Locator(".demo-edit-status")).ToContainTextAsync("Edited");
        Assert.Equal(0, await DiagonalSegmentsAsync());
    }

    [Fact]
    public async Task ARefusedMoveLeavesTheConnectorsAsTheyWere()
    {
        /*
         * The live squaring rewrites bends, and a refused move restores positions. Without the
         * bends being restored too, a rejected drag would leave the connectors re-shaped for a
         * position the document never took.
         */
        await GoToSampleAsync("order-process.bpmn");

        var before = await RouteOfAsync("Flow_1");
        Assert.NotEmpty(before);

        var victim = await Page.Locator(".joint-cell[model-id='Task_ShipOrder'] [joint-selector='body']")
            .First.BoundingBoxAsync() ?? throw new InvalidOperationException("no victim");

        var box = await Page.Locator(".joint-cell[model-id='Task_ValidateOrder'] [joint-selector='body']")
            .First.BoundingBoxAsync() ?? throw new InvalidOperationException("no body");

        // Dropped onto a peer, which DI-011 refuses.
        await Page.Mouse.MoveAsync(box.X + (box.Width / 2), box.Y + (box.Height / 2));
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(victim.X + (victim.Width / 2), victim.Y + (victim.Height / 2), new() { Steps = 10 });
        await Page.Mouse.UpAsync();
        await Page.WaitForTimeoutAsync(1200);

        await Expect(Page.Locator(".demo-edit-status")).ToContainTextAsync("DI-011");
        Assert.Equal(Describe(before), Describe(await RouteOfAsync("Flow_1")));
    }

    [Fact]
    public async Task NoConnectorSegmentsOverlapAtAnyStepOfADrag()
    {
        await GoToSampleAsync("order-process.bpmn");

        var box = await Page.Locator(".joint-cell[model-id='Task_ValidateOrder'] [joint-selector='body']")
            .First.BoundingBoxAsync() ?? throw new InvalidOperationException("no body");
        var x = box.X + (box.Width / 2);
        var y = box.Y + (box.Height / 2);

        Assert.Equal(0, await OverlappingSegmentsAsync());
        await Page.Mouse.MoveAsync(x, y);
        await Page.Mouse.DownAsync();

        try
        {
            for (var step = 1; step <= 14; step++)
            {
                await Page.Mouse.MoveAsync(x + (step * 2), y + (step % 2 == 0 ? step * 9 : -step * 7));
                await Page.WaitForTimeoutAsync(90);

                Assert.Equal(0, await OverlappingSegmentsAsync());
            }
        }
        finally
        {
            await Page.Mouse.UpAsync();
        }

        Assert.Equal(0, await OverlappingSegmentsAsync());
    }

    [Fact]
    public async Task ALooseEndIsReachedByOneElbowNotAHook()
    {
        // While an end is dragged through empty space it has no shape to approach, and the router
        // used to hook: out past the pointer and back in. One elbow is the whole route.
        await Page.SetViewportSizeAsync(1500, 900);
        await GoToSampleAsync("order-process.bpmn");

        var flow = await Page.Locator(".joint-link[model-id='Flow_4'] [joint-selector='line']").First.BoundingBoxAsync()
            ?? throw new InvalidOperationException("no flow");
        await Page.Mouse.ClickAsync(flow.X + 1, flow.Y + (flow.Height / 2));
        await Page.WaitForTimeoutAsync(400);

        var head = await Page.Locator(".joint-tool.target-arrowhead").First.BoundingBoxAsync()
            ?? throw new InvalidOperationException("no arrowhead");
        var x = head.X + (head.Width / 2);
        var y = head.Y + (head.Height / 2);

        await Page.Mouse.MoveAsync(x, y);
        await Page.Mouse.DownAsync();

        try
        {
            await Page.Mouse.MoveAsync(x - 40, y + 30, new() { Steps = 8 });
            await Page.WaitForTimeoutAsync(250);

            Assert.Equal(0, await DiagonalSegmentsAsync());
            var route = await RouteOfAsync("Flow_4");
            Assert.True(route.Count <= 4, $"the route to a loose end is more than one elbow: {Describe(route)}");
        }
        finally
        {
            await Page.Mouse.UpAsync();
        }
    }

    [Fact]
    public async Task DeletingAFlowAndDrawingItAgainRaisesNoPageError()
    {
        // Releasing a connector removes the provisional link, and the viewport pass that follows used
        // to look up its ends on a graph it no longer had.
        var errors = new System.Collections.Concurrent.ConcurrentQueue<string>();
        Page.PageError += (_, error) => errors.Enqueue(error);

        await Page.SetViewportSizeAsync(1700, 1100);
        await GoToSampleAsync("pizza-store.bpmn");

        const string flow = "id-d6905eed-2870-422a-af05-456c0bb91908";
        var line = await Page.Locator($".joint-link[model-id='{flow}'] [joint-selector='line']").First.BoundingBoxAsync()
            ?? throw new InvalidOperationException("no flow");
        await Page.Mouse.ClickAsync(line.X + (line.Width / 2), line.Y + line.Height - 1);
        await Page.WaitForTimeoutAsync(300);
        await Page.Keyboard.PressAsync("Delete");
        await Expect(Page.Locator($".joint-link[model-id='{flow}']")).ToHaveCountAsync(0);

        await DragHandleToAsync("id-47ca6bc5-0ab5-4403-afd4-ccd31976defe", "id-ae419819-4312-445c-b377-c908ad7c5a49");

        var linkCount = await Page.Locator(".bpmn-canvas .joint-link[model-id]").CountAsync();
        var status = await Page.Locator(".demo-edit-status").InnerTextAsync();
        Assert.True(linkCount == 24, $"Expected recreated link; found {linkCount}. Status: {status}");
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("Flow_3", "source")]
    [InlineData("Flow_3", "target")]
    [InlineData("Flow_2", "source")]
    [InlineData("Flow_4", "target")]
    public async Task SlidingAnEndAlongItsShapeKeepsTheFlowOrthogonal(string flow, string end)
    {
        await Page.SetViewportSizeAsync(1500, 900);
        await GoToSampleAsync("order-process.bpmn");

        var line = await Page.Locator($".joint-link[model-id='{flow}'] [joint-selector='line']").First.BoundingBoxAsync()
            ?? throw new InvalidOperationException("no flow");
        await Page.Mouse.ClickAsync(line.X + 1, line.Y + (line.Height / 2));
        await Page.WaitForTimeoutAsync(400);

        var head = await Page.Locator($".joint-tool.{end}-arrowhead").First.BoundingBoxAsync()
            ?? throw new InvalidOperationException("no end handle");
        var x = head.X + (head.Width / 2);
        var y = head.Y + (head.Height / 2);

        await Page.Mouse.MoveAsync(x, y);
        await Page.Mouse.DownAsync();

        try
        {
            // Along the shape's own outline, in steps, so every frame is looked at.
            for (var step = 1; step <= 8; step++)
            {
                await Page.Mouse.MoveAsync(x + (step * 4), y + (step * 3));
                await Page.WaitForTimeoutAsync(80);

                Assert.Equal(0, await DiagonalSegmentsAsync());
            }
        }
        finally
        {
            await Page.Mouse.UpAsync();
        }

        // And once the document has answered, which redraws the flow from what it stored.
        await Page.WaitForTimeoutAsync(1200);
        Assert.Equal(0, await DiagonalSegmentsAsync());
        Assert.Equal(0, await OverlappingSegmentsAsync());
    }

    [Fact]
    public async Task AnEndSlidOverItsOwnShapeInAPoolStaysOnThatShape()
    {
        // The Task and the Pool around it both contain the pointer, and JointJS breaks that tie by
        // visiting order, which picked the Pool: the end was reconnected to it and refused.
        await Page.SetViewportSizeAsync(1700, 1100);
        await GoToSampleAsync("pizza-store.bpmn");

        const string flow = "id-324c2202-75b8-4bc6-8859-37dc8573d57b";
        var line = await Page.Locator($".joint-link[model-id='{flow}'] [joint-selector='line']").First.BoundingBoxAsync()
            ?? throw new InvalidOperationException("no flow");
        await Page.Mouse.ClickAsync(line.X + 1, line.Y + (line.Height / 2));
        await Page.WaitForTimeoutAsync(400);

        var head = await Page.Locator(".joint-tool.target-arrowhead").First.BoundingBoxAsync()
            ?? throw new InvalidOperationException("no end handle");
        var x = head.X + (head.Width / 2);
        var y = head.Y + (head.Height / 2);

        await Page.Mouse.MoveAsync(x, y);
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(x, y + 20, new() { Steps = 8 });
        await Page.Mouse.UpAsync();
        await Page.WaitForTimeoutAsync(1000);

        await Expect(Page.Locator(".demo-edit-status")).Not.ToContainTextAsync("Refused");
        Assert.Equal(0, await DiagonalSegmentsAsync());
    }

    [Fact]
    public async Task ARefusedEndDropPutsTheFlowBackExactlyAsItWas()
    {
        // Dropping an end on a shape the rules refuse snaps it back. It used to leave the link with
        // the end the pointer had left it on, and the restored route ran from there: a diagonal.
        await Page.SetViewportSizeAsync(1700, 1100);
        await GoToSampleAsync("pizza-store.bpmn");

        const string flow = "id-ef82c6af-a239-45bb-9e4b-fc2c8fa8299f";
        var before = Describe(await RouteOfAsync(flow));

        var line = await Page.Locator($".joint-link[model-id='{flow}'] [joint-selector='line']").First.BoundingBoxAsync()
            ?? throw new InvalidOperationException("no flow");
        await Page.Mouse.ClickAsync(line.X + (line.Width / 2), line.Y + (line.Height / 2));
        await Page.WaitForTimeoutAsync(400);

        var head = await Page.Locator(".joint-tool.target-arrowhead").First.BoundingBoxAsync()
            ?? throw new InvalidOperationException("no end handle");
        var x = head.X + (head.Width / 2);
        var y = head.Y + (head.Height / 2);

        // Out of the Pool altogether, where there is nothing to attach to.
        await Page.Mouse.MoveAsync(x, y);
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(x - 100, y - 100, new() { Steps = 8 });
        await Page.Mouse.UpAsync();
        await Page.WaitForTimeoutAsync(1000);

        Assert.Equal(before, Describe(await RouteOfAsync(flow)));
        Assert.Equal(0, await DiagonalSegmentsAsync());
    }

    [Fact]
    public async Task DraggingAnyPointOnTheLineSlidesThatRunAndOneUndoTakesItBack()
    {
        await Page.SetViewportSizeAsync(1500, 900);
        await GoToSampleAsync("order-process.bpmn");

        var before = Describe(await RouteOfAsync("Flow_3"));

        // A point on the vertical run, a little way up from the Gateway. No handle is drawn there.
        var (x, y) = await PointOnFlowAsync("Flow_3", 30);
        await Page.Mouse.MoveAsync(x, y);

        // With no handle to look at, the cursor says which way the run under it goes.
        var cursor = await Page.EvaluateAsync<string>(
            "([x, y]) => getComputedStyle(document.elementFromPoint(x, y)).cursor",
            new object[] { (double)x, (double)y });
        Assert.Equal("ew-resize", cursor);

        await Page.Mouse.DownAsync();

        try
        {
            for (var step = 1; step <= 8; step++)
            {
                await Page.Mouse.MoveAsync(x + (step * 6), y);
                await Page.WaitForTimeoutAsync(60);

                Assert.Equal(0, await DiagonalSegmentsAsync());
                Assert.Equal(0, await OverlappingSegmentsAsync());
            }
        }
        finally
        {
            await Page.Mouse.UpAsync();
        }

        await Page.WaitForTimeoutAsync(1000);
        await Expect(Page.Locator(".demo-edit-status")).ToContainTextAsync("Edited");

        var after = await RouteOfAsync("Flow_3");
        Assert.NotEqual(before, Describe(after));
        AssertOrthogonal(after);

        // One gesture, one undo.
        await Page.GetByRole(AriaRole.Button, new() { Name = "Undo" }).ClickAsync();
        await Page.WaitForTimeoutAsync(900);
        Assert.Equal(before, Describe(await RouteOfAsync("Flow_3")));
    }

    [Fact]
    public async Task ASlidRouteIsStoredAndSurvivesTheDocumentBeingDrawnAgain()
    {
        await Page.SetViewportSizeAsync(1700, 1100);
        await GoToSampleAsync("pizza-store.bpmn");

        // A flow with an interior run, inside a Pool.
        const string flow = "id-d6905eed-2870-422a-af05-456c0bb91908";
        var before = await RouteOfAsync(flow);

        var (x, y) = await PointOnFlowAsync(flow, 100);
        await Page.Mouse.MoveAsync(x, y);
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(x - 20, y, new() { Steps = 8 });
        await Page.Mouse.UpAsync();
        await Page.WaitForTimeoutAsync(1000);

        await Expect(Page.Locator(".demo-edit-status")).Not.ToContainTextAsync("Refused");
        var slid = await RouteOfAsync(flow);
        Assert.NotEqual(Describe(before), Describe(slid));
        AssertOrthogonal(slid);

        // Undo then redo draws the document again, from what it stored.
        await Page.GetByRole(AriaRole.Button, new() { Name = "Undo" }).ClickAsync();
        await Page.WaitForTimeoutAsync(800);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Redo" }).ClickAsync();
        await Page.WaitForTimeoutAsync(1000);

        Assert.Equal(Describe(slid), Describe(await RouteOfAsync(flow)));
    }

    [Fact]
    public async Task AClickOnTheLineOnlySelectsItAndSlidingStopsAgainstWhatIsInTheWay()
    {
        await Page.SetViewportSizeAsync(1500, 900);
        await GoToSampleAsync("order-process.bpmn");

        var before = Describe(await RouteOfAsync("Flow_2"));
        var (x, y) = await PointOnFlowAsync("Flow_2", 25);

        // A click that goes nowhere: selected, not edited.
        await Page.Mouse.ClickAsync(x, y);
        await Page.WaitForTimeoutAsync(500);

        await Expect(Page.Locator(".demo-edit-status")).ToContainTextAsync("Selected");
        Assert.Equal(before, Describe(await RouteOfAsync("Flow_2")));

        // Dragged a long way across the Task it leaves: the run stops at the shape, it does not go through it.
        await Page.Mouse.MoveAsync(x, y);
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(x - 200, y + 8, new() { Steps = 10 });
        await Page.Mouse.UpAsync();
        await Page.WaitForTimeoutAsync(800);

        Assert.Equal(0, await DiagonalSegmentsAsync());
        Assert.Equal(0, await OverlappingSegmentsAsync());
    }

    /// <summary>A point a distance along a drawn connector, in screen coordinates.</summary>
    private async Task<(float X, float Y)> PointOnFlowAsync(string modelId, double length)
    {
        var point = await Page.EvaluateAsync<double[]>(
            """
            ([id, length]) => {
              const line = document.querySelector(`.joint-link[model-id="${id}"] [joint-selector='line']`);
              const m = line.getScreenCTM();
              const p = line.getPointAtLength(length);
              return [p.x * m.a + m.e, p.y * m.d + m.f];
            }
            """,
            new object[] { modelId, length });

        return ((float)point[0], (float)point[1]);
    }

    /// <summary>Pairs of drawn segments, on one connector or two, that lie along the same line.</summary>
    private Task<int> OverlappingSegmentsAsync() => Page.EvaluateAsync<int>(
        "(() => { const polylineOf = " + PolylineOf + """
          ;
          const segments = [];
          document.querySelectorAll(".bpmn-canvas .joint-link[model-id] [joint-selector='line']").forEach((line, link) => {
            const p = polylineOf(line.getAttribute('d') ?? '');
            for (let i = 1; i < p.length; i++) segments.push({ link, a: p[i - 1], b: p[i], i, n: p.length - 1 });
          });
          let overlaps = 0;
          for (let m = 0; m < segments.length; m++) {
            for (let n = m + 1; n < segments.length; n++) {
              const s = segments[m], t = segments[n];
              if (s.link === t.link && Math.abs(s.i - t.i) <= 1) continue;
              // Two connectors attached at one point arrive by the same last few units. That is where
              // they meet, not one lying on the other, and no route can change it.
              const ends = (g) => [...(g.i === 1 ? [g.a] : []), ...(g.i === g.n ? [g.b] : [])];
              if (s.link !== t.link && ends(s).some((e) => ends(t).some((f) => Math.abs(e.x - f.x) < 1 && Math.abs(e.y - f.y) < 1))) continue;
              for (const vertical of [true, false]) {
                const k = vertical ? 'x' : 'y', r = vertical ? 'y' : 'x';
                if (Math.abs(s.a[k] - s.b[k]) > 1 || Math.abs(t.a[k] - t.b[k]) > 1) continue;
                if (Math.abs(s.a[k] - t.a[k]) > 1) continue;
                const lo = Math.max(Math.min(s.a[r], s.b[r]), Math.min(t.a[r], t.b[r]));
                const hi = Math.min(Math.max(s.a[r], s.b[r]), Math.max(t.a[r], t.b[r]));
                if (hi - lo > 1) overlaps += 1;
              }
            }
          }
          return overlaps;
        })
        """);

    /// <summary>
    /// A drawn path read back as the right-angled polyline it was routed as.
    /// </summary>
    /// <remarks>
    /// Connectors are drawn with rounded corners, so a corner is a cubic curve rather than a point.
    /// The curve's first control point lies on the segment that comes in and its second on the one
    /// that goes out, which is enough to put the corner back: the two lines meet at it.
    /// A step of a few units between two long runs is drawn as an S and read back as the step.
    /// </remarks>
    private const string PolylineOf =
        """
        d => {
          const tokens = [...d.matchAll(/([MLC])([^MLC]*)/g)]
            .map((m) => ({ c: m[1], n: m[2].match(/-?\d+(?:\.\d+)?/g).map(Number) }));
          const points = [];
          let current = null;
          for (const { c, n } of tokens) {
            if (c === 'C') {
              const [x1, y1, x2, y2, x, y] = n;
              // A jog is drawn as a gentle S: a few units across and a corner's width along. Read it as
              // the small step it replaces, not as a corner.
              const across = Math.min(Math.abs(x - current.x), Math.abs(y - current.y));
              const along = Math.max(Math.abs(x - current.x), Math.abs(y - current.y));
              if (across < 3 && along >= 3) {
                points.push(Math.abs(x - current.x) > Math.abs(y - current.y) ? { x: current.x, y } : { x, y: current.y });
                current = { x, y };
                continue;
              }
              const horizontal = Math.abs(x1 - current.x) >= Math.abs(y1 - current.y);
              points.push(horizontal ? { x: x2, y: y1 } : { x: x1, y: y2 });
              current = { x, y };
              continue;
            }
            current = { x: n[0], y: n[1] };
            points.push(current);
          }
          // A zero-length line either side of each curve is the connector's own, not a segment.
          return points.filter((p, i) => i === 0 || Math.hypot(p.x - points[i - 1].x, p.y - points[i - 1].y) > 0.5);
        }
        """;

    /// <summary>How many drawn segments run neither horizontally nor vertically.</summary>
    private Task<int> DiagonalSegmentsAsync() => Page.EvaluateAsync<int>(
        "(() => { const polylineOf = " + PolylineOf + """
          ;
          let diagonal = 0;
          for (const line of document.querySelectorAll(".bpmn-canvas .joint-link [joint-selector='line']")) {
            const points = polylineOf(line.getAttribute('d') ?? '');

            for (let i = 1; i < points.length; i++) {
              if (Math.abs(points[i - 1].x - points[i].x) > 1.5
                && Math.abs(points[i - 1].y - points[i].y) > 1.5) diagonal += 1;
            }
          }
          return diagonal;
        })
        """);

    private async Task GoToSampleAsync(string sample)
    {
        await GoToEditorAsync();

        await SelectSampleAsync(sample);
    }

    private static void AssertOrthogonal(IReadOnlyList<(double X, double Y)> route)
    {
        for (var index = 1; index < route.Count; index++)
        {
            var (X, Y) = route[index - 1];
            var current = route[index];
            var axial = Math.Abs(current.X - X) < 1.5 || Math.Abs(current.Y - Y) < 1.5;

            Assert.True(axial, $"segment {index} of {Describe(route)} runs diagonally");
        }
    }

    private async Task<IReadOnlyList<(double X, double Y)>> RouteOfAsync(string modelId)
    {
        var points = await Page.EvaluateAsync<double[][]>(
            "id => { const polylineOf = " + PolylineOf + """
              ;
              const d = document.querySelector(`.joint-cell[model-id="${id}"] [joint-selector="line"]`)
                ?.getAttribute('d')
                ?? document.querySelector(`.joint-cell[model-id="${id}"] path`)?.getAttribute('d')
                ?? '';
              return d === '' ? [] : polylineOf(d).map((p) => [p.x, p.y]);
            }
            """,
            modelId);

        return [.. points.Select(point => (point[0], point[1]))];
    }

    /// <summary>Every connector currently drawn.</summary>
    private async Task<IReadOnlyList<string>> LinkIdsAsync() =>
        await Page.EvaluateAsync<string[]>(
            """
            () => [...document.querySelectorAll('.bpmn-canvas .joint-link[model-id]')]
              .map((l) => l.getAttribute('model-id'))
            """);

    /// <summary>
    /// The connector that appeared since <paramref name="before"/> was taken.
    /// </summary>
    /// <remarks>
    /// By difference rather than by name: a Sequence Flow the editor creates is called
    /// <c>Flow_&lt;guid&gt;</c>, which is exactly what the fixtures call theirs.
    /// </remarks>
    private async Task<string> LinkAddedSinceAsync(IReadOnlyList<string> before)
    {
        var now = await LinkIdsAsync();
        var added = now.Except(before, StringComparer.Ordinal).ToList();

        return Assert.Single(added);
    }

    private async Task DragHandleToAsync(string sourceId, string targetId)
    {
        await Page.Locator($".joint-cell[model-id='{sourceId}'] [joint-selector='body']").First.ClickAsync();
        await Expect(Page.Locator(".bpmn-connect-handle")).ToHaveCountAsync(1);

        var handle = await Page.Locator(".bpmn-connect-handle").BoundingBoxAsync()
            ?? throw new InvalidOperationException("the connect handle has no box");

        var target = await Page
            .Locator($".joint-cell[model-id='{targetId}'] [joint-selector='body'], "
                + $".joint-cell[model-id='{targetId}'] [joint-selector='outerRing']")
            .First.BoundingBoxAsync() ?? throw new InvalidOperationException("no drawn target");

        await Page.Mouse.MoveAsync(handle.X + (handle.Width / 2), handle.Y + (handle.Height / 2));
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(
            target.X + (target.Width / 2),
            target.Y + (target.Height / 2),
            new() { Steps = 12 });
        await Page.Mouse.UpAsync();
        await Page.WaitForTimeoutAsync(1200);
    }

    /// <summary>
    /// A drop point to the right of the source and clear below it.
    /// </summary>
    /// <remarks>
    /// The corridor this test is about only exists when the two shapes are not side by side: an
    /// orthogonal route between boxes that overlap vertically is one straight segment, and a
    /// straight segment has no interior to separate. Measured from the source rather than inset
    /// from the canvas corner, which moved whenever the demo's chrome changed height and quietly
    /// turned this into a test of two identical straight lines.
    /// </remarks>
    private async Task<(float X, float Y)> ClearBelowAsync(string sourceId)
    {
        var canvas = await Page.Locator(".bpmn-canvas").BoundingBoxAsync()
            ?? throw new InvalidOperationException("the canvas has no bounding box");

        var source = await Page.Locator($".joint-cell[model-id='{sourceId}'] [joint-selector='body']").First.BoundingBoxAsync()
            ?? throw new InvalidOperationException($"'{sourceId}' has no drawn body");

        // Half a Task's height plus a margin below the source's lower edge, so the two cannot share
        // a horizontal band however the paper happens to be scaled.
        var y = source.Y + source.Height + 70;

        Assert.True(
            y < canvas.Y + canvas.Height - 20,
            $"the canvas is too short to place a target clear of '{sourceId}'");

        return (canvas.X + canvas.Width - 180, y);
    }

    private async Task<(float X, float Y)> FreeSpotAsync()
    {
        var canvas = await Page.Locator(".bpmn-canvas").BoundingBoxAsync()
            ?? throw new InvalidOperationException("the canvas has no bounding box");

        return (canvas.X + canvas.Width - 200, canvas.Y + canvas.Height - 160);
    }

    private async Task DropToolAsync(string label, float x, float y)
    {
        await Page.Locator(".bpmn-stencil__tool", new() { HasTextString = label }).First.HoverAsync();
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(x, y, new() { Steps = 8 });
        await Page.Mouse.UpAsync();
        await Page.WaitForTimeoutAsync(900);
    }

    private async Task SelectSampleAsync(string fileName)
    {
        await Page.OpenDemoFilesAsync();
        await Page.GetByRole(AriaRole.Combobox, new() { Name = "Sample diagram" }).ClickAsync();
        await Page.Locator("fluent-option", new() { HasTextString = fileName }).ClickAsync();
        await Page.WaitForTimeoutAsync(1800);
    }

    private async Task GoToEditorAsync()
    {
        await Page.GotoAsync(app.Url(this));

        await Expect(Page.Locator(".bpmn-canvas__paper > svg")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.Locator(".bpmn-canvas .joint-cell").First).ToBeVisibleAsync();
        await Expect(Page.Locator(".bpmn-stencil__tool").First).ToBeVisibleAsync();
    }
}
