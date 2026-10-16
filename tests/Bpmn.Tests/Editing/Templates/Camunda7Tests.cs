using System.Xml.Linq;
using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions.Templates;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;
using Hasseware.AspNetCore.Components.Bpmn.Validation;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Editing.Templates;

public sealed class Camunda7Tests
{
    private const string Uri = BpmnTemplateMarkers.CamundaNamespaceUri;
    private const string Prefix = "scope:camunda:Connector/";
    private const string ConnectorJson = """
        { "$schema":"https://unpkg.com/@camunda/element-templates-json-schema/resources/schema.json",
          "id":"mail", "name":"Mail connector", "version":1, "appliesTo":["bpmn:ServiceTask"],
          "properties":[
            {"id":"name","type":"String","value":"Mail","binding":{"type":"property","name":"name"}},
            {"id":"id","type":"Hidden","generatedValue":{"type":"uuid"},"binding":{"type":"camunda:property","name":"request-id"}}
          ],
          "scopes":[{"type":"camunda:Connector","properties":[
            {"type":"Hidden","value":"mail-send","binding":{"type":"property","name":"connectorId"}},
            {"id":"mode","type":"Dropdown","value":"text","choices":[{"name":"Text","value":"text"},{"name":"HTML","value":"html"}],"binding":{"type":"camunda:inputParameter","name":"mode"}},
            {"id":"body","type":"String","value":"hello","constraints":{"notEmpty":true},"binding":{"type":"camunda:inputParameter","name":"body"}},
            {"id":"html","type":"Text","value":"<p>hello</p>","condition":{"property":"mode","equals":"html"},"binding":{"type":"camunda:inputParameter","name":"html"}},
            {"id":"result","type":"String","value":"sent","binding":{"type":"camunda:outputParameter","source":"response"}}
          ]}]
        }
        """;

