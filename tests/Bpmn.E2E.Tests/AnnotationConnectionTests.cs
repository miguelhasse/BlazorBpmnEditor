using Hasseware.AspNetCore.Components.Bpmn.E2E.Tests.Fixtures;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;

using Microsoft.Playwright;
using Microsoft.Playwright.Xunit.v3;

namespace Hasseware.AspNetCore.Components.Bpmn.E2E.Tests;

public class AnnotationConnectionTests(DemoAppFixture app) : PageTest, IClassFixture<DemoAppFixture>
{
    [Fact]
    public async Task AnnotationHandleIsLeftOfBracketAndVerticallyCentered()
    {
        await OpenAsync();
        await SelectAnnotationAsync();
        var bracket = await Page.Locator("[model-id='Note'] [joint-selector='bracket']").EvaluateAsync<double[]>(
            "path => { const box = path.getBBox(); const point = new DOMPoint(box.x, box.y + box.height / 2).matrixTransform(path.getScreenCTM()); return [point.x, point.y]; }");
        var handle = await Page.Locator(".bpmn-connect-handle").EvaluateAsync<double[]>(
            "circle => { const point = new DOMPoint(circle.cx.baseVal.value, circle.cy.baseVal.value).matrixTransform(circle.getScreenCTM()); return [point.x, point.y, circle.r.baseVal.value]; }");
        var scale = await Page.Locator("[model-id='A'] [joint-selector='body']").EvaluateAsync<double>(
            "element => element.getScreenCTM().a");

        Assert.InRange(Math.Abs(handle[0] - (bracket[0] - (12 * scale))), 0, 0.1);
        Assert.InRange(Math.Abs(handle[1] - bracket[1]), 0, 0.1);
        Assert.Equal(7, handle[2]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnnotationAttachesToTheVisibleRoundedFlowCorner(bool message)
    {
        await OpenAsync(message, bent: true);
        await SelectAnnotationAsync();
        var point = await Page.Locator("[model-id='Flow'] [joint-selector='line']").EvaluateAsync<float[]>(
            """
            path => {
              const point = path.getPointAtLength(98);
              const screen = point.matrixTransform(path.getScreenCTM());
              return [screen.x, screen.y, point.x, point.y];
            }
            """);
        await Page.EvaluateAsync(
            "() => document.addEventListener('mouseup', event => { document.body.dataset.annotationRelease = JSON.stringify({ x: event.clientX, y: event.clientY }); }, { once: true })");
        await DragHandleAsync(point[0], point[1]);
        await Expect(Page.Locator(".joint-link[model-id^='Association_']")).ToHaveCountAsync(1);
        var xml = await ExportAsync();
        var document = BpmnXmlReader.Read(xml).Document;
        var association = Assert.Single(document.Associations);
        var endpoint = document.Diagrams[0].Plane.Edges.Single(edge => edge.BpmnElement == association.Id).Waypoints[^1];
        var nearest = await Page.Locator("[model-id='Flow'] [joint-selector='line']").EvaluateAsync<double[]>(
            """
            path => {
              const release = JSON.parse(document.body.dataset.annotationRelease);
              delete document.body.dataset.annotationRelease;
              const local = new DOMPoint(release.x, release.y).matrixTransform(path.getScreenCTM().inverse());
              let nearest, distance = Infinity;
              for (let at = 0; at <= path.getTotalLength(); at += 0.01) {
                const p = path.getPointAtLength(at);
                const d = Math.hypot(p.x - local.x, p.y - local.y);
                if (d < distance) { nearest = p; distance = d; }
              }
              return [nearest.x, nearest.y];
            }
            """);
        Assert.InRange(Math.Sqrt(Math.Pow(endpoint.X - nearest[0], 2) + Math.Pow(endpoint.Y - nearest[1], 2)), 0, 0.15);
        await ImportAsync(xml);
        await AssertAttachedAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnnotationConnectsToFlowAndFollowsMovementUndoReloadAndDeletion(bool message)
    {
        await OpenAsync(message);
        await SelectAnnotationAsync();
        var target = await FlowPointAsync();
        await DragHandleAsync(target[0], target[1] + 5);
        var association = Page.Locator(".joint-link[model-id^='Association_']");
        await Expect(association).ToHaveCountAsync(1);
        await Expect(Page.Locator(".demo-edit-status")).ToContainTextAsync("Edited");
        await AssertAttachedAsync();
        var before = await association.Locator("[joint-selector='line']").GetAttributeAsync("d");
        var id = await association.GetAttributeAsync("model-id");

        var task = await Page.Locator("[model-id='B'] [joint-selector='body']").BoundingBoxAsync();
        await Page.Mouse.MoveAsync(task!.X + (task.Width / 2), task.Y + (task.Height / 2));
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(task.X + (task.Width / 2), task.Y + (task.Height / 2) + 70, new() { Steps = 15 });
        await Page.Mouse.UpAsync();
        await Page.WaitForTimeoutAsync(600);
        await AssertAttachedAsync();
        Assert.NotEqual(before, await association.Locator("[joint-selector='line']").GetAttributeAsync("d"));
        await Page.GetByRole(AriaRole.Button, new() { Name = "Undo", Exact = true }).ClickAsync();
        await Expect(association.Locator("[joint-selector='line']")).ToHaveAttributeAsync("d", before!);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Redo", Exact = true }).ClickAsync();
        await AssertAttachedAsync();

        var xml = await ExportAsync();
        var saved = BpmnXmlReader.Read(xml).Document;
        Assert.Equal("Flow", Assert.Single(saved.Associations).TargetRef);
        Assert.Equal(BpmnAssociationDirection.None, saved.Associations[0].Direction);
        Assert.Equal(2, saved.Diagrams[0].Plane.Edges.Single(edge => edge.BpmnElement == id).Waypoints.Count);
        await ImportAsync(xml);
        await Expect(Page.Locator($".joint-link[model-id='{id}']")).ToHaveCountAsync(1);
        await AssertAttachedAsync();

        var onFlow = await FlowPointAsync(0.2);
        await Page.Mouse.ClickAsync(onFlow[0], onFlow[1]);
        await Page.Keyboard.PressAsync("Delete");
        await Expect(Page.Locator(".joint-link[model-id='Flow']")).ToHaveCountAsync(0);
        await Expect(association).ToHaveCountAsync(0);
        await Expect(Page.Locator(".joint-element[model-id='Note']")).ToHaveCountAsync(0);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Undo", Exact = true }).ClickAsync();
        await Expect(association).ToHaveCountAsync(1);
        await Expect(Page.Locator(".joint-element[model-id='Note']")).ToBeVisibleAsync();
        await AssertAttachedAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnnotationCanReconnectBetweenFlowAndShape(bool message)
    {
        await OpenAsync(message);
        await SelectAnnotationAsync();
        var onFlow = await FlowPointAsync();
        await DragHandleAsync(onFlow[0], onFlow[1]);
        var association = Page.Locator(".joint-link[model-id^='Association_']");
        await Expect(association).ToHaveCountAsync(1);
        var id = await association.GetAttributeAsync("model-id");
        var middle = await association.Locator("[joint-selector='line']").EvaluateAsync<float[]>(
            "path => { const p = path.getPointAtLength(path.getTotalLength() / 2).matrixTransform(path.getScreenCTM()); return [p.x, p.y]; }");
        await Page.Mouse.ClickAsync(middle[0], middle[1]);
        var target = await Page.Locator("[model-id='A'] [joint-selector='body']").BoundingBoxAsync();
        await DragTargetAsync(target!.X + (target.Width / 2), target.Y + (target.Height / 2));
        Assert.Equal("A", Assert.Single(BpmnXmlReader.Read(await ExportAsync()).Document.Associations).TargetRef);
        middle = await association.Locator("[joint-selector='line']").EvaluateAsync<float[]>(
            "path => { const p = path.getPointAtLength(path.getTotalLength() / 2).matrixTransform(path.getScreenCTM()); return [p.x, p.y]; }");
        await Page.Mouse.ClickAsync(middle[0], middle[1]);
        onFlow = await FlowPointAsync(0.6);
        await DragTargetAsync(onFlow[0], onFlow[1]);
        var final = Assert.Single(BpmnXmlReader.Read(await ExportAsync()).Document.Associations);
        Assert.Equal("Flow", final.TargetRef);
        await Expect(Page.Locator($".joint-link[model-id='{id}']")).ToHaveCountAsync(1);
        await AssertAttachedAsync();
    }

    [Theory]
    [InlineData("A", "body")]
    [InlineData("Data", "body")]
    public async Task AnnotationCreatesAssociationToShapesIncludingData(string targetId, string selector)
    {
        await OpenAsync();
        await SelectAnnotationAsync();
        var target = await Page.Locator($"[model-id='{targetId}'] [joint-selector='{selector}']").BoundingBoxAsync();
        await DragHandleAsync(target!.X + (target.Width / 2), target.Y + (target.Height / 2));
        await Expect(Page.Locator(".joint-link[model-id^='Association_']")).ToHaveCountAsync(1);
        var document = BpmnXmlReader.Read(await ExportAsync()).Document;
        var association = Assert.Single(document.Associations);
        Assert.Equal("Note", association.SourceRef);
        Assert.Equal(targetId, association.TargetRef);
        Assert.Equal(BpmnAssociationDirection.None, association.Direction);
        Assert.Empty(document.DataAssociations);
    }

    [Fact]
    public async Task AnnotationRejectsSecondTargetAndFollowsShapeDuringDragWithoutDoubleMovement()
    {
        await OpenAsync();
        await SelectAnnotationAsync();
        var target = await Page.Locator("[model-id='A'] [joint-selector='body']").BoundingBoxAsync();
        await DragHandleAsync(target!.X + (target.Width / 2), target.Y + (target.Height / 2));
        await Expect(Page.Locator(".joint-link[model-id^='Association_']")).ToHaveCountAsync(1);
        await SelectAnnotationAsync();
        var other = await Page.Locator("[model-id='B'] [joint-selector='body']").BoundingBoxAsync();
        await DragHandleAsync(other!.X + (other.Width / 2), other.Y + (other.Height / 2));
        var initial = BpmnXmlReader.Read(await ExportAsync()).Document;
        Assert.Equal("A", Assert.Single(initial.Associations).TargetRef);

        var noteBefore = await Page.Locator("[model-id='Note'] [joint-selector='bracket']").BoundingBoxAsync();
        await Page.Mouse.MoveAsync(target.X + (target.Width / 2), target.Y + (target.Height / 2));
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(target.X + (target.Width / 2), target.Y + (target.Height / 2) + 50, new() { Steps = 15 });
        var taskDuring = await Page.Locator("[model-id='A'] [joint-selector='body']").BoundingBoxAsync();
        var noteDuring = await Page.Locator("[model-id='Note'] [joint-selector='bracket']").BoundingBoxAsync();
        Assert.InRange(Math.Abs(taskDuring!.Y - target.Y - (noteDuring!.Y - noteBefore!.Y)), 0, 1);
        await Page.Mouse.UpAsync();
        var updated = BpmnXmlReader.Read(await ExportAsync()).Document;
        var oldTarget = initial.Diagrams[0].Plane.Shapes.Single(shape => shape.BpmnElement == "A").Bounds;
        var newTarget = updated.Diagrams[0].Plane.Shapes.Single(shape => shape.BpmnElement == "A").Bounds;
        var oldNote = initial.Diagrams[0].Plane.Shapes.Single(shape => shape.BpmnElement == "Note").Bounds;
        var newNote = updated.Diagrams[0].Plane.Shapes.Single(shape => shape.BpmnElement == "Note").Bounds;
        Assert.True(newTarget.Y > oldTarget.Y);
        Assert.Equal(newTarget.Y - oldTarget.Y, newNote.Y - oldNote.Y, 6);
        Assert.Equal(2, updated.Diagrams[0].Plane.Edges.Single(edge => edge.BpmnElement == updated.Associations[0].Id).Waypoints.Count);
    }

    [Fact]
    public async Task AnnotationFollowsFlowSlidePreviewAndEscapeRestoresBoth()
    {
        await OpenAsync();
        await SelectAnnotationAsync();
        var anchor = await FlowPointAsync();
        await DragHandleAsync(anchor[0], anchor[1]);
        await Expect(Page.Locator(".joint-link[model-id^='Association_']")).ToHaveCountAsync(1);
        var flow = Page.Locator("[model-id='Flow'] [joint-selector='line']");
        var beforeRoute = await flow.GetAttributeAsync("d");
        var before = await Page.Locator("[model-id='Note'] [joint-selector='bracket']").BoundingBoxAsync();
        var press = await FlowPointAsync(0.25);

        await Page.Mouse.MoveAsync(press[0], press[1]);
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(press[0], press[1] + 40, new() { Steps = 15 });
        var during = await Page.Locator("[model-id='Note'] [joint-selector='bracket']").BoundingBoxAsync();
        Assert.True(during!.Y > before!.Y + 5);
        Assert.NotEqual(beforeRoute, await flow.GetAttributeAsync("d"));
        await Page.Keyboard.PressAsync("Escape");
        await Page.Mouse.UpAsync();

        await Expect(flow).ToHaveAttributeAsync("d", beforeRoute!);
        var restored = await Page.Locator("[model-id='Note'] [joint-selector='bracket']").BoundingBoxAsync();
        Assert.InRange(Math.Abs(restored!.Y - before.Y), 0, 0.1);
        Assert.Equal(new Bounds(280, 300, 100, 50), BpmnXmlReader.Read(await ExportAsync()).Document.Diagrams[0].Plane.Shapes.Single(shape => shape.BpmnElement == "Note").Bounds);
    }

    [Fact]
    public async Task ImportListsAnnotationLossesAndRequiresConsentBeforeNormalizing()
    {
        await OpenAsync();
        await SelectAnnotationAsync();
        var anchor = await FlowPointAsync();
        await DragHandleAsync(anchor[0], anchor[1]);
        await Expect(Page.Locator(".joint-link[model-id^='Association_']")).ToHaveCountAsync(1);
        var document = BpmnXmlReader.Read(await ExportAsync()).Document;
        var original = Assert.Single(document.Associations);
        document = document with
        {
            Associations = [.. document.Associations, original with { Id = "Extra", TargetRef = "B" }],
        };
        var xml = BpmnXmlWriter.Write(document).Xml;
        var transfer = await Page.EvaluateHandleAsync(
            """
            xml => {
              const transfer = new DataTransfer();
              transfer.items.add(new File([xml], 'annotations.bpmn', { type: 'application/xml' }));
              return transfer;
            }
            """, xml);
        await Page.Locator(".bpmn-canvas").DispatchEventAsync("drop", new Dictionary<string, object> { ["dataTransfer"] = transfer });
        await Expect(Page.Locator(".bpmn-import [role='alert']")).ToContainTextAsync("Extra");
        await Page.ImportAccept().ExpectDisabledAsync();
        // Fluent exposes its checkbox role through ElementInternals, which Playwright's role
        // selector does not inspect. Resolve the label and still verify the accessible name.
        var consent = Page.GetByLabel("I confirm these annotation changes.", new() { Exact = true });
        await Expect(consent).ToHaveAccessibleNameAsync("I confirm these annotation changes.");
        await consent.ClickAsync();
        await Page.ImportAccept().ClickAsync();
        await Expect(Page.Locator(".bpmn-import")).ToHaveCountAsync(0);

        var normalized = BpmnXmlReader.Read(await ExportAsync()).Document;
        Assert.Equal(original.Id, Assert.Single(normalized.Associations).Id);
        Assert.Equal("Flow", normalized.Associations[0].TargetRef);
    }

    private async Task OpenAsync(bool message = false, bool bent = false)
    {
        await Page.GotoAsync(app.Url(this));
        await Expect(Page.Locator(".bpmn-canvas__paper > svg")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        Assert.Equal(2, await Page.EvaluateAsync<int>("1 + 1"));
        var builder = new BpmnDocumentBuilder()
            .Node("A", BpmnFlowNodeType.Task, new Bounds(100, 100, 100, 80))
            .Node("B", BpmnFlowNodeType.Task, new Bounds(400, bent ? 200 : 100, 100, 80), containerId: message ? "Process_2" : "Process_1")
            .TextAnnotation("Note", "Explanation", new Bounds(280, 300, 100, 50))
            .DataObject("Data", "Data", new Bounds(500, 300, 36, 50));
        if (message)
        {
            builder.Process("Process_2")
                .Pool("Pool_1", "Sender", new Bounds(60, 60, 180, 220))
                .Pool("Pool_2", "Recipient", new Bounds(360, 60, 180, 260), processRef: "Process_2")
                .MessageFlow("Flow", "A", "B", FlowRoute(bent));
        }
        else
        {
            builder.Flow("Flow", "A", "B", FlowRoute(bent));
        }
        await ImportAsync(BpmnXmlWriter.Write(builder.Build()).Xml);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Fit" }).ClickAsync();
    }

    private static IReadOnlyList<DiPoint> FlowRoute(bool bent) => bent
        ? [new DiPoint(200, 140), new DiPoint(300, 140), new DiPoint(300, 240), new DiPoint(400, 240)]
        : [new DiPoint(200, 140), new DiPoint(400, 140)];

    private async Task ImportAsync(string xml)
    {
        var transfer = await Page.EvaluateHandleAsync(
            """
            xml => {
              const transfer = new DataTransfer();
              transfer.items.add(new File([xml], 'annotations.bpmn', { type: 'application/xml' }));
              return transfer;
            }
            """, xml);
        await Page.Locator(".bpmn-canvas").DispatchEventAsync("drop", new Dictionary<string, object> { ["dataTransfer"] = transfer });
        await Page.ImportAccept().ClickAsync();
        await Expect(Page.Locator(".bpmn-import")).ToHaveCountAsync(0);
        await Expect(Page.Locator(".joint-element[model-id='Note']")).ToBeVisibleAsync();
    }

    private async Task SelectAnnotationAsync()
    {
        var bracket = await Page.Locator("[model-id='Note'] [joint-selector='bracket']").BoundingBoxAsync();
        await Page.Mouse.ClickAsync(bracket!.X + 2, bracket.Y + (bracket.Height / 2));
        await Expect(Page.Locator(".bpmn-connect-handle")).ToBeVisibleAsync();
    }

    private async Task DragHandleAsync(float x, float y)
    {
        var handle = await Page.Locator(".bpmn-connect-handle").BoundingBoxAsync();
        await Page.Mouse.MoveAsync(handle!.X + (handle.Width / 2), handle.Y + (handle.Height / 2));
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(handle.X - 20, handle.Y - 20, new() { Steps = 3 });
        await Page.Mouse.MoveAsync(x, y, new() { Steps = 15 });
        await Page.Mouse.UpAsync();
    }

    private async Task DragTargetAsync(float x, float y)
    {
        var handle = await Page.Locator(".joint-tool.target-arrowhead").BoundingBoxAsync();
        await Page.Mouse.MoveAsync(handle!.X + (handle.Width / 2), handle.Y + (handle.Height / 2));
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(handle.X + 15, handle.Y - 15, new() { Steps = 3 });
        await Page.Mouse.MoveAsync(x, y, new() { Steps = 15 });
        await Page.Mouse.UpAsync();
        await Page.WaitForTimeoutAsync(500);
    }

    private Task<float[]> FlowPointAsync(double ratio = 0.5) =>
        Page.Locator("[model-id='Flow'] [joint-selector='line']").EvaluateAsync<float[]>(
            "(path, ratio) => { const p = path.getPointAtLength(path.getTotalLength() * ratio).matrixTransform(path.getScreenCTM()); return [p.x, p.y]; }", ratio);

    private async Task AssertAttachedAsync()
    {
        var distance = await Page.EvaluateAsync<double>(
            """
            () => {
              const association = document.querySelector('.joint-link[model-id^="Association_"] [joint-selector="line"]');
              const flow = document.querySelector('[model-id="Flow"] [joint-selector="line"]');
              const end = association.getPointAtLength(association.getTotalLength()).matrixTransform(association.getScreenCTM());
              const length = flow.getTotalLength();
              let nearest = Infinity;
              for (let at = 0; at <= length; at += 0.25) {
                const p = flow.getPointAtLength(at).matrixTransform(flow.getScreenCTM());
                nearest = Math.min(nearest, Math.hypot(p.x - end.x, p.y - end.y));
              }
              return nearest;
            }
            """);
        Assert.InRange(distance, 0, 1);
    }

    private async Task<string> ExportAsync()
    {
        await Page.OpenDemoFilesAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Export XML", Exact = true }).ClickAsync();
        var xml = await Page.Locator(".demo-export-xml").TextContentAsync() ?? throw new InvalidOperationException("XML export is empty.");
        if (await Page.Locator(".demo-export-xml").IsVisibleAsync())
        {
            await Page.Locator(".demo-exported > summary").DispatchEventAsync("click");
        }
        await Expect(Page.Locator(".demo-export-xml")).ToBeHiddenAsync();
        return xml;
    }
}
