using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Editing;

public sealed class PropertyBatchTests
{
    public static TheoryData<BpmnFlowNodeType, int, int> Cases
    {
        get
        {
            var data = new TheoryData<BpmnFlowNodeType, int, int>();
            foreach (var type in Enum.GetValues<BpmnFlowNodeType>())
            {
                foreach (var nodes in new[] { 8, 9 })
                {
                    foreach (var flows in new[] { 0, 1, 3 }) data.Add(type, nodes, flows);
                }
            }
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void BatchDescriptorsMatchSingleInspection(BpmnFlowNodeType type, int nodes, int outgoingFlows)
    {
        var builder = new BpmnDocumentBuilder();
        builder.Node("first", BpmnFlowNodeType.Task, new Bounds(0, 0, 100, 80));
        builder.Node("subject", type, new Bounds(100, 0, 100, 80));
        for (var index = 2; index < nodes; index++)
        {
            builder.Node($"node_{index}", BpmnFlowNodeType.Task, new Bounds(index * 100, 0, 100, 80));
        }
        builder.Flow("unrelated", "first", "node_2", []);
        for (var index = 0; index < outgoingFlows; index++)
        {
            builder.Flow($"flow_{index}", "subject", $"node_{index + 2}", [], name: index == 1 ? null : $"Flow {index}");
        }
        var document = builder.Build();
        // Include duplicate flow IDs and a loop without losing their original choice order.
        if (outgoingFlows > 1)
        {
            document = document with
            {
                SequenceFlows = [.. document.SequenceFlows, document.SequenceFlows[1] with { TargetRef = "subject" }],
            };
        }
        var read = BpmnElementInspector.CreatePropertyReader(document);
        foreach (var id in new[] { "first", "subject", "node_2", "unrelated", "missing", "subject" })
        {
            var expected = BpmnElementInspector.PropertiesOf(document, id);
            var actual = read(id);

            Assert.Equal(expected.Count, actual.Count);
            for (var index = 0; index < expected.Count; index++)
            {
                Assert.Equal(expected[index].Choices, actual[index].Choices);
                Assert.Equal(expected[index] with { Choices = [] }, actual[index] with { Choices = [] });
            }
        }
    }
}
