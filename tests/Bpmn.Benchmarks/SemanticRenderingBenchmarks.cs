using BenchmarkDotNet.Attributes;
using Hasseware.AspNetCore.Components.Bpmn.Accessibility;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Validation;

namespace Hasseware.AspNetCore.Components.Bpmn.Benchmarks;

/// <summary>Semantic derivation on flat, nested and annotation-heavy diagrams, without browser paint.</summary>
[MemoryDiagnoser]
public class SemanticRenderingBenchmarks
{
    private BpmnDocument _document = null!;

    [Params(602, 2000, 5000)]
    public int TaskCount { get; set; }

    [Params("Flat", "Nested", "Annotations")]
    public string Layout { get; set; } = "Flat";

    [GlobalSetup]
    public void Setup()
    {
        var builder = new BpmnDocumentBuilder();
        for (var index = 0; index < TaskCount; index++)
        {
            var container = Layout == "Nested" ? $"Sub_{index / 10}" : null;
            if (container is not null && index % 10 == 0)
                builder.Node(container, BpmnFlowNodeType.SubProcess,
                    new Bounds(index * 200, 0, 2000, 200), isExpanded: true);
            builder.Node($"Task_{index}", BpmnFlowNodeType.Task,
                new Bounds(index * 200, 40, 100, 80), $"Task {index}", containerId: container);
            if (index > 0 && (container is null || index % 10 != 0))
                builder.Flow($"Flow_{index}", $"Task_{index - 1}", $"Task_{index}",
                    [new DiPoint(((index - 1) * 200) + 100, 80), new DiPoint(index * 200, 80)],
                    containerId: container);
            if (Layout == "Annotations")
            {
                builder.TextAnnotation($"Note_{index}", $"Note {index}", new Bounds(index * 200, 180, 100, 40))
                    .Association($"Association_{index}", $"Note_{index}", $"Task_{index}",
                        [new DiPoint(index * 200, 180), new DiPoint(index * 200, 120)]);
            }
        }
        _document = builder.Build();
    }

    [Benchmark]
    public IReadOnlyList<BpmnGraphNode> AccessibleGraph() => BpmnAccessibleGraph.For(_document, null);

    [Benchmark]
    public BpmnValidationReport Validate() => BpmnValidator.Validate(_document);
}
