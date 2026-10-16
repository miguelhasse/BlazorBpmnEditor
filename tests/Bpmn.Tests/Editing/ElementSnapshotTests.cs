using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Editing;

/// <summary>
/// The section 7 property snapshot.
/// </summary>
/// <remarks>
/// The contract is "sufficient for <c>BpmnContextMenu</c> to display and bind standard element
/// properties without re-querying or inferring them from SVG/JointJS cells". So what is asserted
/// here is mostly sufficiency: that the owner, the Lane, the geometry and the references are all
/// answered from the document rather than left for a surface to guess at — and that Readonly
/// answers them too, while offering nothing.
/// </remarks>
public sealed class ElementSnapshotTests
{
    [Fact]
    public void DescribesAnElementsIdentityAndScope()
    {
        var snapshot = Describe(Collaboration(), "Task_1");

        Assert.Equal(BpmnElementSnapshot.CurrentVersion, snapshot.Version);
        Assert.Equal("Task_1", snapshot.ElementId);
        Assert.Equal("serviceTask", snapshot.ElementType);
        Assert.Equal(BpmnElementCategory.Activity, snapshot.Category);
        Assert.Equal("Process_1", snapshot.ContainerId);
        Assert.Equal("Pool_1", snapshot.PoolId);
        Assert.Equal("Lane_1", snapshot.LaneId);
        Assert.Equal("Validate", snapshot.Label);
        Assert.Equal(new Bounds(200, 120, 100, 80), snapshot.Bounds);
    }

    [Fact]
    public void ReadsLaneMembershipFromTheLaneRatherThanTheNode()
    {
        // Section 2.8: membership is the Lane's statement about the node. A snapshot that echoed
        // the node's convenience projection would report exactly the stale value section 7 forbids.
        var document = Collaboration();
        var stale = document with
        {
            FlowNodes = [.. document.FlowNodes.Select(node => node.Id == "Task_1"
                ? node with { LaneId = "Lane_Nonexistent" }
                : node)],
        };

        Assert.Equal("Lane_1", Describe(stale, "Task_1").LaneId);
    }

    [Fact]
    public void ResolvesReferencesToNames()
    {
        var snapshot = Describe(Gatewayed(), "Flow_Yes");

        var source = Assert.Single(snapshot.References, reference => reference.Name == "sourceRef");
        Assert.Equal("Gateway_1", source.TargetId);
        Assert.Equal("Approved?", source.TargetName);
        Assert.True(source.IsResolved);
    }

    [Fact]
    public void ReportsADanglingReferenceAsUnresolved()
    {
        var document = Gatewayed();
        var broken = document with
        {
            SequenceFlows = [.. document.SequenceFlows.Select(flow => flow.Id == "Flow_Yes"
                ? flow with { TargetRef = "Nowhere" }
                : flow)],
        };

        var target = Assert.Single(Describe(broken, "Flow_Yes").References, r => r.Name == "targetRef");
        Assert.False(target.IsResolved);
    }

    [Fact]
    public void CarriesTheRouteForAConnector()
    {
        var snapshot = Describe(Gatewayed(), "Flow_Yes");

        Assert.Equal(BpmnElementCategory.Connector, snapshot.Category);
        Assert.Null(snapshot.Bounds);
        Assert.Equal(2, snapshot.Waypoints.Count);
    }

    // --- the editable property set ------------------------------------------------

    [Fact]
    public void OffersTheStandardPropertiesOfAServiceTask()
    {
        var names = Describe(Collaboration(), "Task_1").Properties.Select(property => property.Name).ToList();

        Assert.Contains("name", names);
        Assert.Contains("loopCharacteristics", names);
        Assert.Contains("isForCompensation", names);

        // Not an Event and not a Call Activity, so neither is offered.
        Assert.DoesNotContain("eventDefinition", names);
        Assert.DoesNotContain("calledElementRef", names);
    }

