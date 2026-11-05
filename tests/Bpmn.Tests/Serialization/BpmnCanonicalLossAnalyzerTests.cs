using System.Xml.Linq;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Serialization;

public sealed class BpmnCanonicalLossAnalyzerTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    public void SiblingMatchingPreservesDuplicateIdsNamespacesAndIdlessOrdinals(int padding)
    {
        var source = XDocument.Parse("""
            <root xmlns="urn:test" xmlns:other="urn:other">
              <item id="duplicate" first="yes" />
              <item id="duplicate" second="yes" />
              <item ordinal="two" />
              <other:item id="duplicate" namespace="other" />
              <item ordinal="three" />
              <!--same--><!--same--><?test value?>
            </root>
            """);
        var canonical = XDocument.Parse("""
            <root xmlns="urn:test" xmlns:other="urn:other">
              <item id="duplicate" first="yes" />
              <item id="duplicate" second="yes" />
              <item ordinal="two" />
              <item ordinal="three" />
              <other:item id="duplicate" namespace="other" />
              <!--same-->
            </root>
            """);
        for (var index = 0; index < padding; index++)
        {
            canonical.Root!.Add(new XElement(XName.Get("padding", "urn:padding"), new XAttribute("id", $"padding_{index}")));
        }
        var original = new XDocument(source);

        var diagnostics = BpmnCanonicalLossAnalyzer.FindLosses(source, canonical, []);

        Assert.Equal([
            "Canonical output cannot preserve source attribute 'second'.",
            "Canonical output cannot preserve the source Comment.",
            "Canonical output cannot preserve the source ProcessingInstruction.",
        ], diagnostics.Select(diagnostic => diagnostic.Message));
        Assert.Equal("duplicate", diagnostics[0].ElementId);
        Assert.True(XNode.DeepEquals(original, source));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(12)]
    public void MissingForeignChildrenRetainSourceDiagnosticOrder(int padding)
    {
        var source = XDocument.Parse("""<root><first id="a" /><second /><first id="b" /></root>""");
        var canonical = new XDocument(new XElement("root",
            Enumerable.Range(0, padding).Select(index => new XElement("padding"))));

        var diagnostics = BpmnCanonicalLossAnalyzer.FindLosses(source, canonical, []);

        Assert.Equal(["a", null, "b"], diagnostics.Select(diagnostic => diagnostic.ElementId));
        Assert.Equal([
            "Canonical output cannot preserve unmodeled <first> content.",
            "Canonical output cannot preserve unmodeled <second> content.",
            "Canonical output cannot preserve unmodeled <first> content.",
        ], diagnostics.Select(diagnostic => diagnostic.Message));
    }
}
