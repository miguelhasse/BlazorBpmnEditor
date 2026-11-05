using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BenchmarkDotNet.Attributes;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions;

namespace Hasseware.AspNetCore.Components.Bpmn.Benchmarks;

/// <summary>Raw extension descriptors and complete property snapshot costs.</summary>
[MemoryDiagnoser]
public class InspectionBenchmarks
{
    private BpmnDocument _document = null!;
    private BpmnFlowNode _node = null!;

    [Params(1, 8, 9, 600, 5000)]
    public int Siblings { get; set; }

    [Params(false, true)]
    public bool RepeatedNames { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _document = new BpmnDocumentBuilder()
            .Node("task", BpmnFlowNodeType.Task, new Bounds(0, 0, 100, 80))
            .Build();
        _node = _document.FlowNodes[0] with
        {
            Extras = new BpmnElementExtras
            {
                Extensions = [new("urn:benchmark", "mapping", new Dictionary<string, string>(),
                    [.. Enumerable.Range(0, Siblings).Select(index => new BpmnExtensionElement(
                        index % 2 == 0 ? "urn:first" : "urn:second",
                        RepeatedNames ? $"input{index % 3}" : $"input{index}",
                        new Dictionary<string, string> { ["target"] = $"target{index}", ["source"] = $"source{index}" },
                        [], $"value{index}"))])],
            },
        };
        _document = _document with { FlowNodes = [_node] };
        if (ExtensionProperties().Count != Siblings * 3 || Describe().ExtensionProperties.Count != Siblings * 3)
        {
            throw new InvalidOperationException("Inspection fixture lost extension descriptors.");
        }
    }

    [Benchmark]
    public IReadOnlyList<BpmnPropertyDescriptor> ExtensionProperties() => BpmnElementInspector.ExtensionProperties(_node);

    [Benchmark]
    public BpmnElementSnapshot Describe() => BpmnElementInspector.Describe(_document, "task")!;

    public static void Report()
    {
        foreach (var siblings in new[] { 1, 8, 9, 600, 5000 })
        {
            foreach (var repeated in new[] { false, true })
            {
                var benchmark = new InspectionBenchmarks { Siblings = siblings, RepeatedNames = repeated };
                benchmark.Setup();
                Console.WriteLine($"siblings={siblings} repeated={repeated}");
                Measure("extensions", () => benchmark.ExtensionProperties(), siblings <= 9 ? 100 : 1);
                Measure("describe", () => benchmark.Describe(), siblings <= 9 ? 20 : 1);
                var output = new StringBuilder();
                foreach (var property in benchmark.ExtensionProperties())
                {
                    output.AppendLine(CultureInfo.InvariantCulture,
                        $"{property.Name}|{property.Label}|{property.Kind}|{property.Value}|{property.IsReadOnly}");
                }
                Console.WriteLine($"output SHA256={Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(output.ToString())))}");
            }
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
