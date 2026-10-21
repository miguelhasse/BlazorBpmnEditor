using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Storage;

using static Hasseware.AspNetCore.Components.Bpmn.Tools.Tests.Documents;

namespace Hasseware.AspNetCore.Components.Bpmn.Tools.Tests;

/// <summary>The tools that need more than a document: a stored copy to save, a history to step through, a selection to show.</summary>
public sealed class SessionToolTests
{
    [Fact]
    public async Task ReportsHowTheOpenDocumentRelatesToTheStoredOne()
    {
        var host = new SessionHost(Simple()) { StoredRevision = "7", IsDirty = true, DocumentName = "order.bpmn" };

        var result = await BpmnTools.GetDocumentInfoAsync(host, Token);

        Assert.True(result.Ok);
        Assert.Equal("order.bpmn", result.Data!.Name!.UntrustedText);
        Assert.Equal("7", result.Data.StoredRevision);
        Assert.True(result.Data.IsDirty);
        Assert.Equal("none", result.Data.ExternalChange);
    }

    [Theory]
    [InlineData(BpmnDocumentChangeKind.Updated, "updated")]
    [InlineData(BpmnDocumentChangeKind.Deleted, "deleted")]
    public async Task ReportsAChangeMadeBehindTheEditor(BpmnDocumentChangeKind kind, string expected)
    {
        var host = new SessionHost(Simple()) { ExternalChange = new BpmnExternalChange(kind, "8", DateTimeOffset.UnixEpoch) };

        var result = await BpmnTools.GetDocumentInfoAsync(host, Token);

        Assert.Equal(expected, result.Data!.ExternalChange);
    }

    [Fact]
    public async Task HasNoStoredDocumentToReportOnAnUnboundEditor()
    {
        var info = await BpmnTools.GetDocumentInfoAsync(new SessionHost(Simple()) { IsBound = false }, Token);
        var plain = await BpmnTools.GetDocumentInfoAsync(Host(Simple()), Token);

        Assert.Equal(BpmnToolCodes.NotFound, info.Code);
        Assert.Equal(BpmnToolCodes.NotFound, plain.Code);
    }

    [Fact]
    public async Task SavesAndReportsTheNewRevision()
    {
        var host = new SessionHost(Simple()) { IsDirty = true, SaveResult = new BpmnStoreResult(BpmnStoreStatus.Succeeded, "8") };

        var result = await BpmnTools.SaveAsync(host, Token);

        Assert.True(result.Ok);
        Assert.Equal("8", result.Data!.StoredRevision);
        Assert.Equal(1, host.Saves);
    }

    [Theory]
    [InlineData(BpmnStoreStatus.Conflict)]
    [InlineData(BpmnStoreStatus.NotFound)]
    public async Task ReportsAConflictInsteadOfOverwriting(BpmnStoreStatus status)
    {
        var host = new SessionHost(Simple()) { SaveResult = new BpmnStoreResult(status, "9") };

        var result = await BpmnTools.SaveAsync(host, Token);

        Assert.False(result.Ok);
        Assert.Equal(BpmnToolCodes.PersistConflict, result.Code);
    }

    [Fact]
    public async Task RefusesToSaveOnTopOfAStaleDocument()
    {
        var host = new SessionHost(Simple()) { ExternalChange = new BpmnExternalChange(BpmnDocumentChangeKind.Updated, "8", DateTimeOffset.UnixEpoch) };

        var result = await BpmnTools.SaveAsync(host, Token);

        Assert.Equal(BpmnToolCodes.StaleDocument, result.Code);
        Assert.Equal(0, host.Saves);
    }

    [Fact]
    public async Task RefusesToSaveAReadOnlyDocument()
    {
        var host = new SessionHost(Simple(), readOnly: true);

        var result = await BpmnTools.SaveAsync(host, Token);

        Assert.Equal(BpmnToolCodes.ReadOnly, result.Code);
        Assert.Equal(0, host.Saves);
    }

    [Fact]
    public async Task SaysWhetherEditsAreUnsavedInEveryResultOfABoundEditor()
    {
        var bound = new SessionHost(Simple()) { IsDirty = true };

        var viaRead = await BpmnTools.ValidateAsync(bound, Token);
        var viaEdit = await BpmnTools.SetPropertyAsync(bound, new BpmnSetPropertyRequest("review", "name", "Look"), Token);
        var viaFailure = await BpmnTools.DescribeElementAsync(bound, new BpmnDescribeElementRequest("nope"), Token);
        var unbound = await BpmnTools.ValidateAsync(Host(Simple()), Token);

        Assert.True(viaRead.IsDirty);
        Assert.True(viaEdit.IsDirty);
        Assert.True(viaFailure.IsDirty);
        Assert.Null(unbound.IsDirty);
    }

    [Fact]
    public async Task UndoesAnEditTheAgentMade()
    {
        var host = new SessionHost(Simple()) { UndoOrigin = Agent };

        var result = await BpmnTools.UndoAsync(host, Token);

        Assert.True(result.Ok);
        Assert.Equal(1, host.Undos);
    }

    [Fact]
    public async Task RefusesToUndoTheUsersWork()
    {
        var host = new SessionHost(Simple()) { UndoOrigin = BpmnEditOrigin.User };

        var result = await BpmnTools.UndoAsync(host, Token);

        Assert.Equal(BpmnToolCodes.Refused, result.Code);
        Assert.Equal(0, host.Undos);
    }

