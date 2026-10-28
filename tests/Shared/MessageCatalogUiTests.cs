using Bunit;
using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Interop;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Components;

public sealed class MessageCatalogUiTests : BunitContext
{
    public MessageCatalogUiTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        KitFixture.Register(Services);
        var module = JSInterop.SetupModule(BpmnInteropContract.ModulePath);
        var canvas = module.SetupModule("initialize", _ => true);
        canvas.Setup<CanvasInitResult>("getInitResult", _ => true).SetResult(new("messages", BpmnInteropContract.Version, 1, true, null));
        canvas.Setup<DocumentLoaded?>("loadDocument", _ => true).SetResult(new("Diagram_1", 2, 0, []));
    }

    private IRenderedComponent<DynamicComponent> Editor(bool readOnly = false, BpmnDocument? document = null)
    {
        document ??= new BpmnDocumentBuilder()
            .Node("Send", BpmnFlowNodeType.SendTask, new Bounds(100, 100, 100, 80))
            .Node("Receive", BpmnFlowNodeType.ReceiveTask, new Bounds(300, 100, 100, 80))
            .Build();
        return Render<DynamicComponent>(p => p.Add(c => c.Type, KitFixture.Composition).Add(c => c.Parameters,
            new Dictionary<string, object> { ["Document"] = document, ["IsReadOnly"] = readOnly, ["ShowPanel"] = true }));
    }

    private static BpmnEditorSession Session(IRenderedComponent<DynamicComponent> editor) =>
        editor.FindComponent<BpmnEditorScope>().Instance.Session;

    [Theory]
    [InlineData("Send")]
    [InlineData("Receive")]
    public async Task EmptyPickerCreatesAndAssignsMessageThenSupportsReuse(string owner)
    {
        var editor = Editor();
        var session = Session(editor);
        await editor.InvokeAsync(() => session.SetSelectionAsync([owner]));
        Assert.Equal("New", editor.Find(".bpmn-message-reference [data-message-action=new]").TextContent.Trim());
        Assert.Equal("Edit Shared", editor.Find(".bpmn-message-reference [data-message-action=edit]").TextContent.Trim());
        Assert.Equal("Manage", editor.Find(".bpmn-message-reference [data-message-action=manage]").TextContent.Trim());
        Assert.Contains("No messages defined", editor.Markup, StringComparison.Ordinal);
        editor.Find(".bpmn-message-reference [data-message-action=new]").Click();
        Assert.True(session.State.MessageCatalog.Draft!.IsNew);
        await KitFixture.ChangeMessageNameAsync(editor, "Order");
        editor.Find(".bpmn-message-catalog form").Submit();
        editor.WaitForAssertion(() => Assert.Single(session.EditedDocument!.Catalog.Messages));
        var message = session.EditedDocument!.Catalog.Messages[0];
        Assert.Equal("Order", message.Name);
        Assert.Equal(message.Id, session.EditedDocument.FindFlowNode(owner)!.MessageRef);
        Assert.Equal([owner], session.State.Selection);
        Assert.False(session.State.MessageCatalog.IsOpen);
        await editor.InvokeAsync(session.UndoAsync);
        Assert.Empty(session.EditedDocument.Catalog.Messages);
        await editor.InvokeAsync(session.RedoAsync);
        Assert.Equal(message.Id, session.EditedDocument.Catalog.Messages[0].Id);
        var other = owner == "Send" ? "Receive" : "Send";
        await editor.InvokeAsync(() => session.SetSelectionAsync([other]));
        await KitFixture.SelectMessageAsync(editor, message.Id);
        Assert.All(session.EditedDocument.FlowNodes, node => Assert.Equal(message.Id, node.MessageRef));
    }

    [Fact]
    public async Task ToolbarCatalogCreatesUnassignedMessageAndDeletesAfterConfirmation()
    {
        var editor = Editor();
        var session = Session(editor);
        editor.Find(".bpmn-toolbar [data-message-action=manage]").Click();
        editor.Find(".bpmn-message-catalog [data-message-action=new]").Click();
        Assert.Equal("off", editor.Find("[data-message-name]").GetAttribute("autocomplete"));
        await KitFixture.ChangeMessageNameAsync(editor, "");
        editor.Find(".bpmn-message-catalog form").Submit();
        editor.WaitForAssertion(() => Assert.Single(session.EditedDocument!.Catalog.Messages));
        Assert.All(session.EditedDocument!.FlowNodes, node => Assert.Null(node.MessageRef));
        Assert.False(editor.Find(".bpmn-message-catalog [data-message-heading]").HasAttribute("aria-pressed"));
        Assert.Single(editor.FindAll(".bpmn-message-catalog [data-message-id] > [data-message-details]"));
        Assert.Single(editor.FindAll(".bpmn-message-catalog [data-message-details]"));
        Assert.Contains("References: 0", editor.Find("[data-message-details]").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Deletion blocked:", editor.Find("[data-message-details]").TextContent, StringComparison.Ordinal);
        Assert.Empty(editor.FindAll("[data-message-details] details"));
        Assert.Single(editor.FindAll(".bpmn-message-catalog [data-message-action=edit]"));
        editor.Find(".bpmn-message-catalog [data-message-action=delete]").Click();
        Assert.Contains("References: 0", editor.Find(".bpmn-message-catalog").TextContent, StringComparison.Ordinal);
        Assert.Empty(editor.FindAll(".bpmn-message-catalog details"));
        Assert.Single(session.EditedDocument.Catalog.Messages);
        editor.Find(".bpmn-message-catalog [data-message-action=confirm-delete]").Click();
        editor.WaitForAssertion(() => Assert.Empty(session.EditedDocument.Catalog.Messages));
    }

    [Fact]
    public async Task MessageFlowUsesSameAtomicCreateAndAssignControl()
    {
        var document = new BpmnDocumentBuilder()
            .Pool("Sender", "Sender", new Bounds(0, 0, 500, 200))
            .BlackBoxPool("Receiver", "Receiver", new Bounds(0, 300, 500, 100))
            .MessageFlow("Flow", "Sender", "Receiver", [new DiPoint(250, 200), new DiPoint(250, 300)])
            .Build();
        var editor = Editor(document: document);
        var session = Session(editor);
        await editor.InvokeAsync(() => session.SetSelectionAsync(["Flow"]));
        editor.Find(".bpmn-message-reference [data-message-action=new]").Click();
        await KitFixture.ChangeMessageNameAsync(editor, "Transfer");
        editor.Find(".bpmn-message-catalog form").Submit();
        editor.WaitForAssertion(() => Assert.Single(session.EditedDocument!.Catalog.Messages));
        Assert.Equal(session.EditedDocument!.Catalog.Messages[0].Id, session.EditedDocument.MessageFlows[0].MessageRef);
        await editor.InvokeAsync(session.UndoAsync);
        Assert.Empty(session.EditedDocument.Catalog.Messages);
        Assert.Null(session.EditedDocument.MessageFlows[0].MessageRef);
    }

    [Fact]
    public async Task SharedNameEditWarnsAndUsedMessageCannotBeDeleted()
    {
        var editor = Editor();
        var session = Session(editor);
        await editor.InvokeAsync(() => session.NewMessage("Send"));
        await editor.InvokeAsync(() => session.SaveMessageAsync("Order"));
        var id = session.EditedDocument!.Catalog.Messages[0].Id;
        await editor.InvokeAsync(() => session.SetSelectionAsync(["Send"]));
        editor.Find(".bpmn-message-reference [data-message-action=edit]").Click();
        Assert.Contains("This definition is shared", editor.Markup, StringComparison.Ordinal);
        Assert.Contains("References: 1", editor.Find(".bpmn-message-catalog form").TextContent, StringComparison.Ordinal);
        Assert.Equal("Referenced messages cannot be deleted. Clear their references first.",
            editor.Find(".bpmn-message-catalog details > summary").TextContent);
        Assert.Equal("Send: messageRef", editor.Find(".bpmn-message-catalog details li").TextContent);
        await KitFixture.ChangeMessageNameAsync(editor, "Renamed");
        editor.Find(".bpmn-message-catalog form").Submit();
        editor.WaitForAssertion(() => Assert.Equal("Renamed", session.EditedDocument.Catalog.Messages[0].Name));
        editor.Find(".bpmn-toolbar [data-message-action=manage]").Click();
        Assert.True(editor.Find(".bpmn-message-catalog [data-message-action=delete]").HasAttribute("disabled"));
        Assert.Contains(id, editor.Find(".bpmn-message-catalog").TextContent, StringComparison.Ordinal);
        Assert.Contains("References: 1", editor.Find("[data-message-details]").TextContent, StringComparison.Ordinal);
        Assert.Equal("Send: messageRef", editor.Find("[data-message-details] details li").TextContent);
    }

    [Fact]
    public void RetainedOpaqueContentShowsUncertainDeletionWarningWithoutKnownUsages()
    {
        var document = Serialization.BpmnXmlReader.Read("""
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                         xmlns:vendor="urn:vendor"
                         id="Definitions_1" targetNamespace="urn:test">
              <message id="Message_1" />
              <process id="Process_1">
                <task id="Task_1">
                  <extensionElements><vendor:correlationKey value="opaque" /></extensionElements>
                </task>
              </process>
            </definitions>
            """).Document;
        var editor = Editor(document: document);
        editor.Find(".bpmn-toolbar [data-message-action=manage]").Click();

        var details = editor.Find("[data-message-details]");
        Assert.Contains("References: 0", details.TextContent, StringComparison.Ordinal);
        Assert.Contains("Deletion blocked: retained content may contain references this editor cannot safely inspect.",
            details.TextContent, StringComparison.Ordinal);
        Assert.Empty(editor.FindAll("[data-message-details] details"));
        Assert.True(editor.Find("[data-message-details] [data-message-action=delete]").HasAttribute("disabled"));
    }

    [Fact]
    public void ReadOnlyCatalogRemainsInspectableWithoutMutationActions()
    {
        var document = Serialization.BpmnXmlReader.Read("""
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                         id="Definitions_1" targetNamespace="urn:test">
              <message id="Message_1" name="Order" />
              <process id="Process_1" />
            </definitions>
            """).Document;
        var editor = Editor(readOnly: true, document: document);
        editor.Find(".bpmn-toolbar [data-message-action=manage]").Click();
        Assert.True(Session(editor).State.MessageCatalog.IsOpen);
        Assert.Contains("References: 0", editor.Find("[data-message-details]").TextContent, StringComparison.Ordinal);
        Assert.Empty(editor.FindAll(".bpmn-message-catalog [data-message-action=new]"));
        Assert.Empty(editor.FindAll(".bpmn-message-catalog [data-message-action=save]"));
        Assert.Empty(editor.FindAll(".bpmn-message-catalog [data-message-action=edit]"));
        Assert.Empty(editor.FindAll(".bpmn-message-catalog [data-message-action=delete]"));
    }

    [Fact]
    public async Task CatalogRowsOwnTheirReferencesAndActionsWithoutSelection()
    {
        var editor = Editor();
        var session = Session(editor);
        await editor.InvokeAsync(() => session.NewMessage("Send"));
        await editor.InvokeAsync(() => session.SaveMessageAsync("Used"));
        await editor.InvokeAsync(() => session.NewMessage());
        await editor.InvokeAsync(() => session.SaveMessageAsync("Unused"));
        editor.Find(".bpmn-toolbar [data-message-action=manage]").Click();
        var rows = editor.FindAll(".bpmn-message-catalog__list > li");
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row =>
        {
            Assert.Equal(row.GetAttribute("data-message-id"),
                row.QuerySelector("[data-message-details]")!.GetAttribute("data-message-details"));
            var heading = row.QuerySelector("[data-message-heading]")!;
            Assert.Equal("0", heading.GetAttribute("tabindex"));
            Assert.False(heading.HasAttribute("aria-pressed"));
            Assert.False(heading.HasAttribute("role"));
            Assert.Equal(2, row.QuerySelectorAll(".bpmn-message-catalog__actions > [data-message-action]").Length);
        });
        Assert.Contains("References: 1", rows[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("Send: messageRef", rows[0].TextContent, StringComparison.Ordinal);
        Assert.True(rows[0].QuerySelector("[data-message-action=delete]")!.HasAttribute("disabled"));
        Assert.Contains("References: 0", rows[1].TextContent, StringComparison.Ordinal);
        Assert.False(rows[1].QuerySelector("[data-message-action=delete]")!.HasAttribute("disabled"));

        if (KitFixture.Composition.Name.StartsWith("Fluent", StringComparison.Ordinal))
        {
            Assert.All(rows, row =>
            {
                var edit = row.QuerySelector("[data-message-action=edit]")!;
                var delete = row.QuerySelector("[data-message-action=delete]")!;
                Assert.Equal("Edit name", edit.GetAttribute("aria-label"));
                Assert.Equal("Delete", delete.GetAttribute("aria-label"));
                Assert.NotNull(edit.QuerySelector("svg"));
                Assert.NotNull(delete.QuerySelector("svg"));
                Assert.Equal("", edit.TextContent.Trim());
                Assert.Equal("", delete.TextContent.Trim());
            });
        }

        editor.FindAll(".bpmn-message-catalog [data-message-action=edit]")[1].Click();
        Assert.Equal(session.EditedDocument!.Catalog.Messages[1].Id, session.State.MessageCatalog.Draft!.Id);
    }

    [Fact]
    public async Task MultiSelectionOffersNoInlineCreateAction()
    {
        var editor = Editor();
        await editor.InvokeAsync(() => Session(editor).SetSelectionAsync(["Send", "Receive"]));
        Assert.Empty(editor.FindAll(".bpmn-message-reference [data-message-action=new]"));
    }

    [Fact]
    public async Task RemovingCatalogPartRemovesInlineActions()
    {
        Services.Configure<BpmnOptions>(options => options.Components.Remove(BpmnEditorPart.MessageCatalog));
        var editor = Editor();
        await editor.InvokeAsync(() => Session(editor).SetSelectionAsync(["Send"]));
        Assert.Empty(editor.FindAll(".bpmn-message-reference [data-message-action]"));
        Assert.True(editor.Find(".bpmn-toolbar [data-message-action=manage]").HasAttribute("disabled"));
    }

    [Fact]
    public void HostCanReplaceCatalogPart()
    {
        Services.Configure<BpmnOptions>(options => options.Components.Use<CustomCatalog>(BpmnEditorPart.MessageCatalog));
        var editor = Editor();
        editor.Find(".bpmn-toolbar [data-message-action=manage]").Click();
        Assert.Contains("custom-message-catalog", editor.Markup, StringComparison.Ordinal);
        Assert.Empty(editor.FindAll(".bpmn-message-catalog"));
    }

    [Fact]
    public void KeyedKitDefaultsStillYieldToHostKindPredicateAndKeyEditors()
    {
        var entry = new BpmnPropertyEntry("messageRef", "Message", BpmnPropertyEntryKind.Select) { EditorKey = "bpmn:messageReference" };
        var kit = new BpmnPropertyEditorRegistry().UseDefaultEditor<CustomCatalog>("bpmn:messageReference");
        var host = new BpmnPropertyEditorRegistry().AddEditor<DynamicComponent>(BpmnPropertyEntryKind.Select);
        Assert.Equal(typeof(DynamicComponent), host.WithKitDefaults(kit).Resolve(entry));
        host.AddEditor<BpmnReadOnlyEntry>(property => property.Id == "messageRef");
        Assert.Equal(typeof(BpmnReadOnlyEntry), host.WithKitDefaults(kit).Resolve(entry));
        host.AddEditor<BpmnPart>("bpmn:messageReference");
        Assert.Equal(typeof(BpmnPart), host.WithKitDefaults(kit).Resolve(entry));
        Assert.Equal(typeof(CustomCatalog), new BpmnPropertyEditorRegistry().WithKitDefaults(kit).Resolve(entry));
    }

    public sealed class CustomCatalog : BpmnMessageCatalogBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            if (Catalog.IsOpen) builder.AddContent(0, "custom-message-catalog");
        }
    }
}
