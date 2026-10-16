using System.Xml.Linq;
using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions.Schema;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions.Templates;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Editing.Templates;

public sealed class TemplateEngineTests
{
    private const string Uri = BpmnTemplateMarkers.ZeebeNamespaceUri;
    private static readonly EditingOptions Options = EditingOptions.Default with
    {
        ExtensionChildOrder = new BpmnSchemaChildOrder(new BpmnExtensionRegistry().Register(BpmnExtensionSchemas.Zeebe))
    };
    private const string RestJson = """
        {
          "$schema": "https://unpkg.com/@camunda/zeebe-element-templates-json-schema/resources/schema.json", "name": "REST connector", "id": "io.camunda.rest", "version": 1,
          "appliesTo": ["bpmn:Task"], "elementType": {"value":"bpmn:ServiceTask"},
          "properties": [
            {"type":"Hidden","value":"io.camunda:http-json:1","binding":{"type":"zeebe:taskDefinition","property":"type"}},
            {"id":"auth","type":"Dropdown","value":"none","choices":[{"name":"None","value":"none"},{"name":"Basic","value":"basic"}],"binding":{"type":"zeebe:input","name":"authentication.type"}},
            {"id":"user","type":"String","value":"alice","condition":{"property":"auth","equals":"basic"},"binding":{"type":"zeebe:input","name":"authentication.username"}},
            {"id":"url","type":"String","value":"https://example.invalid","binding":{"type":"zeebe:input","name":"url"}},
            {"id":"optional","type":"String","value":"","optional":true,"binding":{"type":"zeebe:input","name":"optional"}},
            {"type":"Hidden","generatedValue":{"type":"uuid"},"binding":{"type":"zeebe:taskHeader","key":"request-id"}},
            {"id":"locked","type":"String","value":"locked","editable":false,"binding":{"type":"zeebe:property","name":"locked"}},
            {"id":"name","type":"String","value":"Call REST","binding":{"type":"property","name":"name"}}
          ]
        }
        """;

