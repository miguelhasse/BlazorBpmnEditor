using Hasseware.AspNetCore.Components.Bpmn.E2E.Tests.Fixtures;
using Microsoft.Playwright.Xunit.v3;

namespace Hasseware.AspNetCore.Components.Bpmn.E2E.Tests;

/// <summary>
/// Dropping a BPMN file on the canvas (implementation plan M6).
/// </summary>
/// <remarks>
/// <para>
/// The real gesture, synthesised: a <c>DataTransfer</c> carrying a file, dispatched at the canvas.
/// That exercises the path the library actually owns — the canvas reads the file, the editor offers
/// it, and nothing changes until the offer is accepted.
/// </para>
/// <para>
/// The exit criterion is about declining: a preview must be rejectable "without corrupting the
/// current graph", and an accidental drop is exactly how someone ends up declining one.
/// </para>
/// </remarks>
public class ImportTests(DemoAppFixture app) : PageTest, IClassFixture<DemoAppFixture>
{
    private const string Imported = """
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                          xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                          id="Definitions_Imported" targetNamespace="http://example.org">
          <bpmn:process id="Process_Imported">
            <bpmn:task id="Task_Imported" name="Imported task" />
          </bpmn:process>
          <bpmndi:BPMNDiagram id="Diagram_Imported">
            <bpmndi:BPMNPlane id="Plane_Imported" bpmnElement="Process_Imported">
              <bpmndi:BPMNShape id="Task_Imported_di" bpmnElement="Task_Imported">
                <dc:Bounds x="160" y="120" width="100" height="80" />
              </bpmndi:BPMNShape>
            </bpmndi:BPMNPlane>
          </bpmndi:BPMNDiagram>
        </bpmn:definitions>
        """;

    [Fact]
    public async Task DroppingAFileOffersItWithoutOpeningIt()
    {
        await GoToEditorAsync();
        var before = await Page.Locator(".bpmn-canvas .joint-cell").CountAsync();

        await DropFileAsync("imported.bpmn", Imported);

        await Expect(Page.Locator(".bpmn-import__title")).ToContainTextAsync("imported.bpmn");

        // Still the document that was there: an offer is a question, not an action.
        await Expect(Page.Locator(".bpmn-canvas .joint-cell")).ToHaveCountAsync(before);
        await Expect(Page.Locator(".joint-cell[model-id='Task_Imported']")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task DecliningLeavesTheDocumentAlone()
    {
        await GoToEditorAsync();
        var before = await Page.Locator(".bpmn-canvas .joint-cell").CountAsync();

        await DropFileAsync("imported.bpmn", Imported);
        await Page.Locator(".bpmn-import__cancel").ClickAsync();
        await Page.WaitForTimeoutAsync(900);

        await Expect(Page.Locator(".bpmn-import")).ToHaveCountAsync(0);
        await Expect(Page.Locator(".bpmn-canvas .joint-cell")).ToHaveCountAsync(before);
    }

    [Fact]
    public async Task FluentImportEscapeCancelsAndLeavesEditorInteractiveAcrossRepeatedOffers()
    {
        await Page.GotoAsync($"{app.BaseUrl}{KitRoute.Fluent}");
        await Expect(Page.Locator(".bpmn-canvas .joint-cell").First).ToBeVisibleAsync(new() { Timeout = 30_000 });
        var task = Page.Locator(".joint-cell[model-id^='Task_']").First;
        var id = await task.GetAttributeAsync("model-id");
        Assert.NotNull(id);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            await Page.Locator(".bpmn-canvas").ClickAsync(new() { Position = new() { X = 4, Y = 4 } });
            await DropFileAsync("imported.bpmn", Imported);
            await Expect(Page.Locator(".bpmn-import__title")).ToBeVisibleAsync();
            await Page.Locator("fluent-dialog.bpmn-import").ExpectSingleFluentSurfaceAsync();
            await Page.Keyboard.PressAsync("Escape");

            await Expect(Page.Locator(".bpmn-import")).ToHaveCountAsync(0);
            await Expect(Page.Locator(".bpmn-canvas")).ToBeFocusedAsync();
            await Expect(Page.Locator(".joint-cell[model-id='Task_Imported']")).ToHaveCountAsync(0);
            await task.ClickAsync();
            await Expect(Page.Locator(".demo-edit-status")).ToContainTextAsync(id);
        }
    }

    [Fact]
    public async Task AcceptingReplacesTheDocument()
    {
        await GoToEditorAsync();

        await DropFileAsync("imported.bpmn", Imported);
        await Page.ImportAccept().ClickAsync();
        await Page.WaitForTimeoutAsync(1800);

        await Expect(Page.Locator(".joint-cell[model-id='Task_Imported']")).ToHaveCountAsync(1);
        await Expect(Page.Locator(".bpmn-import")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task AFileThatIsNotBpmnSaysSoAndCannotBeOpened()
    {
        // The accidental drop. It has to be survivable, and it has to explain itself.
        await GoToEditorAsync();
        var before = await Page.Locator(".bpmn-canvas .joint-cell").CountAsync();

        await DropFileAsync("notes.bpmn", "this is not xml at all");

        await Expect(Page.Locator(".bpmn-import__error")).ToBeVisibleAsync();
        await Page.ImportAccept().ExpectDisabledAsync();

        await Page.Locator(".bpmn-import__cancel").ClickAsync();
        await Page.WaitForTimeoutAsync(900);

        await Expect(Page.Locator(".bpmn-canvas .joint-cell")).ToHaveCountAsync(before);
    }

    [Fact]
    public async Task TheOfferSaysHowMuchIsInTheFile()
    {
        await GoToEditorAsync();
        await DropFileAsync("imported.bpmn", Imported);

        await Expect(Page.Locator(".bpmn-import__facts")).ToContainTextAsync("1 flow nodes");
        await Expect(Page.Locator(".bpmn-import__note")).ToContainTextAsync("replaces");
    }

    // --- helpers ------------------------------------------------------------------

    /// <summary>
    /// Drops a file on the canvas the way a browser does.
    /// </summary>
    /// <remarks>
    /// Playwright cannot drag a file in from the desktop, so the <c>DataTransfer</c> is built in
    /// the page and dispatched at the canvas root. Everything after that — reading it, offering
    /// it — is the library's own code path.
    /// </remarks>
    private async Task DropFileAsync(string fileName, string xml)
    {
        var handle = await Page.EvaluateHandleAsync(
            """
            ([name, text]) => {
              const transfer = new DataTransfer();
              transfer.items.add(new File([text], name, { type: 'application/xml' }));
              return transfer;
            }
            """,
            new[] { fileName, xml });

        await Page.Locator(".bpmn-canvas").First.DispatchEventAsync("drop", new Dictionary<string, object>
        {
            ["dataTransfer"] = handle,
        });

        await Page.WaitForTimeoutAsync(1400);
    }

    private async Task GoToEditorAsync()
    {
        await Page.GotoAsync(app.Url(this));

        await Expect(Page.Locator(".bpmn-canvas__paper > svg")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.Locator(".bpmn-canvas .joint-cell").First).ToBeVisibleAsync();
    }
}
