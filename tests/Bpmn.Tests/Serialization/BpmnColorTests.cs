using System.Text.Json;
using System.Xml.Linq;

using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Interop;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Serialization;

public sealed class BpmnColorTests
{
    private const string Xml = """
        <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
          xmlns:d="http://www.omg.org/spec/BPMN/20100524/DI"
          xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
          xmlns:di="http://www.omg.org/spec/DD/20100524/DI"
          xmlns:c="http://www.omg.org/spec/BPMN/non-normative/color/1.0"
          xmlns:bioc="http://bpmn.io/schema/bpmn/biocolor/1.0"
          xmlns:vendor="urn:test" id="Definitions" targetNamespace="urn:test">
          <process id="Process"><task id="A" name="First"/><task id="B" name="Second"/>
            <sequenceFlow id="Flow" sourceRef="A" targetRef="B" name="Next"/>
          </process>
          <d:BPMNDiagram id="Diagram"><d:BPMNPlane id="Plane" bpmnElement="Process">
            <d:BPMNShape id="A_di" bpmnElement="A" c:background-color="#aAbB09"
              c:border-color="#123456" bioc:fill="#FF0000" bioc:stroke="#00FF00" vendor:keep="shape">
              <dc:Bounds x="0" y="0" width="100" height="80"/>
              <d:BPMNLabel c:color="#ABCDEF" vendor:keep="label"/>
            </d:BPMNShape>
            <d:BPMNShape id="B_di" bpmnElement="B" bioc:fill="#112233" bioc:stroke="#445566">
              <dc:Bounds x="200" y="0" width="100" height="80"/>
            </d:BPMNShape>
            <d:BPMNEdge id="Flow_di" bpmnElement="Flow" c:border-color="#987654">
              <di:waypoint x="100" y="40"/><di:waypoint x="200" y="40"/>
              <d:BPMNLabel c:color="#FEDCBA"/>
            </d:BPMNEdge>
          </d:BPMNPlane></d:BPMNDiagram>
        </definitions>
        """;

    [Theory]
    [InlineData(BpmnWriteMode.Canonical)]
    [InlineData(BpmnWriteMode.Fidelity)]
    public void WriteColoredDiPreservesColorsAndLabelExtras(BpmnWriteMode mode)
    {
        var before = BpmnXmlReader.Read(Xml).Document;

        var after = BpmnXmlReader.Read(BpmnXmlWriter.Write(before, new BpmnWriteOptions { Mode = mode }).Xml).Document;

        var shape = after.Diagrams[0].Plane.Shapes[0];
        Assert.Equal("#aAbB09", shape.BackgroundColor);
        Assert.Equal("#123456", shape.BorderColor);
        Assert.Equal("#ABCDEF", shape.Label!.Color);
        Assert.Null(shape.Label.Bounds);
        Assert.Equal("label", shape.Label.Extras.ForeignAttributes["{urn:test}keep"]);
        Assert.Equal("#112233", after.Diagrams[0].Plane.Shapes[1].BackgroundColor);
        Assert.Equal("#445566", after.Diagrams[0].Plane.Shapes[1].BorderColor);
        Assert.Equal("#987654", after.Diagrams[0].Plane.Edges[0].BorderColor);
        Assert.Equal("#FEDCBA", after.Diagrams[0].Plane.Edges[0].Label!.Color);
        Assert.Equal(before.Diagrams[0].Plane.Shapes.Select(item => item.Bounds),
            after.Diagrams[0].Plane.Shapes.Select(item => item.Bounds));
        Assert.Equal(before.Diagrams[0].Plane.Edges[0].Waypoints, after.Diagrams[0].Plane.Edges[0].Waypoints);
    }

    [Theory]
    [InlineData("#abc")]
    [InlineData("#12345678")]
    [InlineData("red")]
    [InlineData("url(https://example.invalid)")]
    public void ReadInvalidMiWgReportsAndPreservesWithoutRendering(string value)
    {
        var xml = Xml.Replace("#aAbB09", value, StringComparison.Ordinal)
            .Replace("bioc:fill=\"#FF0000\"", "", StringComparison.Ordinal);

        var result = BpmnXmlReader.Read(xml);

        Assert.Contains(result.Diagnostics, item => item.Message.Contains("Color attribute", StringComparison.Ordinal));
        Assert.Null(result.Document.Diagrams[0].Plane.Shapes[0].BackgroundColor);
        Assert.Equal(value, result.Document.Diagrams[0].Plane.Shapes[0].Extras
            .ForeignAttributes[$"{{{BpmnDiColors.Namespace}}}background-color"]);
    }

