using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BenchmarkDotNet.Attributes;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;

namespace Hasseware.AspNetCore.Components.Bpmn.Benchmarks;

/// <summary>Full activity inspection with increasingly large default-flow choice lists.</summary>
[MemoryDiagnoser]
public class SnapshotBenchmarks
{
    private BpmnDocument _document = null!;

    [Params(0, 8, 600, 5000)]
    public int Flows { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var builder = new BpmnDocumentBuilder()
            .Node("task", BpmnFlowNodeType.Task, new Bounds(0, 0, 100, 80), "Task")
            .Node("target", BpmnFlowNodeType.EndEvent, new Bounds(200, 0, 36, 36));
        for (var index = 0; index < Flows; index++)
        {
            builder.Flow($"flow_{index}", "task", "target", [], $"Flow {index}");
        }
        _document = builder.Build();
        var snapshot = Describe();
        if (!snapshot.Capabilities.CanEditLabel
            || snapshot.Properties.SingleOrDefault(property => property.Name == "defaultFlowRef")?.Choices.Count
                != (Flows > 1 ? Flows + 1 : (int?)null))
        {
            throw new InvalidOperationException("Snapshot fixture lost capabilities or choices.");
        }
    }

    [Benchmark]
    public BpmnElementSnapshot Describe() => BpmnElementInspector.Describe(_document, "task")!;

    public static void Report()
    {
        foreach (var flows in new[] { 0, 8, 600, 5000 })
        {
            var benchmark = new SnapshotBenchmarks { Flows = flows };
            benchmark.Setup();
            for (var warm = 0; warm < 5; warm++) benchmark.Describe();
            var samples = new double[7];
            long bytes = 0;
            var calls = flows <= 8 ? 100 : 5;
            for (var round = 0; round < samples.Length; round++)
            {
                var allocated = GC.GetAllocatedBytesForCurrentThread();
                var started = Stopwatch.GetTimestamp();
                for (var call = 0; call < calls; call++) benchmark.Describe();
                samples[round] = Stopwatch.GetElapsedTime(started).TotalMilliseconds / calls;
                bytes += GC.GetAllocatedBytesForCurrentThread() - allocated;
            }
            Array.Sort(samples);
            Console.WriteLine($"flows={flows}: median={samples[3]:F4}ms allocated={bytes / samples.Length / calls}B");
            var snapshot = benchmark.Describe();
            var output = new StringBuilder();
            output.AppendLine(snapshot.Capabilities.ToString());
            foreach (var property in snapshot.Properties)
            {
                output.AppendLine(CultureInfo.InvariantCulture,
                    $"{property.Name}|{property.Label}|{property.Kind}|{property.Value}|{property.IsReadOnly}");
                foreach (var choice in property.Choices)
                {
                    output.AppendLine(choice.ToString());
                }
            }
            Console.WriteLine($"output SHA256={Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(output.ToString())))}");
        }
    }
}
