using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Editing;

public sealed class EventEditPolicyTests
{
    [Fact]
    public void RejectsInvalidCombinedPropertyBatchWithoutPartialChanges()
    {
        var document = EventSubProcessStart(BpmnEventDefinitionType.Message, isInterrupting: false);

        var result = BpmnDocumentEditor.Apply(document, new UpdatePropertiesCommand("Start_1",
        [
            new BpmnPropertyValue("name", "Changed"),
            new BpmnPropertyValue("eventDefinition", "error"),
        ]));

        Assert.False(result.IsApplied);
        Assert.Same(document, result.Document);
        Assert.Null(result.Document.FindFlowNode("Start_1")!.Name);
        Assert.Contains(result.Rejections, rejection => rejection.Code == "EDIT-084");
    }

    [Theory]
    [MemberData(nameof(ValidErrorPropertyOrders))]
    public void AcceptsValidCombinedTriggerAndInterruptionInEitherOrder(IReadOnlyList<BpmnPropertyValue> values)
    {
        var document = EventSubProcessStart(BpmnEventDefinitionType.Message, isInterrupting: false);

        var result = BpmnDocumentEditor.Apply(document, new UpdatePropertiesCommand("Start_1", values));

        Assert.True(result.IsApplied);
        var updated = result.Document.FindFlowNode("Start_1")!;
        Assert.Equal(BpmnEventDefinitionType.Error, updated.EventDefinition);
        Assert.True(updated.IsInterrupting);
    }

    [Fact]
    public void AutoAdjustsOnlyInterruptionAndHistoryRestoresExactEvent()
    {
        var original = EventSubProcessStart(BpmnEventDefinitionType.Message, isInterrupting: false);
        var history = new BpmnEditHistory(original, AutoAdjust);

        var result = history.Apply(new UpdatePropertiesCommand("Start_1", "eventDefinition", "error"));

        Assert.True(result.IsApplied);
        Assert.True(history.Document.FindFlowNode("Start_1")!.IsInterrupting);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("EDIT-084", diagnostic.Code);
        Assert.Equal(BpmnEditSeverity.Info, diagnostic.Severity);

        Assert.Same(original, history.Undo());
        Assert.True(history.Redo().FindFlowNode("Start_1")!.IsInterrupting);
    }

