using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Interop;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;

namespace Hasseware.AspNetCore.Components.Bpmn.Benchmarks;

/// <summary>Separates managed patch construction and outgoing serialization from command application.</summary>
[MemoryDiagnoser]
public class CanvasPatchBenchmarks
{
    private BpmnDocument _before = null!;
    private BpmnDocument _after = null!;
    private CanvasDocumentPatch _patch = null!;
    private BpmnEditCommand _command = null!;

    [Params(602, 2000, 5000)]
    public int TaskCount { get; set; }

    [Params("Rename", "Move", "Semantic", "Delete", "UndoDelete", "Equivalent")]
    public string Change { get; set; } = "Rename";

    [GlobalSetup]
    public void Setup()
    {
        var builder = new BpmnDocumentBuilder();
        for (var index = 0; index < TaskCount; index++)
        {
            builder.Node($"Task_{index}", BpmnFlowNodeType.Task, new Bounds(index * 200, 0, 100, 80), "Task");
            if (index > 0)
                builder.Flow($"Flow_{index}", $"Task_{index - 1}", $"Task_{index}",
                    [new DiPoint(((index - 1) * 200) + 100, 40), new DiPoint(index * 200, 40)]);
        }
        _before = builder.Build();
        _command = Change == "Move"
            ? new MoveShapesCommand([new ShapeGeometry(_before.Diagrams[0].Plane.Shapes[0].Id, new Bounds(10, 0, 100, 80))])
            : new DeleteElementsCommand(["Task_0"]);
        var nodes = _before.FlowNodes.ToArray();
        nodes[0] = nodes[0] with { Name = Change == "Rename" ? "Renamed" : nodes[0].Name, Instantiate = Change == "Semantic" };
        _after = Change is "Move" or "Delete" or "UndoDelete"
            ? BpmnDocumentEditor.Apply(_before, _command).Document
            : _before with { FlowNodes = nodes };
        if (Change == "UndoDelete") (_before, _after) = (_after, _before);
        _patch = CanvasDocumentPatch.Create(_before, _after, null, 0, 1)
            ?? throw new InvalidOperationException("Benchmark edit must support a patch.");
    }

    [Benchmark]
    public object CreatePatch() => CanvasDocumentPatch.Create(_before, _after, null, 0, 1)!;

    [Benchmark]
    public string SerializePatch() => JsonSerializer.Serialize(_patch, JsonSerializerOptions.Web);

    [Benchmark]
    public string FullProjection() => CanvasDocumentPayload.Serialize(_after);
}
