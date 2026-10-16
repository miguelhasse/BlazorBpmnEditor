using BenchmarkDotNet.Attributes;
using Hasseware.AspNetCore.Components.Bpmn.Accessibility;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;

namespace Hasseware.AspNetCore.Components.Bpmn.Benchmarks;

/// <summary>
/// Editor, serializer and accessibility-tree cost on the 600-shape stress fixture.
/// </summary>
[MemoryDiagnoser]
public class EditorBenchmarks
{
    private string _xml = "";
    private BpmnDocument _document = null!;
    private string _diagramId = "";
    private IReadOnlyList<ShapeGeometry> _moves = [];

    [GlobalSetup]
    public void Setup()
    {
        _xml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "large-virtualization-stress.bpmn"));
        _document = BpmnXmlReader.Read(_xml).Document;

        var diagram = _document.Diagrams[0];
        _diagramId = diagram.Id;

        // Nudge the first twenty shapes: a multi-select drag.
        _moves = [.. diagram.Plane.Shapes.Take(20)
            .Select(shape => new ShapeGeometry(shape.Id, shape.Bounds with { X = shape.Bounds.X + 10 }))];
    }

    [Benchmark]
    public BpmnImportResult Read() => BpmnXmlReader.Read(_xml);

    [Benchmark]
    public BpmnExportResult WriteCanonical() => BpmnXmlWriter.Write(_document);

    [Benchmark]
    public IReadOnlyList<BpmnGraphNode> AccessibleGraph() => BpmnAccessibleGraph.For(_document, _diagramId);

    [Benchmark]
    public BpmnEditResult MoveShapes() => BpmnDocumentEditor.Apply(_document, new MoveShapesCommand(_moves));
}
