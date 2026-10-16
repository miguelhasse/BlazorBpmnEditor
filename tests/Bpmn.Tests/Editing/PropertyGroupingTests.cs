using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Editing;

public sealed class PropertyGroupingTests
{
    [Fact]
    public void LegacyDescriptorsDefaultToGeneralWithoutChangingDeconstruction()
    {
        var descriptor = new BpmnPropertyDescriptor("name", "Name", BpmnPropertyKind.Text, "Work");
        var (name, label, kind, value, readOnly, choices) = descriptor;

        Assert.Equal(BpmnPropertyGroupKind.General, descriptor.Group);
        Assert.Equal(("name", "Name", BpmnPropertyKind.Text, "Work", false), (name, label, kind, value, readOnly));
        Assert.Empty(choices!);
    }

    [Theory]
    [InlineData(BpmnFlowNodeType.Task, "loopCharacteristics", BpmnPropertyGroupKind.LoopCharacteristics)]
    [InlineData(BpmnFlowNodeType.Task, "isForCompensation", BpmnPropertyGroupKind.Activity)]
    [InlineData(BpmnFlowNodeType.StartEvent, "eventDefinition", BpmnPropertyGroupKind.Event)]
    [InlineData(BpmnFlowNodeType.BoundaryEvent, "isInterrupting", BpmnPropertyGroupKind.Event)]
    [InlineData(BpmnFlowNodeType.ScriptTask, "scriptFormat", BpmnPropertyGroupKind.ScriptTask)]
    [InlineData(BpmnFlowNodeType.ScriptTask, "script", BpmnPropertyGroupKind.ScriptTask)]
    [InlineData(BpmnFlowNodeType.CallActivity, "calledElementRef", BpmnPropertyGroupKind.CallActivity)]
    [InlineData(BpmnFlowNodeType.SendTask, "messageRef", BpmnPropertyGroupKind.SendTask)]
    [InlineData(BpmnFlowNodeType.ReceiveTask, "messageRef", BpmnPropertyGroupKind.ReceiveTask)]
    [InlineData(BpmnFlowNodeType.ComplexGateway, "activationCondition", BpmnPropertyGroupKind.ComplexGateway)]
    [InlineData(BpmnFlowNodeType.ComplexGateway, "activationCondition.language", BpmnPropertyGroupKind.ComplexGateway)]
    [InlineData(BpmnFlowNodeType.ComplexGateway, "activationCondition.type", BpmnPropertyGroupKind.ComplexGateway)]
    public void FlowNodePropertiesDeclareTheirBpmnConcept(BpmnFlowNodeType type, string propertyName, BpmnPropertyGroupKind expected)
    {
        var document = new BpmnDocumentBuilder().Node("Node", type, new Bounds(0, 0, 100, 80)).Build();

        var properties = BpmnElementInspector.PropertiesOf(document, "Node");
        var snapshot = BpmnElementInspector.Describe(document, "Node")!;

        Assert.Equal(expected, Assert.Single(properties, property => property.Name == propertyName).Group);
        Assert.Equal(properties.Select(property => (property.Name, property.Group)),
            snapshot.Properties.Select(property => (property.Name, property.Group)));
        Assert.All(properties.Where(property => property.Name is "name" or "documentation"),
            property => Assert.Equal(BpmnPropertyGroupKind.General, property.Group));
    }

    [Theory]
    [InlineData(BpmnFlowNodeType.Task, BpmnPropertyGroupKind.Activity)]
    [InlineData(BpmnFlowNodeType.ExclusiveGateway, BpmnPropertyGroupKind.Gateway)]
    [InlineData(BpmnFlowNodeType.ComplexGateway, BpmnPropertyGroupKind.ComplexGateway)]
    public void DefaultFlowsBelongToTheirOwningActivityOrGateway(BpmnFlowNodeType type, BpmnPropertyGroupKind expected)
    {
        var document = new BpmnDocumentBuilder().Node("Node", type, new Bounds(0, 0, 100, 80)).Build();
        document = document with
        {
            SequenceFlows = [new("Flow_1", "Node", "Target_1", "Process_1"), new("Flow_2", "Node", "Target_2", "Process_1")],
        };

        var property = Assert.Single(BpmnElementInspector.PropertiesOf(document, "Node"), property => property.Name == "defaultFlowRef");

        Assert.Equal(expected, property.Group);
    }

