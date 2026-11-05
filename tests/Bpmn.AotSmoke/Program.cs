using System.Text.Json;
using System.Xml.Linq;
using Hasseware.AspNetCore.Components.Bpmn.Accessibility;
using Hasseware.AspNetCore.Components.Bpmn;
using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Execution;
using Hasseware.AspNetCore.Components.Bpmn.Icons;
using Hasseware.AspNetCore.Components.Bpmn.Interop;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions.Schema;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions.Templates;
using Hasseware.AspNetCore.Components.Bpmn.Model.Geometry;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;
using Hasseware.AspNetCore.Components.Bpmn.Storage;
using Hasseware.AspNetCore.Components.Bpmn.Storage.FileSystem;
using Hasseware.AspNetCore.Components.Bpmn.Tools;
using Hasseware.AspNetCore.Components.Bpmn.Validation;
#if BPMN_SMOKE_CSHARP
using Hasseware.AspNetCore.Components.Bpmn.Scripting.CSharp;
#endif
#if BPMN_SMOKE_JAVASCRIPT
using Hasseware.AspNetCore.Components.Bpmn.Scripting.JavaScript;
#endif
#if BPMN_SMOKE_LUA
using Hasseware.AspNetCore.Components.Bpmn.Scripting.Lua;
#endif
#if BPMN_SMOKE_FEEL
using Hasseware.AspNetCore.Components.Bpmn.FeelSharp;
using Microsoft.Extensions.DependencyInjection;
#endif

if (JsonSerializer.IsReflectionEnabledByDefault)
{
    throw new InvalidOperationException("The smoke host must disable reflection-based JSON.");
}

var document = new BpmnDocumentBuilder()
    .Node("start", BpmnFlowNodeType.StartEvent, new Bounds(100, 100, 36, 36), "Start")
    .Node("task", BpmnFlowNodeType.UserTask, new Bounds(200, 80, 100, 80), "Review")
    .Flow("flow", "start", "task", [new DiPoint(136, 118), new DiPoint(200, 118)])
    .Build();
var json = BpmnJsonWriter.Write(document);
var restored = BpmnJsonReader.Read(json).Document;
var xml = BpmnXmlWriter.Write(restored).Xml;
var imported = BpmnXmlReader.Read(xml).Document;
var graphDocument = imported with
{
    SequenceFlows = [.. imported.SequenceFlows, imported.SequenceFlows[0] with { Id = "repeat" },
        imported.SequenceFlows[0] with { Id = "self", TargetRef = "start" }],
};
var accessibleGraph = BpmnAccessibleGraph.For(graphDocument, null);
Require(accessibleGraph[0].Outgoing.SequenceEqual(["task", "task", "start"])
    && accessibleGraph[0].Incoming.SequenceEqual(["start"])
    && accessibleGraph[1].Incoming.SequenceEqual(["start", "start"]),
    "Accessible graph lost ordered duplicate or self-reference adjacency.");
var taskSnapshot = BpmnElementInspector.Describe(imported, "task");
Require(taskSnapshot is { Capabilities.CanEditLabel: true }
    && taskSnapshot.Properties.Any(property => property.Name == "name"),
    "Snapshot label capability lost its property descriptors.");
var normalizationRoute = Enumerable.Range(0, 32)
    .Select(index => new DiPoint(100 + (100d * index / 31), 40)).ToArray();
var normalizationDocument = new BpmnDocumentBuilder()
    .Node("source", BpmnFlowNodeType.ServiceTask, new Bounds(0, 0, 100, 80))
    .Node("target", BpmnFlowNodeType.Task, new Bounds(200, 0, 100, 80))
    .Flow("normalized", "source", "target", normalizationRoute).Build();
Require(ReferenceEquals(normalizationDocument, ConnectorEndpoints.Normalize(normalizationDocument)),
    "Unchanged normalization stopped preserving document identity.");
normalizationRoute[0] = new DiPoint(105, 40);
var normalizedDocument = ConnectorEndpoints.Normalize(normalizationDocument);
Require(normalizedDocument.Diagrams[0].Plane.Edges[0].Waypoints[0] == new DiPoint(100, 40)
    && normalizationRoute[0] == new DiPoint(105, 40),
    "Normalization lost endpoint correction or mutated host waypoints.");
