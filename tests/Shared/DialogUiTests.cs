using Bunit;
using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Interop;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Microsoft.AspNetCore.Components;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Components;

public sealed class DialogUiTests : BunitContext
{
    public DialogUiTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        KitFixture.Register(Services);
        var canvas = JSInterop.SetupModule(BpmnInteropContract.ModulePath).SetupModule("initialize", _ => true);
        canvas.Setup<CanvasInitResult>("getInitResult", _ => true).SetResult(new("dialogs", BpmnInteropContract.Version, 1, true, null));
        canvas.Setup<DropTarget?>("resolveDrop", _ => true).SetResult(new(new Bounds(300, 300, 100, 80), null, null));
    }

    [Theory]
    [InlineData(BpmnEditorPart.MessageCatalog)]
    [InlineData(BpmnEditorPart.ShortcutHelp)]
    [InlineData(BpmnEditorPart.CalledElementPicker)]
    [InlineData(BpmnEditorPart.ImportOffer)]
    [InlineData(BpmnEditorPart.AgentConfirmation)]
    public async Task OneHeaderCloseSafelyDismissesEveryModal(BpmnEditorPart part)
    {
        var document = new BpmnDocumentBuilder().Node("Task", BpmnFlowNodeType.Task, new Bounds(100, 100, 100, 80)).Build();
        var editor = Render<DynamicComponent>(parameters => parameters.Add(p => p.Type, KitFixture.Composition)
            .Add(p => p.Parameters, new Dictionary<string, object> { ["Document"] = document }));
        var session = editor.FindComponent<BpmnEditorScope>().Instance.Session;
        Task<bool>? confirmation = null;
        await editor.InvokeAsync(async () =>
        {
            switch (part)
            {
                case BpmnEditorPart.MessageCatalog:
                    session.NewMessage();
                    break;
                case BpmnEditorPart.ShortcutHelp:
                    session.ShowShortcutHelp();
                    break;
                case BpmnEditorPart.CalledElementPicker:
                    await session.CreateAtAsync(new(BpmnFlowNodeType.CallActivity, "Call", Kind: BpmnStencilKind.CallActivity), 300, 300);
                    break;
                case BpmnEditorPart.ImportOffer:
                    session.PreviewImport("other.bpmn", """
                        <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" targetNamespace="urn:test" id="Definitions">
                          <process id="Other"><task id="OtherTask" /></process>
                        </definitions>
                        """);
                    break;
                case BpmnEditorPart.AgentConfirmation:
                    confirmation = session.RequestConfirmationAsync(new("Delete task?", "Remove Task", BpmnEditOrigin.FromAgent("test"), []),
                        Xunit.TestContext.Current.CancellationToken);
                    break;
            }
        });
        var dialog = editor.Find(".bpmn-dialog");
        Assert.Single(dialog.QuerySelectorAll(".bpmn-dialog__close"));
        Assert.Empty(dialog.QuerySelectorAll("span[tabindex]"));
        Assert.Single(dialog.QuerySelectorAll("h2"));
        var actions = dialog.QuerySelectorAll("button, fluent-button");
        Assert.Single(actions, button => button.TextContent.Trim() == "Close");
        switch (part)
        {
            case BpmnEditorPart.MessageCatalog:
                var save = dialog.QuerySelector("[data-message-action=save]")!;
                Assert.Equal(dialog.QuerySelector("form")!.Id, save.GetAttribute("form"));
                break;
            case BpmnEditorPart.ImportOffer:
                Assert.NotNull(dialog.QuerySelector(".bpmn-import__accept"));
                Assert.NotNull(dialog.QuerySelector(".bpmn-import__cancel"));
                break;
            case BpmnEditorPart.AgentConfirmation:
                Assert.NotNull(dialog.QuerySelector(".bpmn-confirm__deny"));
                Assert.NotNull(dialog.QuerySelector(".bpmn-confirm__allow"));
                break;
        }
        dialog.QuerySelector(".bpmn-dialog__close")!.Click();
        editor.WaitForAssertion(() => Assert.Empty(editor.FindAll(".bpmn-dialog")));
        Assert.False(session.State.MessageCatalog.IsOpen);
        Assert.False(session.State.ShortcutHelpVisible);
        Assert.False(session.IsPickingCalledElement);
        Assert.Null(session.PendingImport);
        Assert.Equal(document, session.EditedDocument);
        if (confirmation is not null) Assert.False(await confirmation);
    }
}
