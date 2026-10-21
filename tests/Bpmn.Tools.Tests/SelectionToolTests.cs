using Hasseware.AspNetCore.Components.Bpmn.Editing;

using static Hasseware.AspNetCore.Components.Bpmn.Tools.Tests.Documents;

namespace Hasseware.AspNetCore.Components.Bpmn.Tools.Tests;

/// <summary>The selection tool: what the user has selected, for hosts that have one.</summary>
public sealed class SelectionToolTests
{
    [Fact]
    public async Task ListsTheSelectedElementsAndConnectors()
    {
        var host = new SelectingHost(Simple(), "review", "f1");

        var result = await BpmnTools.GetSelectionAsync(host, Token);

        Assert.True(result.Ok);
        Assert.Equal("review", Assert.Single(result.Data!.Elements).Id);
        Assert.Equal("f1", Assert.Single(result.Data.Connectors).Id);
        Assert.Equal("Review", result.Data.Elements[0].Name?.UntrustedText);
    }

    [Fact]
    public async Task IsEmptyWhenNothingIsSelected()
    {
        var result = await BpmnTools.GetSelectionAsync(new SelectingHost(Simple()), Token);

        Assert.Empty(result.Data!.Elements);
        Assert.Empty(result.Data.Connectors);
    }

    [Fact]
    public async Task IsEmptyForAHostWithoutASelection()
    {
        var result = await BpmnTools.GetSelectionAsync(Host(Simple()), Token);

        Assert.True(result.Ok);
        Assert.Empty(result.Data!.Elements);
    }

    [Fact]
    public async Task IgnoresIdsThatAreNotInTheDocument()
    {
        var result = await BpmnTools.GetSelectionAsync(new SelectingHost(Simple(), "gone"), Token);

        Assert.Empty(result.Data!.Elements);
    }

    [Fact]
    public async Task CutsALargeSelectionToTheLimit()
    {
        var host = new SelectingHost(Simple(), "start", "review", "end") { Limit = 2 };

        var result = await BpmnTools.GetSelectionAsync(host, Token);

        Assert.Equal(2, result.Data!.Elements.Count);
    }

    [Fact]
    public async Task ListsTheSelectionInTheOrderItWasMade()
    {
        var host = new SelectingHost(Simple(), "end", "start", "review");

        var result = await BpmnTools.GetSelectionAsync(host, Token);

        Assert.Equal(["end", "start", "review"], result.Data!.Elements.Select(element => element.Id));
    }

    [Fact]
    public async Task SaysHowManyWereSelectedWhenTheLimitCutThem()
    {
        var host = new SelectingHost(Simple(), "start", "review", "end") { Limit = 2 };

        var result = await BpmnTools.GetSelectionAsync(host, Token);

        Assert.Equal(3, result.Data!.SelectedCount);
    }

    [Fact]
    public async Task IsReachableByNameThroughTheCatalog()
    {
        var result = await BpmnToolCatalog.InvokeAsync(new SelectingHost(Simple(), "review"), "bpmn_get_selection", Empty(), Token);

        Assert.True(result.Ok);
    }

    private static System.Text.Json.JsonElement Empty() => System.Text.Json.JsonElement.Parse("{}");

    private sealed class SelectingHost(Model.BpmnDocument document, params string[] selected) : IBpmnToolHost, IBpmnSelectionHost
    {
        private readonly BpmnDocumentToolHost _inner = new(document, Agent);

        public int Limit { get; init; } = 200;

        public IReadOnlyList<string> SelectedIds { get; } = selected;

        public BpmnEditOrigin Origin => _inner.Origin;

        public bool IsReadOnly => _inner.IsReadOnly;

        public string? ActiveDiagramId => _inner.ActiveDiagramId;

        public Configuration.EditingOptions Editing => _inner.Editing;

        public IReadOnlyList<Validation.IBpmnProfileValidator> Profiles => _inner.Profiles;

        public BpmnToolLimits Limits => new(MaxElements: Limit);

        public string? Revision => _inner.Revision;

        public ValueTask<Model.BpmnDocument?> GetDocumentAsync(CancellationToken cancellationToken = default) => _inner.GetDocumentAsync(cancellationToken);

        public ValueTask<BpmnEditResult> ApplyAsync(BpmnEditCommand command, CancellationToken cancellationToken = default) => _inner.ApplyAsync(command, cancellationToken);
    }
}