var schemaSnapshot = BpmnElementInspector.Describe(normalizationDocument, "source",
    extensionOptions: new BpmnExtensionOptions().AddZeebe().AddCamunda7())!;
Require(schemaSnapshot.ExtensionGroups.Any(group => group.Id == "task-definition")
    && schemaSnapshot.ExtensionGroups.Any(group => group.Id == "inputs")
    && schemaSnapshot.ExtensionGroups.Any(group => group.Id == "camunda:implementation"),
    "Operation-scoped schema applicability lost registered groups.");
var queryRegistry = new BpmnExtensionRegistry().Register(BpmnExtensionSchemas.Zeebe);
if (!queryRegistry.TryResolveType("zeebe:IoMapping", out var mappingType, out _))
    throw new InvalidOperationException("Registry query smoke could not resolve its type.");
var childOrderBefore = queryRegistry.GetChildOrder(mappingType);
queryRegistry.GetAllProperties(mappingType);
Require(queryRegistry.GetChildOrder(mappingType).SequenceEqual(childOrderBefore),
    "Warmed child-order lookup changed its result.");
queryRegistry.Register(new("Smoke trait", "urn:smoke-trait", "smoke",
    [new("Trait", [], ["zeebe:IoMapping"], [new("late", "String", false, false, false, false, null)])]));
Require(queryRegistry.GetChildOrder(mappingType).Contains("late")
    && !childOrderBefore.Contains("late"),
    "Late registration lost cache invalidation or mutated an earlier result.");
var annotationFreePreview = BpmnImportPreview.Of("smoke.bpmn", xml);
Require(annotationFreePreview.CanAccept && !annotationFreePreview.AnnotationNormalization.RequiresConfirmation
    && annotationFreePreview.AnnotationNormalization.AnnotationIds.Count == 0,
    "Annotation-free import preview changed its acceptance or consent.");
var attachmentBuilder = new BpmnDocumentBuilder()
    .Node("a", BpmnFlowNodeType.Task, new Bounds(0, 0, 100, 80))
    .Node("b", BpmnFlowNodeType.Task, new Bounds(200, 0, 100, 80))
    .Flow("attached", "a", "b", [new(100, 40), new(200, 40)]);
for (var index = 0; index < 65; index++)
{
    attachmentBuilder.TextAnnotation($"note{index}", "Note", new Bounds(110, 160 + index * 60, 80, 40))
        .Association($"association{index}", $"note{index}", "attached", [new(150, 160 + index * 60), new(150, 40)]);
}
var attachmentBefore = ConnectorEndpoints.Normalize(attachmentBuilder.Build());
var attachmentDiagram = attachmentBefore.Diagrams[0];
var attachmentAfter = attachmentBefore with
{
    Diagrams = [attachmentDiagram with
    {
        Plane = attachmentDiagram.Plane with
        {
            Edges = [attachmentDiagram.Plane.Edges[0] with
            { Waypoints = [new(100, 40), new(130, 40), new(130, 120), new(170, 120), new(170, 40), new(200, 40)] },
                .. attachmentDiagram.Plane.Edges.Skip(1)],
        },
    }],
};
var attachmentsFollowed = ConnectorEndpoints.FollowFlowAttachments(attachmentAfter, attachmentBefore);
var validationDocument = attachmentBefore with
{
    FlowNodes = [.. attachmentBefore.FlowNodes, new("orphan", BpmnFlowNodeType.Task, "other")],
    Associations = [.. attachmentBefore.Associations, new("extraAttachment", "note0", "a", "Process_1")],
};
Require(BpmnValidator.Validate(validationDocument).Diagnostics.Any(diagnostic =>
    diagnostic.RuleId == "AN-001" && diagnostic.ElementId == "note0"),
    "Indexed annotation validation lost a multiple-binding diagnostic.");