    [Fact]
    public void OffersOnlyTheTriggersTheEventTypeAllows()
    {
        var trigger = Assert.Single(
            Describe(Gatewayed(), "End_1").Properties,
            property => property.Name == "eventDefinition");

        var values = trigger.Choices.Select(choice => choice.Value).ToList();

        Assert.Contains("terminate", values);
        Assert.Contains("none", values);

        // Timer is catch-only, and Cancel needs a Transaction (EV-002).
        Assert.DoesNotContain("timer", values);
        Assert.DoesNotContain("cancel", values);
    }

    [Fact]
    public void OffersCancelOnATransactionBoundaryOnly()
    {
        var document = new BpmnDocumentBuilder()
            .Node("Txn_1", BpmnFlowNodeType.Transaction, new Bounds(100, 100, 300, 200))
            .Node("Task_1", BpmnFlowNodeType.Task, new Bounds(600, 100, 100, 80))
            .Node("Boundary_Txn", BpmnFlowNodeType.BoundaryEvent, new Bounds(380, 180, 36, 36), attachedToRef: "Txn_1")
            .Node("Boundary_Task", BpmnFlowNodeType.BoundaryEvent, new Bounds(680, 160, 36, 36), attachedToRef: "Task_1")
            .Build();

        Assert.Contains(BpmnEventDefinitionType.Cancel, Triggers(document, "Boundary_Txn"));
        Assert.DoesNotContain(BpmnEventDefinitionType.Cancel, Triggers(document, "Boundary_Task"));
    }

    [Fact]
    public void OffersIsInterruptingOnlyWhereTheNotationDrawsIt()
    {
        var document = new BpmnDocumentBuilder()
            .Node("Task_1", BpmnFlowNodeType.Task, new Bounds(100, 100, 100, 80))
            .Node("Boundary_1", BpmnFlowNodeType.BoundaryEvent, new Bounds(180, 160, 36, 36), attachedToRef: "Task_1")
            .Node("Start_1", BpmnFlowNodeType.StartEvent, new Bounds(300, 100, 36, 36))
            .Build();

        Assert.Contains(Describe(document, "Boundary_1").Properties, p => p.Name == "isInterrupting");
        Assert.DoesNotContain(Describe(document, "Start_1").Properties, p => p.Name == "isInterrupting");
    }

    [Fact]
    public void OffersADefaultFlowOnlyWhenThereIsAChoiceToMake()
    {
        var snapshot = Describe(Gatewayed(), "Gateway_1");
        var property = Assert.Single(snapshot.Properties, p => p.Name == "defaultFlowRef");

        // Two branches plus "(none)".
        Assert.Equal(3, property.Choices.Count);
    }

    // --- capabilities and readonly --------------------------------------------------

    [Fact]
    public void ReadonlyDescribesEverythingAndPermitsNothing()
    {
        var snapshot = Describe(Collaboration(), "Task_1", isReadOnly: true);

        Assert.Equal("Validate", snapshot.Label);
        Assert.NotEmpty(snapshot.Properties);
        Assert.All(snapshot.Properties, property => Assert.True(property.IsReadOnly));

        Assert.Equal(BpmnElementCapabilities.None, snapshot.Capabilities);
        Assert.Empty(snapshot.SwitchableTypes);
    }

    [Fact]
    public void ABoundaryEventIsNotReparented()
    {
        // CT-005: it moves by re-attaching to another host, so offering "reparent" would offer the
        // wrong operation entirely.
        var document = new BpmnDocumentBuilder()
            .Node("Task_1", BpmnFlowNodeType.Task, new Bounds(100, 100, 100, 80))
            .Node("Boundary_1", BpmnFlowNodeType.BoundaryEvent, new Bounds(180, 160, 36, 36), attachedToRef: "Task_1")
            .Build();

        Assert.False(Describe(document, "Boundary_1").Capabilities.CanReparent);
        Assert.True(Describe(document, "Task_1").Capabilities.CanReparent);
    }

    [Fact]
    public void AGroupNeverBecomesAParent()
    {
        // CT-003: a Group categorises and never owns.
        var document = new BpmnDocumentBuilder()
            .Group("Group_1", "Region", new Bounds(80, 80, 300, 200))
            .Build();

        Assert.False(Describe(document, "Group_1").Capabilities.CanReparent);
        Assert.True(Describe(document, "Group_1").Capabilities.CanResize);
        Assert.True(Describe(document, "Group_1").Capabilities.CanDelete);
        Assert.False(Describe(document, "Group_1").Capabilities.CanConnect);
    }

