using Bunit;
using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;
using Hasseware.AspNetCore.Components.Bpmn.Tests.Components;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Editing;

public sealed class BpmnSchemaPropertiesTests : BunitContext
{
    private const string Zeebe = "http://camunda.org/schema/zeebe/1.0";

    [Fact]
    public void InspectorBuildsZeebeGroupsAndRemovesClaimedRootsFromRawFallback()
    {
        var options = ZeebeOptions();
        var document = BpmnXmlReader.Read(Xml).Document;

        var snapshot = BpmnElementInspector.Describe(document, "Task_1", extensionOptions: options.Extensions)!;

        Assert.Equal(2, snapshot.Version);
        Assert.Contains(snapshot.ExtensionGroups, group => group.Id == "task-definition");
        Assert.Contains(snapshot.ExtensionGroups, group => group.Id == "inputs");
        Assert.Contains(snapshot.ExtensionGroups, group => group.Id == "outputs");
        Assert.Contains(snapshot.ExtensionGroups, group => group.Id == "headers");
        Assert.Contains(snapshot.ExtensionGroups, group => group.Id == "properties");
        Assert.DoesNotContain(snapshot.ExtensionProperties, property => property.Label.StartsWith("taskDefinition", StringComparison.Ordinal));
        Assert.DoesNotContain(snapshot.ExtensionProperties, property => property.Label.StartsWith("ioMapping", StringComparison.Ordinal));
        Assert.Contains(snapshot.ExtensionProperties, property => property.Label.StartsWith("opaque", StringComparison.Ordinal));
    }

    [Fact]
    public void BoundTaskDefinitionCanBeCreatedEditedAndUndone()
    {
        var options = ZeebeOptions();
        var document = WithoutTaskDefinition(BpmnXmlReader.Read(Xml).Document);
        var snapshot = BpmnElementInspector.Describe(document, "Task_1", extensionOptions: options.Extensions)!;
        var entry = Assert.Single(snapshot.ExtensionGroups.Single(group => group.Id == "task-definition").Entries,
            item => item.Id == "type");
        var command = BpmnPropertyEntryCommands.ToCommand("Task_1", entry, "new-worker");
        Assert.IsType<SetExtensionValueCommand>(command);

        var history = new BpmnEditHistory(document);
        var applied = history.Apply(command!);
        Assert.True(applied.IsApplied);
        Assert.Contains(history.Document.FindFlowNode("Task_1")!.Extras.Extensions,
            extension => extension.LocalName == "taskDefinition" && extension.Attributes["type"] == "new-worker");
        Assert.Equal("Edit extension value", history.UndoLabel);
        Assert.DoesNotContain(history.Undo().FindFlowNode("Task_1")!.Extras.Extensions,
            extension => extension.LocalName == "taskDefinition");
    }

    [Fact]
    public void InputRowsReplaceThroughCommandAndUndo()
    {
        var options = ZeebeOptions();
        var document = BpmnXmlReader.Read(Xml).Document;
        var snapshot = BpmnElementInspector.Describe(document, "Task_1", extensionOptions: options.Extensions)!;
        var entry = snapshot.ExtensionGroups.Single(group => group.Id == "inputs").Entries.Single();
        var rows = new IReadOnlyDictionary<string, string?>[]
        {
            new Dictionary<string, string?> { ["target"] = "customerId", ["source"] = "=customer.id" },
        };
        var command = BpmnPropertyEntryCommands.ToCommand("Task_1", entry, null, rows);
        Assert.IsType<ReplaceExtensionCollectionCommand>(command);
        var history = new BpmnEditHistory(document);

        Assert.True(history.Apply(command!).IsApplied);
        var mapping = history.Document.FindFlowNode("Task_1")!.Extras.Extensions.Single(item => item.LocalName == "ioMapping");
        var input = Assert.Single(mapping.Children, item => item.LocalName == "input");
        Assert.Equal("customerId", input.Attributes["target"]);
        Assert.Equal("=customer.id", input.Attributes["source"]);
        history.Undo();
        mapping = history.Document.FindFlowNode("Task_1")!.Extras.Extensions.Single(item => item.LocalName == "ioMapping");
        Assert.Equal("orderId", Assert.Single(mapping.Children, item => item.LocalName == "input").Attributes["target"]);
    }

    [Fact]
    public void SchemaOrderInsertsTaskDefinitionBeforeIoMappingRegardlessOfCreationOrder()
    {
        var options = ZeebeOptions();
        var document = BpmnXmlReader.Read(Xml).Document;
        var task = document.FindFlowNode("Task_1")! with { Extras = BpmnElementExtras.None };
        document = document with { FlowNodes = [.. document.FlowNodes.Select(node => node.Id == task.Id ? task : node)] };
        var editing = EditingOptions.Default with { ExtensionChildOrder = new BpmnSchemaChildOrder(options.Extensions.Registry) };
        var history = new BpmnEditHistory(document, editing);
        var io = new BpmnExtensionElement(Zeebe, "ioMapping", new Dictionary<string, string>(), []);
        var definition = new BpmnExtensionElement(Zeebe, "taskDefinition", new Dictionary<string, string>(), []);

        Assert.True(history.Apply(new AddExtensionElementCommand("Task_1", null, io)).IsApplied);
        Assert.True(history.Apply(new AddExtensionElementCommand("Task_1", null, definition)).IsApplied);
        Assert.Equal(["taskDefinition", "ioMapping"], history.Document.FindFlowNode("Task_1")!.Extras.Extensions.Select(item => item.LocalName));
    }