var traversalDocument = new BpmnDocument
{
    Definitions = new("definitions", "urn:native-validation"),
    Processes = [new("process"), new("other")],
    FlowNodes = [new("start", BpmnFlowNodeType.StartEvent, "process"),
        new("host", BpmnFlowNodeType.Task, "process"),
        new("boundary", BpmnFlowNodeType.BoundaryEvent, "process",
            EventDefinition: BpmnEventDefinitionType.Timer, AttachedToRef: "host"),
        new("escape", BpmnFlowNodeType.Task, "process"),
        new("gateway", BpmnFlowNodeType.ExclusiveGateway, "process"),
        new("end", BpmnFlowNodeType.EndEvent, "process"),
        new("otherStart", BpmnFlowNodeType.StartEvent, "other"),
        new("otherOrphan", BpmnFlowNodeType.Task, "other")],
    SequenceFlows = [new("begin", "start", "host", "process"),
        new("toGateway", "host", "gateway", "process"),
        new("escapeFlow", "boundary", "escape", "process"),
        new("rejoin", "escape", "gateway", "process"),
        new("firstDefault", "gateway", "end", "process", IsDefault: true),
        new("secondDefault", "gateway", "end", "process", IsDefault: true)],
};
var traversalReport = BpmnValidator.Validate(traversalDocument);
Require(traversalReport.Diagnostics.Any(diagnostic =>
    diagnostic.RuleId == "D004" && diagnostic.RelatedId == "firstDefault")
    && traversalReport.Diagnostics.Any(diagnostic =>
        diagnostic.RuleId == "D009" && diagnostic.ElementId == "otherOrphan")
    && !traversalReport.Diagnostics.Any(diagnostic =>
        diagnostic.RuleId == "D009" && diagnostic.ElementId == "escape"),
    "Native validation lost gateway order, container grouping or boundary reachability.");
Require(attachmentsFollowed.Diagrams[0].Plane.Shapes[2].Bounds != attachmentBefore.Diagrams[0].Plane.Shapes[2].Bounds
    && attachmentBefore.Diagrams[0].Plane.Shapes[2].Bounds.Y == 160,
    "Operation-scoped annotation following lost motion or mutated the source.");
Require(BpmnJsonWriter.Write(imported) == BpmnJsonWriter.Write(BpmnXmlReader.Read(BpmnXmlWriter.Write(document).Xml).Document),
    "XML/JSON round trip changed the document.");
var wideBuilder = new BpmnDocumentBuilder();
for (var index = 0; index < 12; index++)
{
    wideBuilder.Node($"task_{index}", BpmnFlowNodeType.Task, new Bounds(index * 120, 100, 100, 80));
}
var wideXml = BpmnXmlWriter.Write(wideBuilder.Build()).Xml;
Require(BpmnXmlWriter.Write(BpmnXmlReader.Read(wideXml).Document).Xml == wideXml,
    "Indexed canonical export changed a wide sibling set.");
for (var index = 0; index < 11; index++)
{
    wideBuilder.Flow($"flow_{index}", $"task_{index}", $"task_{index + 1}", []);
}
var referencedXml = BpmnXmlWriter.Write(wideBuilder.Build(), new() { WriteFlowNodeReferences = true }).Xml;
var referencedDocument = BpmnXmlReader.Read(referencedXml).Document;
Require(BpmnXmlWriter.Write(referencedDocument, BpmnWriteOptions.Fidelity).Xml == referencedXml,
    "Indexed fidelity export changed correct flow references.");
var changedReferences = referencedDocument with
{
    SequenceFlows = [referencedDocument.SequenceFlows[0] with { TargetRef = "task_2" }, .. referencedDocument.SequenceFlows.Skip(1)],
};
XNamespace modelNamespace = "http://www.omg.org/spec/BPMN/20100524/MODEL";
var changedReferenceXml = XDocument.Parse(BpmnXmlWriter.Write(changedReferences, BpmnWriteOptions.Fidelity).Xml);
Require(changedReferenceXml.Descendants(modelNamespace + "task").Single(element => (string?)element.Attribute("id") == "task_2")
    .Elements(modelNamespace + "incoming").Select(element => element.Value).SequenceEqual(["flow_0", "flow_1"]),
    "Indexed fidelity export lost changed reference order.");
Require(BpmnXmlWriter.Write(referencedDocument, BpmnWriteOptions.Fidelity).Xml == referencedXml,
    "Fidelity export modified the retained source.");
