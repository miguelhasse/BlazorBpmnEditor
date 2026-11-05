using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Editing;

public sealed class SnapshotReuseTests
{
    [Fact]
    public void LabelCapabilityMatchesDisplayedPropertiesForEveryFlowNodeType()
    {
        foreach (var type in Enum.GetValues<BpmnFlowNodeType>())
        {
            var document = new BpmnDocumentBuilder()
                .Node("task", type, new Bounds(0, 0, 100, 80)).Build();

            var snapshot = BpmnElementInspector.Describe(document, "task")!;
            var readOnly = BpmnElementInspector.Describe(document, "task", isReadOnly: true)!;

            Assert.Equal(snapshot.Properties.Any(property => property.Name is "name" or "text"),
                snapshot.Capabilities.CanEditLabel);
            Assert.Equal(BpmnElementCapabilities.None, readOnly.Capabilities);
            Assert.All(readOnly.Properties, property => Assert.True(property.IsReadOnly));
        }
    }

    [Fact]
    public void RepeatedSnapshotsObserveMutableFlowChoiceNames()
    {
        var document = new BpmnDocumentBuilder()
            .Node("task", BpmnFlowNodeType.Task, new Bounds(0, 0, 100, 80)).Build();
        var flows = new List<BpmnSequenceFlow>
        {
            new("first", "task", "target", "Process_1", "Before"),
            new("second", "task", "target", "Process_1"),
        };
        document = document with { SequenceFlows = flows };
        var before = BpmnElementInspector.Describe(document, "task")!;
        flows[0] = flows[0] with { Name = "After" };

        var after = BpmnElementInspector.Describe(document, "task")!;

        Assert.Equal("Before", before.Properties.Single(property => property.Name == "defaultFlowRef").Choices[1].Label);
        Assert.Equal("After", after.Properties.Single(property => property.Name == "defaultFlowRef").Choices[1].Label);
        Assert.Equal(before.Capabilities, after.Capabilities);
    }

    [Fact]
    public void BranchingSnapshotStaysWithinAllocationBudget()
    {
        var builder = new BpmnDocumentBuilder()
            .Node("task", BpmnFlowNodeType.Task, new Bounds(0, 0, 100, 80), "Task")
            .Node("target", BpmnFlowNodeType.EndEvent, new Bounds(200, 0, 36, 36));
        for (var index = 0; index < 600; index++)
        {
            builder.Flow($"flow_{index}", "task", "target", [], $"Flow {index}");
        }
        var document = builder.Build();
        for (var warm = 0; warm < 3; warm++) BpmnElementInspector.Describe(document, "task");
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();

        var snapshot = BpmnElementInspector.Describe(document, "task")!;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.True(snapshot.Capabilities.CanEditLabel);
        Assert.InRange(allocated, 0, 225_000);
    }
}