    [Fact]
    public void MultiInstanceLoopFieldsEditNestedExtensionElementsAndUndo()
    {
        var options = ZeebeOptions();
        var document = BpmnXmlReader.Read(Xml).Document;
        var snapshot = BpmnElementInspector.Describe(document, "MultiTask", extensionOptions: options.Extensions)!;
        var entry = snapshot.ExtensionGroups.Single(group => group.Id == "multi-instance").Entries
            .Single(item => item.Id == "inputCollection");
        var command = BpmnPropertyEntryCommands.ToCommand("MultiTask", entry, "=records");
        Assert.IsType<SetLoopExtensionValueCommand>(command);
        var history = new BpmnEditHistory(document);

        Assert.True(history.Apply(command!).IsApplied);
        var loop = history.Document.FindFlowNode("MultiTask")!.MultiInstanceLoop!;
        Assert.Equal("=records", loop.Extras.Extensions.Single().Attributes["inputCollection"]);
        var xml = BpmnXmlWriter.Write(history.Document).Xml;
        Assert.Contains("<zeebe:loopCharacteristics inputCollection=\"=records\"", xml, StringComparison.Ordinal);
        var reread = BpmnXmlReader.Read(xml).Document.FindFlowNode("MultiTask")!.MultiInstanceLoop!;
        Assert.Equal("=records", reread.Extras.Extensions.Single().Attributes["inputCollection"]);
        history.Undo();
        Assert.Equal("=items", history.Document.FindFlowNode("MultiTask")!.MultiInstanceLoop!.Extras.Extensions
            .Single().Attributes["inputCollection"]);
    }

    [Fact]
    public void UnregisteredSchemasKeepRawOnlySnapshotAndPanelRendersSchemaGroupsWhenRegistered()
    {
        var document = BpmnXmlReader.Read(Xml).Document;
        var rawSnapshot = BpmnElementInspector.Describe(document, "Task_1")!;
        Assert.Empty(rawSnapshot.ExtensionGroups);
        Assert.NotEmpty(rawSnapshot.ExtensionProperties);

        var options = ZeebeOptions();
        var snapshot = BpmnElementInspector.Describe(document, "Task_1", extensionOptions: options.Extensions)!;
        var panel = this.RenderSeeded<BpmnPropertyPanel>(seed => seed.Snapshots([snapshot]));
        Assert.NotNull(panel.Find("details[data-group-id=task-definition]"));
        Assert.NotNull(panel.Find("details[data-group-id=inputs]"));
    }

    private static Hasseware.AspNetCore.Components.Bpmn.Model.BpmnDocument WithoutTaskDefinition(
        Hasseware.AspNetCore.Components.Bpmn.Model.BpmnDocument document)
    {
        var task = document.FindFlowNode("Task_1")!;
        var updated = task with
        {
            Extras = task.Extras with
            {
                Extensions = task.Extras.Extensions.Where(extension => extension.LocalName != "taskDefinition").ToArray(),
            }
        };
        return document with { FlowNodes = [.. document.FlowNodes.Select(node => node.Id == updated.Id ? updated : node)] };
    }

    private static BpmnOptions ZeebeOptions()
    {
        var options = new BpmnOptions();
        options.Extensions.AddZeebe();
        return options;
    }

    private const string Xml = """
        <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                     xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
                     xmlns:vendor="urn:other" id="Definitions_1" targetNamespace="urn:test">
          <process id="Process_1">
            <serviceTask id="Task_1">
              <extensionElements>
                <zeebe:taskDefinition type="worker" retries="3" />
                <zeebe:ioMapping>
                  <zeebe:input target="orderId" source="=order.id" />
                  <zeebe:output target="result" source="=payload" />
                </zeebe:ioMapping>
                <zeebe:taskHeaders><zeebe:header key="content-type" value="application/json" /></zeebe:taskHeaders>
                <zeebe:properties><zeebe:property name="custom" value="one" /></zeebe:properties>
                <vendor:opaque flag="untouched" />
              </extensionElements>
            </serviceTask>
            <serviceTask id="MultiTask">
              <multiInstanceLoopCharacteristics isSequential="false">
                <extensionElements>
                  <zeebe:loopCharacteristics inputCollection="=items" inputElement="item" />
                </extensionElements>
              </multiInstanceLoopCharacteristics>
            </serviceTask>
          </process>
        </definitions>
        """;
}
