using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;

using static Hasseware.AspNetCore.Components.Bpmn.Tools.Tests.Documents;

namespace Hasseware.AspNetCore.Components.Bpmn.Tools.Tests;

/// <summary>
/// The editing tools go through the editor's own commands, attribute every edit to the agent, and refuse
/// atomically.
/// </summary>
public sealed class EditingToolTests
{
    // --- create ---------------------------------------------------------------------------------

    [Fact]
    public async Task CreatesBesideAnAnchorClearOfItsNeighbours()
    {
        var host = Host(Simple());

        var result = await BpmnTools.CreateElementAsync(host, new BpmnCreateElementRequest("task", "Check", AnchorId: "review"), Token);

        Assert.True(result.Ok, result.Code);
        var created = result.Data!;
        Assert.Equal("Process_1", created.ContainerId);
        Assert.True(created.Bounds.X > 300, "to the right of the anchor");
        Assert.Equal("Check", host.Document.FindFlowNode(created.ElementId)!.Name);
        Assert.Equal(Agent, host.History.UndoOrigin);
        Assert.NotEqual("r0", result.Revision);
    }

    [Fact]
    public async Task TreatsTheActiveDiagramAsAPreferenceWhenPlacingBesideAnAnchor()
    {
        // The anchor came from get_diagram, which lists every diagram; the active one need not draw it.
        var host = new BpmnDocumentToolHost(Simple(), Agent) { ActiveDiagramId = "some-other-diagram" };

        var result = await BpmnTools.CreateElementAsync(host, new BpmnCreateElementRequest("task", "Check", AnchorId: "review"), Token);

        Assert.True(result.Ok, result.Code);
        Assert.NotNull(host.Document.FindFlowNode(result.Data!.ElementId));
    }

    [Fact]
    public async Task CreatesAndConnectsInOneUndoableEdit()
    {
        var host = Host(Simple());

        var result = await BpmnTools.CreateElementAsync(
            host,
            new BpmnCreateElementRequest("exclusiveGateway", AnchorId: "review", ConnectFromAnchor: true),
            Token);

        Assert.True(result.Ok, result.Code);
        Assert.NotNull(result.Data!.ConnectorId);
        Assert.Contains(host.Document.SequenceFlows, flow => flow.SourceRef == "review" && flow.TargetRef == result.Data.ElementId);

        host.History.Undo();

        Assert.Null(host.Document.FindFlowNode(result.Data.ElementId));
        Assert.DoesNotContain(host.Document.SequenceFlows, flow => flow.Id == result.Data.ConnectorId);
    }

    [Fact]
    public async Task RefusesAnIllegalConnectionAndCreatesNothing()
    {
        var host = Host(Simple());
        var before = host.Document;

        // Nothing may flow out of an End Event (SF-002).
        var result = await BpmnTools.CreateElementAsync(
            host,
            new BpmnCreateElementRequest("task", AnchorId: "end", ConnectFromAnchor: true),
            Token);

        Assert.Equal(BpmnToolCodes.IllegalConnection, result.Code);
        Assert.Contains(result.Diagnostics, finding => finding.Code == "SF-002");
        Assert.Same(before, host.Document);
    }

    [Fact]
    public async Task CreatesAnEventWithItsTrigger()
    {
        var host = Host(Simple());

        var result = await BpmnTools.CreateElementAsync(
            host,
            new BpmnCreateElementRequest("intermediateCatchEvent", AnchorId: "review", Direction: "below", EventDefinition: "timer"),
            Token);

        Assert.True(result.Ok, result.Code);
        Assert.Equal(BpmnEventDefinitionType.Timer, host.Document.FindFlowNode(result.Data!.ElementId)!.EventDefinition);
    }

    [Theory]
    [InlineData("spaceship", null, null)]
    [InlineData("boundaryEvent", null, null)]
    [InlineData("task", "timer", null)]
    [InlineData("task", null, "sideways")]
    public async Task RefusesMalformedRequests(string type, string? eventDefinition, string? direction)
    {
        var host = Host(Simple());

        var result = await BpmnTools.CreateElementAsync(
            host,
            new BpmnCreateElementRequest(type, AnchorId: "review", Direction: direction, EventDefinition: eventDefinition),
            Token);

        Assert.Equal(BpmnToolCodes.InvalidArgument, result.Code);
        Assert.False(host.History.CanUndo);
    }

    [Fact]
    public async Task ReportsWhyNoPlaceWasFound()
    {
        var result = await BpmnTools.CreateElementAsync(Host(Simple()), new BpmnCreateElementRequest("task", AnchorId: "missing"), Token);

        Assert.Equal("PL-001", result.Code);
    }

    // --- connect --------------------------------------------------------------------------------

    [Fact]
    public async Task ConnectsWithASequenceFlowByDefault()
    {
        var host = Host(Simple());

        var result = await BpmnTools.ConnectAsync(host, new BpmnConnectRequest("review", "end", Name: "approved"), Token);

        Assert.True(result.Ok, result.Code);
        Assert.Equal("sequenceFlow", result.Data!.Kind);
        var flow = host.Document.SequenceFlows.Single(candidate => candidate.Id == result.Data.ConnectorId);
        Assert.Equal("approved", flow.Name);
        Assert.Equal(Agent, host.History.UndoOrigin);
    }