    [Fact]
    public void AutoAdjustsCompensationStartToNonInterruptingWhenCreated()
    {
        var document = EventSubProcessStartContainer();

        var result = BpmnDocumentEditor.Apply(
            document,
            new CreateElementCommand(
                "Start_2",
                BpmnFlowNodeType.StartEvent,
                "EventSub_1",
                new Bounds(220, 120, 36, 36),
                EventDefinition: BpmnEventDefinitionType.Compensation)
            {
                IsInterrupting = true,
            },
            AutoAdjust);

        Assert.True(result.IsApplied);
        Assert.False(result.Document.FindFlowNode("Start_2")!.IsInterrupting);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "EDIT-084");
    }

    [Fact]
    public void AutoAdjustsBoundaryErrorToInterruptingWhenCreated()
    {
        var document = new BpmnDocumentBuilder("Process_1")
            .Node("Task_1", BpmnFlowNodeType.Task, new Bounds(100, 100, 100, 80))
            .Build();

        var result = BpmnDocumentEditor.Apply(
            document,
            new CreateElementCommand(
                "Boundary_1",
                BpmnFlowNodeType.BoundaryEvent,
                "Process_1",
                new Bounds(140, 82, 36, 36),
                EventDefinition: BpmnEventDefinitionType.Error,
                AttachedToRef: "Task_1")
            {
                IsInterrupting = false,
            },
            AutoAdjust);

        Assert.True(result.IsApplied);
        Assert.True(result.Document.FindFlowNode("Boundary_1")!.IsInterrupting);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "EDIT-084");
    }

    [Fact]
    public void AutoAdjustsBoundaryCompensationToInterruptingWhenCreated()
    {
        var document = new BpmnDocumentBuilder("Process_1")
            .Node("Task_1", BpmnFlowNodeType.Task, new Bounds(100, 100, 100, 80))
            .Build();

        var result = BpmnDocumentEditor.Apply(
            document,
            new CreateElementCommand(
                "Boundary_1",
                BpmnFlowNodeType.BoundaryEvent,
                "Process_1",
                new Bounds(140, 82, 36, 36),
                EventDefinition: BpmnEventDefinitionType.Compensation,
                AttachedToRef: "Task_1")
            {
                IsInterrupting = false,
            },
            AutoAdjust);

        Assert.True(result.IsApplied);
        Assert.True(result.Document.FindFlowNode("Boundary_1")!.IsInterrupting);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "EDIT-084");
    }

    [Fact]
    public void AutoAdjustsErrorDuringTypeSwitchWithoutSubstitutingTheTrigger()
    {
        var document = EventSubProcessStart(BpmnEventDefinitionType.Message, isInterrupting: false);

        var result = BpmnDocumentEditor.Apply(
            document,
            new SwitchElementTypeCommand("Start_1", BpmnFlowNodeType.StartEvent, BpmnEventDefinitionType.Error),
            AutoAdjust);

        Assert.True(result.IsApplied);
        var node = result.Document.FindFlowNode("Start_1")!;
        Assert.Equal(BpmnEventDefinitionType.Error, node.EventDefinition);
        Assert.True(node.IsInterrupting);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "EDIT-084");
    }

    [Fact]
    public void AutoPolicyStillRejectsUnsupportedTriggersAndLeavesDocumentUntouched()
    {
        var document = new BpmnDocumentBuilder("Process_1").Build();

        var result = BpmnDocumentEditor.Apply(
            document,
            new CreateElementCommand(
                "Start_1",
                BpmnFlowNodeType.StartEvent,
                "Process_1",
                new Bounds(80, 100, 36, 36),
                EventDefinition: BpmnEventDefinitionType.Error)
            {
                IsInterrupting = false,
            },
            AutoAdjust);

        Assert.False(result.IsApplied);
        Assert.Same(document, result.Document);
        Assert.Contains(result.Rejections, rejection => rejection.Code == "EDIT-084");
        Assert.DoesNotContain(result.Document.FlowNodes, node => node.Id == "Start_1");
    }

    [Fact]
    public void AutoPolicyRejectsReparentingIntoAnUnsupportedStartContext()
    {
        var document = new BpmnDocumentBuilder("Process_1")
            .Node("EventSub_1", BpmnFlowNodeType.EventSubProcess, new Bounds(40, 40, 300, 180))
            .Node("Sub_1", BpmnFlowNodeType.SubProcess, new Bounds(400, 40, 300, 180))
            .Node(
                "Start_1",
                BpmnFlowNodeType.StartEvent,
                new Bounds(100, 100, 36, 36),
                eventDefinition: BpmnEventDefinitionType.Error,
                containerId: "EventSub_1")
            .Build();

        var result = BpmnDocumentEditor.Apply(
            document,
            new ReparentElementCommand("Start_1", "Sub_1", null, new Bounds(460, 100, 36, 36)),
            AutoAdjust);

        Assert.False(result.IsApplied);
        Assert.Same(document, result.Document);
        Assert.Contains(result.Rejections, rejection => rejection.Code == "EDIT-084");
    }

    [Fact]
    public void UnrelatedEditsDoNotBlockOrRepairAnImportedInvalidEvent()
    {
        var document = EventSubProcessStart(BpmnEventDefinitionType.Error, isInterrupting: false);

        var result = BpmnDocumentEditor.Apply(
            document,
            new UpdatePropertiesCommand("Start_1", "name", "Keep working"),
            AutoAdjust);

        Assert.True(result.IsApplied);
        var node = result.Document.FindFlowNode("Start_1")!;
        Assert.Equal("Keep working", node.Name);
        Assert.False(node.IsInterrupting);
    }

    [Fact]
    public void ExplicitEventEditCanRepairAnImportedInterruptionViolation()
    {
        var document = EventSubProcessStart(BpmnEventDefinitionType.Error, isInterrupting: false);

        var rejected = BpmnDocumentEditor.Apply(
            document,
            new UpdatePropertiesCommand("Start_1", "eventDefinition", "error"));
        var adjusted = BpmnDocumentEditor.Apply(
            document,
            new UpdatePropertiesCommand("Start_1", "eventDefinition", "error"),
            AutoAdjust);

        Assert.False(rejected.IsApplied);
        Assert.True(adjusted.IsApplied);
        Assert.True(adjusted.Document.FindFlowNode("Start_1")!.IsInterrupting);
        Assert.Contains(adjusted.Diagnostics, diagnostic => diagnostic.Code == "EDIT-084");
    }

    [Fact]
    public void HotConfigurationChangesAreUsedByAnExistingHistory()
    {
        var original = EventSubProcessStart(BpmnEventDefinitionType.Message, isInterrupting: false);
        var history = new BpmnEditHistory(original);
        history.UpdateOptions(AutoAdjust);

        var result = history.Apply(new UpdatePropertiesCommand("Start_1", "eventDefinition", "error"));

        Assert.True(result.IsApplied);
        Assert.True(history.Document.FindFlowNode("Start_1")!.IsInterrupting);
    }

    [Fact]
    public void RejectPolicyDoesNotAddUndoEntryForInvalidEventEdit()
    {
        var document = EventSubProcessStart(BpmnEventDefinitionType.Message, isInterrupting: false);
        var history = new BpmnEditHistory(document);

        var result = history.Apply(new UpdatePropertiesCommand("Start_1", "eventDefinition", "error"));

        Assert.False(result.IsApplied);
        Assert.False(history.CanUndo);
        Assert.Same(document, history.Document);
    }

    public static IEnumerable<object[]> ValidErrorPropertyOrders()
    {
        yield return
        [
            (IReadOnlyList<BpmnPropertyValue>)
            [
                new("eventDefinition", "error"),
                new("isInterrupting", "true"),
            ],
        ];
        yield return
        [
            (IReadOnlyList<BpmnPropertyValue>)
            [
                new("isInterrupting", "true"),
                new("eventDefinition", "error"),
            ],
        ];
    }

    private static EditingOptions AutoAdjust =>
        EditingOptions.Default with { EventEditPolicy = BpmnEventEditPolicy.AutoAdjustInterrupting };

    private static BpmnDocument EventSubProcessStart(
        BpmnEventDefinitionType trigger,
        bool isInterrupting) =>
        EventSubProcessStartContainer()
            .WithStart(trigger, isInterrupting);

    private static BpmnDocument EventSubProcessStartContainer() =>
        new BpmnDocumentBuilder("Process_1")
            .Node("EventSub_1", BpmnFlowNodeType.EventSubProcess, new Bounds(40, 40, 300, 180))
            .Build();
}

internal static class EventEditPolicyTestDocumentExtensions
{
    internal static BpmnDocument WithStart(
        this BpmnDocument document,
        BpmnEventDefinitionType trigger,
        bool isInterrupting)
    {
        var start = new BpmnFlowNode(
            "Start_1",
            BpmnFlowNodeType.StartEvent,
            "EventSub_1",
            EventDefinition: trigger,
            IsInterrupting: isInterrupting);

        return document with
        {
            FlowNodes = [.. document.FlowNodes, start],
            Diagrams = [.. document.Diagrams.Select(diagram => diagram with
            {
                Plane = diagram.Plane with
                {
                    Shapes = [.. diagram.Plane.Shapes, new BpmnShape("Start_1_di", "Start_1", new Bounds(100, 120, 36, 36))],
                },
            })],
        };
    }
}
