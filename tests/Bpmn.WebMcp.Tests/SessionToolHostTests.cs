using Bunit;

using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Tools;
using Hasseware.AspNetCore.Components.Bpmn.WebMcp.Tests.Support;

using static Hasseware.AspNetCore.Components.Bpmn.WebMcp.Tests.Support.Documents;

namespace Hasseware.AspNetCore.Components.Bpmn.WebMcp.Tests;

/// <summary>An open editor, seen by the shared tools as a document they can read and edit.</summary>
public sealed class SessionToolHostTests : EditorContext
{
    [Fact]
    public async Task ReadsTheDocumentTheEditorHolds()
    {
        var scope = RenderScope();
        var host = new BpmnSessionToolHost(scope.Instance.Session);

        var document = await host.GetDocumentAsync(Token);

        Assert.Same(scope.Instance.Session.EditedDocument, document);
    }

    [Fact]
    public void FollowsTheEditorsReadOnlyStateAndConfiguration()
    {
        var scope = RenderScope(readOnly: true);
        var host = new BpmnSessionToolHost(scope.Instance.Session);

        Assert.True(host.IsReadOnly);
        Assert.Equal(scope.Instance.Session.State.Configuration.Editing, host.Editing);
        Assert.NotEmpty(host.Profiles);
    }

    [Fact]
    public async Task AppliesEditsThroughTheEditorAsTheAgent()
    {
        var scope = RenderScope();
        var session = scope.Instance.Session;
        var host = new BpmnSessionToolHost(session);

        var result = await scope.InvokeAsync(() => host.ApplyAsync(new UpdatePropertiesCommand("review", "name", "Changed") { Origin = host.Origin }, Token).AsTask());

        Assert.True(result.IsApplied);
        Assert.Equal("Changed", session.EditedDocument!.FindFlowNode("review")!.Name);
        Assert.Equal(host.Origin, session.UndoOrigin);
        Assert.True(session.CanUndo);
    }

    [Fact]
    public async Task AnAgentsEditCanBeUndoneLikeAnyOther()
    {
        var scope = RenderScope();
        var session = scope.Instance.Session;
        var host = new BpmnSessionToolHost(session);
        await scope.InvokeAsync(() => host.ApplyAsync(new UpdatePropertiesCommand("review", "name", "Changed") { Origin = host.Origin }, Token).AsTask());

        await scope.InvokeAsync(session.UndoAsync);

        Assert.Equal("Review", session.EditedDocument!.FindFlowNode("review")!.Name);
    }

    [Fact]
    public async Task ChangesRevisionWhenTheDocumentChangesAndNotWhenItIsRead()
    {
        var scope = RenderScope();
        var host = new BpmnSessionToolHost(scope.Instance.Session);
        var before = host.Revision;

        await host.GetDocumentAsync(Token);
        Assert.Equal(before, host.Revision);

        await scope.InvokeAsync(() => host.ApplyAsync(new UpdatePropertiesCommand("review", "name", "Changed") { Origin = host.Origin }, Token).AsTask());
        var afterAgent = host.Revision;
        Assert.NotEqual(before, afterAgent);

        // The user editing in between is exactly what an agent needs to notice.
        await scope.InvokeAsync(() => scope.Instance.Session.ApplyAsync(new UpdatePropertiesCommand("review", "name", "By hand")));
        Assert.NotEqual(afterAgent, host.Revision);
    }

    [Fact]
    public async Task ListsWhatTheUserHasSelected()
    {
        var scope = RenderScope();
        var host = new BpmnSessionToolHost(scope.Instance.Session);
        await scope.InvokeAsync(() => scope.Instance.Session.SetSelectionAsync(["review"]));

        Assert.Equal(["review"], host.SelectedIds);
    }

    [Fact]
    public void ActsOnlyForAnAgent()
    {
        var scope = RenderScope();

        Assert.Throws<ArgumentException>(() => new BpmnSessionToolHost(scope.Instance.Session, origin: BpmnEditOrigin.User));
    }

    [Fact]
    public async Task LetsTheSharedHandlersEditTheOpenEditor()
    {
        var scope = RenderScope();
        var host = new BpmnSessionToolHost(scope.Instance.Session);

        var result = await scope.InvokeAsync(() => BpmnTools.SetPropertyAsync(host, new BpmnSetPropertyRequest("review", "name", "Renamed"), Token).AsTask());

        Assert.True(result.Ok, result.Code);
        Assert.Equal("Renamed", scope.Instance.Session.EditedDocument!.FindFlowNode("review")!.Name);
        Assert.Equal(host.Origin, scope.Instance.Session.UndoOrigin);
    }

    [Fact]
    public async Task RefusesEditsWhileTheEditorIsReadOnly()
    {
        var scope = RenderScope(readOnly: true);
        var host = new BpmnSessionToolHost(scope.Instance.Session);

        var result = await BpmnTools.SetPropertyAsync(host, new BpmnSetPropertyRequest("review", "name", "Renamed"), Token);

        Assert.Equal(BpmnToolCodes.ReadOnly, result.Code);
        Assert.Equal("Review", scope.Instance.Session.EditedDocument!.FindFlowNode("review")!.Name);
    }
}
