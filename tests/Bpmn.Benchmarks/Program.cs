using System.Text.Json;
using BenchmarkDotNet.Running;
using Hasseware.AspNetCore.Components.Bpmn.Benchmarks;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;
using Hasseware.AspNetCore.Components.Bpmn.Validation;

// dotnet run -c Release --project tests/Bpmn.Benchmarks -- --filter "*"     time and allocation per call
// dotnet run -c Release --project tests/Bpmn.Benchmarks -- memory           what stays in memory (history, index, source tree)
if (args.Length > 0 && args[0] == "validation")
{
    ValidationBenchmarks.Report();
    return;
}
if (args.Length > 0 && args[0] == "memory")
{
    MemoryReport.Run(args[1..]);
    return;
}
if (args.Length > 0 && args[0] == "feel")
{
    FeelSharpBenchmarks.Report();
    return;
}
if (args.Length > 0 && args[0] == "export")
{
    ExportBenchmarks.Report();
    return;
}
if (args.Length > 0 && args[0] == "completion-corpus")
{
    CompletionCorpus.Report();
    return;
}
if (args.Length > 0 && args[0] == "diff")
{
    DiffBenchmarks.Report();
    return;
}
if (args.Length > 0 && args[0] == "snapshot")
{
    SnapshotBenchmarks.Report();
    return;
}
if (args.Length > 0 && args[0] == "schema")
{
    SchemaInspectionBenchmarks.Report();
    return;
}
if (args.Length > 0 && args[0] == "descriptor-reuse")
{
    SchemaInspectionBenchmarks.ReportReuse();
    return;
}
if (args.Length > 0 && args[0] == "registry-cache")
{
    RegistryCacheBenchmarks.Report();
    return;
}
if (args.Length > 0 && args[0] == "normalization")
{
    NormalizationBenchmarks.Report();
    return;
}
if (args.Length > 0 && args[0] == "annotation-inspection")
{
    AnnotationInspectionBenchmarks.Report();
    return;
}
if (args.Length > 0 && args[0] == "annotation-attachments")
{
    AnnotationAttachmentBenchmarks.Report();
    return;
}
if (args.Length > 0 && args[0] == "inspection")
{
    InspectionBenchmarks.Report();
    return;
}
if (args.Length > 0 && args[0] == "fidelity")
{
    FidelityBenchmarks.Report();
    return;
}

if (args.Length > 0 && args[0] == "validation-reports")
{
    if (args.Length != 2) throw new ArgumentException("Supply the directory containing the BPMN fixtures.");
    var reports = Directory.EnumerateFiles(args[1], "*.bpmn")
        .Order(StringComparer.Ordinal)
        .ToDictionary(path => Path.GetFileName(path), path =>
            BpmnValidator.Validate(BpmnXmlReader.Read(File.ReadAllText(path)).Document).Diagnostics);
    Console.WriteLine(JsonSerializer.Serialize(reports));
    return;
}

BenchmarkSwitcher.FromAssembly(typeof(EditorBenchmarks).Assembly).Run(args);
