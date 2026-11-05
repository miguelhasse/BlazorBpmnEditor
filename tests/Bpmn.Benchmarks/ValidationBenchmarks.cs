using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;
using Hasseware.AspNetCore.Components.Bpmn.Validation;

namespace Hasseware.AspNetCore.Components.Bpmn.Benchmarks;

/// <summary>Complete validation costs, including operation-local indexes.</summary>
[MemoryDiagnoser]
public class ValidationBenchmarks
{
    private BpmnDocument _document = null!;

    [Params(8, 60, 600, 5000)]
    public int Nodes { get; set; }

    [Params("flat", "containers", "events", "gateways", "notes", "dense-notes", "invalid")]
    public string Scenario { get; set; } = "flat";

    [GlobalSetup]
    public void Setup() => _document = Create(Nodes, Scenario);

    [Benchmark]
    public BpmnValidationReport Validate() => BpmnValidator.Validate(_document);

    public static void Report()
    {
        foreach (var nodes in new[] { 8, 60, 600, 5000 })
        {
            foreach (var scenario in new[] { "flat", "containers", "events", "gateways", "notes", "dense-notes", "invalid" })
            {
                var benchmark = new ValidationBenchmarks { Nodes = nodes, Scenario = scenario };
                benchmark.Setup();
                Run($"{scenario} nodes={nodes}", benchmark._document);
            }
        }
        Run("annotation-threshold-64", Create(64, "dense-notes"));
        Run("annotation-threshold-65", Create(65, "dense-notes"));
        var fixture = BpmnXmlReader.Read(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "large-virtualization-stress.bpmn"))).Document;
        Run("stress-fixture", fixture);
    }

    private static void Run(string name, BpmnDocument document)
    {
        var report = BpmnValidator.Validate(document);
        Console.WriteLine($"{name} actual-nodes={document.FlowNodes.Count} notes={document.TextAnnotations.Count} diagnostics={report.Diagnostics.Count}");
        Measure("validate", () => BpmnValidator.Validate(document), document.FlowNodes.Count < 100 ? 100 : 3);
        Measure("new-document", () => BpmnValidator.Validate(document with { }), 1);
        var output = JsonSerializer.Serialize(report);
        Console.WriteLine($"output SHA256={Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(output)))}");
    }

    private static BpmnDocument Create(int count, string scenario)
    {
        var nodes = new List<BpmnFlowNode>();
        var flows = new List<BpmnSequenceFlow>();
        var notes = new List<BpmnTextAnnotation>();
        var associations = new List<BpmnAssociation>();
        var processes = new List<BpmnProcess> { new("process") };
        var containers = scenario is "containers" or "events" ? Math.Max(1, count / 3) : 1;
        if (scenario == "containers")
        {
            processes.Clear();
            for (var index = 0; index < containers; index++) processes.Add(new($"process{index}"));
        }
        else if (scenario == "events")
        {
            for (var index = 0; index < containers; index++)
                nodes.Add(new($"sub{index}", BpmnFlowNodeType.EventSubProcess, "process", FlowElementsOwned: true));
        }

        for (var index = 0; index < count; index++)
        {
            var container = scenario switch
            {
                "containers" => $"process{index % containers}",
                "events" => $"sub{index % containers}",
                _ => "process",
            };
            var first = index < containers;
            var last = index + containers >= count;
            var type = first ? BpmnFlowNodeType.StartEvent
                : last ? BpmnFlowNodeType.EndEvent
                : scenario == "gateways" && index % 3 == 0 ? BpmnFlowNodeType.ExclusiveGateway
                : BpmnFlowNodeType.Task;
            nodes.Add(new($"node{index}", type, container));
            if (!first)
                flows.Add(new($"flow{index}", $"node{index - containers}", $"node{index}", container));
            if (scenario == "gateways" && type == BpmnFlowNodeType.ExclusiveGateway && !last)
                flows.Add(new($"branch{index}", $"node{index}", $"node{index + 1}", container));
        }

        var noteCount = scenario == "dense-notes" ? count : scenario == "notes" ? Math.Max(1, count / 10) : 0;
        for (var index = 0; index < noteCount; index++)
        {
            notes.Add(new($"note{index}", "Note", "process"));
            associations.Add(new($"association{index}", $"note{index}", $"node{index}", "process"));
            if (index % 2 == 0)
                associations.Add(new($"extra{index}", $"node{(index + 1) % count}", $"note{index}", "process"));
        }
        if (scenario == "invalid")
        {
            nodes.Add(new("orphan", BpmnFlowNodeType.Task, "process"));
            nodes.Add(new("boundary", BpmnFlowNodeType.BoundaryEvent, "process",
                EventDefinition: BpmnEventDefinitionType.Timer, AttachedToRef: "node1"));
            nodes.Add(new("escape", BpmnFlowNodeType.Task, "process"));
            flows.Add(new("escapeFlow", "boundary", "escape", "process"));
            flows.Add(new("back", "node1", "node0", "process"));
            flows.Add(new("missing", "unknown", "node1", "process"));
            flows.Add(new("cross", "node1", "orphan", "unknownContainer"));
        }
        return new BpmnDocument
        {
            Definitions = new("definitions", "urn:validation-benchmark"),
            Processes = processes,
            FlowNodes = nodes,
            SequenceFlows = flows,
            TextAnnotations = notes,
            Associations = associations,
        };
    }

    private static void Measure(string name, Action operation, int calls)
    {
        for (var warm = 0; warm < 30; warm++) operation();
        var samples = new double[7];
        long bytes = 0;
        for (var round = 0; round < samples.Length; round++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            var started = Stopwatch.GetTimestamp();
            for (var call = 0; call < calls; call++) operation();
            samples[round] = Stopwatch.GetElapsedTime(started).TotalMilliseconds / calls;
            bytes += GC.GetAllocatedBytesForCurrentThread() - before;
        }
        Array.Sort(samples);
        Console.WriteLine($"{name}: median={samples[3]:F4}ms allocated={bytes / samples.Length / calls}B");
    }
}
