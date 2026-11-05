using System.Diagnostics;
using System.Xml.Linq;
using BenchmarkDotNet.Attributes;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;

namespace Hasseware.AspNetCore.Components.Bpmn.Benchmarks;

[MemoryDiagnoser]
public class ExportBenchmarks
{
    private XDocument _source = null!;
    private XDocument _canonical = null!;

    [Params(1, 8, 600, 2000, 5000)]
    public int Elements { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        XNamespace model = "http://www.omg.org/spec/BPMN/20100524/MODEL";
        _source = new XDocument(new XElement(model + "definitions",
            new XElement(model + "process", Enumerable.Range(0, Elements)
                .Select(index => new XElement(model + "task", new XAttribute("id", $"task_{index}"))))));
        _canonical = new XDocument(_source);
        if (LossAnalysis().Count != 0)
        {
            throw new InvalidOperationException("Equivalent export trees must have no losses.");
        }
    }

    [Benchmark]
    public IReadOnlyList<BpmnExportDiagnostic> LossAnalysis() =>
        BpmnCanonicalLossAnalyzer.FindLosses(_source, _canonical, []);

    public static void Report()
    {
        foreach (var count in new[] { 1, 8, 600, 2000, 5000 })
        {
            var benchmark = new ExportBenchmarks { Elements = count };
            benchmark.Setup();
            for (var warm = 0; warm < 5; warm++) benchmark.LossAnalysis();
            var samples = new double[7];
            var calls = count <= 8 ? 2000 : 5;
            long bytes = 0;
            for (var round = 0; round < samples.Length; round++)
            {
                var allocated = GC.GetAllocatedBytesForCurrentThread();
                var start = Stopwatch.GetTimestamp();
                for (var call = 0; call < calls; call++)
                {
                    if (benchmark.LossAnalysis().Count != 0) throw new InvalidOperationException("Unexpected loss.");
                }
                samples[round] = Stopwatch.GetElapsedTime(start).TotalMilliseconds / calls;
                bytes += GC.GetAllocatedBytesForCurrentThread() - allocated;
            }
            Array.Sort(samples);
            Console.WriteLine($"loss-analysis elements={count}: median={samples[3]:F3}ms allocated={bytes / 7 / calls}B");
        }
    }
}
