using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Model.Geometry;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;

namespace Hasseware.AspNetCore.Components.Bpmn.Benchmarks;

/// <summary>Connector normalization, complete import and geometry edit costs.</summary>
[MemoryDiagnoser]
public class NormalizationBenchmarks
{
    private BpmnDocument _document = null!;
    private string _xml = "";
    private MoveShapesCommand _move = null!;

    [Params(2, 600, 5000)]
    public int Nodes { get; set; } = 600;

    [Params(2, 4, 32)]
    public int Points { get; set; } = 4;

    [Params(false, true)]
    public bool Displaced { get; set; }

    [Params(false, true)]
    public bool Annotations { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var builder = new BpmnDocumentBuilder();
        for (var index = 0; index < Nodes; index++)
        {
            builder.Node($"task{index}", BpmnFlowNodeType.Task, new Bounds(index * 200, 0, 100, 80));
            if (index == 0) continue;
            var start = (index - 1) * 200 + 100;
            var end = index * 200;
            builder.Flow($"flow{index}", $"task{index - 1}", $"task{index}",
                [.. Enumerable.Range(0, Points).Select(point => new DiPoint(
                    start + (double)(end - start) * point / (Points - 1)
                        + (Displaced ? point == 0 ? 5 : point == Points - 1 ? -5 : 0 : 0),
                    Displaced && (point == 0 || point == Points - 1) ? 50 : 40))]);
            if (Annotations && index % 10 == 1)
            {
                builder.TextAnnotation($"note{index}", "Note", new Bounds(start, 120, 80, 40))
                    .Association($"association{index}", $"flow{index}", $"note{index}",
                        [new DiPoint(start + 50, 40), new DiPoint(start + 50, 120)]);
            }
        }
        _document = builder.Build();
        _xml = BpmnXmlWriter.Write(_document).Xml;
        _move = new([new ShapeGeometry(_document.Diagrams[0].Plane.Shapes[0].Id,
            _document.Diagrams[0].Plane.Shapes[0].Bounds with { X = 10 })]);
        var moved = new List<MovedConnector>();
        var normalized = ConnectorEndpoints.Normalize(_document, moved);
        if (normalized.Diagrams[0].Plane.Edges.Count != Nodes - 1 + _document.Associations.Count
            || !ReferenceEquals(normalized, ConnectorEndpoints.Normalize(normalized))
            || (Displaced && moved.Count < Nodes - 1))
            throw new InvalidOperationException("Normalization fixture lost edges or idempotence.");
    }

    [Benchmark]
    public BpmnDocument Normalize() => ConnectorEndpoints.Normalize(_document);

    [Benchmark]
    public BpmnImportResult Read() => BpmnXmlReader.Read(_xml);

    [Benchmark]
    public BpmnEditResult Move() => BpmnDocumentEditor.Apply(_document, _move);

    public static void Report()
    {
        foreach (var nodes in new[] { 2, 600, 5000 })
        {
            foreach (var points in new[] { 2, 4, 32 })
            {
                foreach (var displaced in new[] { false, true })
                    Run(nodes, points, displaced, false);
            }
        }
        Run(600, 4, false, true);
        Run(600, 4, true, true);

        static void Run(int nodes, int points, bool displaced, bool annotations)
        {
            var benchmark = new NormalizationBenchmarks
            { Nodes = nodes, Points = points, Displaced = displaced, Annotations = annotations };
            benchmark.Setup();
            Console.WriteLine($"nodes={nodes} points={points} displaced={displaced} annotations={annotations}");
            var calls = nodes == 2 ? 50 : 1;
            Measure("normalize", () => benchmark.Normalize(), calls);
            Measure("read", () => benchmark.Read(), calls);
            Measure("move", () => benchmark.Move(), calls);
            var moved = new List<MovedConnector>();
            var normalized = ConnectorEndpoints.Normalize(benchmark._document, moved);
            var edit = benchmark.Move();
            var read = benchmark.Read();
            if (!edit.IsApplied) throw new InvalidOperationException("Move fixture did not apply.");
            var output = BpmnJsonWriter.Write(normalized) + JsonSerializer.Serialize(moved)
                + BpmnJsonWriter.Write(edit.Document) + JsonSerializer.Serialize(edit.Diagnostics)
                + BpmnJsonWriter.Write(read.Document) + JsonSerializer.Serialize(read.Diagnostics);
            Console.WriteLine($"output SHA256={Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(output)))}");
        }
    }

    private static void Measure(string name, Action action, int calls)
    {
        for (var warm = 0; warm < 5; warm++) action();
        var samples = new double[7];
        long bytes = 0;
        for (var round = 0; round < samples.Length; round++)
        {
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            var started = Stopwatch.GetTimestamp();
            for (var call = 0; call < calls; call++) action();
            samples[round] = Stopwatch.GetElapsedTime(started).TotalMilliseconds / calls;
            bytes += GC.GetAllocatedBytesForCurrentThread() - allocated;
        }
        Array.Sort(samples);
        Console.WriteLine($"{name}: median={samples[3]:F4}ms allocated={bytes / samples.Length / calls}B");
    }
}
