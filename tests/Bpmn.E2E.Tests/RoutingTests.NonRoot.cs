using System.Xml.Linq;

using Microsoft.Playwright;

namespace Hasseware.AspNetCore.Components.Bpmn.E2E.Tests;

public partial class RoutingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MovingANonRootEndpointKeepsNewAndExistingFlowsRouted(bool moveCreatedTask)
    {
        var original = await CaptureOriginalAsync("linked-processes.bpmn");
        await OpenShipmentDiagramAsync();

        var imported = await RouteOfAsync("Flow_PackToShip");
        Assert.Equal(2, imported.Count);
        var (task, flow) = await DrawShipmentFlowAsync();
        var before = await RouteOfAsync(flow);
        Assert.True(before.Count > 2, $"new non-root flow has no bends: {Describe(before)}");
        AssertOrthogonal(before);

        await MoveNonRootShapeAsync(moveCreatedTask ? task : "Task_PackItems", -35, 55, flow);

        var moved = await RouteOfAsync(flow);
        Assert.NotEqual(Describe(before), Describe(moved));
        AssertOrthogonal(moved);
        AssertOrthogonal(await RouteOfAsync("Flow_PackToShip"));
        await AssertShipmentHistoryAsync(flow, before, moved);

        var exported = await ExportRoutingDocumentAsync();
        AssertRootPlaneUnchanged(original, exported, "Process_Main");
        AssertStoredRoute(exported, flow, "Process_HandleShipment", moved);
        var node = Assert.Single(exported.Descendants(ModelNamespace + "task"),
            element => (string?)element.Attribute("id") == task);
        Assert.Equal("Process_HandleShipment", (string?)node.Parent!.Attribute("id"));
        var sequence = Assert.Single(exported.Descendants(ModelNamespace + "sequenceFlow"),
            element => (string?)element.Attribute("id") == flow);
        Assert.Equal("Process_HandleShipment", (string?)sequence.Parent!.Attribute("id"));
    }

    [Fact]
    public async Task SlidingANonRootBendKeepsItsRouteThroughNavigationAndHistory()
    {
        var original = await CaptureOriginalAsync("linked-processes.bpmn");
        await OpenShipmentDiagramAsync();
        var (_, flow) = await DrawShipmentFlowAsync();
        var before = await RouteOfAsync(flow);
        Assert.True(before.Count >= 4, $"no interior segment: {Describe(before)}");

        var segment = Enumerable.Range(2, before.Count - 3)
            .MaxBy(index => Math.Abs(before[index].X - before[index - 1].X)
                + Math.Abs(before[index].Y - before[index - 1].Y));
        var first = before[segment - 1];
        var second = before[segment];
        var position = await Page.EvaluateAsync<double[]>(CTM_EXPRESSION,
            new[] { (first.X + second.X) / 2, (first.Y + second.Y) / 2 });
        var vertical = Math.Abs(first.X - second.X) < 1.5;

        await DragNonRootPointerAsync(position[0], position[1],
            vertical ? 24 : 0, vertical ? 0 : 24, flow);
        var slid = await RouteOfAsync(flow);
        Assert.NotEqual(Describe(before), Describe(slid));
        await AssertShipmentHistoryAsync(flow, before, slid);

        var exported = await ExportRoutingDocumentAsync();
        AssertRootPlaneUnchanged(original, exported, "Process_Main");
        AssertStoredRoute(exported, flow, "Process_HandleShipment", slid);
    }

    [Fact]
    public async Task SlidingANonRootConnectorEndpointKeepsItsRouteThroughHistory()
    {
        var original = await CaptureOriginalAsync("linked-processes.bpmn");
        await OpenShipmentDiagramAsync();
        var (task, flow) = await DrawShipmentFlowAsync();
        var before = await RouteOfAsync(flow);
        var (x, y) = await PointOnFlowAsync(flow, 20);
        await Page.Mouse.ClickAsync(x, y);
        await Expect(Page.Locator(".joint-tool.target-arrowhead")).ToBeVisibleAsync();
        var head = await Page.Locator(".joint-tool.target-arrowhead").BoundingBoxAsync()
            ?? throw new InvalidOperationException("no target handle");
        var target = await Page.Locator($".joint-cell[model-id='{task}'] [joint-selector='body']")
            .First.BoundingBoxAsync() ?? throw new InvalidOperationException("no target task");
        var startX = head.X + (head.Width / 2);
        var startY = head.Y + (head.Height / 2);

        await DragNonRootPointerAsync(startX, startY,
            target.X + (target.Width / 2) - startX,
            target.Y + 2 - startY, flow);
        var slid = await RouteOfAsync(flow);
        Assert.NotEqual(Describe(before), Describe(slid));
        await AssertShipmentHistoryAsync(flow, before, slid);

        var exported = await ExportRoutingDocumentAsync();
        AssertRootPlaneUnchanged(original, exported, "Process_Main");
        AssertStoredRoute(exported, flow, "Process_HandleShipment", slid);
        var sequence = Assert.Single(exported.Descendants(ModelNamespace + "sequenceFlow"),
            element => (string?)element.Attribute("id") == flow);
        Assert.Equal(task, (string?)sequence.Attribute("targetRef"));
    }

    [Fact]
    public async Task ResizingAnExpandedSubprocessOnANonRootPlaneReroutesItsFlows()
    {
        var original = await CaptureOriginalAsync("nested-subprocesses.bpmn");
        await Page.PickAsync(Page.DiagramSelect(), 1);
        await Expect(Page.Locator(".joint-cell[model-id='expandedProcess']")).ToBeVisibleAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Fit to content", Exact = true }).ClickAsync();
        await Page.WaitForTimeoutAsync(600);

        const string flow = "sid-FB543319-8DFB-4445-AAA3-720137FB230B";
        var before = await RouteOfAsync(flow);
        var body = await Page.Locator(".joint-cell[model-id='expandedProcess'] [joint-selector='body']")
            .First.BoundingBoxAsync() ?? throw new InvalidOperationException("no expanded subprocess");
        await Page.Mouse.ClickAsync(body.X + 12, body.Y + 12);
        await Expect(Page.Locator(".bpmn-resize-handle")).ToHaveCountAsync(8);
        var west = await Page.Locator(".bpmn-resize-handle").Nth(7).BoundingBoxAsync()
            ?? throw new InvalidOperationException("no west resize handle");
        await Page.Mouse.MoveAsync(west.X + (west.Width / 2), west.Y + (west.Height / 2));
        await Page.Mouse.DownAsync();
        try
        {
            await Page.Mouse.MoveAsync(west.X - 20, west.Y + (west.Height / 2), new() { Steps = 6 });
            await Expect(Page.Locator(".bpmn-resize-preview")).ToHaveCountAsync(1);
            AssertOrthogonal(await RouteOfAsync(flow));
        }
        finally
        {
            await Page.Mouse.UpAsync();
        }

        await Page.WaitForTimeoutAsync(1000);
        await Expect(Page.Locator(".demo-edit-status")).ToContainTextAsync("Edited");
        var resized = await RouteOfAsync(flow);
        Assert.NotEqual(Describe(before), Describe(resized));
        AssertOrthogonal(resized);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Undo", Exact = true }).ClickAsync();
        await Page.WaitForTimeoutAsync(700);
        Assert.Equal(Describe(before), Describe(await RouteOfAsync(flow)));
        await Page.GetByRole(AriaRole.Button, new() { Name = "Redo", Exact = true }).ClickAsync();
        await Page.WaitForTimeoutAsync(700);
        Assert.Equal(Describe(resized), Describe(await RouteOfAsync(flow)));
        var exported = await ExportRoutingDocumentAsync();
        AssertRootPlaneUnchanged(original, exported, "rootProcess");
        AssertStoredRoute(exported, flow, "collapsedProcess", resized);
    }

    private static readonly XNamespace ModelNamespace = "http://www.omg.org/spec/BPMN/20100524/MODEL";
    private static readonly XNamespace DiagramNamespace = "http://www.omg.org/spec/BPMN/20100524/DI";
    private static readonly XNamespace WaypointNamespace = "http://www.omg.org/spec/DD/20100524/DI";

    private async Task OpenShipmentDiagramAsync()
    {
        await Page.PickAsync(Page.DiagramSelect(), "Handle Shipment Diagram");
        await Expect(Page.SelectedDiagram()).ToHaveAttributeAsync("value", "Diagram_HandleShipment");
        await Expect(Page.Locator(".joint-cell[model-id='Task_PackItems']")).ToBeVisibleAsync();
        await Page.WaitForTimeoutAsync(500);
    }

    private async Task<(string Task, string Flow)> DrawShipmentFlowAsync()
    {
        var (x, y) = await ClearBelowAsync("Task_PackItems");
        await DropToolAsync("Task", x, y);
        var task = await Page.Locator(".joint-cell[model-id^='task_']").GetAttributeAsync("model-id")
            ?? throw new InvalidOperationException("no new task");
        var before = await LinkIdsAsync();
        await DragHandleToAsync("Task_PackItems", task);
        await Expect(Page.Locator(".bpmn-canvas .joint-link[model-id]")).ToHaveCountAsync(before.Count + 1);
        return (task, await LinkAddedSinceAsync(before));
    }

    private async Task MoveNonRootShapeAsync(string modelId, double dx, double dy, string flow)
    {
        var body = await Page.Locator($".joint-cell[model-id='{modelId}'] [joint-selector='body']")
            .First.BoundingBoxAsync() ?? throw new InvalidOperationException("no shape body");
        await DragNonRootPointerAsync(body.X + (body.Width / 2), body.Y + (body.Height / 2), dx, dy, flow);
    }

    private async Task DragNonRootPointerAsync(double x, double y, double dx, double dy, string flow)
    {
        await Page.Mouse.MoveAsync((float)x, (float)y);
        await Page.Mouse.DownAsync();
        try
        {
            for (var step = 1; step <= 6; step++)
            {
                await Page.Mouse.MoveAsync((float)(x + (dx * step / 6)), (float)(y + (dy * step / 6)));
                await Page.WaitForTimeoutAsync(90);
                var route = await RouteOfAsync(flow);
                Assert.True(route.Count >= 2, "flow disappeared during gesture");
                AssertOrthogonal(route);
            }
        }
        finally
        {
            await Page.Mouse.UpAsync();
        }

        await Page.WaitForTimeoutAsync(1000);
        await Expect(Page.Locator(".demo-edit-status")).ToContainTextAsync("Edited");
        AssertOrthogonal(await RouteOfAsync(flow));
    }

    private async Task AssertShipmentHistoryAsync(string flow,
        IReadOnlyList<(double X, double Y)> before, IReadOnlyList<(double X, double Y)> after)
    {
        await Page.PickAsync(Page.DiagramSelect(), "Main Process Diagram");
        await Expect(Page.SelectedDiagram()).ToHaveAttributeAsync("value", "Diagram_Main");
        await Expect(Page.Locator($".joint-cell[model-id='{flow}']")).ToHaveCountAsync(0);
        await OpenShipmentDiagramAsync();
        Assert.Equal(Describe(after), Describe(await RouteOfAsync(flow)));
        await Page.GetByRole(AriaRole.Button, new() { Name = "Undo", Exact = true }).ClickAsync();
        await Page.WaitForTimeoutAsync(800);
        Assert.Equal(Describe(before), Describe(await RouteOfAsync(flow)));
        await Page.GetByRole(AriaRole.Button, new() { Name = "Redo", Exact = true }).ClickAsync();
        await Page.WaitForTimeoutAsync(800);
        Assert.Equal(Describe(after), Describe(await RouteOfAsync(flow)));
    }

    // The demo's export panel collapses the canvas until the page reloads, which culls every cell.
    private async Task<XDocument> CaptureOriginalAsync(string sample)
    {
        await GoToSampleAsync(sample);
        var original = await ExportRoutingDocumentAsync();
        await GoToSampleAsync(sample);
        return original;
    }

    private async Task<XDocument> ExportRoutingDocumentAsync()
    {
        await Page.OpenDemoFilesAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Export XML", Exact = true }).ClickAsync();
        await Expect(Page.Locator(".demo-export-xml")).ToBeVisibleAsync();
        return XDocument.Parse(await Page.Locator(".demo-export-xml").InnerTextAsync());
    }

    private static void AssertRootPlaneUnchanged(XDocument before, XDocument after, string root)
    {
        var original = Assert.Single(before.Descendants(DiagramNamespace + "BPMNPlane"),
            plane => (string?)plane.Attribute("bpmnElement") == root);
        var exported = Assert.Single(after.Descendants(DiagramNamespace + "BPMNPlane"),
            plane => (string?)plane.Attribute("bpmnElement") == root);
        Assert.True(XNode.DeepEquals(original, exported), "editing a non-root plane changed the root BPMNDI");
    }

    private static void AssertStoredRoute(XDocument document, string flow, string owner,
        IReadOnlyList<(double X, double Y)> drawn)
    {
        var edge = Assert.Single(document.Descendants(DiagramNamespace + "BPMNEdge"),
            element => (string?)element.Attribute("bpmnElement") == flow);
        Assert.Equal(owner, (string?)edge.Parent!.Attribute("bpmnElement"));
        var stored = edge.Elements(WaypointNamespace + "waypoint")
            .Select(point => ((double)point.Attribute("x")!, (double)point.Attribute("y")!)).ToList();
        Assert.True(stored.Count >= 2, "exported route has no endpoints");
        AssertOrthogonal(stored);
        Assert.True(drawn.Count >= stored.Count, "drawn route has fewer points than stored");
        AssertNear(stored[0], drawn[0]);
        AssertNear(stored[^1], drawn[^1]);

        // Rounded corners draw each stored bend as two points, so the drawing is checked against the
        // stored polyline rather than point by point.
        foreach (var point in drawn)
        {
            var distance = Enumerable.Range(1, stored.Count - 1)
                .Min(index => DistanceToSegment(point, stored[index - 1], stored[index]));
            Assert.True(distance <= 1.5, $"drawn point {point} is {distance:0.##} away from the stored route");
        }
    }

    private static void AssertNear((double X, double Y) stored, (double X, double Y) drawn)
    {
        Assert.InRange(Math.Abs(stored.X - drawn.X), 0, 1.5);
        Assert.InRange(Math.Abs(stored.Y - drawn.Y), 0, 1.5);
    }

    private static double DistanceToSegment((double X, double Y) point, (double X, double Y) start,
        (double X, double Y) end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var lengthSquared = (dx * dx) + (dy * dy);
        var t = lengthSquared == 0
            ? 0
            : Math.Clamp((((point.X - start.X) * dx) + ((point.Y - start.Y) * dy)) / lengthSquared, 0, 1);
        var x = start.X + (t * dx) - point.X;
        var y = start.Y + (t * dy) - point.Y;
        return Math.Sqrt((x * x) + (y * y));
    }
}
