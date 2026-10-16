using System.Xml.Linq;

using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;
using Hasseware.AspNetCore.Components.Bpmn.Validation;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Serialization;

public sealed class RootElementExtrasTests
{
    private const string Zeebe = "http://camunda.org/schema/zeebe/1.0";
    private static readonly XNamespace Model = "http://www.omg.org/spec/BPMN/20100524/MODEL";

    private const string Xml = """
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
                          id="Definitions_1" targetNamespace="http://example.org">
          <bpmn:message id="Message_1" name="Order" zeebe:modelerTemplate="message-template">
            <bpmn:documentation>Message notes</bpmn:documentation>
            <bpmn:extensionElements><zeebe:subscription correlationKey="=x" /></bpmn:extensionElements>
          </bpmn:message>
          <bpmn:signal id="Signal_1" name="Ready" zeebe:modelerTemplate="signal-template">
            <bpmn:documentation>Signal notes</bpmn:documentation>
            <bpmn:extensionElements><zeebe:properties><zeebe:property name="scope" value="global" /></zeebe:properties></bpmn:extensionElements>
          </bpmn:signal>
          <bpmn:error id="Error_1" errorCode="failed" zeebe:modelerTemplate="error-template">
            <bpmn:documentation>Error notes</bpmn:documentation>
            <bpmn:extensionElements><zeebe:properties /></bpmn:extensionElements>
          </bpmn:error>
          <bpmn:escalation id="Escalation_1" escalationCode="retry" zeebe:modelerTemplate="escalation-template">
            <bpmn:documentation>Escalation notes</bpmn:documentation>
            <bpmn:extensionElements><zeebe:properties /></bpmn:extensionElements>
          </bpmn:escalation>
          <bpmn:process id="Process_1">
            <bpmn:startEvent id="Start_1">
              <bpmn:conditionalEventDefinition id="Conditional_1" zeebe:custom="condition-value">
                <bpmn:documentation>Condition notes</bpmn:documentation>
                <bpmn:extensionElements><zeebe:conditionalFilter variableName="x" variableEvents="create,update" /></bpmn:extensionElements>
                <bpmn:condition>=x &gt; 0</bpmn:condition>
              </bpmn:conditionalEventDefinition>
            </bpmn:startEvent>
            <bpmn:intermediateCatchEvent id="Catch_1">
              <bpmn:timerEventDefinition id="Timer_1" zeebe:custom="timer-value">
                <bpmn:documentation>Timer notes</bpmn:documentation>
                <bpmn:extensionElements><zeebe:properties><zeebe:property name="timer" value="delay" /></zeebe:properties></bpmn:extensionElements>
                <bpmn:timeDuration>PT1M</bpmn:timeDuration>
              </bpmn:timerEventDefinition>
            </bpmn:intermediateCatchEvent>
          </bpmn:process>
        </bpmn:definitions>
        """;

    [Fact]
    public void ImportPopulatesEveryNewCarrier()
    {
        var document = BpmnXmlReader.Read(Xml).Document;

        var message = Assert.Single(document.Catalog.Messages);
        Assert.Equal("message-template", message.Extras.ForeignAttributes[$"{{{Zeebe}}}modelerTemplate"]);
        Assert.Equal("Message notes", message.Extras.Documentation);
        Assert.Equal("=x", Assert.Single(message.Extras.Extensions).Attributes["correlationKey"]);
        var signal = Assert.Single(document.Catalog.Signals);
        Assert.Equal("global", Assert.Single(Assert.Single(signal.Extras.Extensions).Children).Attributes["value"]);
        Assert.Equal("error-template", Assert.Single(document.Catalog.Errors).Extras.ForeignAttributes[$"{{{Zeebe}}}modelerTemplate"]);
        Assert.Equal("escalation-template", Assert.Single(document.Catalog.Escalations).Extras.ForeignAttributes[$"{{{Zeebe}}}modelerTemplate"]);

        var conditional = Assert.Single(document.FindFlowNode("Start_1")!.EventDefinitions);
        Assert.Equal("conditionalFilter", Assert.Single(conditional.Extras.Extensions).LocalName);
        Assert.Equal("x", Assert.Single(conditional.Extras.Extensions).Attributes["variableName"]);
        Assert.Equal("Condition notes", conditional.Extras.Documentation);
        Assert.Equal("=x > 0", conditional.Expression);
        var timer = Assert.Single(document.FindFlowNode("Catch_1")!.EventDefinitions);
        Assert.Equal("properties", Assert.Single(timer.Extras.Extensions).LocalName);
        Assert.Equal("PT1M", timer.Expression);
        Assert.Empty(document.Catalog.Preserved);
    }

