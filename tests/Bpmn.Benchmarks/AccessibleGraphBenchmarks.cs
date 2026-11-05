using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hasseware.AspNetCore.Components.Bpmn.Accessibility;
using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Microsoft.Extensions.DependencyInjection;

namespace Hasseware.AspNetCore.Components.Bpmn.Benchmarks;

/// <summary>Paired complete graph allocation, ordering and semantic-edit measurements.</summary>
internal static class AccessibleGraphBenchmarks
{
    internal static void Report()
    {
        var attachedTypes = typeof(BpmnGraphNode).GetProperty("AttachedTypes", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetMethod!.CreateDelegate<Func<BpmnGraphNode, IReadOnlyList<string?>>>();
        using var services = new ServiceCollection().BuildServiceProvider();
        foreach (var size in new[] { 8, 60, 600, 5000 })
        {
            foreach (var scenario in new[] { "flat", "connected", "lanes", "artifacts", "nested", "cycle", "hub", "hidden" })
            {
                var document = Create(size, scenario);
                var changed = document with
                {
                    FlowNodes = [document.FlowNodes[0] with { Name = "Renamed" }, .. document.FlowNodes.Skip(1)],
                };
                var session = new BpmnEditorSession(new BpmnOptions(), services) { Document = document };
                _ = session.AccessibleGraph;
                var toggle = false;
                Measure("graph", () => BpmnAccessibleGraph.For(document, null));
                Measure("fresh", () => BpmnAccessibleGraph.For(document with { }, null));
                Measure("semantic-edit", () =>
                {
                    toggle = !toggle;
                    session.Document = toggle ? changed : document;
                    return session.AccessibleGraph;
                });
                var graph = BpmnAccessibleGraph.For(document, null);
                var output = JsonSerializer.Serialize(graph.Select(node => new
                {
                    Node = node,
                    AttachedTypes = attachedTypes(node),
                }));
                Console.WriteLine($"size={size} scenario={scenario} SHA256={Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(output)))}");

                void Measure(string operation, Func<IReadOnlyList<BpmnGraphNode>> action)
                {
                    for (var warm = 0; warm < 30; warm++) _ = action();
                    var times = new double[7];
                    long bytes = 0;
                    var calls = size < 100 ? 100 : 3;
                    for (var round = 0; round < times.Length; round++)
                    {
                        var allocated = GC.GetAllocatedBytesForCurrentThread();
                        var start = Stopwatch.GetTimestamp();
                        for (var call = 0; call < calls; call++) _ = action();
                        times[round] = Stopwatch.GetElapsedTime(start).TotalMilliseconds / calls;
                        bytes += GC.GetAllocatedBytesForCurrentThread() - allocated;
                    }
                    Array.Sort(times);
                    Console.WriteLine($"size={size} scenario={scenario} operation={operation} median={times[3]:F4}ms allocated={bytes / times.Length / calls}B");
                }
            }
        }
        Console.WriteLine($"empty={BpmnAccessibleGraph.For(new BpmnDocumentBuilder().Build(), null).Count}");
        Console.WriteLine($"no-diagram={BpmnAccessibleGraph.For(new BpmnDocumentBuilder().Build() with { Diagrams = [] }, null).Count}");
    }

    private static BpmnDocument Create(int size, string scenario)
    {
        var builder = new BpmnDocumentBuilder();
        if (scenario == "lanes")
        {
            builder.Pool("Pool", "Pool", new Bounds(0, 0, size * 200, 1200));
            for (var lane = 0; lane < 10; lane++)
                builder.Lane($"Lane_{lane}", $"Lane {lane}", new Bounds(30, lane * 120, size * 200, 120),
                    [.. Enumerable.Range(0, size).Where(index => index % 10 == lane).Select(index => $"Node_{index}")]);
        }
        for (var index = 0; index < size; index++)
        {
            var container = scenario is "nested" or "cycle"
                ? index == size - 1 ? scenario == "cycle" ? $"Node_{size / 2}" : "Process_1" : $"Node_{index + 1}"
                : "Process_1";
            builder.Node($"Node_{index}", scenario is "nested" or "cycle" ? BpmnFlowNodeType.SubProcess : BpmnFlowNodeType.Task,
                new Bounds(index * 200, 0, 100, 80), $"Node {index}", containerId: container, isExpanded: true);
            if (index > 0 && scenario is not "flat")
                builder.Flow($"Flow_{index}", scenario == "hub" ? "Node_0" : $"Node_{index - 1}",
                    scenario == "hidden" ? $"Undrawn_{index}" : $"Node_{index}", [new(100, 40), new(200, 40)]);
            if (scenario == "artifacts")
                builder.TextAnnotation($"Note_{index}", "  A note\n with whitespace ", new Bounds(index * 200, 100, 100, 50))
                    .Association($"Association_{index}", $"Note_{index}", $"Node_{index}", [new(100, 100), new(100, 40)]);
        }
        builder.Flow("Self", "Node_0", "Node_0", [new(0, 0), new(1, 1)]);
        builder.Flow("Duplicate", "Node_0", "Node_0", [new(0, 0), new(1, 1)]);
        builder.Flow("Hidden", "Node_0", "Undrawn", [new(0, 0), new(1, 1)]);
        return builder.Build();
    }
}
