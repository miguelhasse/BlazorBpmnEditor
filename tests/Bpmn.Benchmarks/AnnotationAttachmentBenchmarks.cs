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

/// <summary>Shape and flow attachment following, isolated and through public edits.</summary>
[MemoryDiagnoser]
public class AnnotationAttachmentBenchmarks
{
    private BpmnDocument _before = null!;
    private BpmnDocument _after = null!;
    private BpmnEditCommand _command = null!;
    private string _xml = "";

    [Params(2, 600, 5000)]
    public int Nodes { get; set; }

    [Params(0, 1, 60, 500)]
    public int Notes { get; set; }

    [Params("shape", "distinct", "shared")]
    public string Target { get; set; } = "shape";

    [Params(false, true)]
    public bool Changed { get; set; } = true;

    [GlobalSetup]
    public void Setup()
    {
        var builder = new BpmnDocumentBuilder();
        for (var index = 0; index < Nodes; index++)
        {
            builder.Node($"task{index}", BpmnFlowNodeType.Task, new Bounds(index * 200, 0, 100, 80));
            if (index > 0)
            {
                var start = index * 200 - 100;
                builder.Flow($"flow{index}", $"task{index - 1}", $"task{index}",
                    [new(start, 40), new(start + 40, 40), new(start + 40, 80),
                        new(start + 60, 80), new(start + 60, 40), new(start + 100, 40)]);
            }
        }
        for (var index = 0; index < Notes; index++)
        {
            var owner = Target == "shared" ? 0 : index % (Nodes - 1);
            var x = owner * 200;
            builder.TextAnnotation($"note{index}", "Note", new Bounds(x + 110, 160 + index % 3 * 60, 80, 40))
                .Association($"association{index}", $"note{index}",
                    Target == "shape" ? $"task{owner}" : $"flow{owner + 1}",
                    [new(x + 150, 160 + index % 3 * 60),
                        Target == "shape" ? new(x + 100, 40) : new(x + 150, 80)]);
        }
        _before = ConnectorEndpoints.Normalize(builder.Build());
        var diagram = _before.Diagrams[0];
        var route = new DiPoint[] { new(100, 40), new(130, 40), new(130, 120), new(170, 120), new(170, 40), new(200, 40) };
        _command = Target == "shape"
            ? new MoveShapesCommand([new(diagram.Plane.Shapes[0].Id, diagram.Plane.Shapes[0].Bounds with { X = 10 })])
            : new UpdateWaypointsCommand("flow1", route);
        _after = !Changed ? _before : _before with
        {
            Diagrams = [diagram with
            {
                Plane = diagram.Plane with
                {
                    Shapes = Target == "shape"
                        ? [diagram.Plane.Shapes[0] with { Bounds = diagram.Plane.Shapes[0].Bounds with { X = 10 } }, .. diagram.Plane.Shapes.Skip(1)]
                        : diagram.Plane.Shapes,
                    Edges = Target == "shape" ? diagram.Plane.Edges
                        : [diagram.Plane.Edges[0] with { Waypoints = route }, .. diagram.Plane.Edges.Skip(1)],
                },
            }],
        };
        _xml = BpmnXmlWriter.Write(_before).Xml;
        var edited = Edit();
        if (!edited.IsApplied || ReferenceEquals(edited.Document, _before)
            || edited.Document.TextAnnotations.Count != Notes)
            throw new InvalidOperationException("Attachment fixture edit did not apply.");
        if (Notes > 0 && Changed && (Target == "shape" ? FollowShapes() : FollowFlows()).Diagrams[0].Plane.Shapes
            .First(shape => shape.BpmnElement == "note0").Bounds
                == diagram.Plane.Shapes.First(shape => shape.BpmnElement == "note0").Bounds)
            throw new InvalidOperationException("Attachment fixture did not move its note.");
    }

    [Benchmark]
    public BpmnDocument FollowShapes() => ConnectorEndpoints.FollowAnnotationShapes(_before, _after);

    [Benchmark]
    public BpmnDocument FollowFlows() => ConnectorEndpoints.FollowFlowAttachments(_after, _before);

    [Benchmark]
    public BpmnEditResult Edit() => BpmnDocumentEditor.Apply(_before, _command);

    [Benchmark]
    public BpmnImportResult Read() => BpmnXmlReader.Read(_xml);

    public static void Report()
    {
        Run(2, 0, "shape", true);
        Run(2, 1, "shape", true);
        Run(2, 1, "shared", true);
        Run(600, 0, "shape", true);
        foreach (var target in new[] { "shape", "distinct", "shared" })
        {
            Run(600, 60, target, true);
            Run(600, 500, target, true);
            Run(600, 500, target, false);
        }
        Run(5000, 500, "distinct", true);
        Run(5000, 500, "shared", true);

        static void Run(int nodes, int notes, string target, bool changed)
        {
            var benchmark = new AnnotationAttachmentBenchmarks
            { Nodes = nodes, Notes = notes, Target = target, Changed = changed };
            benchmark.Setup();
            Console.WriteLine($"nodes={nodes} notes={notes} target={target} changed={changed}");
            var calls = nodes == 2 ? 20 : 1;
            Measure("shapes", () => benchmark.FollowShapes(), calls);
            Measure("flows", () => benchmark.FollowFlows(), calls);
            Measure("edit", () => benchmark.Edit(), calls);
            Measure("read", () => benchmark.Read(), calls);
            var moved = new List<MovedConnector>();
            var followed = ConnectorEndpoints.FollowFlowAttachments(benchmark._after, benchmark._before, moved);
            var edited = benchmark.Edit();
            var read = benchmark.Read();
            var output = BpmnJsonWriter.Write(benchmark.FollowShapes()) + BpmnJsonWriter.Write(followed)
                + JsonSerializer.Serialize(moved) + BpmnJsonWriter.Write(edited.Document)
                + JsonSerializer.Serialize(edited.Diagnostics) + BpmnJsonWriter.Write(read.Document)
                + JsonSerializer.Serialize(read.Diagnostics)
                + BpmnXmlWriter.Write(read.Document, BpmnWriteOptions.Fidelity).Xml;
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
