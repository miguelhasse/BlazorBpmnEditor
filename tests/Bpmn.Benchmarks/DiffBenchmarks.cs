using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BenchmarkDotNet.Attributes;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Tools;

namespace Hasseware.AspNetCore.Components.Bpmn.Benchmarks;

/// <summary>Full document diff costs, including property and multi-point route projection.</summary>
[MemoryDiagnoser]
public class DiffBenchmarks
{
    private BpmnDocument _before = null!;
    private BpmnDocument _after = null!;

    [Params(8, 600, 5000)]
    public int Nodes { get; set; }

    [Params(2, 32)]
    public int Waypoints { get; set; }

    [Params(false, true)]
    public bool Changed { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var builder = new BpmnDocumentBuilder();
        for (var index = 0; index < Nodes; index++)
        {
            builder.Node($"task_{index}", BpmnFlowNodeType.Task, new Bounds(index * 120, 0, 100, 80), $"Task {index}");
            if (index > 0)
            {
                builder.Flow($"flow_{index}", $"task_{index - 1}", $"task_{index}",
                    [.. Enumerable.Range(0, Waypoints).Select(point => new DiPoint(index * 120 + point, point * 2))]);
            }
        }
        _before = builder.Build();
        var diagram = _before.Diagrams[0];
        _after = Changed ? _before with
        {
            FlowNodes = [.. _before.FlowNodes.Select((node, index) => index % 10 == 0 ? node with { Name = $"Changed {index}" } : node)],
            Diagrams = [diagram with
            {
                Plane = diagram.Plane with
                {
                    Edges = [.. diagram.Plane.Edges.Select((edge, index) => index % 10 == 0
                        ? edge with { Waypoints = [.. edge.Waypoints.Select(point => point with { Y = point.Y + 1 })] } : edge)],
                },
            }],
        } : _before;
        var result = Compare();
        if (result.Truncated || result.Added.Count != 0 || result.Removed.Count != 0
            || result.Changed.Count != (Changed ? ((Nodes + 9) / 10) + ((Nodes + 8) / 10) : 0))
        {
            throw new InvalidOperationException("Diff benchmark fixture produced unexpected changes.");
        }
    }

    [Benchmark]
    public BpmnDocumentDiffResult Compare() => BpmnDocumentDiff.Compare(_before, _after, new(MaxElements: int.MaxValue));

    public static void Report()
    {
        foreach (var nodes in new[] { 8, 600, 5000 })
            foreach (var waypoints in new[] { 2, 32 })
                foreach (var changed in new[] { false, true })
                {
                    var benchmark = new DiffBenchmarks { Nodes = nodes, Waypoints = waypoints, Changed = changed };
                    benchmark.Setup();
                    for (var warm = 0; warm < 5; warm++) benchmark.Compare();
                    var samples = new double[7];
                    long bytes = 0;
                    var calls = nodes == 8 ? 50 : 1;
                    for (var round = 0; round < samples.Length; round++)
                    {
                        var allocated = GC.GetAllocatedBytesForCurrentThread();
                        var started = Stopwatch.GetTimestamp();
                        for (var call = 0; call < calls; call++) benchmark.Compare();
                        samples[round] = Stopwatch.GetElapsedTime(started).TotalMilliseconds / calls;
                        bytes += GC.GetAllocatedBytesForCurrentThread() - allocated;
                    }
                    Array.Sort(samples);
                    Console.WriteLine($"nodes={nodes} waypoints={waypoints} changed={changed}: median={samples[3]:F4}ms allocated={bytes / samples.Length / calls}B");
                    var output = new StringBuilder();
                    var result = benchmark.Compare();
                    output.AppendLine(CultureInfo.InvariantCulture, $"truncated={result.Truncated};added={result.Added.Count};removed={result.Removed.Count}");
                    foreach (var element in result.Changed)
                    {
                        output.AppendLine(CultureInfo.InvariantCulture, $"{element.Id}|{element.Type}|{element.SourceBefore}|{element.SourceAfter}|{element.TargetBefore}|{element.TargetAfter}|{element.GeometryChanged}");
                        foreach (var property in element.Properties)
                        {
                            output.AppendLine(CultureInfo.InvariantCulture, $"{property.Name}|{property.Before?.UntrustedText}|{property.After?.UntrustedText}");
                        }
                    }
                    Console.WriteLine($"output SHA256={Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(output.ToString())))}");
                }
    }
}