    [Theory]
    [InlineData(BpmnWriteMode.Canonical)]
    [InlineData(BpmnWriteMode.Fidelity)]
    public void ExtrasSurviveExactlyOnceAndPrecedeDefinitionContent(BpmnWriteMode mode)
    {
        var before = BpmnXmlReader.Read(Xml).Document;
        var result = BpmnXmlWriter.Write(before, new BpmnWriteOptions { Mode = mode });
        var after = BpmnXmlReader.Read(result.Xml).Document;

        Assert.Equal(before.Catalog.Messages, after.Catalog.Messages);
        Assert.Equal(before.Catalog.Signals, after.Catalog.Signals);
        Assert.Equal(before.Catalog.Errors, after.Catalog.Errors);
        Assert.Equal(before.Catalog.Escalations, after.Catalog.Escalations);
        foreach (var (id, extras) in Carriers(before)) AssertExtras(extras, Carriers(after).Single(pair => pair.Id == id).Extras);
        foreach (var node in before.FlowNodes)
        {
            Assert.Equal(node.EventDefinitions, after.FindFlowNode(node.Id)!.EventDefinitions);
        }

        var tree = XDocument.Parse(result.Xml);
        Assert.Equal(Zeebe, tree.Root!.GetNamespaceOfPrefix("zeebe")!.NamespaceName);
        foreach (var (id, _) in Carriers(before))
        {
            var element = Assert.Single(tree.Descendants(), element => (string?)element.Attribute("id") == id);
            Assert.Single(element.Elements(Model + "documentation"));
            Assert.Single(element.Elements(Model + "extensionElements"));
            var expected = id switch
            {
                "Conditional_1" => new[] { "documentation", "extensionElements", "condition" },
                "Timer_1" => new[] { "documentation", "extensionElements", "timeDuration" },
                _ => new[] { "documentation", "extensionElements" },
            };
            Assert.Equal(expected, element.Elements().Select(child => child.Name.LocalName));
        }
        Assert.Equal(BpmnXmlWriter.Write(after).Xml, BpmnXmlWriter.Write(before).Xml);
    }

    [Theory]
    [InlineData("Message_1")]
    [InlineData("Signal_1")]
    [InlineData("Error_1")]
    [InlineData("Escalation_1")]
    [InlineData("Conditional_1")]
    [InlineData("Timer_1")]
    public void ValidationReachesNewCarriers(string id)
    {
        var tree = XDocument.Parse(Xml);
        var owner = tree.Descendants().Single(element => (string?)element.Attribute("id") == id);
        owner.Element(Model + "extensionElements")!.Add(new XElement("unqualified"));
        owner.SetAttributeValue(XName.Get("href", Zeebe), "javascript:alert(1)");
        var document = BpmnXmlReader.Read(tree.ToString()).Document;

        var report = BpmnValidator.Validate(document);
        Assert.Contains(report.Diagnostics, diagnostic => diagnostic.RuleId == "EXT-001" && diagnostic.ElementId == id);
        Assert.Contains(report.Diagnostics, diagnostic => diagnostic.RuleId == "EXT-010" && diagnostic.ElementId == id);
    }

    [Fact]
    public void AnonymousEventDefinitionDiagnosticsUseOwnerId()
    {
        var tree = XDocument.Parse(Xml);
        var definition = tree.Descendants(Model + "conditionalEventDefinition").Single();
        definition.Attribute("id")!.Remove();
        definition.Element(Model + "extensionElements")!.Add(new XElement("unqualified"));

        var report = BpmnValidator.Validate(BpmnXmlReader.Read(tree.ToString()).Document);
        Assert.Contains(report.Diagnostics, diagnostic => diagnostic.RuleId == "EXT-001" && diagnostic.ElementId == "Start_1");
    }

    [Fact]
    public void ExtrasEqualityIgnoresAttributeInsertionOrderButIncludesChildOrder()
    {
        var extension = new BpmnExtensionElement(Zeebe, "first", new Dictionary<string, string>(), []);
        var other = extension with { LocalName = "second" };
        var extras = new BpmnElementExtras
        {
            Documentation = "Notes",
            ForeignAttributes = new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" },
            Extensions = [extension, other],
        };
        var equal = extras with
        {
            ForeignAttributes = new Dictionary<string, string> { ["b"] = "2", ["a"] = "1" },
            Extensions = [extension with { }, other with { }],
        };

        Assert.Equal(extras, equal);
        Assert.Equal(extras.GetHashCode(), equal.GetHashCode());
        Assert.NotEqual(extras, equal with { Extensions = [other, extension] });
        Assert.NotEqual(extras, equal with { Documentation = "Changed" });
        Assert.NotEqual(extras, equal with { ForeignAttributes = new Dictionary<string, string> { ["a"] = "9", ["b"] = "2" } });
        Assert.Equal(BpmnElementExtras.None, new BpmnElementExtras());
    }

    private static IEnumerable<(string Id, BpmnElementExtras Extras)> Carriers(BpmnDocument document)
    {
        foreach (var message in document.Catalog.Messages) yield return (message.Id, message.Extras);
        foreach (var signal in document.Catalog.Signals) yield return (signal.Id, signal.Extras);
        foreach (var error in document.Catalog.Errors) yield return (error.Id, error.Extras);
        foreach (var escalation in document.Catalog.Escalations) yield return (escalation.Id, escalation.Extras);
        foreach (var node in document.FlowNodes)
            foreach (var definition in node.EventDefinitions) yield return (definition.Id!, definition.Extras);
    }

    private static void AssertExtras(BpmnElementExtras expected, BpmnElementExtras actual)
    {
        Assert.Equal(expected, actual);
        Assert.Equal(expected.GetHashCode(), actual.GetHashCode());
        Assert.Equal(expected.Documentation, actual.Documentation);
        Assert.Equal(expected.ForeignAttributes.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            actual.ForeignAttributes.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        Assert.Equal(expected.Extensions, actual.Extensions);
    }
}