var referenceDiff = BpmnDocumentDiff.Compare(referencedDocument, changedReferences);
Require(referenceDiff.Changed.Count == 1 && referenceDiff.Changed[0].Id == "flow_0"
    && referenceDiff.Changed[0].TargetBefore == "task_1" && referenceDiff.Changed[0].TargetAfter == "task_2"
    && !referenceDiff.Changed[0].GeometryChanged,
    "Document diff changed its endpoint or geometry semantics.");
var extensionNode = referencedDocument.FlowNodes[0] with
{
    Extras = new()
    {
        Extensions = [.. Enumerable.Range(0, 12).Select(index =>
            new Hasseware.AspNetCore.Components.Bpmn.Model.Extensions.BpmnExtensionElement(
                "urn:smoke", "input", new Dictionary<string, string>(), [], $"value{index}"))],
    },
};
var extensionDescriptors = BpmnElementInspector.ExtensionProperties(extensionNode);
Require(extensionDescriptors.Count == 12 && extensionDescriptors[^1].Label == "input[12] text"
    && extensionDescriptors[^1].Name == "extension:11#text", "Wide extension descriptor labels changed.");
Require(BpmnWireNames.Of(BpmnFlowNodeType.UserTask) == "userTask", "Enum wire names changed.");
Require(BpmnWireNames.Of((BpmnFlowNodeType)int.MaxValue) == "2147483647", "Unknown enum values changed.");
using var canvas = JsonDocument.Parse(CanvasDocumentPayload.Serialize(restored));
Require(canvas.RootElement.GetProperty("flowNodes")[1].GetProperty("type").GetString() == "userTask",
    "Canvas projection lost its enum wire names.");
Require(!canvas.RootElement.TryGetProperty("processes", out _), "Canvas projection included headless-only properties.");

var interop = BpmnInteropJsonContext.Default;
var configuration = JsonSerializer.SerializeToElement(EditorConfig.CreateDefault(), interop.EditorConfig);
Require(configuration.Deserialize(interop.EditorConfig)?.Canvas is not null, "Canvas configuration lost generated metadata.");
var viewport = JsonSerializer.SerializeToElement(ViewportCommand.SetZoom(1.5), interop.ViewportCommand);
Require(viewport.GetProperty("zoom").GetDouble() == 1.5, "Viewport JSON changed.");
var execution = JsonSerializer.SerializeToElement(
    new BpmnExecutionState("engine", [new BpmnElementExecution("task", BpmnActivityStatus.Active)]), interop.BpmnExecutionState);
Require(execution.GetProperty("elements")[0].GetProperty("status").GetString() == "Active", "Execution JSON changed.");
using (var bridge = new BpmnCanvasEventBridge())
{
    CanvasInitialized? initialized = null;
    EditRequest? requested = null;
    bridge.InitializedReceived += payload => initialized = payload;
    bridge.EditRequestedReceived += payload => requested = payload;
    bridge.ReceiveInitialized(JsonElement.Parse("""{"instanceId":"native","configRevision":2}"""));
    bridge.ReceiveEditRequested(JsonElement.Parse("""
        {"kind":"move","diagramId":"diagram","moves":[{"shapeId":"task","bounds":{"x":10,"y":20,"width":100,"height":80}}]}
        """));
    Require(initialized is { InstanceId: "native", ConfigRevision: 2 }, "Canvas callback metadata was trimmed.");
    Require(requested is { DiagramId: "diagram", Moves.Count: 1 } && requested.Moves[0].Bounds.X == 10,
        "Nested edit callback metadata was trimmed.");
}

var host = new BpmnDocumentToolHost(restored, BpmnEditOrigin.FromAgent("aot-smoke"));
var result = await BpmnTools.GetDiagramAsync(host, new BpmnGetDiagramRequest(), CancellationToken.None);
Require(result.Ok && result.Data?.Elements.Count == 2, "Headless tool could not read the diagram.");
using var response = JsonDocument.Parse(BpmnToolJson.Serialize(result));
Require(response.RootElement.GetProperty("data").GetProperty("elements")[1].GetProperty("type").GetString() == "userTask",
    "Tool JSON changed.");
