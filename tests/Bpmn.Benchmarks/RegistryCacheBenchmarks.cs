using System.Diagnostics;
using System.Runtime.CompilerServices;
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
using Hasseware.AspNetCore.Components.Bpmn.Serialization;

namespace Hasseware.AspNetCore.Components.Bpmn.Benchmarks;

/// <summary>Registry queries and schema-ordered extension edits, with registration and retained costs.</summary>
[MemoryDiagnoser]
public class RegistryCacheBenchmarks
{
    private BpmnExtensionSchema _schema = null!;
    private BpmnExtensionRegistry _registry = null!;
    private BpmnExtensionType _type = null!;
    private BpmnSchemaChildOrder _order = null!;
    private BpmnExtensionElement _parent = null!;
    private BpmnDocument _document = null!;
    private EditingOptions _editing = null!;
    private AddExtensionElementCommand _command = null!;
    private BpmnExtensionSchema _trait = null!;

    [Params(3, 32, 128)]
    public int Width { get; set; }

    [Params(1, 8)]
    public int Depth { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var types = new List<BpmnExtensionType>();
        for (var index = 0; index < Depth; index++)
        {
            types.Add(new($"Base{index}", index == 0 ? ["Element"] : [$"Base{index - 1}"], [],
                [new($"attribute{index}", "String", true, false, false, false, null)]));
        }
        types.Add(new("Container", [$"Base{Depth - 1}"], [],
            [.. Enumerable.Range(0, Width).Select(index =>
                new BpmnExtensionProperty($"child{index}", "String", false, false, false, false, null)
                { Serialize = "property" })]));
        _schema = new("Cache benchmark", "urn:cache-benchmark", "cache", types);
        _trait = new("Late trait", "urn:cache-trait", "late",
            [new("Trait", [], ["cache:Container"], [new("late", "String", true, false, false, true, null)])]);
        _registry = CreateRegistry();
        _type = _registry.TryGetSchema(_schema.Uri)!.Types[^1];
        _order = new(_registry);
        _parent = new(_schema.Uri, "Container", new Dictionary<string, string>(),
            [.. Enumerable.Range(0, Width - 1).Select(index =>
                new BpmnExtensionElement(_schema.Uri, $"child{index}", new Dictionary<string, string>(), [], $"value{index}"))]);
        var builder = new BpmnDocumentBuilder().Node("task", BpmnFlowNodeType.ServiceTask, new Bounds(0, 0, 100, 80));
        var document = builder.Build();
        _document = document with
        { FlowNodes = [document.FlowNodes[0] with { Extras = new() { Extensions = [_parent] } }] };
        _editing = EditingOptions.Default with { ExtensionChildOrder = _order };
        _command = new("task", $"{{{_schema.Uri}}}Container",
            new(_schema.Uri, $"child{Width - 1}", new Dictionary<string, string>(), [], "added"));
        if (Properties().Count != Width + Depth || Children().Count != Width
            || Insertion() != Width - 1 || !Edit().IsApplied || AfterRegistration().Count != Width + Depth + 1)
            throw new InvalidOperationException("Registry benchmark fixture lost inheritance, order or applied edit.");
    }

    [Benchmark]
    public IReadOnlyList<BpmnExtensionProperty> Properties() => _registry.GetAllProperties(_type);

    [Benchmark]
    public IReadOnlyList<string> Children() => _registry.GetChildOrder(_type);

    [Benchmark]
    public int Insertion() => _order.InsertionIndex(_parent, _parent.Children, _schema.Uri, $"child{Width - 1}");

    [Benchmark]
    public BpmnEditResult Edit() => BpmnDocumentEditor.Apply(_document, _command, _editing);

    [Benchmark]
    public BpmnExtensionRegistry Register() => CreateRegistry();

    [Benchmark]
    public IReadOnlyList<string> ColdChildren()
    {
        var registry = CreateRegistry();
        return registry.GetChildOrder(registry.TryGetSchema(_schema.Uri)!.Types[^1]);
    }

    [Benchmark]
    public IReadOnlyList<BpmnExtensionProperty> AfterRegistration()
    {
        var registry = CreateRegistry();
        var type = registry.TryGetSchema(_schema.Uri)!.Types[^1];
        registry.GetAllProperties(type);
        registry.GetChildOrder(type);
        registry.Register(_trait);
        return registry.GetAllProperties(type);
    }

    [Benchmark]
    public BpmnExtensionRegistry RegisterIdentical() => _registry.Register(_schema);

    private BpmnExtensionRegistry CreateRegistry() => new BpmnExtensionRegistry()
        .Register(BpmnExtensionSchemas.Zeebe).Register(BpmnExtensionSchemas.Camunda7).Register(_schema);

    public static void Report()
    {
        foreach (var width in new[] { 3, 32, 128 })
            foreach (var depth in new[] { 1, 8 })
            {
                var benchmark = new RegistryCacheBenchmarks { Width = width, Depth = depth };
                benchmark.Setup();
                Console.WriteLine($"width={width} depth={depth}");
                Measure("properties", () => benchmark.Properties(), 50);
                Measure("children", () => benchmark.Children(), 50);
                Measure("insertion", () => benchmark.Insertion(), 5);
                Measure("edit", () => benchmark.Edit(), 2);
                Measure("registration", () => benchmark.Register(), 5);
                Measure("cold-children-with-registration", () => benchmark.ColdChildren(), 5);
                Measure("after-registration-with-setup", () => benchmark.AfterRegistration(), 5);
                Measure("identical-registration", () => benchmark.RegisterIdentical(), 5);
                var edit = benchmark.Edit();
                var output = JsonSerializer.Serialize(benchmark.Properties()) + JsonSerializer.Serialize(benchmark.Children())
                    + benchmark.Insertion() + JsonSerializer.Serialize(benchmark.AfterRegistration())
                    + JsonSerializer.Serialize(edit.Diagnostics) + BpmnJsonWriter.Write(edit.Document)
                    + BpmnXmlWriter.Write(edit.Document, BpmnWriteOptions.Fidelity).Xml;
                Console.WriteLine($"output SHA256={Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(output)))}");
                Console.WriteLine($"retained-query-growth-per-registry={RetainedGrowth(benchmark)}B");
            }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long RetainedGrowth(RegistryCacheBenchmarks benchmark)
    {
        var registries = new BpmnExtensionRegistry[16];
        for (var index = 0; index < registries.Length; index++)
            registries[index] = benchmark.CreateRegistry();
        var before = GC.GetTotalMemory(true);
        foreach (var registry in registries) WarmRegistry(registry);
        var bytes = GC.GetTotalMemory(true) - before;
        GC.KeepAlive(registries);
        return bytes / registries.Length;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void WarmRegistry(BpmnExtensionRegistry registry)
    {
        foreach (var type in registry.Schemas.SelectMany(schema => schema.Types))
        {
            registry.GetAllProperties(type);
            registry.GetChildOrder(type);
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