    [Fact]
    public void ConnectorApplyConditionsUpgradeRemoveRoundTripAndUndo()
    {
        var template = Connector();
        var original = Document();
        var history = new BpmnEditHistory(original);
        Assert.True(history.Apply(new ApplyElementTemplateCommand("Task", template)).IsApplied);
        var applied = history.Document;
        var xml = BpmnXmlWriter.Write(applied).Xml;
        Assert.Contains("camunda:modelerTemplate=\"mail\"", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("zeebe:modelerTemplate", xml, StringComparison.Ordinal);
        Assert.Contains("<camunda:connectorId>mail-send</camunda:connectorId>", xml, StringComparison.Ordinal);
        Assert.Contains("<camunda:outputParameter name=\"sent\">response</camunda:outputParameter>", xml, StringComparison.Ordinal);
        Assert.Null(Values(applied, template)[Prefix + "html"]);
        Assert.True(history.Apply(new SetTemplatePropertyCommand("Task", template, Prefix + "mode", "html")).IsApplied);
        Assert.Equal("<p>hello</p>", Values(history.Document, template)[Prefix + "html"]);
        Assert.True(history.Apply(new SetTemplatePropertyCommand("Task", template, Prefix + "html", "mine")).IsApplied);
        Assert.True(history.Apply(new SetTemplatePropertyCommand("Task", template, Prefix + "mode", "text")).IsApplied);
        Assert.Null(Values(history.Document, template)[Prefix + "html"]);
        history.Undo(); Assert.Equal("mine", Values(history.Document, template)[Prefix + "html"]);
        history.Undo(); history.Undo(); Assert.Same(applied, history.Document);
        history.Undo(); Assert.Same(original, history.Document);
        var edited = Apply(applied, new SetTemplatePropertyCommand("Task", template, Prefix + "body", "user body"));
        var next = template with { Version = 2, Scopes = template.Scopes.Select(s => s with { Properties = s.Properties.Select(p => p.Id == "body" ? p with { Value = "new" } : p).ToArray() }).ToArray() };
        var upgraded = Apply(edited, new UpgradeElementTemplateCommand("Task", template, next));
        Assert.Equal("user body", Values(upgraded, next)[Prefix + "body"]);
        upgraded = BpmnXmlReader.Read(BpmnXmlWriter.Write(upgraded).Xml).Document;
        Assert.Equal("user body", Values(upgraded, next)[Prefix + "body"]);
        var registry = new BpmnElementTemplateRegistry(); registry.Add(next);
        Assert.Same(next, BpmnAppliedTemplate.Resolve(upgraded, "Task", registry).Template);
        var removed = Apply(upgraded, new RemoveElementTemplateCommand("Task", next));
        Assert.DoesNotContain("<camunda:connector", BpmnXmlWriter.Write(removed, BpmnWriteOptions.Canonical with { AllowLossyCanonicalExport = true }).Xml, StringComparison.Ordinal);
        Assert.Null(Values(removed, next)[Prefix + "body"]);
    }

    [Fact]
    public void ScopedGroupsValidateAndDedupeTemplateBindings()
    {
        var template = Connector();
        var options = new BpmnOptions(); options.Extensions.AddCamunda7(); options.Templates.Registry.Add(template);
        var doc = Apply(Document(), new ApplyElementTemplateCommand("Task", template));
        var snapshot = BpmnElementInspector.Describe(doc, "Task", extensionOptions: options.Extensions, templateOptions: options.Templates)!;
        var group = snapshot.ExtensionGroups.Single(g => g.Id == "template:scope:camunda:Connector");
        Assert.Equal("Connector", group.Label);
        Assert.Contains(group.Entries, e => e.Id == "template:" + Prefix + "body");
        Assert.DoesNotContain(snapshot.ExtensionProperties, p => p.Value is "hello" or "response" or "mail-send");
        Assert.DoesNotContain(snapshot.ExtensionGroups.Where(g => !g.Id.StartsWith("template:", StringComparison.Ordinal)).SelectMany(g => g.Entries), e => e.Value == "mail-send" || e.Id == "inputs-connector");
        doc = Apply(doc, new SetTemplatePropertyCommand("Task", template, Prefix + "body", ""));
        Assert.Contains(new BpmnElementTemplateValidator(options.Templates).Validate(doc), d => d.RuleId == "PROF-101");
        var transformed = new BpmnTemplatePasteTransform(options.Templates.Registry).Transform(doc, ["Task"], EditingOptions.Default);
        Assert.NotEqual(Values(doc, template)["id"], Values(transformed, template)["id"]);
        Assert.Equal("", Values(transformed, template)[Prefix + "body"]);
    }

    [Fact]
    public void BindingFamiliesMatchCamundaSemantics()
    {
        var template = Template(
            Property("foreign", "property", "org.example.Work", ("name", "camunda:class")),
            Property("property", "camunda:property", "value", ("name", "metadata")),
            Property("input", "camunda:inputParameter", "return 1", ("name", "scripted"), ("scriptFormat", "groovy")),
            Property("output", "camunda:outputParameter", "target", ("source", "return 2"), ("scriptFormat", "groovy")),
            Property("field", "camunda:field", "${service}", ("name", "injected"), ("expression", "true")),
            Property("listener", "camunda:executionListener", "println 3", ("event", "start"), ("implementationType", "script"), ("scriptFormat", "groovy")) with { Type = BpmnTemplatePropertyType.Hidden });
        var document = Apply(Document(), new ApplyElementTemplateCommand("Task", template));
        var values = Values(document, template);
        Assert.Equal("org.example.Work", values["foreign"]);
        Assert.Equal("return 1", values["input"]);
        Assert.Equal("target", values["output"]);
        Assert.Equal("${service}", values["field"]);
        Assert.Equal("println 3", values["listener"]);
        var root = XDocument.Parse(BpmnXmlWriter.Write(document).Xml);
        XNamespace ns = Uri;
        Assert.Contains(root.Descendants(ns + "script"), s => s.Attribute("scriptFormat")?.Value == "groovy" && s.Value == "return 2");
        Assert.Contains(root.Descendants(ns + "field"), f => f.Attribute("expression")?.Value == "${service}");
        document = BpmnXmlReader.Read(BpmnXmlWriter.Write(document).Xml).Document;
        Assert.Equal("target", Values(document, template)["output"]);
        var removed = Apply(document, new RemoveElementTemplateCommand("Task", template));
        Assert.All(Values(removed, template).Values, value => Assert.Null(value));
    }

    [Theory]
    [InlineData("class")]
    [InlineData("expression")]
    [InlineData("delegateExpression")]
    public void ExecutionListenerImplementationsAndStringFields(string implementation)
    {
        var template = Template(Property("listener", "camunda:executionListener", "value", ("event", "end"), ("implementationType", implementation)) with { Type = BpmnTemplatePropertyType.Hidden },
            Property("field", "camunda:field", "text", ("name", "text")));
        var doc = Apply(Document(), new ApplyElementTemplateCommand("Task", template));
        Assert.Equal("value", Values(doc, template)["listener"]);
        Assert.Contains("stringValue=\"text\"", BpmnXmlWriter.Write(doc).Xml, StringComparison.Ordinal);
    }

    [Fact]
    public void CallMappingsSourceExpressionsVariablesBusinessKeyAndEditing()
    {
        var template = Template(
            Property("input", "camunda:in", "${customer}", ("target", "customer"), ("expression", "true"), ("variables", "local")),
            Property("all", "camunda:in", "ignored", ("variables", "all")) with { Type = BpmnTemplatePropertyType.Hidden },
            Property("business", "camunda:in:businessKey", "${businessKey}"),
            Property("output", "camunda:out", "answer", ("sourceExpression", "${result}")),
            Property("local", "camunda:out", "ignored", ("variables", "local")) with { Type = BpmnTemplatePropertyType.Hidden }) with
        { AppliesTo = ["bpmn:CallActivity"] };
        var doc = Apply(Document(BpmnFlowNodeType.CallActivity), new ApplyElementTemplateCommand("Task", template));
        var values = Values(doc, template);
        Assert.Equal("${customer}", values["input"]); Assert.Equal("all", values["all"]);
        Assert.Equal("${businessKey}", values["business"]); Assert.Equal("answer", values["output"]);
        var xml = BpmnXmlWriter.Write(doc).Xml;
        Assert.Contains("sourceExpression=\"${customer}\"", xml, StringComparison.Ordinal);
        Assert.Contains("local=\"true\"", xml, StringComparison.Ordinal);
        doc = Apply(doc, new SetTemplatePropertyCommand("Task", template, "output", "changed"));
        Assert.Equal("changed", Values(doc, template)["output"]);
        doc = Apply(doc, new RemoveElementTemplateCommand("Task", template));
        Assert.Empty(doc.FlowNodes[0].Extras.Extensions);
    }

    [Fact]
    public void ErrorScopeCreatesLinkedRootUpdatesUpgradesRemovesAndPasteIsolates()
    {
        var template = Template(
            Property("external", "property", "external", ("name", "camunda:type")) with { Type = BpmnTemplatePropertyType.Hidden },
            Property("topic", "property", "work", ("name", "camunda:topic")),
            Property("throw", "camunda:errorEventDefinition", "${failed}", ("errorRef", "mail")) with { Type = BpmnTemplatePropertyType.Hidden }) with
        { Scopes = [new("bpmn:Error", [Property("name", "property", "Mail failure", ("name", "name")), Property("code", "property", "MAIL", ("name", "errorCode")), Property("message", "property", "Send failed", ("name", "camunda:errorMessage"))], "mail")] };
        var original = Document();
        var history = new BpmnEditHistory(original);
        Assert.True(history.Apply(new ApplyElementTemplateCommand("Task", template)).IsApplied);
        var applied = history.Document;
        var error = Assert.Single(applied.Catalog.Errors);
        Assert.Equal("Mail failure", error.Name); Assert.Equal("MAIL", error.ErrorCode);
        Assert.Equal("Send failed", error.Extras.ForeignAttributes["{" + Uri + "}errorMessage"]);
        var xml = BpmnXmlWriter.Write(applied).Xml;
        Assert.Contains("errorRef=\"" + error.Id + "\"", xml, StringComparison.Ordinal);
        applied = BpmnXmlReader.Read(xml).Document;
        Assert.Equal("Mail failure", Values(applied, template)["scope:bpmn:Error:mail/name"]);
        var edited = Apply(applied, new SetTemplatePropertyCommand("Task", template, "scope:bpmn:Error:mail/code", "CHANGED"));
        Assert.Equal("CHANGED", edited.Catalog.Errors[0].ErrorCode);
        var upgraded = Apply(edited, new UpgradeElementTemplateCommand("Task", template, template with { Version = 2 }));
        Assert.Equal("CHANGED", Values(upgraded, template)["scope:bpmn:Error:mail/code"]);
        var registry = new BpmnElementTemplateRegistry(); registry.Add(template);
        var pasted = new BpmnTemplatePasteTransform(registry).Transform(edited, ["Task"], EditingOptions.Default);
        Assert.Equal(2, pasted.Catalog.Errors.Count);
        Assert.Equal("CHANGED", Values(pasted, template)["scope:bpmn:Error:mail/code"]);
        var removed = Apply(edited, new RemoveElementTemplateCommand("Task", template));
        Assert.DoesNotContain(removed.FlowNodes[0].Extras.Extensions, e => e.LocalName == "errorEventDefinition");
        Assert.Null(removed.Catalog.Errors[0].ErrorCode);
        history.Undo(); Assert.Same(original, history.Document);
        var invalid = template with { Properties = template.Properties.Skip(1).ToArray() };
        Assert.False(BpmnDocumentEditor.Apply(Document(), new ApplyElementTemplateCommand("Task", invalid)).IsApplied);
    }

    [Theory]
    [InlineData("executionListener", false)]
    [InlineData("inputParameter", true)]
    public void CamundaRowsKeepIdentityAcrossReorderDeleteAndAdd(string itemName, bool structured)
    {
        BpmnExtensionElement Item(string name) => new(Uri, itemName,
            new Dictionary<string, string> { ["name"] = name, ["class"] = name, ["custom"] = name + " opaque" },
            structured ? [] : [new("urn:opaque", name, new Dictionary<string, string>(), [])], name + " text");
        var first = Item("first");
        var second = Item("second");
        var complex = new BpmnExtensionElement(Uri, itemName, new Dictionary<string, string> { ["name"] = "structured" },
            [new(Uri, "list", new Dictionary<string, string>(), [])]);
        var items = structured ? new[] { complex, first, second } : new[] { first, second };
        var document = Document();
        document = document with
        {
            FlowNodes = [document.FlowNodes[0] with
            {
                Extras = new BpmnElementExtras
                {
                    Extensions = structured ? [new(Uri, "inputOutput", new Dictionary<string, string>(), items)] : items
                }
            }]
        };
        var options = new BpmnOptions(); options.Extensions.AddCamunda7();
        var snapshot = BpmnElementInspector.Describe(document, "Task", extensionOptions: options.Extensions)!;
        var entry = snapshot.ExtensionGroups.SelectMany(group => group.Entries)
            .Single(entry => entry.Id == (structured ? "inputs" : "execution-listeners"));
        Assert.Equal(structured ? ["1", "2"] : ["0", "1"], entry.Rows!.Select(row => row["$index"]));
        foreach (var operation in new[] { "reorder", "delete", "add" })
        {
            IReadOnlyList<IReadOnlyDictionary<string, string?>> rows = operation switch
            {
                "reorder" => [entry.Rows![1], entry.Rows![0]],
                "delete" => [entry.Rows![1]],
                _ => [entry.Rows![1], new Dictionary<string, string?> { [structured ? "name" : "class"] = "new" }, entry.Rows![0]]
            };
            var changed = Apply(document, BpmnPropertyEntryCommands.ToCommand("Task", entry, null, rows)!);
            var extensions = changed.FlowNodes[0].Extras.Extensions;
            var actual = structured ? extensions[0].Children : extensions;
            if (structured) Assert.Same(complex, actual[0]);
            var editable = actual.Where(item => !ReferenceEquals(item, complex)).ToArray();
            Assert.Equal("second opaque", editable[0].Attributes["custom"]);
            Assert.Equal(second.Children, editable[0].Children);
            if (operation == "add")
            {
                Assert.Empty(editable[1].Children);
                Assert.False(editable[1].Attributes.ContainsKey("custom"));
            }
            if (operation != "delete")
            {
                Assert.Equal("first opaque", editable[^1].Attributes["custom"]);
                Assert.Equal(first.Children, editable[^1].Children);
            }
            Assert.DoesNotContain("$index", BpmnXmlWriter.Write(changed).Xml, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void SchemaGroupsEditUndoAndPreserveStructuredParametersAndListenerFields()
    {
        var doc = BpmnXmlReader.Read("""
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL" xmlns:camunda="http://camunda.org/schema/1.0/bpmn" id="Definitions" targetNamespace="urn:test">
              <bpmn:process id="Process">
                <bpmn:serviceTask id="Task" camunda:class="Old" camunda:asyncBefore="true">
                  <bpmn:extensionElements>
                    <camunda:inputOutput>
                      <camunda:inputParameter name="complex"><camunda:script scriptFormat="groovy">return 1</camunda:script></camunda:inputParameter>
                      <camunda:inputParameter name="simple">old</camunda:inputParameter>
                      <camunda:inputParameter name="list"><camunda:list><camunda:value>a</camunda:value></camunda:list></camunda:inputParameter>
                    </camunda:inputOutput>
                    <camunda:executionListener event="start" class="Listener"><camunda:field name="nested" stringValue="keep"/></camunda:executionListener>
                    <camunda:properties><camunda:property name="key" value="value"/></camunda:properties>
                  </bpmn:extensionElements>
                </bpmn:serviceTask>
                <bpmn:userTask id="User" camunda:assignee="alice"/>
                <bpmn:callActivity id="Call" camunda:calledElementBinding="latest"/>
              </bpmn:process>
            </bpmn:definitions>
            """).Document;
        var options = new BpmnOptions(); options.Extensions.AddCamunda7(); options.Extensions.AlwaysOfferRegisteredSchemas = false;
        var snapshot = BpmnElementInspector.Describe(doc, "Task", extensionOptions: options.Extensions)!;
        Assert.Contains(snapshot.ExtensionGroups, g => g.Label == "Implementation");
        Assert.Contains(snapshot.ExtensionGroups, g => g.Label == "Asynchronous continuations");
        Assert.Empty(snapshot.ExtensionProperties);
        var entries = snapshot.ExtensionGroups.SelectMany(g => g.Entries).ToArray();
        Assert.Equal(3, entries.Count(e => e.Kind == BpmnPropertyEntryKind.ReadOnly));
        var input = entries.Single(e => e.Id == "inputs");
        Assert.Single(input.Rows!); Assert.Equal("old", input.Rows![0]["#text"]);
        var history = new BpmnEditHistory(doc);
        var rows = new IReadOnlyDictionary<string, string?>[] { new Dictionary<string, string?> { ["name"] = "simple", ["#text"] = "new" } };
        Assert.True(history.Apply(BpmnPropertyEntryCommands.ToCommand("Task", input, null, rows)!).IsApplied);
        var xml = BpmnXmlWriter.Write(history.Document, BpmnWriteOptions.Canonical with { AllowLossyCanonicalExport = true }).Xml;
        Assert.Contains("return 1", xml, StringComparison.Ordinal); Assert.Contains("<camunda:list>", xml, StringComparison.Ordinal);
        Assert.Contains("<camunda:inputParameter name=\"simple\">new", xml, StringComparison.Ordinal);
        var listener = entries.Single(e => e.Id == "execution-listeners");
        Assert.Single(listener.Rows!);
        var listenerRows = listener.Rows!.Select(row => (IReadOnlyDictionary<string, string?>)new Dictionary<string, string?>(row) { ["class"] = "ChangedListener" }).ToArray();
        Assert.True(history.Apply(BpmnPropertyEntryCommands.ToCommand("Task", listener, null, listenerRows)!).IsApplied);
        Assert.Contains("stringValue=\"keep\"", BpmnXmlWriter.Write(history.Document, BpmnWriteOptions.Canonical with { AllowLossyCanonicalExport = true }).Xml, StringComparison.Ordinal);
        history.Undo();
        var foreign = entries.Single(e => e.Id == "camunda:class");
        Assert.True(history.Apply(BpmnPropertyEntryCommands.ToCommand("Task", foreign, "New")!).IsApplied);
        Assert.Equal("New", history.Document.FlowNodes[0].Extras.ForeignAttributes["{" + Uri + "}class"]);
        history.Undo(); history.Undo(); Assert.Same(doc, history.Document);
        var cleared = Apply(doc, new ReplaceCamundaCollectionCommand("Task", "{" + Uri + "}inputOutput", "inputParameter", []));
        Assert.Contains("return 1", BpmnXmlWriter.Write(cleared, BpmnWriteOptions.Canonical with { AllowLossyCanonicalExport = true }).Xml, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"simple\"", BpmnXmlWriter.Write(cleared, BpmnWriteOptions.Canonical with { AllowLossyCanonicalExport = true }).Xml, StringComparison.Ordinal);
        Assert.Contains(BpmnElementInspector.Describe(doc, "User", extensionOptions: options.Extensions)!.ExtensionGroups, g => g.Label == "User assignment");
        Assert.Contains(BpmnElementInspector.Describe(doc, "Call", extensionOptions: options.Extensions)!.ExtensionGroups, g => g.Label == "In mappings");
    }

    [Fact]
    public void MixedEngineHostsOfferOnlyDeclaredOrUsedSchemasAndFlavorRoutingIsStrict()
    {
        var options = new BpmnOptions(); options.Extensions.AddZeebe().AddCamunda7(); options.Extensions.AlwaysOfferRegisteredSchemas = false;
        Assert.Empty(BpmnElementInspector.Describe(Document(), "Task", extensionOptions: options.Extensions)!.ExtensionGroups);
        var doc = Apply(Document(), new ApplyElementTemplateCommand("Task", Connector()));
        Assert.All(BpmnElementInspector.Describe(doc, "Task", extensionOptions: options.Extensions)!.ExtensionGroups, g => Assert.Equal(Uri, g.Namespace));
        var registry = new BpmnElementTemplateRegistry(); registry.Add(Connector() with { Flavor = BpmnTemplateFlavor.Zeebe });
        Assert.True(BpmnAppliedTemplate.Resolve(doc, "Task", registry).IsMissing);
    }

    [Fact]
    public void ScopedIdsAndConditionsUseScopeKeysWithoutGlobalCollisions()
    {
        var loaded = BpmnElementTemplateLoader.Load(ConnectorJson.Replace("\"id\":\"name\"", "\"id\":\"mode\"", StringComparison.Ordinal));
        Assert.Empty(loaded.Errors);
        Assert.Single(loaded.Templates);
    }

    [Fact]
    public void ErrorBindingBeforeExternalPropertyAndNonXmlScopeIdRemainValid()
    {
        var template = Template(
            Property("error", "camunda:errorEventDefinition", "${failed}", ("errorRef", "mail.failure")) with { Type = BpmnTemplatePropertyType.Hidden },
            Property("external", "property", "external", ("name", "camunda:type")) with { Type = BpmnTemplatePropertyType.Hidden }) with
        { Scopes = [new("bpmn:Error", [Property("code", "property", "FAIL", ("name", "errorCode"))], "mail.failure")] };
        var doc = Apply(Document(), new ApplyElementTemplateCommand("Task", template));
        Assert.Single(doc.Catalog.Errors);
        Assert.Equal("FAIL", Values(doc, template)["scope:bpmn:Error:mail.failure/code"]);
        doc = BpmnXmlReader.Read(BpmnXmlWriter.Write(doc).Xml).Document;
        Assert.Equal("FAIL", Values(doc, template)["scope:bpmn:Error:mail.failure/code"]);
    }

    [Fact]
    public void SchemaCollectionsRetainStructuredRowsWhenSimpleRowsAreDeleted()
    {
        var doc = Document();
        var nested = new BpmnExtensionElement(Uri, "inputParameter", new Dictionary<string, string> { ["name"] = "nested" },
            [new(Uri, "script", new Dictionary<string, string> { ["scriptFormat"] = "groovy" }, [], "return 1")]);
        var simple = new BpmnExtensionElement(Uri, "inputParameter", new Dictionary<string, string> { ["name"] = "simple" }, [], "text");
        doc = doc with { FlowNodes = [doc.FlowNodes[0] with { Extras = new() { Extensions = [new(Uri, "inputOutput", new Dictionary<string, string>(), [nested, simple])] } }] };
        var options = new BpmnOptions(); options.Extensions.AddCamunda7();
        var snapshot = BpmnElementInspector.Describe(doc, "Task", extensionOptions: options.Extensions)!;
        var entry = snapshot.ExtensionGroups.SelectMany(g => g.Entries).Single(e => e.Id == "inputs");
        Assert.Single(entry.Rows!);
        doc = Apply(doc, BpmnPropertyEntryCommands.ToCommand("Task", entry, null, [])!);
        Assert.Same(nested, Assert.Single(Assert.Single(doc.FlowNodes[0].Extras.Extensions).Children));
        var invalid = BpmnDocumentEditor.Apply(doc, new ReplaceCamundaCollectionCommand("Task", "invalid", "executionListener", []));
        Assert.False(invalid.IsApplied); Assert.Same(doc, invalid.Document);
    }

    [Fact]
    public void SchemaConnectorTextAndNestedIoCollectionsAreEditable()
    {
        var template = Connector();
        var doc = Apply(Document(), new ApplyElementTemplateCommand("Task", template));
        doc = Apply(doc, new UnlinkElementTemplateCommand("Task"));
        var options = new BpmnOptions(); options.Extensions.AddCamunda7();
        var snapshot = BpmnElementInspector.Describe(doc, "Task", extensionOptions: options.Extensions)!;
        var entry = snapshot.ExtensionGroups.SelectMany(g => g.Entries).Single(e => e.Id == "connectorId");
        doc = Apply(doc, BpmnPropertyEntryCommands.ToCommand("Task", entry, "other")!);
        Assert.Equal("other", Values(doc, template)[Prefix + "#0"]);
        var input = snapshot.ExtensionGroups.SelectMany(g => g.Entries).Single(e => e.Id == "inputs-connector");
        var rows = input.Rows!.Select(row => (IReadOnlyDictionary<string, string?>)new Dictionary<string, string?>(row) { ["#text"] = "edited" }).ToArray();
        doc = Apply(doc, BpmnPropertyEntryCommands.ToCommand("Task", input, null, rows)!);
        Assert.Equal("edited", Values(doc, template)[Prefix + "body"]);
    }

    private static BpmnElementTemplate Connector()
    {
        var loaded = BpmnElementTemplateLoader.Load(ConnectorJson); Assert.Empty(loaded.Errors); return Assert.Single(loaded.Templates);
    }
    private static BpmnElementTemplate Template(params BpmnTemplateProperty[] properties) => new()
    { Id = "c7", Name = "C7", Flavor = BpmnTemplateFlavor.Camunda7, AppliesTo = ["bpmn:ServiceTask"], Properties = properties };
    private static BpmnTemplateProperty Property(string id, string type, string? value, params (string Key, string Value)[] parameters) => new()
    { Id = id, Value = value, Binding = new(type, parameters.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal)) };
    private static BpmnDocument Document(BpmnFlowNodeType type = BpmnFlowNodeType.ServiceTask) => new()
    { Definitions = new("Definitions", "urn:test"), Processes = [new("Process")], FlowNodes = [new("Task", type, "Process")] };
    private static IReadOnlyDictionary<string, string?> Values(BpmnDocument document, BpmnElementTemplate template) => BpmnTemplateValues.Read(document, "Task", template);
    private static BpmnDocument Apply(BpmnDocument document, BpmnEditCommand command)
    {
        var result = BpmnDocumentEditor.Apply(document, command); Assert.True(result.IsApplied, string.Join("; ", result.Diagnostics.Select(d => d.Message))); return result.Document;
    }
}
