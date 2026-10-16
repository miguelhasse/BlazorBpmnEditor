using System.Text.Json;
using System.Text.RegularExpressions;

using Hasseware.AspNetCore.Components.Bpmn.Interop;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.CanvasPayload;

/// <summary>
/// The canvas is sent what it reads and nothing it does not: every field the TypeScript contract requires is present, the C#-only ones are not.
/// </summary>
public sealed partial class CanvasDocumentPayloadTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static readonly string Fixture = Path.Combine(FindRoot(), "playground", "Bpmn.Demo", "wwwroot", "samples", "large-virtualization-stress.bpmn");

    private static BpmnDocument Stress() => BpmnXmlReader.Read(File.ReadAllText(Fixture)).Document;

    [Fact]
    public void NothingIsSentWhenThereIsNoDocument() => Assert.Null(CanvasDocumentPayload.For(null));

    [Fact]
    public void TheSameDocumentIsSerializedOnce()
    {
        var document = Stress();

        var first = CanvasDocumentPayload.For(document);
        var second = CanvasDocumentPayload.For(document);

        Assert.NotNull(first);
        Assert.Equal(first.Value.GetRawText(), second!.Value.GetRawText());
    }

    [Fact]
    public void RendersOneDocumentFromSeveralCircuitsAtOnceWithoutFailing()
    {
        for (var round = 0; round < 20; round++)
        {
            var document = Stress();

            var results = Enumerable.Range(0, 8).AsParallel().WithDegreeOfParallelism(8)
                .Select(_ => CanvasDocumentPayload.For(document)!.Value.GetRawText().Length).ToList();

            Assert.Single(results.Distinct());
        }
    }

    [Fact]
    public void EveryFieldTheTypeScriptContractRequiresIsStillSent()
    {
        var contracts = File.ReadAllText(Path.Combine(FindRoot(), "src", "Bpmn.Scripts", "src", "model", "contracts.ts"));
        using var json = JsonDocument.Parse(CanvasDocumentPayload.Serialize(Stress()));
        var root = json.RootElement;

        // The first element of each collection the canvas walks, against the interface that describes it.
        var checks = new (string Interface, JsonElement Sample)[]
        {
            ("BpmnDocumentContract", root),
            ("BpmnFlowNodeContract", root.GetProperty("flowNodes")[0]),
            ("BpmnSequenceFlowContract", root.GetProperty("sequenceFlows")[0]),
            ("BpmnDiagramContract", root.GetProperty("diagrams")[0]),
            ("BpmnPlaneContract", root.GetProperty("diagrams")[0].GetProperty("plane")),
            ("BpmnShapeContract", root.GetProperty("diagrams")[0].GetProperty("plane").GetProperty("shapes")[0]),
            ("BpmnEdgeContract", root.GetProperty("diagrams")[0].GetProperty("plane").GetProperty("edges")[0]),
            ("BoundsContract", root.GetProperty("diagrams")[0].GetProperty("plane").GetProperty("shapes")[0].GetProperty("bounds")),
        };

        foreach (var (name, sample) in checks)
        {
            foreach (var required in RequiredFields(contracts, name))
            {
                Assert.True(sample.TryGetProperty(required, out _), $"{name}.{required} is required by the canvas but was not sent.");
            }
        }
    }

    [Fact]
    public void NothingTheCanvasNeverReadsIsSent()
    {
        using var json = JsonDocument.Parse(CanvasDocumentPayload.Serialize(Stress()));
        var root = json.RootElement;
        var node = root.GetProperty("flowNodes")[0];
        var flow = root.GetProperty("sequenceFlows")[0];
        var shape = root.GetProperty("diagrams")[0].GetProperty("plane").GetProperty("shapes")[0];

        foreach (var property in new[] { "processes", "dataObjects", "catalog" }) Assert.False(root.TryGetProperty(property, out _), property);
        foreach (var property in new[] { "instantiate", "scriptFormat", "script", "standardLoop", "multiInstanceLoop", "activationCondition", "eventDefinitions", "defaultFlowRef" })
        {
            Assert.False(node.TryGetProperty(property, out _), $"flowNode.{property}");
        }

        Assert.False(flow.TryGetProperty("extras", out _));
        Assert.False(flow.TryGetProperty("conditionLanguage", out _));
        Assert.False(shape.TryGetProperty("isMarkerVisible", out _));
        Assert.False(node.TryGetProperty("extras", out _), "A node with no foreign attributes sends no extras.");
    }

    [Fact]
    public void ValuesTheCanvasTreatsAsNullStayNullAndAreNotDropped()
    {
        using var json = JsonDocument.Parse(CanvasDocumentPayload.Serialize(Stress()));
        var node = json.RootElement.GetProperty("flowNodes")[0];

        Assert.Equal(JsonValueKind.Null, node.GetProperty("attachedToRef").ValueKind);
        Assert.Equal(JsonValueKind.Null, node.GetProperty("poolId").ValueKind);
        Assert.Equal(JsonValueKind.Null, node.GetProperty("laneId").ValueKind);
    }

    [Fact]
    public void AFlowNodeWithForeignAttributesSendsThemAndNothingElseOfItsExtras()
    {
        var document = Stress();
        var node = document.FlowNodes[0] with
        {
            Extras = new Model.Extensions.BpmnElementExtras
            {
                Documentation = "notes",
                ForeignAttributes = new Dictionary<string, string> { ["{urn:t}icon"] = "star" },
            },
        };
        document = document with { FlowNodes = [node, .. document.FlowNodes.Skip(1)] };

        using var json = JsonDocument.Parse(CanvasDocumentPayload.Serialize(document));
        var extras = json.RootElement.GetProperty("flowNodes")[0].GetProperty("extras");

        Assert.Equal("star", extras.GetProperty("foreignAttributes").GetProperty("{urn:t}icon").GetString());
        Assert.False(extras.TryGetProperty("documentation", out _));
        Assert.False(extras.TryGetProperty("extensions", out _));
    }

    [Fact]
    public void ThePayloadIsMuchSmallerThanTheWholeDocument()
    {
        var document = Stress();
        var full = JsonSerializer.Serialize(document, Web);
        var trimmed = CanvasDocumentPayload.Serialize(document);

        Assert.True(trimmed.Length < full.Length * 0.65, $"{trimmed.Length:N0} of {full.Length:N0} characters");
    }

    /// <summary>The property names an interface declares without a question mark.</summary>
    private static List<string> RequiredFields(string contracts, string interfaceName)
    {
        var block = InterfaceBlock(interfaceName).Match(contracts);
        Assert.True(block.Success, $"contracts.ts no longer declares {interfaceName}.");

        return [.. RequiredProperty().Matches(block.Groups["body"].Value).Select(match => match.Groups["name"].Value)];
    }

    private static Regex InterfaceBlock(string name) =>
        new($@"export interface {Regex.Escape(name)}\s*\{{(?<body>.*?)\n\}}", RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    [GeneratedRegex(@"^\s{2}readonly (?<name>\w+): ", RegexOptions.Multiline | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 2000)]
    private static partial Regex RequiredProperty();

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "Bpmn"))) return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("The repository root was not found above the test assembly.");
    }
}
