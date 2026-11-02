using Hasseware.AspNetCore.Components.Bpmn.E2E.Tests.Fixtures;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;

using Microsoft.Playwright;
using Microsoft.Playwright.Xunit.v3;

namespace Hasseware.AspNetCore.Components.Bpmn.E2E.Tests;

public class ContextualSwimlaneTests(DemoAppFixture app) : PageTest, IClassFixture<DemoAppFixture>
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task SelectionOutlineEnclosesSwimlaneHeaderWithoutInnerContour(bool horizontal, bool keyboard)
    {
        await OpenAsync(new BpmnDocumentBuilder()
            .Pool("Pool", "Pool", new Bounds(30, 40, 600, 300), isHorizontal: horizontal)
            .Lane("Lane", "Lane", horizontal ? new Bounds(60, 40, 570, 150) : new Bounds(30, 70, 300, 270),
                ["Task"], isHorizontal: horizontal)
            .Lane("Neighbor", "Neighbor", horizontal ? new Bounds(60, 190, 570, 150) : new Bounds(330, 70, 300, 270),
                [], isHorizontal: horizontal)
            .Node("Task", BpmnFlowNodeType.Task, new Bounds(180, 100, 100, 80), "Task")
            .Build());
        await Page.GetByRole(AriaRole.Button, new() { Name = "Reset zoom" }).ClickAsync();
        await Expect(Page.Locator(".bpmn-toolbar__zoom")).ToHaveTextAsync("100%");

        foreach (var id in new[] { "Pool", "Lane" })
        {
            if (keyboard)
            {
                await Page.Locator(".bpmn-canvas").FocusAsync();
                await Page.Keyboard.PressAsync("Tab");
                await Page.Keyboard.PressAsync("Home");
                if (id == "Lane")
                    await Page.Keyboard.PressAsync("ArrowDown");
            }
            else
            {
                await Page.Locator($".joint-element[model-id='{id}'] .bpmn-swimlane-header")
                    .ClickAsync(new() { Position = new() { X = 10, Y = 10 } });
            }

            await Expect(Page.Locator($"#bpmn-a11y-{id}")).ToHaveAttributeAsync("aria-selected", "true");
            await Expect(Page.Locator(".joint-highlight-stroke")).ToHaveCountAsync(1);
            await Expect(Page.Locator(".joint-highlight-mask")).ToHaveCountAsync(0);
            await AssertSwimlaneOutlineAsync(id);

            foreach (var zoom in new[] { "91%", "83%", "75%" })
            {
                await Page.GetByRole(AriaRole.Button, new() { Name = "Zoom out" }).ClickAsync();
                await Expect(Page.Locator(".bpmn-toolbar__zoom")).ToHaveTextAsync(zoom);
            }
            await AssertSwimlaneOutlineAsync(id);
            await Page.GetByRole(AriaRole.Button, new() { Name = "Reset zoom" }).ClickAsync();
            await Expect(Page.Locator(".bpmn-toolbar__zoom")).ToHaveTextAsync("100%");
        }

        await Page.Locator(".joint-element[model-id='Task'] [joint-selector='body']").ClickAsync();
        await Expect(Page.Locator(".joint-highlight-stroke")).ToHaveCountAsync(0);
        await Expect(Page.Locator(".joint-highlight-mask")).ToHaveCountAsync(1);
        await Expect(Page.Locator("#bpmn-a11y-Task")).ToHaveAttributeAsync("aria-selected", "true");
        await Page.Locator(".bpmn-canvas").ClickAsync(new() { Position = new() { X = 5, Y = 5 } });
        await Expect(Page.Locator(".joint-highlight-stroke, .joint-highlight-mask")).ToHaveCountAsync(0);
    }

    private async Task AssertSwimlaneOutlineAsync(string id)
    {
        var geometry = await Page.Locator($".joint-element[model-id='{id}']").EvaluateAsync<bool[]>(
            """
            cell => {
              const bodyNode = cell.querySelector('.bpmn-swimlane-body');
              const body = bodyNode.getBoundingClientRect();
              const header = cell.querySelector('.bpmn-swimlane-header').getBoundingClientRect();
              const outline = document.querySelector('.joint-highlight-stroke');
              const box = outline.getBoundingClientRect();
              const matrix = outline.getScreenCTM();
              const bodyMatrix = bodyNode.getScreenCTM();
              const scale = Math.hypot(bodyMatrix.a, bodyMatrix.b);
              const offset = 5.75 * scale;
              const inverse = matrix.inverse();
              const onStroke = (x, y) => outline.isPointInStroke(new DOMPoint(x, y).matrixTransform(inverse));
              const x = header.left + header.width / 2;
              const y = header.top + header.height / 2;
              let overlaysNeighbor = true;
              if (cell.getAttribute('model-id') === 'Lane') {
                const neighbor = document.querySelector(".joint-element[model-id='Neighbor'] .bpmn-swimlane-header")
                  .getBoundingClientRect();
                const horizontal = neighbor.top >= body.bottom - 0.1;
                const point = horizontal
                  ? { x, y: body.bottom + offset }
                  : { x: body.right + offset, y };
                const insideNeighbor = point.x > neighbor.left && point.x < neighbor.right
                  && point.y > neighbor.top && point.y < neighbor.bottom;
                outline.style.pointerEvents = 'stroke';
                try {
                  overlaysNeighbor = insideNeighbor && document.elementFromPoint(point.x, point.y) === outline;
                } finally {
                  outline.style.removeProperty('pointer-events');
                }
              }
              return [
                outline.tagName.toLowerCase() === 'path' && /z\s*$/i.test(outline.getAttribute('d')),
                getComputedStyle(outline).fill === 'none' && getComputedStyle(outline).pointerEvents === 'none',
                Math.abs(box.left - (body.left - offset)) < 0.1,
                Math.abs(box.top - (body.top - offset)) < 0.1,
                Math.abs(box.right - (body.right + offset)) < 0.1,
                Math.abs(box.bottom - (body.bottom + offset)) < 0.1,
                onStroke(body.left - offset, y) && onStroke(body.right + offset, y),
                onStroke(x, body.top - offset) && onStroke(x, body.bottom + offset),
                !onStroke(body.left + offset, y) && !onStroke(body.right - offset, y),
                !onStroke(x, body.top + offset) && !onStroke(x, body.bottom - offset),
                overlaysNeighbor
              ];
            }
            """);
        Assert.All(geometry, Assert.True);
    }

    [Theory]
    [InlineData("Task")]
    [InlineData("End")]
    [InlineData("Data")]
    [InlineData("Pool")]
    [InlineData("Lane")]
    public async Task AnnotationActionCreatesConnectedAnnotationAndOpensEditor(string sourceId)
    {
        await OpenAsync(new BpmnDocumentBuilder()
            .Pool("Pool", "Pool", new Bounds(30, 40, 600, 250))
            .Lane("Lane", "Lane", new Bounds(60, 40, 570, 250), ["Task", "End"])
            .Node("Task", BpmnFlowNodeType.Task, new Bounds(120, 100, 100, 80), "Task")
            .Node("End", BpmnFlowNodeType.EndEvent, new Bounds(300, 120, 36, 36), "End")
            .DataObject("Data", "Data", new Bounds(400, 100, 36, 50))
            .Build());
        var source = Page.Locator($".joint-element[model-id='{sourceId}']");
        if (sourceId is "Pool" or "Lane")
        {
            var box = await source.BoundingBoxAsync();
            await Page.Mouse.ClickAsync(box!.X + 14, box.Y + 12);
        }
        else
        {
            await source.Locator("[joint-selector='body'], [joint-selector='outerRing']").First.ClickAsync();
        }
        await Page.GetByRole(AriaRole.Button, new() { Name = "Add Text Annotation", Exact = true }).ClickAsync();
        var editor = Page.Locator(".bpmn-label-editor__input");
        await Expect(editor).ToBeVisibleAsync();
        await Expect(Page.Locator(".joint-element[model-id^='TextAnnotation_']")).ToHaveCountAsync(1);
        await Expect(Page.Locator(".joint-link[model-id^='Association_']")).ToHaveCountAsync(1);
        await Page.Keyboard.PressAsync("Escape");
        var saved = await ExportAsync();
        var annotation = Assert.Single(saved.TextAnnotations);
        var association = Assert.Single(saved.Associations);
        Assert.Equal(sourceId, association.SourceRef);
        Assert.Equal(annotation.Id, association.TargetRef);
        Assert.Equal(BpmnAssociationDirection.None, association.Direction);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Undo", Exact = true }).ClickAsync();
        await Expect(Page.Locator(".joint-element[model-id^='TextAnnotation_']")).ToHaveCountAsync(0);
        await Expect(Page.Locator(".joint-link[model-id^='Association_']")).ToHaveCountAsync(0);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Redo", Exact = true }).ClickAsync();
        await Expect(Page.Locator(".joint-element[model-id^='TextAnnotation_']")).ToHaveCountAsync(1);
        await Expect(Page.Locator(".joint-link[model-id^='Association_']")).ToHaveCountAsync(1);
    }

    [Theory]
    [InlineData(false, "Process_1")]
    [InlineData(true, "Process_1")]
    [InlineData(false, "constructor")]
    [InlineData(true, "__proto__")]
    [InlineData(false, "toString")]
    public async Task FirstPoolAndLaneWrapContentWithoutMovingIt(bool drag, string processId)
    {
        var document = new BpmnDocumentBuilder(processId)
            .Node("A", BpmnFlowNodeType.Task, new Bounds(100, 100, 100, 80), "A")
            .Node("B", BpmnFlowNodeType.Task, new Bounds(450, 100, 100, 80), "B")
            .DataObject("Data", "Long external data label\nsecond label line", new Bounds(330, 300, 36, 50))
            .TextAnnotation("Note", "Existing note", new Bounds(100, 320, 100, 50))
            .Flow("Flow", "A", "B", [new DiPoint(200, 140), new DiPoint(450, 140)])
            .Build();
        await OpenAsync(document);
        var before = await ExportAsync();
        var tool = Page.Locator(".bpmn-stencil__tool", new() { HasTextString = "Pool / Lane" }).First;
        await Expect(tool).ToBeEnabledAsync();
        if (drag)
        {
            var paper = await Page.Locator(".bpmn-canvas").BoundingBoxAsync();
            await tool.HoverAsync();
            await Page.Mouse.DownAsync();
            await Page.Mouse.MoveAsync(paper!.X + 50, paper.Y + 50, new() { Steps = 8 });
            await Page.Mouse.UpAsync();
        }
        else
        {
            await tool.FocusAsync();
            await Page.Keyboard.PressAsync("Enter");
        }
        await Expect(Page.Locator(".joint-element[model-id^='Participant_']")).ToHaveCountAsync(1);
        var pooled = await ExportAsync();
        AssertGeometryUnchanged(before, pooled);
        var pool = Assert.Single(pooled.Collaboration!.Participants);
        Assert.Equal(processId, pool.ProcessRef);
        Assert.Single(pooled.Processes);
        Assert.All(pooled.FlowNodes, node => Assert.Equal(pool.Id, node.PoolId));
        var enclosure = await Page.Locator(".joint-element[model-id='Data'] [joint-selector='label']").EvaluateAsync<string>(
            """
            label => {
              const box = label.getBoundingClientRect();
              const pool = document.querySelector(".joint-element[model-id^='Participant_'] [joint-selector='body']").getBoundingClientRect();
              return JSON.stringify({
                enclosed: box.left > pool.left && box.right < pool.right && box.top > pool.top && box.bottom < pool.bottom,
                label: box.toJSON(), pool: pool.toJSON()
              });
            }
            """);
        Assert.Contains("\"enclosed\":true", enclosure);

        await Page.GetByRole(AriaRole.Button, new() { Name = "Fit" }).ClickAsync();
        await Page.Locator(".joint-element[model-id='A'] [joint-selector='body']").ClickAsync();
        await tool.FocusAsync();
        await Page.Keyboard.PressAsync("Enter");
        await Expect(Page.Locator(".joint-element[model-id^='Lane_']")).ToHaveCountAsync(1);
        var laned = await ExportAsync();
        AssertGeometryUnchanged(before, laned);
        var lane = Assert.Single(laned.Lanes);
        Assert.Equal(["A", "B"], lane.FlowNodeRefs);
        Assert.All(laned.FlowNodes, node => Assert.Equal(lane.Id, node.LaneId));
        await Page.GetByRole(AriaRole.Button, new() { Name = "Undo", Exact = true }).ClickAsync();
        await Expect(Page.Locator(".joint-element[model-id^='Lane_']")).ToHaveCountAsync(0);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Undo", Exact = true }).ClickAsync();
        await Expect(Page.Locator(".joint-element[model-id^='Participant_']")).ToHaveCountAsync(0);
        AssertGeometryUnchanged(before, await ExportAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptyDocumentOffersPoolToolAndCreatesPoolWithoutLane(bool rootless)
    {
        await OpenAsync(rootless
            ? new BpmnDocument { Definitions = new BpmnDefinitions("Definitions", "http://example.org/bpmn") }
            : new BpmnDocumentBuilder().Build());
        var tool = Page.Locator(".bpmn-stencil__tool", new() { HasTextString = "Pool / Lane" }).First;
        await Expect(tool).ToBeEnabledAsync();
        await tool.FocusAsync();
        await Page.Keyboard.PressAsync("Enter");
        await Expect(Page.Locator(".demo-edit-status")).ToContainTextAsync("Edited");
        await Expect(Page.Locator(".joint-element[model-id^='Participant_']")).ToHaveCountAsync(1);
        var document = await ExportAsync();
        Assert.Single(document.Processes);
        Assert.Single(document.Collaboration!.Participants);
        Assert.Empty(document.Lanes);
    }

    private async Task OpenAsync(BpmnDocument document)
    {
        await Page.GotoAsync(app.Url(this));
        await Expect(Page.Locator(".bpmn-canvas__paper > svg")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        Assert.Equal(2, await Page.EvaluateAsync<int>("1 + 1"));
        var transfer = await Page.EvaluateHandleAsync(
            """
            xml => {
              const transfer = new DataTransfer();
              transfer.items.add(new File([xml], 'contextual-swimlane.bpmn', { type: 'application/xml' }));
              return transfer;
            }
            """, BpmnXmlWriter.Write(document).Xml);
        await Page.Locator(".bpmn-canvas").DispatchEventAsync("drop", new Dictionary<string, object> { ["dataTransfer"] = transfer });
        await Page.ImportAccept().ClickAsync();
        await Expect(Page.Locator(".bpmn-import")).ToHaveCountAsync(0);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Fit" }).ClickAsync();
    }

    private async Task<BpmnDocument> ExportAsync()
    {
        await Page.OpenDemoFilesAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Export XML", Exact = true }).ClickAsync();
        var xml = await Page.Locator(".demo-export-xml").TextContentAsync()
            ?? throw new InvalidOperationException("XML export is empty.");
        if (await Page.Locator(".demo-export-xml").IsVisibleAsync())
            await Page.Locator(".demo-exported > summary").DispatchEventAsync("click");
        await Expect(Page.Locator(".demo-export-xml")).ToBeHiddenAsync();
        return BpmnXmlReader.Read(xml).Document;
    }

    private static void AssertGeometryUnchanged(BpmnDocument before, BpmnDocument after)
    {
        foreach (var shape in before.Diagrams[0].Plane.Shapes)
            Assert.Equal(shape, after.Diagrams[0].Plane.Shapes.Single(candidate => candidate.Id == shape.Id));
        foreach (var edge in before.Diagrams[0].Plane.Edges)
        {
            var current = after.Diagrams[0].Plane.Edges.Single(candidate => candidate.Id == edge.Id);
            Assert.Equal(edge.Waypoints, current.Waypoints);
            Assert.Equal(edge.Label, current.Label);
        }
    }
}