    [Fact]
    public void StandardLoopFieldsAndExpressionMetadataStayTogether()
    {
        var document = TaskDocument(new BpmnFlowNode("Task", BpmnFlowNodeType.Task, "Process_1")
        {
            LoopCharacteristics = BpmnLoopCharacteristics.Standard,
            StandardLoop = new(LoopCondition: new("ready")),
        });

        var groups = Groups(document, "Task");

        Assert.Equal(["general", "activity", "loop-characteristics"], groups.Select(group => group.Id));
        Assert.Equal(["loopCharacteristics", "standardLoop.testBefore", "standardLoop.loopMaximum",
            "standardLoop.loopCondition", "standardLoop.loopCondition.language", "standardLoop.loopCondition.type"],
            groups[2].Entries.Select(entry => entry.Id));
    }

    [Theory]
    [InlineData(BpmnLoopCharacteristics.MultiInstanceParallel)]
    [InlineData(BpmnLoopCharacteristics.MultiInstanceSequential)]
    public void MultiInstanceDetailsSeparateComplexBehaviorDefinitions(BpmnLoopCharacteristics loop)
    {
        var document = TaskDocument(new BpmnFlowNode("Task", BpmnFlowNodeType.Task, "Process_1")
        {
            LoopCharacteristics = loop,
            MultiInstanceLoop = new(ComplexBehaviorDefinitions:
                [new("Event_1", new("first")), new("Event_2", new("second"))]),
        });

        var groups = Groups(document, "Task");

        Assert.Equal(["general", "activity", "loop-characteristics", "complex-behavior-definition"], groups.Select(group => group.Id));
        Assert.Equal("Loop Characteristics", groups[2].Label);
        Assert.Equal("Complex Behavior Definition", groups[3].Label);
        Assert.Equal(["multiInstance.complexBehavior.0.event", "multiInstance.complexBehavior.0.condition",
            "multiInstance.complexBehavior.0.condition.language", "multiInstance.complexBehavior.0.condition.type",
            "multiInstance.complexBehavior.1.event", "multiInstance.complexBehavior.1.condition",
            "multiInstance.complexBehavior.1.condition.language", "multiInstance.complexBehavior.1.condition.type"],
            groups[3].Entries.Select(entry => entry.Id));
        Assert.Contains(groups[2].Entries, entry => entry.Id == "multiInstance.inputDataItem.id");
        Assert.Contains(groups[2].Entries, entry => entry.Id == "multiInstance.outputDataItem.name");
        Assert.DoesNotContain(groups[2].Entries, entry => entry.Id.Contains(".complexBehavior.", StringComparison.Ordinal));
        Assert.Equal(8, groups[3].Entries.Count);
        Assert.All(groups, group => Assert.All(group.Entries, entry => Assert.Equal(group.Id, entry.GroupId)));
    }

    [Fact]
    public void InactiveLoopsDoNotCreateEmptyDetailGroups()
    {
        var document = TaskDocument(new("Task", BpmnFlowNodeType.Task, "Process_1"));

        var groups = Groups(document, "Task");

        Assert.Equal(["general", "activity", "loop-characteristics"], groups.Select(group => group.Id));
        Assert.Equal("loopCharacteristics", Assert.Single(groups[2].Entries).Id);
        Assert.All(groups, group => Assert.True(group.OpenByDefault));
    }