    [Fact]
    public void JsonColoredDiPreservesSingleAttributeSource()
    {
        var document = BpmnXmlReader.Read(Xml).Document;

        var json = BpmnJsonWriter.Write(document);
        var after = BpmnJsonReader.Read(json).Document;

        Assert.DoesNotContain("\"backgroundColor\"", json, StringComparison.Ordinal);
        Assert.Equal("#aAbB09", after.Diagrams[0].Plane.Shapes[0].BackgroundColor);
        Assert.Equal("#ABCDEF", after.Diagrams[0].Plane.Shapes[0].Label!.Color);
    }

    [Fact]
    public void CanvasColoredDiSendsResolvedColorsWithoutExtras()
    {
        using var json = JsonDocument.Parse(CanvasDocumentPayload.Serialize(BpmnXmlReader.Read(Xml).Document));

        var plane = json.RootElement.GetProperty("diagrams")[0].GetProperty("plane");
        var shape = plane.GetProperty("shapes")[0];

        Assert.Equal("#aAbB09", shape.GetProperty("backgroundColor").GetString());
        Assert.Equal("#123456", shape.GetProperty("borderColor").GetString());
        Assert.Equal("#ABCDEF", shape.GetProperty("label").GetProperty("color").GetString());
        Assert.False(shape.TryGetProperty("extras", out _));
        Assert.Equal("#112233", plane.GetProperty("shapes")[1].GetProperty("backgroundColor").GetString());
    }

    [Theory]
    [InlineData(BpmnWriteMode.Canonical)]
    [InlineData(BpmnWriteMode.Fidelity)]
    public void ResetAllChannelsRemovesMiWgAndAliasesWithoutLossOrGeometryChange(BpmnWriteMode mode)
    {
        var before = BpmnXmlReader.Read(Xml).Document;
        var result = BpmnDocumentEditor.Apply(before,
            new SetColorsCommand("Diagram", ["A", "B", "Flow"], BpmnColorChannel.All));

        var output = BpmnXmlWriter.Write(result.Document, new BpmnWriteOptions { Mode = mode });
        var xml = XDocument.Parse(output.Xml);
        var after = BpmnXmlReader.Read(output.Xml).Document;

        Assert.True(result.IsApplied);
        Assert.DoesNotContain(xml.Descendants().Attributes(), attribute =>
            attribute.Name.NamespaceName is BpmnDiColors.Namespace or BpmnDiColors.BiocNamespace);
        Assert.Null(after.Diagrams[0].Plane.Shapes[0].BackgroundColor);
        Assert.Equal("label", after.Diagrams[0].Plane.Shapes[0].Label!.Extras.ForeignAttributes["{urn:test}keep"]);
        Assert.Equal(before.Diagrams[0].Plane.Shapes[0].Bounds, after.Diagrams[0].Plane.Shapes[0].Bounds);
    }

    [Fact]
    public void ApplyMultiSelectionIsOneUndoAndDoesNotChangeOtherDiagram()
    {
        var original = BpmnXmlReader.Read(Xml).Document;
        var other = original.Diagrams[0] with { Id = "Other" };
        var before = original with { Diagrams = [original.Diagrams[0], other] };
        var history = new BpmnEditHistory(before);

        var result = history.Apply(new SetColorsCommand("Diagram", ["A", "Flow"], BpmnColorChannel.Border, "#102030"));

        Assert.True(result.IsApplied);
        Assert.Equal("#102030", result.Document.Diagrams[0].Plane.Shapes[0].BorderColor);
        Assert.Equal("#102030", result.Document.Diagrams[0].Plane.Edges[0].BorderColor);
        Assert.Same(other, result.Document.Diagrams[1]);
        Assert.Equal("#aAbB09", result.Document.Diagrams[0].Plane.Shapes[0].BackgroundColor);
        Assert.DoesNotContain($"{{{BpmnDiColors.BiocNamespace}}}stroke",
            result.Document.Diagrams[0].Plane.Shapes[0].Extras.ForeignAttributes.Keys);
        Assert.Same(before, history.Undo());
        Assert.False(history.CanUndo);
        Assert.Same(result.Document, history.Redo());
    }

