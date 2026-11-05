using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using BenchmarkDotNet.Attributes;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;

namespace Hasseware.AspNetCore.Components.Bpmn.Benchmarks;

/// <summary>Retained-source export and redundant sequence-flow reference costs.</summary>
[MemoryDiagnoser]
public class FidelityBenchmarks
{
    private BpmnDocument _document = null!;
    private XElement _root = null!;
    private BpmnFidelityWriter _writer = null!;

    [Params(1, 8, 9, 600, 2000, 5000)]
    public int Nodes { get; set; }

    [Params(0, 1, 1000)]
    public int ReferenceStride { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        XNamespace model = "http://www.omg.org/spec/BPMN/20100524/MODEL";
        var process = new XElement(model + "process", new XAttribute("id", "process"));
        for (var index = 0; index < Nodes; index++)
        {
            var task = new XElement(model + "task", new XAttribute("id", $"task_{index}"));
            if (ReferenceStride > 0 && index % ReferenceStride == 0)
            {
                if (index > 0) task.Add(new XElement(model + "incoming", $"flow_{index - 1}"));
                if (index + 1 < Nodes) task.Add(new XElement(model + "outgoing", $"flow_{index}"));
            }
            process.Add(task);
        }
        for (var index = 0; index + 1 < Nodes; index++)
        {
            process.Add(new XElement(model + "sequenceFlow", new XAttribute("id", $"flow_{index}"),
                new XAttribute("sourceRef", $"task_{index}"), new XAttribute("targetRef", $"task_{index + 1}")));
        }
        _root = new XElement(model + "definitions", new XAttribute("id", "definitions"),
            new XAttribute("targetNamespace", "urn:benchmark"), process);
        _document = BpmnXmlReader.Read(_root.ToString()).Document;
        if (_document.FlowNodes.Count != Nodes || _document.SequenceFlows.Count != Nodes - 1)
        {
            throw new InvalidOperationException("Fidelity benchmark fixture was not fully imported.");
        }
        _writer = new BpmnFidelityWriter(_document, []);
        var unchanged = new XElement(_root);
        SynchroniseReferences();
        if (!XNode.DeepEquals(unchanged, _root))
        {
            throw new InvalidOperationException("Already-correct references must remain unchanged.");
        }
    }

    [Benchmark]
    public void SynchroniseReferences() => _writer.SynchroniseFlowReferences(_root);

    [Benchmark]
    public BpmnExportResult WriteFidelity() => BpmnXmlWriter.Write(_document, BpmnWriteOptions.Fidelity);

    public static void Report()
    {
        foreach (var nodes in new[] { 1, 8, 9, 600, 2000, 5000 })
        {
            foreach (var stride in new[] { 0, 1, 1000 })
            {
                var benchmark = new FidelityBenchmarks { Nodes = nodes, ReferenceStride = stride };
                benchmark.Setup();
                Console.WriteLine($"nodes={nodes} referenceStride={stride}");
                Measure("synchronise", benchmark.SynchroniseReferences, nodes == 1 ? 1000 : 5);
                Measure("fidelity", () => benchmark.WriteFidelity(), nodes == 1 ? 100 : 1);
                var xml = benchmark.WriteFidelity().Xml;
                Console.WriteLine($"output SHA256={Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(xml)))}");
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