    [Fact]
    public async Task InfersAMessageFlowAcrossPools()
    {
        var host = Host(TwoPools());

        var result = await BpmnTools.ConnectAsync(host, new BpmnConnectRequest("ask", "answer"), Token);

        Assert.True(result.Ok, result.Code);
        Assert.Equal("messageFlow", result.Data!.Kind);
        Assert.Single(host.Document.MessageFlows);
    }

    [Fact]
    public async Task RefusesASequenceFlowAcrossPools()
    {
        var host = Host(TwoPools());

        var result = await BpmnTools.ConnectAsync(host, new BpmnConnectRequest("ask", "answer", "sequenceFlow"), Token);

        Assert.Equal(BpmnToolCodes.IllegalConnection, result.Code);
        Assert.Contains(result.Diagnostics, finding => finding.Code == "SF-006");
        Assert.False(host.History.CanUndo);
    }

    [Fact]
    public async Task RefusesAMessageFlowInsideOnePool()
    {
        var host = Host(TwoPools());

        var result = await BpmnTools.ConnectAsync(host, new BpmnConnectRequest("ask", "other", "messageFlow"), Token);

        Assert.Equal(BpmnToolCodes.IllegalConnection, result.Code);
        Assert.Contains(result.Diagnostics, finding => finding.Code == "MF-002");
    }

    [Fact]
    public async Task ReportsAMissingEnd()
    {
        var result = await BpmnTools.ConnectAsync(Host(Simple()), new BpmnConnectRequest("review", "missing"), Token);

        Assert.Equal(BpmnToolCodes.NotFound, result.Code);
    }

    // --- properties and deletion ----------------------------------------------------------------

    [Fact]
    public async Task SetsAStandardProperty()
    {
        var host = Host(Simple());

        var result = await BpmnTools.SetPropertyAsync(host, new BpmnSetPropertyRequest("review", "name", "Approve"), Token);

        Assert.True(result.Ok, result.Code);
        Assert.Equal("Approve", host.Document.FindFlowNode("review")!.Name);
    }

    [Fact]
    public async Task PassesTheEditorsRefusalThrough()
    {
        var host = Host(Simple());

        var result = await BpmnTools.SetPropertyAsync(host, new BpmnSetPropertyRequest("review", "noSuchProperty", "x"), Token);

        Assert.Equal("EDIT-070", result.Code);
        Assert.False(result.Applied);
    }

    [Fact]
    public async Task RefusesExtensionPaths()
    {
        var result = await BpmnTools.SetPropertyAsync(Host(Simple()), new BpmnSetPropertyRequest("review", "extension:zeebe:taskDefinition", "x"), Token);

        Assert.Equal(BpmnToolCodes.InvalidArgument, result.Code);
    }

    [Fact]
    public async Task DeletesAndReportsWhatWentWithIt()
    {
        var host = Host(Simple());

        var result = await BpmnTools.DeleteAsync(host, new BpmnDeleteRequest(["review"]), Token);

        Assert.True(result.Ok, result.Code);
        Assert.Equal(["f1", "review"], result.Data!.DeletedIds);
        Assert.Contains(result.Diagnostics, finding => finding.Code == "EDIT-023");
        Assert.Empty(host.Document.SequenceFlows);
    }

    [Fact]
    public async Task PassesARefusedDeletionThrough()
    {
        var result = await BpmnTools.DeleteAsync(Host(TwoPools()), new BpmnDeleteRequest(["PoolA"]), Token);

        Assert.Equal("EDIT-022", result.Code);
    }

    [Fact]
    public async Task RefusesToDeleteMoreThanTheBatchLimit()
    {
        var host = Host(Simple(), limits: new BpmnToolLimits(MaxBatch: 1));

        var result = await BpmnTools.DeleteAsync(host, new BpmnDeleteRequest(["start", "end"]), Token);

        Assert.Equal(BpmnToolCodes.TooLarge, result.Code);
    }

    // --- read-only ------------------------------------------------------------------------------

    [Fact]
    public async Task EveryEditingToolRefusesAReadOnlyDocument()
    {
        var host = Host(Simple(), isReadOnly: true);

        var results = new BpmnToolResult[]
        {
            await BpmnTools.CreateElementAsync(host, new BpmnCreateElementRequest("task", AnchorId: "review"), Token),
            await BpmnTools.ConnectAsync(host, new BpmnConnectRequest("review", "end"), Token),
            await BpmnTools.SetPropertyAsync(host, new BpmnSetPropertyRequest("review", "name", "x"), Token),
            await BpmnTools.DeleteAsync(host, new BpmnDeleteRequest(["review"]), Token),
        };

        Assert.All(results, result => Assert.Equal(BpmnToolCodes.ReadOnly, result.Code));
        Assert.False(host.History.CanUndo);
    }

    [Fact]
    public void RefusesAHostThatActsAsTheUser()
    {
        Assert.Throws<ArgumentException>(() => new BpmnDocumentToolHost(Simple(), BpmnEditOrigin.User));
    }
}
