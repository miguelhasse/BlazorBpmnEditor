using Hasseware.AspNetCore.Components.Bpmn.E2E.Tests.Fixtures;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;

using Microsoft.Playwright;
using Microsoft.Playwright.Xunit.v3;

namespace Hasseware.AspNetCore.Components.Bpmn.E2E.Tests;

public class DataReferenceConnectionTests(DemoAppFixture app) : PageTest, IClassFixture<DemoAppFixture>
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HandleStaysBesideTheShapeIgnoringItsExternalLabel(bool store)
    {
        await Page.GotoAsync(app.Url(this));
        await Expect(Page.Locator(".bpmn-canvas__paper > svg")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        Assert.Equal(2, await Page.EvaluateAsync<int>("1 + 1"));

        var builder = new BpmnDocumentBuilder()
            .Node("Task", BpmnFlowNodeType.Task, new Bounds(100, 100, 100, 80));
        var bounds = new Bounds(300, 100, 36, 50);
        const string Label = "External data reference\nwith a long second line";
        var document = (store
            ? builder.DataStore("Data", Label, bounds)
            : builder.DataObject("Data", Label, bounds)).Build();
        var transfer = await Page.EvaluateHandleAsync(
            """
            xml => {
              const transfer = new DataTransfer();
              transfer.items.add(new File([xml], 'data-reference.bpmn', { type: 'application/xml' }));
              return transfer;
            }
            """, BpmnXmlWriter.Write(document).Xml);
        await Page.Locator(".bpmn-canvas").DispatchEventAsync("drop", new Dictionary<string, object> { ["dataTransfer"] = transfer });
        await Page.ImportAccept().ClickAsync();
        await Expect(Page.Locator(".bpmn-import")).ToHaveCountAsync(0);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Fit" }).ClickAsync();
        await Page.Locator(".joint-element[model-id='Data'] [joint-selector='body']").ClickAsync();
        await Expect(Page.Locator(".bpmn-connect-handle")).ToBeVisibleAsync();
        await AssertPlacementAsync();

        var scale = await Page.Locator(".bpmn-connect-handle").EvaluateAsync<double>("circle => circle.getScreenCTM().a");
        await Page.Locator(".bpmn-canvas").FocusAsync();
        await Page.Keyboard.PressAsync("+");
        await Expect(Page.Locator(".bpmn-connect-handle")).ToBeVisibleAsync();
        await Page.WaitForFunctionAsync(
            "before => document.querySelector('.bpmn-connect-handle').getScreenCTM().a !== before", scale);
        await AssertPlacementAsync();
    }

    private async Task AssertPlacementAsync()
    {
        var placement = await Page.Locator(".bpmn-connect-handle").EvaluateAsync<double[]>(
            """
            circle => {
              const body = document.querySelector(".joint-element[model-id='Data'] [joint-selector='body']");
              const box = body.getBBox();
              const expected = new DOMPoint(box.x + box.width + 12, box.y + box.height / 2)
                .matrixTransform(body.getScreenCTM());
              const actual = new DOMPoint(circle.cx.baseVal.value, circle.cy.baseVal.value)
                .matrixTransform(circle.getScreenCTM());
              return [actual.x - expected.x, actual.y - expected.y, circle.r.baseVal.value];
            }
            """);
        Assert.InRange(Math.Abs(placement[0]), 0, 0.1);
        Assert.InRange(Math.Abs(placement[1]), 0, 0.1);
        Assert.Equal(7, placement[2]);
    }
}