    [Theory]
    [InlineData("#abc", "A")]
    [InlineData("#123456", "missing")]
    public void ApplyInvalidRequestRejectsWholeSelection(string color, string target)
    {
        var document = BpmnXmlReader.Read(Xml).Document;

        var result = BpmnDocumentEditor.Apply(document,
            new SetColorsCommand("Diagram", ["B", target], BpmnColorChannel.Border, color));

        Assert.False(result.IsApplied);
        Assert.Same(document, result.Document);
        Assert.NotEmpty(result.Rejections);
    }

    [Fact]
    public void ApplyTextOnUnlabeledShapeCreatesColorOnlyLabel()
    {
        var document = BpmnXmlReader.Read(Xml).Document;

        var result = BpmnDocumentEditor.Apply(document,
            new SetColorsCommand("Diagram", ["B"], BpmnColorChannel.Text, "#010203"));

        Assert.Equal("#010203", result.Document.Diagrams[0].Plane.Shapes[1].Label!.Color);
        Assert.Null(result.Document.Diagrams[0].Plane.Shapes[1].Label!.Bounds);
    }

    [Fact]
    public void ApplyRepeatedColorDoesNotAddHistoryEntry()
    {
        var history = new BpmnEditHistory(BpmnXmlReader.Read(Xml).Document);
        var command = new SetColorsCommand("Diagram", ["A"], BpmnColorChannel.Text, "#010203");
        history.Apply(command);

        var repeated = history.Apply(command);

        Assert.False(repeated.IsApplied);
        Assert.Empty(repeated.Diagnostics);
        history.Undo();
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void InvalidMiWgFallsBackToBiocAndJsonReportsInvalidSource()
    {
        var imported = BpmnXmlReader.Read(Xml.Replace("#aAbB09", "#abc", StringComparison.Ordinal));

        var result = BpmnJsonReader.Read(BpmnJsonWriter.Write(imported.Document));

        Assert.Equal("#FF0000", result.Document.Diagrams[0].Plane.Shapes[0].BackgroundColor);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Message.Contains("Color attribute", StringComparison.Ordinal));
    }

    [Fact]
    public void BackgroundIgnoresSelectedEdgesAndPreservesOtherChannels()
    {
        var document = BpmnXmlReader.Read(Xml).Document;

        var result = BpmnDocumentEditor.Apply(document,
            new SetColorsCommand("Diagram", ["A", "Flow"], BpmnColorChannel.Background, "#010203"));

        Assert.True(result.IsApplied);
        Assert.Equal("#010203", result.Document.Diagrams[0].Plane.Shapes[0].BackgroundColor);
        Assert.Same(document.Diagrams[0].Plane.Edges[0], result.Document.Diagrams[0].Plane.Edges[0]);
        Assert.Equal("#ABCDEF", result.Document.Diagrams[0].Plane.Shapes[0].Label!.Color);
    }

    [Fact]
    public void ClipboardPastePreservesShapeEdgeAndLabelColors()
    {
        var document = BpmnXmlReader.Read(Xml).Document;
        var copied = BpmnClipboard.Copy(document, ["A", "B"]);

        var result = BpmnDocumentEditor.Apply(document, new PasteCommand(copied, "Process", new DiPoint(0, 200)));

        Assert.True(result.IsApplied);
        var node = result.Document.FlowNodes.Single(item => result.CreatedIds.Contains(item.Id) && item.Name == "First");
        var shape = result.Document.Diagrams[0].Plane.Shapes.Single(item => item.BpmnElement == node.Id);
        Assert.Equal("#aAbB09", shape.BackgroundColor);
        Assert.Equal("#123456", shape.BorderColor);
        Assert.Equal("#ABCDEF", shape.Label!.Color);
        Assert.Equal("label", shape.Label.Extras.ForeignAttributes["{urn:test}keep"]);
        var edge = result.Document.Diagrams[0].Plane.Edges.Single(item => item.BpmnElement != "Flow");
        Assert.Equal("#987654", edge.BorderColor);
        Assert.Equal("#FEDCBA", edge.Label!.Color);
    }
}
