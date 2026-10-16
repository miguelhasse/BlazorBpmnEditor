using System.Text;
using System.Text.Json;

using Hasseware.AspNetCore.Components.Bpmn.Interop;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Serialization;

/// <summary>
/// A document is input from outside: how deep it nests and what numbers it carries must not be able to end the process or break what is built from it.
/// </summary>
public sealed class BpmnXmlHostileInputTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private const string Open = """
        <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                     xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                     xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                     xmlns:di="http://www.omg.org/spec/DD/20100524/DI"
                     xmlns:x="urn:vendor" id="d" targetNamespace="t">
        """;

    private static string NestedExtensions(int depth)
    {
        var text = new StringBuilder(Open);
        text.Append("<process id=\"p\"><task id=\"t1\"><extensionElements>");
        for (var level = 0; level < depth; level++) text.Append("<x:a>");
        for (var level = 0; level < depth; level++) text.Append("</x:a>");
        text.Append("</extensionElements></task></process></definitions>");
        return text.ToString();
    }

    private static string NestedSubProcesses(int depth)
    {
        var text = new StringBuilder(Open);
        text.Append("<process id=\"p\">");
        for (var level = 0; level < depth; level++) text.Append(System.Globalization.CultureInfo.InvariantCulture, $"<subProcess id=\"s{level}\">");
        for (var level = 0; level < depth; level++) text.Append("</subProcess>");
        text.Append("</process></definitions>");
        return text.ToString();
    }

    private static string NestedLaneSets(int depth)
    {
        var text = new StringBuilder(Open);
        text.Append("<process id=\"p\"><laneSet id=\"ls\">");
        for (var level = 0; level < depth; level++) text.Append(System.Globalization.CultureInfo.InvariantCulture, $"<lane id=\"l{level}\"><childLaneSet id=\"c{level}\">");
        for (var level = 0; level < depth; level++) text.Append("</childLaneSet></lane>");
        text.Append("</laneSet></process></definitions>");
        return text.ToString();
    }

    [Theory]
    [InlineData(300)]
    [InlineData(100_000)]
    public void RefusesExtensionElementsNestedTooDeepInsteadOfOverflowingTheStack(int depth) =>
        Assert.Throws<BpmnXmlException>(() => BpmnXmlReader.Read(NestedExtensions(depth)));

    [Theory]
    [InlineData(300)]
    [InlineData(100_000)]
    public void RefusesSubProcessesNestedTooDeep(int depth) =>
        Assert.Throws<BpmnXmlException>(() => BpmnXmlReader.Read(NestedSubProcesses(depth)));

    [Theory]
    [InlineData(300)]
    [InlineData(100_000)]
    public void RefusesLaneSetsNestedTooDeep(int depth) =>
        Assert.Throws<BpmnXmlException>(() => BpmnXmlReader.Read(NestedLaneSets(depth)));

    [Fact]
    public void StillReadsADocumentNestedWellWithinTheLimit()
    {
        var result = BpmnXmlReader.Read(NestedExtensions(100));

        Assert.NotNull(result.Document);
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("1e999")]
    public void IgnoresANumberThatIsNotFiniteAndSaysSoSoNothingBuiltFromTheDocumentBreaks(string number)
    {
        var xml = Open + $$"""
              <process id="p"><task id="t1" /><task id="t2" /><sequenceFlow id="f" sourceRef="t1" targetRef="t2" /></process>
              <bpmndi:BPMNDiagram id="dg"><bpmndi:BPMNPlane id="pl" bpmnElement="p">
                <bpmndi:BPMNShape id="s1" bpmnElement="t1"><dc:Bounds x="{{number}}" y="10" width="100" height="80" /></bpmndi:BPMNShape>
                <bpmndi:BPMNShape id="s2" bpmnElement="t2"><dc:Bounds x="200" y="10" width="{{number}}" height="80" /></bpmndi:BPMNShape>
                <bpmndi:BPMNEdge id="e" bpmnElement="f"><di:waypoint x="100" y="{{number}}" /><di:waypoint x="200" y="50" /></bpmndi:BPMNEdge>
              </bpmndi:BPMNPlane></bpmndi:BPMNDiagram>
            </definitions>
            """;

        var result = BpmnXmlReader.Read(xml);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Message.Contains("not a finite number", StringComparison.Ordinal));

        // What a canvas load and a JSON export do with the document is what used to throw on a non-finite double.
        Assert.NotEmpty(CanvasDocumentPayload.Serialize(result.Document));
        Assert.NotEmpty(JsonSerializer.Serialize(result.Document, Web));
        foreach (var shape in result.Document.Diagrams.SelectMany(diagram => diagram.Plane.Shapes))
        {
            Assert.True(double.IsFinite(shape.Bounds.X) && double.IsFinite(shape.Bounds.Width));
        }
    }
}
