using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;
using Hasseware.AspNetCore.Components.Bpmn.Validation;

namespace Hasseware.AspNetCore.Components.Bpmn.Benchmarks;

/// <summary>Annotation-policy inspection and complete import-preview costs.</summary>
[MemoryDiagnoser]
public class AnnotationInspectionBenchmarks
{
    private BpmnDocument _document = null!;
    private string _content = "";

    [Params(2, 600, 5000)]
    public int Nodes { get; set; }

    [Params(0, 1, 60, 500)]
    public int Notes { get; set; }

    [Params(false, true)]
    public bool MultipleConnections { get; set; }

    [Params(false, true)]
    public bool Json { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var builder = new BpmnDocumentBuilder();
        for (var index = 0; index < Nodes; index++)
        {
            builder.Node($"task{index}", BpmnFlowNodeType.Task, new Bounds(index * 200, 0, 100, 80));
            if (index > 0) builder.Flow($"flow{index}", $"task{index - 1}", $"task{index}",
                [new(index * 200 - 100, 40), new(index * 200, 40)]);
        }
        for (var index = 0; index < Notes; index++)
        {
            var x = (index % Nodes) * 200;
            builder.TextAnnotation($"note{index}", "Note", new Bounds(x, 160, 80, 40))
                .Association($"association{index}", $"note{index}", $"task{index % Nodes}",
                    [new(x + 40, 160), new(x + 40, 100), new(x + 40, 80)]);
            if (MultipleConnections)
                builder.Association($"extra{index}", $"task{(index + 1) % Nodes}", $"note{index}",
                    [new(x + 200, 80), new(x + 80, 180)]);
        }
        var document = builder.Build();
        _content = Json ? BpmnJsonWriter.Write(document) : BpmnXmlWriter.Write(document).Xml;
        _document = Read().Document;
        var preview = Preview();
        if (!preview.CanAccept || preview.Document!.TextAnnotations.Count != Notes
            || preview.AnnotationNormalization.RemovedAssociationIds.Count != (MultipleConnections ? Notes : 0)
            || (Notes > 0 && !preview.AnnotationNormalization.RequiresConfirmation))
            throw new InvalidOperationException("Annotation preview fixture lost notes or consent requirements.");
    }

    [Benchmark]
    public BpmnAnnotationNormalization Inspect() => BpmnAnnotationNormalization.Inspect(_document);

    [Benchmark]
    public BpmnImportResult Read() => Json ? BpmnJsonReader.Read(_content) : BpmnXmlReader.Read(_content);

    [Benchmark]
    public BpmnValidationReport Validate() => BpmnValidator.Validate(_document);

    [Benchmark]
    public BpmnImportPreview Preview() => BpmnImportPreview.Of(Json ? "notes.json" : "notes.bpmn", _content);

    public static void Report()
    {
        foreach (var nodes in new[] { 2, 600, 5000 })
            foreach (var json in new[] { false, true }) Run(nodes, 0, false, json);
        foreach (var json in new[] { false, true })
        {
            Run(600, 1, false, json);
            Run(600, 60, false, json);
            Run(600, 60, true, json);
        }
        Run(5000, 500, true, false);

        static void Run(int nodes, int notes, bool multiple, bool json)
        {
            var benchmark = new AnnotationInspectionBenchmarks
            { Nodes = nodes, Notes = notes, MultipleConnections = multiple, Json = json };
            benchmark.Setup();
            Console.WriteLine($"nodes={nodes} notes={notes} multiple={multiple} json={json}");
            var calls = nodes == 2 ? 20 : 1;
            Measure("inspect", () => benchmark.Inspect(), calls);
            Measure("read", () => benchmark.Read(), calls);
            Measure("validate", () => benchmark.Validate(), calls);
            Measure("preview", () => benchmark.Preview(), calls);
            var preview = benchmark.Preview();
            var output = JsonSerializer.Serialize(benchmark.Inspect())
                + JsonSerializer.Serialize(preview.AnnotationNormalization)
                + JsonSerializer.Serialize(preview.Diagnostics) + JsonSerializer.Serialize(preview.Validation)
                + $"{preview.CanAccept}|{preview.Error}|{preview.SchemaValidationStatus}"
                + BpmnJsonWriter.Write(preview.Document!)
                + BpmnXmlWriter.Write(preview.Document!, BpmnWriteOptions.Fidelity).Xml;
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
