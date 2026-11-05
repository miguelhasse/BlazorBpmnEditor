using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions.Schema;

namespace Hasseware.AspNetCore.Components.Bpmn.Benchmarks;

/// <summary>Registered-schema builder and complete snapshot costs.</summary>
[MemoryDiagnoser]
public class SchemaInspectionBenchmarks
{
    private BpmnDocument _document = null!;
    private BpmnFlowNode _node = null!;
    private BpmnElementSnapshot _snapshot = null!;
    private BpmnExtensionOptions _options = null!;
    private readonly BpmnSchemaPropertiesProvider _builder = new();

    [Params("none", "zeebe", "camunda", "both", "extra")]
    public string Schemas { get; set; } = "zeebe";

    [Params(BpmnFlowNodeType.ServiceTask, BpmnFlowNodeType.CallActivity, BpmnFlowNodeType.EndEvent)]
    public BpmnFlowNodeType NodeType { get; set; } = BpmnFlowNodeType.ServiceTask;

    [Params(0, 600)]
    public int Rows { get; set; }

    [Params(false, true)]
    public bool AlwaysOffer { get; set; } = true;

    [Params(false, true)]
    public bool CustomCallbacks { get; set; }

    [Params(false, true)]
    public bool IsReadOnly { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _options = new() { AlwaysOfferRegisteredSchemas = AlwaysOffer };
        if (CustomCallbacks)
        {
            _options.GroupBuilders[0] = new ForwardingBuilder();
            _options.LabelProvider = new HostLabels();
        }
        if (Schemas is "zeebe" or "both" or "extra") _options.AddZeebe();
        if (Schemas is "camunda" or "both" or "extra") _options.AddCamunda7();
        if (Schemas == "extra")
        {
            for (var index = 0; index < 4; index++)
            {
                _options.AddSchema(new($"Extra {index}", $"urn:extra:{index}", $"extra{index}",
                    [.. Enumerable.Range(0, 25).Select(type => new BpmnExtensionType($"Type{type}", ["Element"], [], []))]));
            }
        }
        _document = new BpmnDocumentBuilder()
            .Node("task", NodeType, new Bounds(0, 0, 100, 80)).Build();
        _node = _document.FlowNodes[0] with
        {
            Extras = new()
            {
                Extensions = Rows == 0 ? [] :
                [new(BpmnExtensionSchemas.ZeebeNamespace, "ioMapping", new Dictionary<string, string>(),
                    [.. Enumerable.Range(0, Rows).Select(index => new BpmnExtensionElement(
                        BpmnExtensionSchemas.ZeebeNamespace, "input",
                        new Dictionary<string, string> { ["target"] = $"target{index}", ["source"] = $"=source{index}" }, []))])],
            },
        };
        _document = _document with { FlowNodes = [_node] };
        _snapshot = BpmnElementInspector.Describe(_document, "task")!;
        if (Build().Groups.Count == 0 && Schemas != "none")
            throw new InvalidOperationException("Schema fixture produced no groups.");
    }

    [Benchmark]
    public BpmnExtensionGroupBuildResult Build() =>
        _builder.Build(_snapshot, _node, _node.Extras, _options.Registry, _options.LabelProvider);

    [Benchmark]
    public BpmnElementSnapshot Describe() =>
        BpmnElementInspector.Describe(_document, "task", IsReadOnly, extensionOptions: _options)!;

    public static void ReportReuse()
    {
        foreach (var schemas in new[] { "none", "zeebe", "camunda", "both" })
            foreach (var rows in new[] { 0, 1, 600 })
                Run(schemas, rows, false, false);
        foreach (var readOnly in new[] { false, true })
        {
            Run("both", 600, false, readOnly);
            Run("both", 600, true, readOnly);
        }

        static void Run(string schemas, int rows, bool custom, bool readOnly)
        {
            var benchmark = new SchemaInspectionBenchmarks
            { Schemas = schemas, Rows = rows, CustomCallbacks = custom, IsReadOnly = readOnly };
            benchmark.Setup();
            Console.WriteLine($"schemas={schemas} rows={rows} custom={custom} readOnly={readOnly}");
            Measure("describe", () => benchmark.Describe(), rows == 0 ? 50 : 2);
            var output = JsonSerializer.Serialize(benchmark.Describe());
            Console.WriteLine($"output SHA256={Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(output)))}");
        }
    }

    private sealed class ForwardingBuilder : IBpmnExtensionGroupBuilder
    {
        public BpmnExtensionGroupBuildResult Build(BpmnElementSnapshot snapshot, object element, BpmnElementExtras extras,
            BpmnExtensionRegistry registry, IBpmnPropertyLabelProvider labels) =>
            new BpmnSchemaPropertiesProvider().Build(snapshot, element, extras, registry, labels);
    }

    private sealed class HostLabels : IBpmnPropertyLabelProvider
    {
        public string GetLabel(string key, string englishFallback) => englishFallback;
    }

    public static void Report()
    {
        foreach (var schemas in new[] { "none", "zeebe", "camunda", "both", "extra" })
        {
            foreach (var type in new[] { BpmnFlowNodeType.ServiceTask, BpmnFlowNodeType.CallActivity, BpmnFlowNodeType.EndEvent })
                Run(schemas, type, 0, true);
        }
        foreach (var schemas in new[] { "zeebe", "both", "extra" })
        {
            Run(schemas, BpmnFlowNodeType.ServiceTask, 600, true);
            Run(schemas, BpmnFlowNodeType.ServiceTask, 0, false);
        }

        static void Run(string schemas, BpmnFlowNodeType type, int rows, bool alwaysOffer)
        {
            var benchmark = new SchemaInspectionBenchmarks
            { Schemas = schemas, NodeType = type, Rows = rows, AlwaysOffer = alwaysOffer };
            benchmark.Setup();
            Console.WriteLine($"schemas={schemas} type={type} rows={rows} alwaysOffer={alwaysOffer}");
            Measure("build", () => benchmark.Build(), rows == 0 ? 50 : 2);
            Measure("describe", () => benchmark.Describe(), rows == 0 ? 50 : 2);
            var output = JsonSerializer.Serialize(benchmark.Build()) + JsonSerializer.Serialize(benchmark.Describe());
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