var validation = await BpmnTools.ValidateAsync(host, CancellationToken.None);
Require(validation.Ok, "Headless validation failed.");
var edit = await BpmnTools.SetPropertyAsync(host, new BpmnSetPropertyRequest("task", "name", "Approved"), CancellationToken.None);
Require(edit.Ok && host.Document.FindFlowNode("task")?.Name == "Approved", "Headless editing failed.");
Require(restored.FindFlowNode("task")?.Name == "Review", "Editing mutated the original document.");

Require(BpmnIconRegistry.Default.ForTaskType(BpmnFlowNodeType.UserTask) is not null,
    "The embedded icon manifest lost its user-task glyph.");
var extensions = new BpmnExtensionOptions().AddZeebe().AddCamunda7();
Require(extensions.Registry.Schemas.Count == 2 && extensions.Registry.Schemas.All(schema => schema.Types.Count > 0),
    "Embedded extension schemas could not be loaded.");
var templates = BpmnElementTemplateLoader.Load("""
    {
      "$schema":"https://unpkg.com/@camunda/zeebe-element-templates-json-schema/resources/schema.json",
      "id":"native-template","name":"Native template","appliesTo":["bpmn:Task"],
      "properties":[{"id":"type","type":"Hidden","value":"native",
        "binding":{"type":"zeebe:taskDefinition","property":"type"}}]
    }
    """);
Require(templates.Errors.Count == 0 && templates.Templates.Count == 1, "Native template loading failed.");
var registry = new BpmnElementTemplateRegistry();
registry.Add(templates.Templates[0]);
Require(registry.TryGet("native-template")?.Properties[0].Value == "native", "The native template registry lost its binding value.");

#if BPMN_SMOKE_PARSERS
var context = new BpmnScriptContext("task", "Smoke");
#endif
#if BPMN_SMOKE_CSHARP
CheckParser(new BpmnCSharpScriptValidator(), "csharp", "return 1;", "return (;", context);
#endif
#if BPMN_SMOKE_JAVASCRIPT
CheckParser(new BpmnJavaScriptScriptValidator(), "javascript", "const value = 1;", "const = ;", context);
#endif
#if BPMN_SMOKE_LUA
CheckParser(new BpmnLuaScriptValidator(), "lua", "local value = 1", "local = ", context);
#endif
#if BPMN_SMOKE_FEEL
using var services = new ServiceCollection().AddBpmnFeelSharpEvaluation().BuildServiceProvider();
var evaluation = services.GetRequiredService<IBpmnFeelSharpEvaluation>().Evaluate("amount + 3",
    new Dictionary<string, object?> { ["amount"] = 4m });
Require(evaluation.IsSuccess && evaluation.Value?.ToObject() is 7m, "FEEL interpretation failed.");
#endif

var root = Path.Combine(Path.GetTempPath(), "bpmn-aot-" + Guid.NewGuid().ToString("N"));
try
{
    var store = new FileBpmnDocumentStore(root);
    var key = new BpmnDocumentKey(null, BpmnDocumentPath.Parse("smoke.json"));
    var saved = await store.SaveDocumentAsync(key, restored, null);
    Require(saved.Succeeded, "File store could not save.");
    var loaded = await store.LoadDocumentAsync(key);
    Require(loaded is not null && BpmnJsonWriter.Write(loaded.Document) == json, "File store round trip changed JSON.");
    var conflict = await store.SaveDocumentAsync(key, restored, "stale");
    Require(conflict.Status == BpmnStoreStatus.Conflict, "File store lost optimistic concurrency.");
}
finally
{
    if (Directory.Exists(root))
    {
        Directory.Delete(root, recursive: true);
    }
}

Console.WriteLine("BPMN Native AOT smoke passed.");

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

#if BPMN_SMOKE_PARSERS
static void CheckParser(IBpmnScriptValidator parser, string format, string valid, string invalid, BpmnScriptContext context)
{
    Require(parser.Validate(format, valid, context).Count == 0, $"{format} parser rejected valid syntax.");
    Require(parser.Validate(format, invalid, context).Count > 0, $"{format} parser accepted invalid syntax.");
}
#endif
