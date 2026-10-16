using BenchmarkDotNet.Running;
using Hasseware.AspNetCore.Components.Bpmn.Benchmarks;

// dotnet run -c Release --project tests/Bpmn.Benchmarks -- --filter "*"
BenchmarkSwitcher.FromAssembly(typeof(EditorBenchmarks).Assembly).Run(args);
