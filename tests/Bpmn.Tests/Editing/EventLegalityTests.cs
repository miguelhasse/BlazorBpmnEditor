using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Validation;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Editing;

public sealed class EventLegalityTests
{
    [Theory]
    [InlineData("rootStart", "None", true, true)]
    [InlineData("rootStart", "Message", true, true)]
    [InlineData("rootStart", "Error", true, false)]
    [InlineData("rootStart", "Compensation", true, false)]
    [InlineData("embeddedStart", "None", true, true)]
    [InlineData("embeddedStart", "Timer", true, false)]
    [InlineData("eventSubStart", "None", true, false)]
    [InlineData("eventSubStart", "Message", false, true)]
    [InlineData("eventSubStart", "Error", true, true)]
    [InlineData("eventSubStart", "Error", false, false)]
    [InlineData("eventSubStart", "Compensation", false, true)]
    [InlineData("eventSubStart", "Compensation", true, false)]
    [InlineData("transactionEnd", "Cancel", true, true)]
    [InlineData("rootEnd", "Cancel", true, false)]
    [InlineData("transactionBoundary", "Cancel", true, true)]
    [InlineData("transactionBoundary", "Cancel", false, false)]
    [InlineData("taskBoundary", "Cancel", true, false)]
    [InlineData("taskBoundary", "Error", false, false)]
    [InlineData("taskBoundary", "Error", true, true)]
    [InlineData("taskBoundary", "Compensation", true, true)]
    [InlineData("taskBoundary", "Compensation", false, false)]
    [InlineData("catch", "Timer", true, true)]
    [InlineData("throw", "Timer", true, false)]
    public void AppliesTheBpmn202EventContextMatrix(
        string context,
        string triggerName,
        bool isInterrupting,
        bool expectedLegal)
    {
        var trigger = Enum.Parse<BpmnEventDefinitionType>(triggerName);
        var document = Document(context, trigger, isInterrupting);
        var node = Assert.Single(document.FlowNodes, candidate => candidate.Id == "Event_1");

        Assert.Equal(expectedLegal, BpmnElementInspector.IsLegalEvent(document, node));
        Assert.Equal(
            expectedLegal,
            BpmnValidator.Validate(document).Diagnostics.All(diagnostic =>
                diagnostic.RuleId != "D020" || diagnostic.ElementId != node.Id));
    }

    [Fact]
    public void ExposesTriggerChoicesForEachStartContainer()
    {
        var topLevel = Document("rootStart", BpmnEventDefinitionType.None, true);
        var embedded = Document("embeddedStart", BpmnEventDefinitionType.None, true);
        var eventSubProcess = Document("eventSubStart", BpmnEventDefinitionType.None, true);
        var nonInterruptingEventSubProcess = Document("eventSubStart", BpmnEventDefinitionType.None, false);

        Assert.Contains(
            BpmnEventDefinitionType.Timer,
            BpmnElementInspector.TriggersFor(topLevel, topLevel.FindFlowNode("Event_1")!));
        Assert.Equal(
            [BpmnEventDefinitionType.None],
            BpmnElementInspector.TriggersFor(embedded, embedded.FindFlowNode("Event_1")!));
        Assert.Contains(
            BpmnEventDefinitionType.Error,
            BpmnElementInspector.TriggersFor(eventSubProcess, eventSubProcess.FindFlowNode("Event_1")!));
        Assert.DoesNotContain(
            BpmnEventDefinitionType.None,
            BpmnElementInspector.TriggersFor(eventSubProcess, eventSubProcess.FindFlowNode("Event_1")!));
        Assert.Contains(
            BpmnEventDefinitionType.Error,
            BpmnElementInspector.TriggersFor(eventSubProcess, eventSubProcess.FindFlowNode("Event_1")!));
        Assert.DoesNotContain(
            BpmnEventDefinitionType.Compensation,
            BpmnElementInspector.TriggersFor(eventSubProcess, eventSubProcess.FindFlowNode("Event_1")!));
        Assert.Contains(
            BpmnEventDefinitionType.Compensation,
            BpmnElementInspector.TriggersFor(
                nonInterruptingEventSubProcess,
                nonInterruptingEventSubProcess.FindFlowNode("Event_1")!));
        Assert.DoesNotContain(
            BpmnEventDefinitionType.Error,
            BpmnElementInspector.TriggersFor(
                nonInterruptingEventSubProcess,
                nonInterruptingEventSubProcess.FindFlowNode("Event_1")!));
    }

