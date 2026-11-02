using BenchmarkDotNet.Attributes;
using Hasseware.AspNetCore.Components.Bpmn.Accessibility;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;

namespace Hasseware.AspNetCore.Components.Bpmn.Benchmarks;

/// <summary>Accessible reading-order construction with increasing lane membership counts.</summary>
[MemoryDiagnoser]
public class SwimlaneGraphBenchmarks
{
    private BpmnDocument _document = null!;

    [Params(602, 2000, 5000)]
    public int TaskCount { get; set; }

    [Params(1, 10, 100)]
    public int LaneCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var builder = new BpmnDocumentBuilder()
            .Pool("Pool_1", "Pool", new Bounds(0, 0, TaskCount * 200, LaneCount * 120));
        for (var lane = 0; lane < LaneCount; lane++)
        {
            var members = Enumerable.Range(0, TaskCount)
                .Where(index => index % LaneCount == lane)
                .Select(index => $"Task_{index}").ToArray();
            builder.Lane($"Lane_{lane}", $"Lane {lane}",
                new Bounds(30, lane * 120, TaskCount * 200, 120), members);
        }
        for (var index = 0; index < TaskCount; index++)
        {
            builder.Node($"Task_{index}", BpmnFlowNodeType.Task,
                new Bounds(index * 200, index % LaneCount * 120, 100, 80), $"Task {index}");
            if (index > 0)
                builder.Flow($"Flow_{index}", $"Task_{index - 1}", $"Task_{index}",
                    [new DiPoint(((index - 1) * 200) + 100, 40), new DiPoint(index * 200, 40)]);
        }
        _document = builder.Build();
    }

    [Benchmark]
    public IReadOnlyList<BpmnGraphNode> AccessibleGraph() => BpmnAccessibleGraph.For(_document, null);
}