    [Fact]
    public void ReturnsNullForAnIdTheDocumentDoesNotHave() =>
        Assert.Null(BpmnElementInspector.Describe(Collaboration(), "Nothing_1"));

    // --- switchable types ------------------------------------------------------------

    [Fact]
    public void OffersTheOtherTasksAndMarksTheCurrentOne()
    {
        var options = Describe(Collaboration(), "Task_1").SwitchableTypes;

        Assert.Equal(8, options.Count);
        Assert.Equal(BpmnFlowNodeType.ServiceTask, Assert.Single(options, option => option.IsCurrent).Type);
        Assert.Contains(options, option => option.Type == BpmnFlowNodeType.UserTask);
    }

    [Fact]
    public void OffersNothingForACallActivityOrAnEventSubProcess()
    {
        // Neither converts while keeping what section 7 promises to keep: a Call Activity's
        // identity is the reference it carries, and no Sequence Flow crosses an Event Sub-Process.
        var document = new BpmnDocumentBuilder()
            .Node("Call_1", BpmnFlowNodeType.CallActivity, new Bounds(100, 100, 100, 80))
            .Node("EventSub_1", BpmnFlowNodeType.EventSubProcess, new Bounds(300, 100, 300, 200))
            .Build();

        Assert.Empty(Describe(document, "Call_1").SwitchableTypes);
        Assert.Empty(Describe(document, "EventSub_1").SwitchableTypes);
        Assert.False(Describe(document, "Call_1").Capabilities.CanSwitchType);
    }

    [Fact]
    public void OffersTriggersRatherThanTypesForAnEvent()
    {
        var options = Describe(Gatewayed(), "End_1").SwitchableTypes;

        Assert.All(options, option => Assert.Equal(BpmnFlowNodeType.EndEvent, option.Type));
        Assert.Contains(options, option => option.EventDefinition == BpmnEventDefinitionType.Terminate);
    }

    // --- helpers ----------------------------------------------------------------------

    private static IReadOnlyList<BpmnEventDefinitionType> Triggers(BpmnDocument document, string id) =>
        BpmnElementInspector.TriggersFor(document, document.FindFlowNode(id)!);

    private static BpmnElementSnapshot Describe(BpmnDocument document, string id, bool isReadOnly = false) =>
        BpmnElementInspector.Describe(document, id, isReadOnly)
        ?? throw new InvalidOperationException($"'{id}' produced no snapshot.");

    private static BpmnDocument Collaboration() => new BpmnDocumentBuilder()
        .Pool("Pool_1", "Customer", new Bounds(40, 40, 900, 400))
        .Lane("Lane_1", "Sales", new Bounds(70, 40, 870, 400), ["Task_1"])
        .Node("Task_1", BpmnFlowNodeType.ServiceTask, new Bounds(200, 120, 100, 80), "Validate", poolId: "Pool_1")
        .Build();

    private static BpmnDocument Gatewayed() => new BpmnDocumentBuilder()
        .Node("Gateway_1", BpmnFlowNodeType.ExclusiveGateway, new Bounds(200, 120, 50, 50), "Approved?")
        .Node("Task_Yes", BpmnFlowNodeType.Task, new Bounds(340, 100, 100, 80), "Ship")
        .Node("Task_No", BpmnFlowNodeType.Task, new Bounds(340, 240, 100, 80), "Reject")
        .Node("End_1", BpmnFlowNodeType.EndEvent, new Bounds(520, 130, 36, 36), "Done")
        .Flow("Flow_Yes", "Gateway_1", "Task_Yes", [new DiPoint(250, 145), new DiPoint(340, 140)], "yes")
        .Flow("Flow_No", "Gateway_1", "Task_No", [new DiPoint(225, 170), new DiPoint(340, 280)], "no")
        .Build();
}