    [Fact]
    public async Task RefusesToUndoAnotherAgentsWork()
    {
        var host = new SessionHost(Simple()) { UndoOrigin = BpmnEditOrigin.FromAgent("someone-else") };

        var result = await BpmnTools.UndoAsync(host, Token);

        Assert.Equal(BpmnToolCodes.Refused, result.Code);
        Assert.Equal(0, host.Undos);
    }

    [Fact]
    public async Task RefusesToUndoWhenThereIsNothingToUndo()
    {
        var result = await BpmnTools.UndoAsync(new SessionHost(Simple()), Token);

        Assert.Equal(BpmnToolCodes.Refused, result.Code);
    }

    [Fact]
    public async Task RedoesOnlyWhatTheAgentUndid()
    {
        var mine = new SessionHost(Simple()) { RedoOrigin = Agent };
        var theirs = new SessionHost(Simple()) { RedoOrigin = BpmnEditOrigin.User };

        Assert.True((await BpmnTools.RedoAsync(mine, Token)).Ok);
        Assert.Equal(BpmnToolCodes.Refused, (await BpmnTools.RedoAsync(theirs, Token)).Code);
        Assert.Equal(1, mine.Redos);
        Assert.Equal(0, theirs.Redos);
    }

    [Fact]
    public async Task RefusesToStepThroughAReadOnlyHistory()
    {
        var host = new SessionHost(Simple(), readOnly: true) { UndoOrigin = Agent };

        var result = await BpmnTools.UndoAsync(host, Token);

        Assert.Equal(BpmnToolCodes.ReadOnly, result.Code);
    }

    [Fact]
    public async Task SelectsWhatTheAgentPointsAt()
    {
        var host = new SessionHost(Simple());

        var result = await BpmnTools.SelectAsync(host, new BpmnSelectRequest(["review", "f1"]), Token);

        Assert.True(result.Ok);
        Assert.Equal(["review", "f1"], host.Selected);
    }

    [Fact]
    public async Task SelectsNothingWhenAnElementDoesNotExist()
    {
        var host = new SessionHost(Simple());

        var result = await BpmnTools.SelectAsync(host, new BpmnSelectRequest(["review", "gone"]), Token);

        Assert.Equal(BpmnToolCodes.NotFound, result.Code);
        Assert.Empty(host.Selected);
    }

    [Fact]
    public async Task ReachesTheNewToolsByNameThroughTheCatalog()
    {
        var host = new SessionHost(Simple()) { UndoOrigin = Agent };

        foreach (var name in new[] { "bpmn_get_document_info", "bpmn_save", "bpmn_undo", "bpmn_redo", "bpmn_select" })
        {
            var result = await BpmnToolCatalog.InvokeAsync(host, name, System.Text.Json.JsonElement.Parse(name == "bpmn_select" ? """{"elementIds":[]}""" : "{}"), Token);

            Assert.NotEqual("There is no tool with that name.", result.Message);
        }
    }

    private sealed class SessionHost(Model.BpmnDocument document, bool readOnly = false) : IBpmnToolHost, IBpmnPersistenceHost, IBpmnInteractiveHost
    {
        public BpmnDocumentToolHost Inner { get; } = new(document, Agent) { IsReadOnly = readOnly };

        public bool IsBound { get; init; } = true;

        public string? DocumentName { get; init; } = "doc.bpmn";

        public string? StoredRevision { get; init; } = "1";

        public bool IsDirty { get; init; }

        public BpmnExternalChange? ExternalChange { get; init; }

        public BpmnStoreResult SaveResult { get; init; } = new(BpmnStoreStatus.Succeeded, "2");

        public int Saves { get; private set; }

        public BpmnEditOrigin? UndoOrigin { get; init; }

        public BpmnEditOrigin? RedoOrigin { get; init; }

        public int Undos { get; private set; }

        public int Redos { get; private set; }

        public List<string> Selected { get; } = [];

        public BpmnEditOrigin Origin => Inner.Origin;

        public bool IsReadOnly => Inner.IsReadOnly;

        public string? ActiveDiagramId => Inner.ActiveDiagramId;

        public Configuration.EditingOptions Editing => Inner.Editing;

        public IReadOnlyList<Validation.IBpmnProfileValidator> Profiles => Inner.Profiles;

        public BpmnToolLimits Limits => Inner.Limits;

        public string? Revision => Inner.Revision;

        public ValueTask<Model.BpmnDocument?> GetDocumentAsync(CancellationToken cancellationToken = default) => Inner.GetDocumentAsync(cancellationToken);

        public ValueTask<BpmnEditResult> ApplyAsync(BpmnEditCommand command, CancellationToken cancellationToken = default) => Inner.ApplyAsync(command, cancellationToken);

        public Task<BpmnStoreResult> SaveAsync(CancellationToken cancellationToken = default)
        {
            Saves++;
            return Task.FromResult(SaveResult);
        }

        public Task UndoAsync()
        {
            Undos++;
            return Task.CompletedTask;
        }

        public Task RedoAsync()
        {
            Redos++;
            return Task.CompletedTask;
        }

        public Task SelectAsync(IReadOnlyList<string> elementIds)
        {
            Selected.Clear();
            Selected.AddRange(elementIds);
            return Task.CompletedTask;
        }
    }
}