    [Theory]
    [InlineData("Process_1", "isExecutable", BpmnPropertyGroupKind.Process)]
    [InlineData("Process_1", "processType", BpmnPropertyGroupKind.Process)]
    [InlineData("Process_1", "isClosed", BpmnPropertyGroupKind.Process)]
    [InlineData("Sequence", "isDefault", BpmnPropertyGroupKind.SequenceFlow)]
    [InlineData("Sequence", "conditionExpression", BpmnPropertyGroupKind.SequenceFlow)]
    [InlineData("Sequence", "conditionLanguage", BpmnPropertyGroupKind.SequenceFlow)]
    [InlineData("Message", "messageRef", BpmnPropertyGroupKind.MessageFlow)]
    [InlineData("Association", "associationDirection", BpmnPropertyGroupKind.Association)]
    [InlineData("Data", "assignment.0.from", BpmnPropertyGroupKind.Assignment)]
    [InlineData("Data", "assignment.0.from.language", BpmnPropertyGroupKind.Assignment)]
    [InlineData("Data", "assignment.0.from.type", BpmnPropertyGroupKind.Assignment)]
    [InlineData("Data", "assignment.0.to", BpmnPropertyGroupKind.Assignment)]
    [InlineData("Data", "assignment.0.to.language", BpmnPropertyGroupKind.Assignment)]
    [InlineData("Data", "assignment.0.to.type", BpmnPropertyGroupKind.Assignment)]
    [InlineData("Annotation", "text", BpmnPropertyGroupKind.TextAnnotation)]
    [InlineData("Annotation", "textFormat", BpmnPropertyGroupKind.TextAnnotation)]
    [InlineData("Group", "categoryValueRef", BpmnPropertyGroupKind.Group)]
    [InlineData("Object", "dataObjectRef", BpmnPropertyGroupKind.DataObjectReference)]
    [InlineData("Object", "isCollection", BpmnPropertyGroupKind.DataObjectReference)]
    [InlineData("Object", "state", BpmnPropertyGroupKind.DataState)]
    [InlineData("Store", "dataStoreRef", BpmnPropertyGroupKind.DataStoreReference)]
    [InlineData("Store", "state", BpmnPropertyGroupKind.DataState)]
    public void OtherElementPropertiesDeclareTheirBpmnConcept(string elementId, string propertyName, BpmnPropertyGroupKind expected)
    {
        var document = OtherElements();

        var property = Assert.Single(BpmnElementInspector.PropertiesOf(document, elementId), property => property.Name == propertyName);

        Assert.Equal(expected, property.Group);
    }

    [Fact]
    public void ReadOnlySnapshotsRetainClassification()
    {
        var document = TaskDocument(new("Task", BpmnFlowNodeType.ScriptTask, "Process_1"));

        var snapshot = BpmnElementInspector.Describe(document, "Task", isReadOnly: true)!;

        Assert.All(snapshot.Properties, property => Assert.True(property.IsReadOnly));
        Assert.Equal(BpmnPropertyGroupKind.ScriptTask, snapshot.Properties.Single(property => property.Name == "script").Group);
    }

    [Fact]
    public void ProviderOrdersGroupsWithoutReorderingEntriesOrDroppingExistingGroups()
    {
        var snapshot = new BpmnElementSnapshot
        {
            ElementId = "Task", ElementType = "task", Category = BpmnElementCategory.Activity,
            Properties = [.. Enum.GetValues<BpmnPropertyGroupKind>().Reverse().Select(group =>
                new BpmnPropertyDescriptor(group.ToString(), group.ToString(), BpmnPropertyKind.Text, null) { Group = group })],
        };
        var provider = new BpmnGeneralPropertiesProvider();
        var existing = new BpmnPropertyGroup("host", "Host", []);

        var groups = provider.GetGroups(snapshot, [existing]);

        Assert.Same(existing, groups[0]);
        Assert.Equal(Enum.GetValues<BpmnPropertyGroupKind>().Select(group => group.ToString()),
            groups.Skip(1).Select(group => Assert.Single(group.Entries).Id));
        Assert.All(groups.Skip(1), group => Assert.True(group.HasBuiltInLabels));
        Assert.Same(groups, provider.GetGroups(snapshot with { Properties = [] }, groups));
    }

    private static IReadOnlyList<BpmnPropertyGroup> Groups(BpmnDocument document, string id) =>
        new BpmnGeneralPropertiesProvider().GetGroups(BpmnElementInspector.Describe(document, id)!, []);

    private static BpmnDocument TaskDocument(BpmnFlowNode node) =>
        new BpmnDocumentBuilder().Node("Task", node.Type, new Bounds(0, 0, 100, 80)).Build() with { FlowNodes = [node] };

    private static BpmnDocument OtherElements() => TaskDocument(new("Task", BpmnFlowNodeType.Task, "Process_1")) with
    {
        SequenceFlows = [new("Sequence", "Task", "Task", "Process_1")],
        MessageFlows = [new("Message", "Task", "Task")],
        Associations = [new("Association", "Task", "Annotation", "Process_1")],
        DataAssociations = [new("Data", BpmnDataAssociationKind.Input, ["Object"], "Task", "Process_1")
            { Assignments = [new(new("source"), new("target"))] }],
        TextAnnotations = [new("Annotation", "Notes", "Process_1")],
        Groups = [new("Group", "Process_1")],
        DataObjectReferences = [new("Object", "Process_1")],
        DataStoreReferences = [new("Store", "Process_1")],
    };
}
