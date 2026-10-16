using BenchmarkDotNet.Running;
using Hasseware.AspNetCore.Components.Bpmn.Benchmarks;

// dotnet run -c Release --project tests/Bpmn.Benchmarks -- --filter "*"     time and allocation per call
// dotnet run -c Release --project tests/Bpmn.Benchmarks -- memory           what stays in memory (history, index, source tree)
if (args.Length > 0 && args[0] == "memory")
{
    MemoryReport.Run(args[1..]);
    return;
}

BenchmarkSwitcher.FromAssembly(typeof(EditorBenchmarks).Assembly).Run(args);