    [Fact]
    public void ChecksBoundaryHostAndContainerAsPartOfTheEventContext()
    {
        var document = new BpmnDocumentBuilder("Process_1")
            .Node("Sub_1", BpmnFlowNodeType.SubProcess, new Bounds(40, 40, 300, 180))
            .Node("Task_1", BpmnFlowNodeType.Task, new Bounds(80, 80, 100, 80), containerId: "Sub_1")
            .Node(
                "Event_1",
                BpmnFlowNodeType.BoundaryEvent,
                new Bounds(120, 62, 36, 36),
                eventDefinition: BpmnEventDefinitionType.Message,
                attachedToRef: "Task_1")
            .Build();
        var boundary = document.FindFlowNode("Event_1")!;

        Assert.False(BpmnElementInspector.IsLegalEvent(document, boundary));
        Assert.Empty(BpmnElementInspector.TriggersFor(document, boundary));
        Assert.Contains(
            BpmnValidator.Validate(document).Diagnostics,
            diagnostic => diagnostic.RuleId == "D020" && diagnostic.ElementId == boundary.Id);
    }

    [Fact]
    public void TriggerChoicesRespectBoundaryInterruptionAndHostContext()
    {
        var document = new BpmnDocumentBuilder("Process_1")
            .Node("Task_1", BpmnFlowNodeType.Task, new Bounds(100, 100, 100, 80))
            .Node(
                "Boundary_1",
                BpmnFlowNodeType.BoundaryEvent,
                new Bounds(140, 82, 36, 36),
                eventDefinition: BpmnEventDefinitionType.Error,
                isInterrupting: false,
                attachedToRef: "Task_1")
            .Build();
        var boundary = document.FindFlowNode("Boundary_1")!;

        Assert.Contains(
            BpmnEventDefinitionType.Error,
            BpmnElementInspector.SupportedTriggersFor(document, boundary));
        Assert.DoesNotContain(
            BpmnEventDefinitionType.Error,
            BpmnElementInspector.TriggersFor(document, boundary));
        Assert.DoesNotContain(
            BpmnEventDefinitionType.Compensation,
            BpmnElementInspector.TriggersFor(document, boundary));
        Assert.Contains(
            BpmnEventDefinitionType.Error,
            BpmnElementInspector.TriggersFor(document, boundary with { IsInterrupting = true }));
        Assert.Contains(
            BpmnEventDefinitionType.Compensation,
            BpmnElementInspector.TriggersFor(document, boundary with { IsInterrupting = true }));
    }

    private static BpmnDocument Document(string context, BpmnEventDefinitionType trigger, bool isInterrupting)
    {
        var builder = new BpmnDocumentBuilder("Process_1");

        switch (context)
        {
            case "rootStart":
                builder.Node(
                    "Event_1", BpmnFlowNodeType.StartEvent, new Bounds(80, 100, 36, 36),
                    eventDefinition: trigger, isInterrupting: isInterrupting);
                break;

            case "embeddedStart":
                builder.Node("Container_1", BpmnFlowNodeType.SubProcess, new Bounds(40, 40, 300, 180))
                    .Node(
                        "Event_1", BpmnFlowNodeType.StartEvent, new Bounds(80, 100, 36, 36),
                        eventDefinition: trigger, isInterrupting: isInterrupting, containerId: "Container_1");
                break;

            case "eventSubStart":
                builder.Node("Container_1", BpmnFlowNodeType.EventSubProcess, new Bounds(40, 40, 300, 180))
                    .Node(
                        "Event_1", BpmnFlowNodeType.StartEvent, new Bounds(80, 100, 36, 36),
                        eventDefinition: trigger, isInterrupting: isInterrupting, containerId: "Container_1");
                break;

            case "transactionEnd":
                builder.Node("Container_1", BpmnFlowNodeType.Transaction, new Bounds(40, 40, 300, 180))
                    .Node(
                        "Event_1", BpmnFlowNodeType.EndEvent, new Bounds(80, 100, 36, 36),
                        eventDefinition: trigger, isInterrupting: isInterrupting, containerId: "Container_1");
                break;

            case "rootEnd":
                builder.Node(
                    "Event_1", BpmnFlowNodeType.EndEvent, new Bounds(80, 100, 36, 36),
                    eventDefinition: trigger, isInterrupting: isInterrupting);
                break;

            case "transactionBoundary":
            case "taskBoundary":
                var hostType = context == "transactionBoundary"
                    ? BpmnFlowNodeType.Transaction
                    : BpmnFlowNodeType.Task;
                builder.Node("Host_1", hostType, new Bounds(40, 40, 300, 180))
                    .Node(
                        "Event_1", BpmnFlowNodeType.BoundaryEvent, new Bounds(160, 22, 36, 36),
                        eventDefinition: trigger,
                        isInterrupting: isInterrupting,
                        attachedToRef: "Host_1");
                break;

            case "catch":
                builder.Node(
                    "Event_1", BpmnFlowNodeType.IntermediateCatchEvent, new Bounds(80, 100, 36, 36),
                    eventDefinition: trigger, isInterrupting: isInterrupting);
                break;

            case "throw":
                builder.Node(
                    "Event_1", BpmnFlowNodeType.IntermediateThrowEvent, new Bounds(80, 100, 36, 36),
                    eventDefinition: trigger, isInterrupting: isInterrupting);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(context), context, "Unknown event context.");
        }

        return builder.Build();
    }
}
