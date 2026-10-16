using System.Text;
using System.Text.Json.Nodes;

using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Serialization;

/// <summary>
/// BPMN JSON: XML → JSON → XML over every sample, fidelity of the model, and refusal of bad input.
/// </summary>
public sealed class BpmnJsonTests
{
    public static TheoryData<string> Samples => [.. InterchangeFixtures.All];

    private static readonly BpmnWriteOptions Canonical =
        BpmnWriteOptions.Canonical with { AllowLossyCanonicalExport = true };

    // --- round trips -------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Samples))]
    public void XmlToJsonToXmlProducesTheSameCanonicalXml(string fileName)
    {
        var original = InterchangeFixtures.Read(fileName).Document;
        var expected = BpmnXmlWriter.Write(original, Canonical).Xml;

        var json = BpmnJsonWriter.Write(original);
        var reread = BpmnJsonReader.Read(json);

        Assert.Equal(expected, BpmnXmlWriter.Write(reread.Document, Canonical).Xml);
        Assert.Empty(reread.Diagnostics);
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void JsonOutputIsStable(string fileName)
    {
        var json = BpmnJsonWriter.Write(InterchangeFixtures.Read(fileName).Document);

        Assert.Equal(json, BpmnJsonWriter.Write(BpmnJsonReader.Read(json).Document));
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void CompactAndStreamOutputReadBackAlike(string fileName)
    {
        var document = InterchangeFixtures.Read(fileName).Document;
        var indented = BpmnJsonWriter.Write(document);

        using var stream = new MemoryStream();
        BpmnJsonWriter.Write(document, stream, BpmnJsonWriteOptions.Compact);
        stream.Position = 0;

        var fromStream = BpmnJsonReader.Read(stream).Document;

        Assert.Equal(indented, BpmnJsonWriter.Write(fromStream));
    }

    [Fact]
    public async Task AsyncOverloadsRoundTrip()
    {
        var document = InterchangeFixtures.Read("collaboration.bpmn").Document;

        using var stream = new MemoryStream();
        await BpmnJsonWriter.WriteAsync(document, stream, cancellationToken: TestContext.Current.CancellationToken);
        stream.Position = 0;

        var reread = await BpmnJsonReader.ReadAsync(stream, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(BpmnJsonWriter.Write(document), BpmnJsonWriter.Write(reread.Document));
    }

    // --- model fidelity ----------------------------------------------------------------

    [Fact]
    public void VendorExtensionsAndForeignAttributesSurvive()
    {
        var before = InterchangeFixtures.Read("zeebe-extensions.bpmn").Document;
        var after = BpmnJsonReader.Read(BpmnJsonWriter.Write(before)).Document;

        Assert.Contains(before.FlowNodes, node => node.Extras.Extensions.Count > 0);

        foreach (var node in before.FlowNodes)
        {
            var match = after.FindFlowNode(node.Id);
            Assert.NotNull(match);
            Assert.Equal(node.Extras, match.Extras);
            Assert.Equal(node.Type, match.Type);
            Assert.Equal(node.ContainerId, match.ContainerId);
            Assert.Equal(node.IsInterrupting, match.IsInterrupting);
        }
    }

    [Fact]
    public void CollaborationLanesAndGeometrySurvive()
    {
        var before = InterchangeFixtures.Read("collaboration.bpmn").Document;
        var after = BpmnJsonReader.Read(BpmnJsonWriter.Write(before)).Document;

        Assert.NotNull(after.Collaboration);
        Assert.Equal(
            before.Collaboration!.Participants.Select(p => (p.Id, p.Name, p.ProcessRef)),
            after.Collaboration.Participants.Select(p => (p.Id, p.Name, p.ProcessRef)));
        Assert.Equal(before.Processes.Select(p => p.Id), after.Processes.Select(p => p.Id));
        Assert.Equal(
            before.Lanes.Select(l => (l.Id, string.Join(',', l.FlowNodeRefs))),
            after.Lanes.Select(l => (l.Id, string.Join(',', l.FlowNodeRefs))));

        var shapes = before.Diagrams.SelectMany(d => d.Plane.Shapes).ToList();
        var reread = after.Diagrams.SelectMany(d => d.Plane.Shapes).ToList();
        Assert.Equal(shapes.Select(s => (s.Id, s.Bounds, s.IsHorizontal, s.Label)), reread.Select(s => (s.Id, s.Bounds, s.IsHorizontal, s.Label)));

        var edges = before.Diagrams.SelectMany(d => d.Plane.Edges).ToList();
        var rereadEdges = after.Diagrams.SelectMany(d => d.Plane.Edges).ToList();
        Assert.Equal(
            edges.Select(e => string.Join(';', e.Waypoints)),
            rereadEdges.Select(e => string.Join(';', e.Waypoints)));
    }

    [Fact]
    public void ImportedJsonHasNoXmlSource()
    {
        var document = BpmnJsonReader.Read(BpmnJsonWriter.Write(InterchangeFixtures.Read("simple-process.bpmn").Document)).Document;

        Assert.Null(document.Source);
    }

    [Fact]
    public void TheEnvelopeDeclaresFormatAndVersion()
    {
        var json = JsonNode.Parse(BpmnJsonWriter.Write(InterchangeFixtures.Read("simple-process.bpmn").Document))!;

        Assert.Equal(BpmnJsonFormat.Name, (string?)json["format"]);
        Assert.Equal(BpmnJsonFormat.Version, (string?)json["version"]);
        Assert.NotNull(json["document"]?["flowNodes"]);
        Assert.NotNull(json["document"]?["definitions"]);
    }

    [Fact]
    public void DefaultsAreLeftOutOfTheFile()
    {
        var json = BpmnJsonWriter.Write(InterchangeFixtures.Read("simple-process.bpmn").Document);

        Assert.DoesNotContain("\"isInterrupting\": true", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"extras\": {}", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"isEmpty\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"source\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void NonInterruptingIsKept()
    {
        var document = InterchangeFixtures.Read("simple-process.bpmn").Document;
        var node = document.FlowNodes[0] with { IsInterrupting = false };
        document = document with { FlowNodes = [node, .. document.FlowNodes.Skip(1)] };

        var reread = BpmnJsonReader.Read(BpmnJsonWriter.Write(document)).Document;

        Assert.False(reread.FindFlowNode(node.Id)!.IsInterrupting);
    }

    // --- robust import -----------------------------------------------------------------

    [Fact]
    public void UnknownPropertiesAreIgnored()
    {
        var json = JsonNode.Parse(BpmnJsonWriter.Write(InterchangeFixtures.Read("simple-process.bpmn").Document))!;
        json["generator"] = "someone else";
        json["document"]!["futureFeature"] = new JsonObject { ["x"] = 1 };
        json["document"]!["flowNodes"]![0]!["somethingNew"] = true;

        var result = BpmnJsonReader.Read(json.ToJsonString());

        Assert.NotEmpty(result.Document.FlowNodes);
    }

    [Fact]
    public void ANewerMinorVersionOpensWithANote()
    {
        var json = Envelope(version: "1.7");

        var result = BpmnJsonReader.Read(json);

        Assert.Contains(result.Diagnostics, d => d.Severity == BpmnImportSeverity.Info && d.Message.Contains("1.7", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("2.0")]
    [InlineData("0.9")]
    [InlineData("one")]
    [InlineData("")]
    public void AnUnsupportedVersionIsRefused(string version)
    {
        var failure = Assert.Throws<BpmnJsonException>(() => BpmnJsonReader.Read(Envelope(version: version)));

        Assert.Contains("version", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("{\"format\":\"something-else\",\"version\":\"1.0\",\"document\":{}}")]
    [InlineData("{\"format\":\"hasseware.bpmn\",\"document\":{}}")]
    [InlineData("{\"format\":\"hasseware.bpmn\",\"version\":\"1.0\"}")]
    [InlineData("{\"format\":\"hasseware.bpmn\",\"version\":\"1.0\",\"document\":{}}")]
    [InlineData("{\"format\":\"hasseware.bpmn\",\"version\":\"1.0\",\"document\":{\"definitions\":{\"id\":\"D\",\"targetNamespace\":\"x\"},\"flowNodes\":[null]}}")]
    [InlineData("{\"format\":\"hasseware.bpmn\",\"version\":\"1.0\",\"document\":{\"definitions\":{\"id\":\"D\",\"targetNamespace\":\"x\"},\"flowNodes\":null}}")]
    [InlineData("{\"format\":\"hasseware.bpmn\",\"version\":\"1.0\",\"document\":{\"definitions\":{\"id\":\"D\",\"targetNamespace\":\"x\"},\"flowNodes\":[{\"type\":\"task\",\"containerId\":\"P\"}]}}")]
    [InlineData("{\"format\":\"hasseware.bpmn\",\"version\":\"1.0\",\"document\":{\"definitions\":{\"id\":\"D\",\"targetNamespace\":\"x\"},\"flowNodes\":[{\"id\":\"T\",\"type\":\"NotAType\",\"containerId\":\"P\"}]}}")]
    [InlineData("{\"format\":\"hasseware.bpmn\",\"format\":\"hasseware.bpmn\",\"version\":\"1.0\",\"document\":{}}")]
    public void MalformedInputThrowsTheImportException(string json)
    {
        var failure = Assert.ThrowsAny<BpmnImportException>(() => BpmnJsonReader.Read(json));

        Assert.IsType<BpmnJsonException>(failure);
        Assert.False(string.IsNullOrWhiteSpace(failure.Message));
    }

    [Fact]
    public void OversizedInputIsRefusedBeforeParsing()
    {
        var json = Envelope();
        var options = new BpmnJsonReadOptions { MaxInputBytes = 64 };

        Assert.Throws<BpmnJsonException>(() => BpmnJsonReader.Read(json, options));

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var failure = Assert.Throws<BpmnJsonException>(() => BpmnJsonReader.Read(stream, options));
        Assert.Contains("64-byte", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExcessiveNestingIsRefused()
    {
        var deep = new StringBuilder("{\"format\":\"hasseware.bpmn\",\"version\":\"1.0\",\"x\":");
        for (var i = 0; i < 300; i++) deep.Append('[');
        for (var i = 0; i < 300; i++) deep.Append(']');
        deep.Append('}');

        Assert.Throws<BpmnJsonException>(() => BpmnJsonReader.Read(deep.ToString()));
    }

    [Theory]
    [InlineData("{\"definitions\":{\"id\":\"D\",\"targetNamespace\":\"x\"},\"catalog\":{\"categories\":[{\"id\":\"C\",\"values\":[null]}]}}")]
    [InlineData("{\"definitions\":{\"id\":\"D\",\"targetNamespace\":\"x\",\"extras\":{\"extensions\":[null]}}}")]
    public void NestedNullEntriesAreRefused(string document)
    {
        var json = "{\"format\":\"hasseware.bpmn\",\"version\":\"1.0\",\"document\":" + document + "}";

        var failure = Assert.Throws<BpmnJsonException>(() => BpmnJsonReader.Read(json));

        Assert.Contains("is null", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AByteOrderMarkIsTolerated()
    {
        var json = Envelope();

        Assert.NotNull(BpmnJsonReader.Read("\uFEFF" + json).Document);

        using var stream = new MemoryStream([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(json)]);
        Assert.NotNull(BpmnJsonReader.Read(stream).Document);
    }

    // --- format detection and preview --------------------------------------------------

    [Theory]
    [InlineData("a.bpmn", "  {\"format\":1}", BpmnFileFormat.Json)]
    [InlineData("a.json", "<definitions/>", BpmnFileFormat.Xml)]
    [InlineData("a.json", "", BpmnFileFormat.Json)]
    [InlineData("a.bpmn", "", BpmnFileFormat.Xml)]
    [InlineData(null, "\uFEFF\n{", BpmnFileFormat.Json)]
    [InlineData(null, "garbage", BpmnFileFormat.Xml)]
    public void DetectsTheFormat(string? fileName, string content, BpmnFileFormat expected)
    {
        Assert.Equal(expected, BpmnFileFormats.Detect(fileName, content));
    }

    [Fact]
    public void TheImportPreviewOpensJson()
    {
        var json = BpmnJsonWriter.Write(InterchangeFixtures.Read("simple-process.bpmn").Document);

        var preview = BpmnImportPreview.Of("simple-process.json", json);

        Assert.True(preview.CanAccept);
        Assert.Null(preview.Error);
        Assert.True(preview.FlowNodeCount > 0);
    }

    [Fact]
    public void TheImportPreviewReportsBadJsonAsAnError()
    {
        var preview = BpmnImportPreview.Of("broken.json", "{\"format\":\"hasseware.bpmn\",\"version\":\"9.0\",\"document\":{}}");

        Assert.False(preview.CanAccept);
        Assert.Contains("9.0", preview.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFacadeReadsEitherFormat()
    {
        var xml = File.ReadAllText(InterchangeFixtures.PathFor("simple-process.bpmn"));
        var fromXml = BpmnFileFormats.Read(xml);
        var fromJson = BpmnFileFormats.Read(BpmnJsonWriter.Write(fromXml.Document));

        Assert.Equal(fromXml.Document.FlowNodes.Count, fromJson.Document.FlowNodes.Count);
        Assert.ThrowsAny<BpmnImportException>(() => BpmnFileFormats.Read("<not-bpmn/>"));
    }

    private static string Envelope(string version = BpmnJsonFormat.Version) =>
        $$"""
        {
          "format": "hasseware.bpmn",
          "version": "{{version}}",
          "document": {
            "definitions": { "id": "Definitions_1", "targetNamespace": "http://example.com" },
            "processes": [ { "id": "Process_1" } ],
            "flowNodes": [ { "id": "Start_1", "type": "startEvent", "containerId": "Process_1" } ]
          }
        }
        """;
}
