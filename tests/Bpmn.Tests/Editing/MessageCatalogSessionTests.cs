using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Microsoft.Extensions.DependencyInjection;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Editing;

public sealed class MessageCatalogSessionTests
{
    private static BpmnDocument Document() => new BpmnDocumentBuilder()
        .Node("Send", BpmnFlowNodeType.SendTask, new Bounds(200, 100, 100, 80))
        .Node("Receive", BpmnFlowNodeType.ReceiveTask, new Bounds(400, 100, 100, 80))
        .Build();

    private static BpmnEditorSession Session(bool readOnly = false)
    {
        var session = new BpmnEditorSession(new BpmnOptions(), new ServiceCollection().BuildServiceProvider())
        {
            Document = Document(),
            IsReadOnly = readOnly,
        };
        session.UpdateParameters(EditorConfig.CreateDefault() with { IsReadOnly = readOnly });
        return session;
    }

    [Theory]
    [InlineData("Send")]
    [InlineData("Receive")]
    public async Task CreatesAndAssignsAsOneHistoryStep(string owner)
    {
        var session = Session();
        await session.SetSelectionAsync([owner]);
        session.NewMessage(owner);
        var id = session.State.MessageCatalog.Draft!.Id;
        Assert.Empty(session.EditedDocument!.Catalog.Messages);
        await session.SaveMessageAsync("Order");
        Assert.Equal(id, Assert.Single(session.EditedDocument.Catalog.Messages).Id);
        Assert.Equal(id, session.EditedDocument.FindFlowNode(owner)!.MessageRef);
        Assert.Equal([owner], session.State.Selection);
        Assert.False(session.State.MessageCatalog.IsOpen);
        await session.UndoAsync();
        Assert.Empty(session.EditedDocument.Catalog.Messages);
        Assert.Null(session.EditedDocument.FindFlowNode(owner)!.MessageRef);
        await session.RedoAsync();
        Assert.Equal(id, session.EditedDocument.FindFlowNode(owner)!.MessageRef);
        Assert.Equal(id, Assert.Single(session.EditedDocument.Catalog.Messages).Id);
    }

    [Fact]
    public async Task CatalogCreationDoesNotAssignToSelectionAndBlankNamesStayLegal()
    {
        var session = Session();
        await session.SetSelectionAsync(["Send"]);
        session.ShowMessages();
        session.NewMessage();
        await session.SaveMessageAsync("");
        Assert.Single(session.EditedDocument!.Catalog.Messages);
        Assert.Null(session.EditedDocument.FindFlowNode("Send")!.MessageRef);
        Assert.True(session.State.MessageCatalog.IsOpen);
        Assert.Null(session.State.MessageCatalog.Draft);
    }

    [Fact]
    public void CancelInlineAndCatalogDraftsDoNotCreateDefinitions()
    {
        var session = Session();
        session.NewMessage("Send");
        session.CancelMessageEdit();
        Assert.False(session.State.MessageCatalog.IsOpen);
        session.ShowMessages();
        session.NewMessage();
        session.CancelMessageEdit();
        Assert.True(session.State.MessageCatalog.IsOpen);
        Assert.Null(session.State.MessageCatalog.Draft);
        Assert.Empty(session.EditedDocument!.Catalog.Messages);
        Assert.False(session.State.CanUndo);
    }

    [Fact]
    public async Task InvalidAssignmentRollsBackCreationAndKeepsDraftWithError()
    {
        var session = Session();
        session.NewMessage("Missing");
        await session.SaveMessageAsync("Order");
        Assert.Empty(session.EditedDocument!.Catalog.Messages);
        Assert.NotNull(session.State.MessageCatalog.Error);
        Assert.NotNull(session.State.MessageCatalog.Draft);
    }

    [Fact]
    public async Task DocumentReplacementDiscardsPendingEdits()
    {
        var session = Session();
        session.NewMessage("Send");
        session.Document = Document();
        session.UpdateParameters(session.State.Configuration);
        Assert.False(session.State.MessageCatalog.IsOpen);
        await session.SaveMessageAsync("Stale");
        Assert.Empty(session.EditedDocument!.Catalog.Messages);
    }

    [Fact]
    public async Task ReadOnlyCatalogCanBeBrowsedButNotChanged()
    {
        var session = Session(readOnly: true);
        session.ShowMessages();
        Assert.True(session.State.MessageCatalog.IsOpen);
        session.NewMessage();
        Assert.Null(session.State.MessageCatalog.Draft);
        await session.SaveMessageAsync("Forbidden");
        Assert.Empty(session.EditedDocument!.Catalog.Messages);
    }

    [Fact]
    public async Task SharedEditingPreservesIdAndUsedDeletionIsRejected()
    {
        var session = Session();
        session.NewMessage("Send");
        await session.SaveMessageAsync("Order");
        var id = session.EditedDocument!.Catalog.Messages[0].Id;
        await session.ApplyAsync(new UpdatePropertiesCommand("Receive", [new("messageRef", id)]));
        session.EditMessage(id, "Send");
        await session.SaveMessageAsync("Renamed");
        Assert.Equal("Renamed", session.EditedDocument.Catalog.Messages[0].Name);
        Assert.All(session.EditedDocument.FlowNodes, node => Assert.Equal(id, node.MessageRef));
        session.ShowMessages();
        session.ConfirmMessageDeletion(id);
        await session.DeleteMessageAsync();
        Assert.Single(session.EditedDocument.Catalog.Messages);
        Assert.NotNull(session.State.MessageCatalog.Error);
    }

    [Fact]
    public async Task PendingSaveCannotSubmitTwiceOrDismissNewDocumentDraft()
    {
        var session = Session();
        session.NewMessage();
        var completion = new TaskCompletionSource<BpmnEditResult?>();
        var calls = 0;
        session.PropertyCommandOverride = command =>
        {
            calls++;
            return completion.Task;
        };
        var first = session.SaveMessageAsync("First");
        Assert.True(session.State.MessageCatalog.IsBusy);
        await session.SaveMessageAsync("Second");
        Assert.Equal(1, calls);
        session.Document = Document();
        session.UpdateParameters(session.State.Configuration);
        session.NewMessage("Receive");
        var newDraft = session.State.MessageCatalog.Draft;
        completion.SetResult(BpmnEditResult.Applied(session.EditedDocument!));
        await first;
        Assert.Same(newDraft, session.State.MessageCatalog.Draft);
        Assert.True(session.State.MessageCatalog.IsOpen);
        Assert.False(session.State.MessageCatalog.IsBusy);
    }
}
