using static Hasseware.AspNetCore.Components.Bpmn.Tools.Tests.Documents;

namespace Hasseware.AspNetCore.Components.Bpmn.Tools.Tests;

/// <summary>The read-only tools: listing, describing, validating, exporting and the palette.</summary>
public sealed class ReadingToolTests
{
    [Fact]
    public async Task ListsElementsThenConnectorsWithTheirPlaces()
    {
        var result = await BpmnTools.GetDiagramAsync(Host(Simple()), new BpmnGetDiagramRequest(), Token);

        Assert.True(result.Ok);
        var page = result.Data!;
        Assert.Equal(["start", "review", "end"], page.Elements.Select(element => element.Id));
        Assert.Equal("userTask", page.Elements[1].Type);
        Assert.Equal("Review", page.Elements[1].Name?.UntrustedText);
        Assert.Equal(new BpmnToolBounds(200, 110, 100, 80), page.Elements[1].Bounds);
        var flow = Assert.Single(page.Connectors);
        Assert.Equal(("sequenceFlow", "start", "review"), (flow.Kind, flow.SourceId, flow.TargetId));
        Assert.Equal(4, page.Total);
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public async Task PagesAcrossElementsAndConnectors()
    {
        var host = Host(Simple());

        var first = (await BpmnTools.GetDiagramAsync(host, new BpmnGetDiagramRequest(Limit: 3), Token)).Data!;
        var second = (await BpmnTools.GetDiagramAsync(host, new BpmnGetDiagramRequest(first.NextCursor, 3), Token)).Data!;

        Assert.Equal(3, first.Elements.Count);
        Assert.Empty(first.Connectors);
        Assert.Equal("3", first.NextCursor);
        Assert.Empty(second.Elements);
        Assert.Single(second.Connectors);
        Assert.Null(second.NextCursor);
    }

    [Fact]
    public async Task CapsThePageAtTheHostsLimit()
    {
        var host = Host(Simple(), limits: new BpmnToolLimits(MaxElements: 2));

        var page = (await BpmnTools.GetDiagramAsync(host, new BpmnGetDiagramRequest(Limit: 100), Token)).Data!;

        Assert.Equal(2, page.Elements.Count + page.Connectors.Count);
    }

    [Fact]
    public async Task RefusesACursorItDidNotProduce()
    {
        var result = await BpmnTools.GetDiagramAsync(Host(Simple()), new BpmnGetDiagramRequest("next"), Token);

        Assert.Equal(BpmnToolCodes.InvalidArgument, result.Code);
    }

    [Fact]
    public async Task DescribesAnElementsProperties()
    {
        var result = await BpmnTools.DescribeElementAsync(Host(Simple()), new BpmnDescribeElementRequest("review"), Token);

        var description = result.Data!;
        Assert.Equal(("userTask", "activity"), (description.Type, description.Category));
        Assert.Equal("Review", description.Properties.Single(property => property.Name == "name").Value?.UntrustedText);
        Assert.True(description.CanConnect);
    }

    [Fact]
    public async Task ReportsAnUnknownElement()
    {
        var result = await BpmnTools.DescribeElementAsync(Host(Simple()), new BpmnDescribeElementRequest("missing"), Token);

        Assert.False(result.Ok);
        Assert.Equal(BpmnToolCodes.NotFound, result.Code);
    }

    [Fact]
    public async Task ValidatesWithRuleIds()
    {
        // "end" has no incoming flow, which the validator reports.
        var result = await BpmnTools.ValidateAsync(Host(Simple()), Token);

        Assert.True(result.Ok);
        Assert.Contains(result.Diagnostics, finding => finding.ElementId == "end");
        Assert.All(result.Diagnostics, finding => Assert.False(string.IsNullOrEmpty(finding.Code)));
    }

    [Theory]
    [InlineData("xml", "<")]
    [InlineData("json", "{")]
    public async Task ExportsInEitherFormat(string format, string startsWith)
    {
        var result = await BpmnTools.ExportAsync(Host(Simple()), new BpmnExportRequest(format), Token);

        Assert.True(result.Ok);
        Assert.StartsWith(startsWith, result.Data!.Content.UntrustedText.TrimStart(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefusesAnExportLargerThanTheLimit()
    {
        var host = Host(Simple(), limits: new BpmnToolLimits(MaxExportBytes: 100));

        var result = await BpmnTools.ExportAsync(host, new BpmnExportRequest("xml"), Token);

        Assert.Equal(BpmnToolCodes.TooLarge, result.Code);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task ReportsADocumentThatCannotBeWrittenAsXmlInsteadOfFailingTheCall()
    {
        // An extension name that is no XML name: it reads from JSON, writes as JSON, and cannot be written as XML.
        const string Xml = "<definitions xmlns=\"http://www.omg.org/spec/BPMN/20100524/MODEL\" xmlns:x=\"urn:v\" id=\"d\" targetNamespace=\"t\"><process id=\"p\"><task id=\"t1\"><extensionElements><x:a /></extensionElements></task></process></definitions>";
        var json = Serialization.BpmnJsonWriter.Write(Serialization.BpmnXmlReader.Read(Xml).Document);
        var document = Serialization.BpmnJsonReader.Read(json.Replace("\"localName\":\"a\"", "\"localName\":\"not a name\"", StringComparison.Ordinal).Replace("\"localName\": \"a\"", "\"localName\": \"not a name\"", StringComparison.Ordinal)).Document;
        var host = Host(document);

        var asXml = await BpmnTools.ExportAsync(host, new BpmnExportRequest("xml"), Token);
        var asJson = await BpmnTools.ExportAsync(host, new BpmnExportRequest("json"), Token);

        Assert.Equal(BpmnToolCodes.InvalidArgument, asXml.Code);
        Assert.Contains("cannot be written", asXml.Message, StringComparison.Ordinal);
        Assert.True(asJson.Ok);
    }

    [Fact]
    public async Task RefusesAnUnknownFormat()
    {
        var result = await BpmnTools.ExportAsync(Host(Simple()), new BpmnExportRequest("pdf"), Token);

        Assert.Equal(BpmnToolCodes.InvalidArgument, result.Code);
    }

    [Fact]
    public async Task ListsTheFlowNodesThePaletteOffers()
    {
        var result = await BpmnTools.ListCreatableAsync(Host(Simple()), new BpmnListCreatableRequest(), Token);

        var list = result.Data!;
        Assert.Equal("Process_1", list.ContainerId);
        Assert.Contains(list.Entries, entry => entry.Type == "task" && entry.IsEnabled);
        Assert.Contains(list.Entries, entry => entry.Type == "exclusiveGateway");
    }

    [Fact]
    public async Task ReportsTheRevisionWithEveryResult()
    {
        var host = Host(Simple());

        var result = await BpmnTools.ValidateAsync(host, Token);

        Assert.Equal(host.Revision, result.Revision);
    }
}
