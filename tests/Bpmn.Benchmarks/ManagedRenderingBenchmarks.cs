using BenchmarkDotNet.Attributes;
using Hasseware.AspNetCore.Components.Bpmn.Accessibility;
using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Microsoft.Extensions.DependencyInjection;

namespace Hasseware.AspNetCore.Components.Bpmn.Benchmarks;

/// <summary>Isolated managed passes on normalized, connected diagrams at rendering stress sizes.</summary>
[MemoryDiagnoser]
public class ManagedRenderingBenchmarks
{
    private BpmnDocument _document = null!;
    private BpmnDocument _moved = null!;
    private Func<BpmnDocument, BpmnDocument> _normalize = null!;
    private Func<BpmnDocument, BpmnDocument, BpmnDocument> _followResize = null!;
    private BpmnEditorSession _session = null!;
    private ServiceProvider _services = null!;

    [Params(602, 2000, 5000)]
    public int ShapeCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var builder = new BpmnDocumentBuilder();
        for (var index = 0; index < ShapeCount; index++)
        {
            builder.Node($"Task_{index}", BpmnFlowNodeType.Task, new Bounds(index * 200, 0, 100, 80), $"Task {index}");
            if (index > 0)
                builder.Flow($"Flow_{index}", $"Task_{index - 1}", $"Task_{index}",
                    [new DiPoint(((index - 1) * 200) + 100, 40), new DiPoint(index * 200, 40)]);
        }
        _document = builder.Build();
        var diagram = _document.Diagrams[0];
        _moved = _document with
        {
            Diagrams = [diagram with
            {
                Plane = diagram.Plane with
                {
                    Shapes = [.. diagram.Plane.Shapes.Select(shape =>
                        shape with { Bounds = shape.Bounds with { X = shape.Bounds.X + 10 } })],
                },
            }],
        };
        var type = typeof(BpmnDocument).Assembly.GetType(
            "Hasseware.AspNetCore.Components.Bpmn.Model.Geometry.ConnectorEndpoints")!;
        _normalize = type.GetMethod("Normalize", [typeof(BpmnDocument)])!
            .CreateDelegate<Func<BpmnDocument, BpmnDocument>>();
        _followResize = type.GetMethod("FollowResizedShapes", [typeof(BpmnDocument), typeof(BpmnDocument)])!
            .CreateDelegate<Func<BpmnDocument, BpmnDocument, BpmnDocument>>();
        _services = new ServiceCollection().BuildServiceProvider();
        _session = new BpmnEditorSession(new BpmnOptions(), _services) { Document = _document };
        _ = _session.AccessibleGraph;
    }

    [Benchmark]
    public BpmnDocument NormalizeUnchanged() => _normalize(_document);

    [Benchmark]
    public BpmnDocument FollowTranslation() => _followResize(_document, _moved);

    [Benchmark]
    public IReadOnlyList<BpmnGraphNode> GeometryOnlyAccessibleGraph()
    {
        _session.Document = ReferenceEquals(_session.Document, _document) ? _moved : _document;
        return _session.AccessibleGraph;
    }

    [GlobalCleanup]
    public void Cleanup() => _services.Dispose();
}
