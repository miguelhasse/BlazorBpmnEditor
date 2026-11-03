using System.Text.Json;
using BenchmarkDotNet.Running;
using Hasseware.AspNetCore.Components.Bpmn.Benchmarks;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;
using Hasseware.AspNetCore.Components.Bpmn.Validation;

// dotnet run -c Release --project tests/Bpmn.Benchmarks -- --filter "*"     time and allocation per call
// dotnet run -c Release --project tests/Bpmn.Benchmarks -- memory           what stays in memory (history, index, source tree)
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