    [Fact]
    public void ApplyWritesDefaultsMarkersAndSchemaOrderAndUndoIsExact()
    {
        var original = Document();
        var history = new BpmnEditHistory(original, Options);
        var result = history.Apply(new ApplyElementTemplateCommand("Task", Rest()));
        Assert.True(result.IsApplied, Codes(result));
        Assert.Equal("Apply element template", history.UndoLabel);
        var node = result.Document.FindFlowNode("Task")!;
        Assert.Equal("Call REST", node.Name);
        Assert.Equal(["taskDefinition", "ioMapping", "taskHeaders", "properties"], node.Extras.Extensions.Where(e => e.Namespace == Uri).Select(e => e.LocalName));
        var values = BpmnTemplateValues.Read(result.Document, "Task", Rest());
        Assert.Equal("io.camunda:http-json:1", values["#0"]);
        Assert.True(Guid.TryParse(values["#5"], out _));
        Assert.Null(values["user"]);
        Assert.Null(values["optional"]);
        var xml = BpmnXmlWriter.Write(result.Document).Xml;
        Assert.Contains("xmlns:zeebe=", xml, StringComparison.Ordinal);
        Assert.Contains("zeebe:modelerTemplate=\"io.camunda.rest\"", xml, StringComparison.Ordinal);
        Assert.Contains("<zeebe:taskDefinition", xml, StringComparison.Ordinal);
        Assert.Contains("target=\"url\"", xml, StringComparison.Ordinal);
        history.Undo();
        Assert.Same(original, history.Document);
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void DropdownActivatesAndRemovesConditionalInputsInOneUndoStep()
    {
        var history = new BpmnEditHistory(Apply(Document(), new ApplyElementTemplateCommand("Task", Rest())), Options);
        var before = history.Document;
        Assert.True(history.Apply(new SetTemplatePropertyCommand("Task", Rest(), "auth", "basic")).IsApplied);
        Assert.Equal("alice", BpmnTemplateValues.Read(history.Document, "Task", Rest())["user"]);
        Assert.True(history.Apply(new SetTemplatePropertyCommand("Task", Rest(), "user", "bob")).IsApplied);
        Assert.True(history.Apply(new SetTemplatePropertyCommand("Task", Rest(), "auth", "none")).IsApplied);
        Assert.DoesNotContain("authentication.username", BpmnXmlWriter.Write(history.Document).Xml, StringComparison.Ordinal);
        history.Undo();
        Assert.Equal("bob", BpmnTemplateValues.Read(history.Document, "Task", Rest())["user"]);
        history.Undo(); history.Undo();
        Assert.Same(before, history.Document);
    }

    [Fact]
    public void UnlinkKeepsBindingsAndRemovePreservesNonTemplateContent()
    {
        var vendor = new BpmnExtensionElement("urn:vendor", "extra", new Dictionary<string, string> { ["value"] = "keep" }, []);
        var original = Document();
        original = original with { FlowNodes = [original.FlowNodes[0] with { Extras = new() { Extensions = [vendor] } }] };
        original = Apply(original, new SetExtensionValueCommand("Task", $"{{{Uri}}}ioMapping/{{{Uri}}}input[target=unrelated]@source", "keep-input"));
        var applied = Apply(original, new ApplyElementTemplateCommand("Task", Rest()));
        var unlinked = Apply(applied, new UnlinkElementTemplateCommand("Task"));
        Assert.Equal(applied.FindFlowNode("Task")!.Extras.Extensions, unlinked.FindFlowNode("Task")!.Extras.Extensions);
        Assert.Empty(unlinked.FindFlowNode("Task")!.Extras.ForeignAttributes);
        var removed = Apply(applied, new RemoveElementTemplateCommand("Task", Rest()));
        Assert.Contains(vendor, removed.FindFlowNode("Task")!.Extras.Extensions);
        Assert.Contains("unrelated", BpmnXmlWriter.Write(removed).Xml, StringComparison.Ordinal);
        Assert.DoesNotContain("authentication.type", BpmnXmlWriter.Write(removed).Xml, StringComparison.Ordinal);
    }

    [Fact]
    public void UpgradeKeepsEditableUserValuesAndRefreshesHiddenDefaults()
    {
        var old = Rest();
        var applied = Apply(Document(), new ApplyElementTemplateCommand("Task", old));
        applied = Apply(applied, new SetTemplatePropertyCommand("Task", old, "url", "https://user.invalid"));
        var next = old with { Version = 2, Properties = old.Properties.Select(p => p.Binding?.Type == "zeebe:taskDefinition" ? p with { Value = "new-worker" } : p with { Value = p.Id == "url" ? "https://default.invalid" : p.Value }).ToArray() };
        var updated = Apply(applied, new UpgradeElementTemplateCommand("Task", old, next));
        var values = BpmnTemplateValues.Read(updated, "Task", next);
        Assert.Equal("https://user.invalid", values["url"]);
        Assert.Equal("new-worker", values["#0"]);
        var registry = new BpmnElementTemplateRegistry(); registry.Add(old); registry.Add(next);
        Assert.Equal((next, next.Id, next.Version, false), BpmnAppliedTemplate.Resolve(updated, "Task", registry));
    }

    [Fact]
    public void ApplyDifferentTemplateRemovesKnownOldBindingsButMissingTemplateOnlyReplacesMarkers()
    {
        var old = Rest();
        var next = new BpmnElementTemplate { Id = "other", AppliesTo = ["bpmn:Task"], Properties = [Property("name", "property", "Other", ("name", "name"))] };
        var applied = Apply(Document(), new ApplyElementTemplateCommand("Task", old));
        var registry = new BpmnElementTemplateRegistry(); registry.Add(old);
        var changed = Apply(applied, new ApplyElementTemplateCommand("Task", next, registry));
        Assert.DoesNotContain("http-json", BpmnXmlWriter.Write(changed).Xml, StringComparison.Ordinal);
        Assert.Equal("Other", changed.FindFlowNode("Task")!.Name);
        var unknown = Apply(applied, new ApplyElementTemplateCommand("Task", next));
        Assert.Contains("http-json", BpmnXmlWriter.Write(unknown).Xml, StringComparison.Ordinal);
        Assert.True(BpmnAppliedTemplate.Resolve(unknown, "Task", registry).IsMissing);
    }

    [Fact]
    public void TypeSwitchAppliesAtomicallyAndMismatchRejects()
    {
        var original = Document(BpmnFlowNodeType.Task);
        var applied = Apply(original, new ApplyElementTemplateCommand("Task", Rest()));
        Assert.Equal(BpmnFlowNodeType.ServiceTask, applied.FindFlowNode("Task")!.Type);
        var wrong = Rest() with { AppliesTo = ["bpmn:Event"] };
        var rejected = BpmnDocumentEditor.Apply(original, new ApplyElementTemplateCommand("Task", wrong), Options);
        Assert.False(rejected.IsApplied); Assert.Same(original, rejected.Document);
        var bad = Rest() with { ElementType = new("bpmn:ExclusiveGateway") };
        rejected = BpmnDocumentEditor.Apply(original, new ApplyElementTemplateCommand("Task", bad), Options);
        Assert.False(rejected.IsApplied); Assert.Same(original, rejected.Document);
    }

    [Theory]
    [InlineData("#0")]
    [InlineData("locked")]
    [InlineData("user")]
    [InlineData("missing")]
    public void HiddenReadonlyInactiveAndUnknownPropertiesReject(string key)
    {
        var document = Apply(Document(), new ApplyElementTemplateCommand("Task", Rest()));
        var result = BpmnDocumentEditor.Apply(document, new SetTemplatePropertyCommand("Task", Rest(), key, "changed"), Options);
        Assert.False(result.IsApplied); Assert.Same(document, result.Document);
        Assert.Equal("EDIT-110", Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void UnknownBindingRejectsAtomically()
    {
        var document = Document();
        var template = Rest() with { Properties = [.. Rest().Properties, Property("bad", "unknown:binding", "bad")] };
        var result = BpmnDocumentEditor.Apply(document, new ApplyElementTemplateCommand("Task", template), Options);
        Assert.False(result.IsApplied); Assert.Same(document, result.Document);
        Assert.Contains("EXT-004", Assert.Single(result.Diagnostics).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(BpmnFlowNodeType.ReceiveTask)]
    [InlineData(BpmnFlowNodeType.IntermediateCatchEvent)]
    public void MessageBindingCreatesReferencedRootAndSubscription(BpmnFlowNodeType type)
    {
        var doc = Document(type, type == BpmnFlowNodeType.IntermediateCatchEvent ? BpmnEventDefinitionType.Message : BpmnEventDefinitionType.None);
        var template = new BpmnElementTemplate
        {
            Id = "message",
            AppliesTo = ["bpmn:FlowNode"],
            Properties =
            [Property("name", "bpmn:Message#property", "Order", ("name", "name")), Property("correlation", "bpmn:Message#zeebe:subscription#property", "=orderId", ("name", "correlationKey"))]
        };
        doc = Apply(doc, new ApplyElementTemplateCommand("Task", template));
        var message = Assert.Single(doc.Catalog.Messages);
        var xml = BpmnXmlWriter.Write(doc).Xml;
        Assert.Contains("messageRef=\"" + message.Id + "\"", xml, StringComparison.Ordinal);
        Assert.Contains("<zeebe:subscription correlationKey=\"=orderId\"", xml, StringComparison.Ordinal);
        Assert.Equal("Order", BpmnTemplateValues.Read(doc, "Task", template)["name"]);
        Assert.Equal("=orderId", BpmnTemplateValues.Read(doc, "Task", template)["correlation"]);
        var roundtrip = BpmnXmlReader.Read(xml).Document;
        Assert.Equal("Order", BpmnTemplateValues.Read(roundtrip, "Task", template)["name"]);
        Assert.Equal("=orderId", BpmnTemplateValues.Read(roundtrip, "Task", template)["correlation"]);
        if (type == BpmnFlowNodeType.ReceiveTask)
        {
            Assert.Equal(message.Id, roundtrip.FindFlowNode("Task")!.MessageRef);
            Assert.DoesNotContain("messageRef", roundtrip.FindFlowNode("Task")!.Extras.ForeignAttributes.Keys);
        }
    }

    [Fact]
    public void TimerAndConditionalBindingsRoundTrip()
    {
        var timer = new BpmnElementTemplate { Id = "timer", AppliesTo = ["bpmn:Event"], Properties = [Property("cycle", "bpmn:TimerEventDefinition#property", "R/PT5M", ("name", "timeCycle"))] };
        var doc = Apply(Document(BpmnFlowNodeType.IntermediateCatchEvent, BpmnEventDefinitionType.Timer), new ApplyElementTemplateCommand("Task", timer));
        var roundtrip = BpmnXmlReader.Read(BpmnXmlWriter.Write(doc).Xml).Document;
        Assert.Equal("R/PT5M", BpmnTemplateValues.Read(roundtrip, "Task", timer)["cycle"]);
        var conditional = new BpmnElementTemplate
        {
            Id = "condition",
            AppliesTo = ["bpmn:Event"],
            Properties =
            [Property("condition", "bpmn:ConditionalEventDefinition#property", "=ready", ("name", "condition")), Property("filter", "bpmn:ConditionalEventDefinition#zeebe:conditionalFilter#property", "order", ("name", "variableName"))]
        };
        doc = Apply(Document(BpmnFlowNodeType.IntermediateCatchEvent, BpmnEventDefinitionType.Conditional), new ApplyElementTemplateCommand("Task", conditional));
        roundtrip = BpmnXmlReader.Read(BpmnXmlWriter.Write(doc).Xml).Document;
        Assert.Equal("=ready", BpmnTemplateValues.Read(roundtrip, "Task", conditional)["condition"]);
        Assert.Equal("order", BpmnTemplateValues.Read(roundtrip, "Task", conditional)["filter"]);
    }

    [Fact]
    public void NumbersBooleansRequiredFeelAndNonOptionalEmptyFollowCloudSemantics()
    {
        var template = new BpmnElementTemplate
        {
            Id = "values",
            AppliesTo = ["bpmn:Task"],
            Properties =
        [
            Property("boolean", "zeebe:input", "True", ("name", "flag")) with { Type = BpmnTemplatePropertyType.Boolean },
            Property("number", "zeebe:input", "1.50", ("name", "amount")) with { Type = BpmnTemplatePropertyType.Number },
            Property("feel", "zeebe:input", "customer", ("name", "customer")) with { Feel = BpmnTemplateFeel.Required },
            Property("empty", "zeebe:input", "", ("name", "empty"))
        ]
        };
        var doc = Apply(Document(), new ApplyElementTemplateCommand("Task", template));
        var values = BpmnTemplateValues.Read(doc, "Task", template);
        Assert.Equal("=true", values["boolean"]); Assert.Equal("=1.50", values["number"]); Assert.Equal("=customer", values["feel"]); Assert.Equal("", values["empty"]);
        Assert.Equal("", XDocument.Parse(BpmnXmlWriter.Write(doc).Xml).Descendants(XName.Get("input", Uri)).Single(e => (string?)e.Attribute("target") == "empty").Attribute("source")!.Value);
    }

    [Fact]
    public void OutputHeadersCalledElementPresenceAndSignalBindings()
    {
        var template = new BpmnElementTemplate
        {
            Id = "bindings",
            AppliesTo = ["bpmn:Task"],
            Properties =
        [Property("output", "zeebe:output", "result", ("source", "=response")), Property("header", "zeebe:taskHeader", "header", ("key", "key")),
            Property("process", "zeebe:calledElement", "process", ("property", "processId")), Property("presence", "zeebe:userTask", "") ]
        };
        var doc = Apply(Document(), new ApplyElementTemplateCommand("Task", template));
        var xml = BpmnXmlWriter.Write(doc).Xml;
        Assert.Contains("source=\"=response\" target=\"result\"", xml, StringComparison.Ordinal);
        Assert.Contains("<zeebe:userTask", xml, StringComparison.Ordinal);
        Assert.Equal("true", BpmnTemplateValues.Read(doc, "Task", template)["presence"]);
        var signalTemplate = new BpmnElementTemplate { Id = "signal", AppliesTo = ["bpmn:Event"], Properties = [Property("name", "bpmn:Signal#property", "Wake", ("name", "name"))] };
        doc = Apply(Document(BpmnFlowNodeType.IntermediateCatchEvent, BpmnEventDefinitionType.Signal), new ApplyElementTemplateCommand("Task", signalTemplate));
        Assert.Equal("Wake", Assert.Single(doc.Catalog.Signals).Name);
    }

    [Theory]
    [InlineData("zeebe:taskDefinition", "retries")]
    [InlineData("zeebe:calledElement", "propagateAllChildVariables")]
    [InlineData("zeebe:formDefinition", "formId")]
    [InlineData("zeebe:assignmentDefinition", "assignee")]
    [InlineData("zeebe:taskSchedule", "dueDate")]
    [InlineData("zeebe:priorityDefinition", "priority")]
    [InlineData("zeebe:jobPriorityDefinition", "priority")]
    [InlineData("zeebe:calledDecision", "decisionId")]
    [InlineData("zeebe:script", "expression")]
    [InlineData("zeebe:adHoc", "completionCondition")]
    [InlineData("zeebe:agentDefinition", "agentId")]
    public void SingletonAttributeBindingsReadWriteAndRemove(string type, string attribute)
    {
        var template = new BpmnElementTemplate { Id = "single", AppliesTo = ["bpmn:Task"], Properties = [Property("value", type, "configured", ("property", attribute))] };
        var applied = Apply(Document(), new ApplyElementTemplateCommand("Task", template));
        Assert.Equal("configured", BpmnTemplateValues.Read(applied, "Task", template)["value"]);
        var edited = Apply(applied, new SetTemplatePropertyCommand("Task", template, "value", "edited"));
        Assert.Equal("edited", BpmnTemplateValues.Read(edited, "Task", template)["value"]);
        var removed = Apply(edited, new RemoveElementTemplateCommand("Task", template));
        Assert.Null(BpmnTemplateValues.Read(removed, "Task", template)["value"]);
    }

    [Theory]
    [InlineData("zeebe:executionListener")]
    [InlineData("zeebe:taskListener")]
    public void ListenerBindingsUseEventAndTypeKeysAndKeepUnrelatedRows(string type)
    {
        var template = new BpmnElementTemplate
        {
            Id = "listener",
            AppliesTo = ["bpmn:Task"],
            Properties =
            [Property("start", type, "start-worker", ("eventType", "start"), ("retries", "3")) with { Type = BpmnTemplatePropertyType.Hidden },
             Property("end", type, "end-worker", ("eventType", "end")) with { Type = BpmnTemplatePropertyType.Hidden }]
        };
        var doc = Apply(Document(), new ApplyElementTemplateCommand("Task", template));
        var values = BpmnTemplateValues.Read(doc, "Task", template);
        Assert.Equal("start-worker", values["start"]); Assert.Equal("end-worker", values["end"]);
        doc = Apply(doc, new ApplyElementTemplateCommand("Task", template));
        var container = Assert.Single(doc.FlowNodes[0].Extras.Extensions);
        Assert.Equal(2, container.Children.Count);
        Assert.Equal("3", container.Children[0].Attributes["retries"]);
        var partial = template with { Properties = [template.Properties[0]] };
        doc = Apply(doc, new RemoveElementTemplateCommand("Task", partial));
        Assert.Equal("end-worker", BpmnTemplateValues.Read(doc, "Task", template)["end"]);
        Assert.Null(BpmnTemplateValues.Read(doc, "Task", template)["start"]);
    }

    [Fact]
    public void LinkedResourceAndForeignAttributesRoundTripAndIconIsCleared()
    {
        var template = new BpmnElementTemplate
        {
            Id = "resource",
            Icon = "data:image/svg+xml;base64,PHN2Zy8+",
            AppliesTo = ["bpmn:Task"],
            Properties =
            [Property("resource", "zeebe:linkedResource", "deployment", ("linkName", "my-form"), ("property", "resourceId")),
             Property("foreign", "property", "attribute", ("name", "zeebe:custom"))]
        };
        var doc = Apply(Document(), new ApplyElementTemplateCommand("Task", template));
        doc = BpmnXmlReader.Read(BpmnXmlWriter.Write(doc).Xml).Document;
        Assert.Equal("deployment", BpmnTemplateValues.Read(doc, "Task", template)["resource"]);
        Assert.Equal("attribute", BpmnTemplateValues.Read(doc, "Task", template)["foreign"]);
        doc = Apply(doc, new ApplyElementTemplateCommand("Task", template with { Icon = null }));
        Assert.DoesNotContain("modelerTemplateIcon", BpmnXmlWriter.Write(doc, BpmnWriteOptions.Canonical with { AllowLossyCanonicalExport = true }).Xml, StringComparison.Ordinal);
    }

    [Fact]
    public void ConditionsOnIdlessPropertiesAndBooleanValuesWork()
    {
        var template = new BpmnElementTemplate
        {
            Id = "boolean",
            AppliesTo = ["bpmn:Task"],
            Properties =
            [Property("flag", "zeebe:input", "true", ("name", "flag")) with { Type = BpmnTemplatePropertyType.Boolean },
             Property("unused", "zeebe:input", "active", ("name", "dependent")) with { Id = null, Condition = new PropertyCondition("flag", BpmnTemplateConditionOperator.Equals, "true") }]
        };
        var doc = Apply(Document(), new ApplyElementTemplateCommand("Task", template));
        Assert.Equal("active", BpmnTemplateValues.Read(doc, "Task", template)["#1"]);
        doc = Apply(doc, new SetTemplatePropertyCommand("Task", template, "flag", "false"));
        Assert.Null(BpmnTemplateValues.Read(doc, "Task", template)["#1"]);
    }

    [Fact]
    public void UpgradeNewlyActivatedPropertiesUseDefaultsRatherThanAbsentOldValues()
    {
        var old = Rest();
        var doc = Apply(Document(), new ApplyElementTemplateCommand("Task", old));
        var next = old with { Version = 2, Properties = old.Properties.Select(p => p.Id == "auth" ? p with { Editable = false, Value = "basic" } : p).ToArray() };
        doc = Apply(doc, new UpgradeElementTemplateCommand("Task", old, next));
        Assert.Equal("alice", BpmnTemplateValues.Read(doc, "Task", next)["user"]);
    }

    [Fact]
    public void DeprecatedTaskDefinitionBindingMatchesModernBindingOnUpgrade()
    {
        var old = new BpmnElementTemplate { Id = "deprecated", AppliesTo = ["bpmn:Task"], Properties = [Property("type", "zeebe:taskDefinition:type", "default")] };
        var doc = Apply(Document(), new ApplyElementTemplateCommand("Task", old));
        doc = Apply(doc, new SetTemplatePropertyCommand("Task", old, "type", "user-worker"));
        var next = old with { Version = 2, Properties = [Property("type", "zeebe:taskDefinition", "new-default", ("property", "type"))] };
        doc = Apply(doc, new UpgradeElementTemplateCommand("Task", old, next));
        Assert.Equal("user-worker", BpmnTemplateValues.Read(doc, "Task", next)["type"]);
    }

    [Theory]
    [InlineData(false, "off")]
    [InlineData(false, "on")]
    [InlineData(true, "off")]
    [InlineData(true, "on")]
    public void ApplyAndUpgradeReconcileConditionalDefaultChains(bool upgrade, string mode)
    {
        var old = new BpmnElementTemplate
        {
            Id = "chain",
            Version = 1,
            AppliesTo = ["bpmn:Task"],
            Properties =
            [Property("mode", "zeebe:input", "on", ("name", "mode")) with { Type = BpmnTemplatePropertyType.Hidden },
             Property("a", "zeebe:input", "alice", ("name", "a")) with { Condition = new PropertyCondition("mode", BpmnTemplateConditionOperator.Equals, "on") },
             Property("b", "zeebe:input", "secret", ("name", "b")) with { Condition = new PropertyCondition("a", BpmnTemplateConditionOperator.Equals, "alice") },
             Property("c", "zeebe:input", "empty-a", ("name", "c")) with { Condition = new PropertyCondition("a", BpmnTemplateConditionOperator.IsEmpty) },
             Property("d", "zeebe:input", "active-c", ("name", "d")) with { Condition = new PropertyCondition("c", BpmnTemplateConditionOperator.Equals, "empty-a") }]
        };
        var next = old with { Version = 2, Properties = old.Properties.Select(p => p.Id == "mode" ? p with { Value = mode } : p).ToArray() };
        var original = upgrade ? Apply(Document(), new ApplyElementTemplateCommand("Task", old)) : Document();
        var history = new BpmnEditHistory(original, Options);
        var command = upgrade ? (BpmnEditCommand)new UpgradeElementTemplateCommand("Task", old, next) : new ApplyElementTemplateCommand("Task", next);
        var result = history.Apply(command);
        Assert.True(result.IsApplied, Codes(result));
        var values = BpmnTemplateValues.Read(result.Document, "Task", next);
        Assert.Equal(mode, values["mode"]);
        Assert.Equal(mode == "on" ? "alice" : null, values["a"]);
        Assert.Equal(mode == "on" ? "secret" : null, values["b"]);
        Assert.Equal(mode == "off" ? "empty-a" : null, values["c"]);
        Assert.Equal(mode == "off" ? "active-c" : null, values["d"]);
        history.Undo();
        Assert.Same(original, history.Document);
    }

    [Fact]
    public void LegacyTaskDefinitionTypeLoadsAndAppliesWithoutPropertyParameter()
    {
        var loaded = BpmnElementTemplateLoader.Load("""
            {"$schema":"https://unpkg.com/@camunda/zeebe-element-templates-json-schema/resources/schema.json",
             "id":"legacy","name":"Legacy worker","appliesTo":["bpmn:Task"],
             "properties":[{"id":"worker","type":"Hidden","value":"legacy-worker","binding":{"type":"zeebe:taskDefinition:type"}}]}
            """);
        Assert.Empty(loaded.Errors);
        var template = Assert.Single(loaded.Templates);
        var document = Apply(Document(), new ApplyElementTemplateCommand("Task", template));
        Assert.Equal("legacy-worker", BpmnTemplateValues.Read(document, "Task", template)["worker"]);
        Assert.Contains("type=\"legacy-worker\"", BpmnXmlWriter.Write(document).Xml, StringComparison.Ordinal);
        var modern = BpmnElementTemplateLoader.Load("""
            {"$schema":"https://unpkg.com/@camunda/zeebe-element-templates-json-schema/resources/schema.json",
             "id":"modern","name":"Modern worker","appliesTo":["bpmn:Task"],
             "properties":[{"type":"Hidden","value":"worker","binding":{"type":"zeebe:taskDefinition"}}]}
            """);
        Assert.Empty(modern.Templates);
        Assert.Equal("EXT-004", Assert.Single(modern.Errors).Code);
    }

    private static BpmnElementTemplate Rest()
    {
        var result = BpmnElementTemplateLoader.Load(RestJson);
        Assert.Empty(result.Errors);
        return Assert.Single(result.Templates);
    }
    private static BpmnTemplateProperty Property(string id, string type, string? value, params (string Key, string Value)[] parameters) =>
        new() { Id = id, Value = value, Binding = new(type, parameters.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal)) };
    private static BpmnDocument Document(BpmnFlowNodeType type = BpmnFlowNodeType.ServiceTask, BpmnEventDefinitionType definition = BpmnEventDefinitionType.None) => new()
    {
        Definitions = new("Definitions", "urn:test"),
        Processes = [new("Process")],
        FlowNodes = [new("Task", type, "Process", EventDefinition: definition) { EventDefinitions = definition == BpmnEventDefinitionType.None ? [] : [new(definition)] }]
    };
    private static BpmnDocument Apply(BpmnDocument document, BpmnEditCommand command)
    {
        var result = BpmnDocumentEditor.Apply(document, command, Options);
        Assert.True(result.IsApplied, Codes(result)); return result.Document;
    }
    private static string Codes(BpmnEditResult result) => string.Join("; ", result.Diagnostics.Select(d => d.Code + ": " + d.Message));
}
