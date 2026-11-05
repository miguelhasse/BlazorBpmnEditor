using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using Hasseware.AspNetCore.Components.Bpmn.FeelSharp;

namespace Hasseware.AspNetCore.Components.Bpmn.Benchmarks;

/// <summary>Isolated managed FEEL costs; browser rendering and interop are measured separately.</summary>
[MemoryDiagnoser]
public class FeelSharpBenchmarks
{
    private FeelSharpSyntax _syntax = new();
    private readonly BpmnFeelSharpEvaluation _evaluator = new();
    private readonly BpmnFeelSharpEditorOptions _options = new()
    {
        LocalVariables = true,
        SignatureHelp = true,
        Snippets = true,
        RankCompletions = true,
    };
    private string _source = "";
    private IReadOnlyList<FeelSharpToken> _tokens = [];
    private IReadOnlyList<BpmnFeelVariable> _variables = [];
    private static object? _result;

    /// <summary>Expression workload size.</summary>
    [Params(1, 25, 100)]
    public int Terms { get; set; }

    /// <summary>Host metadata size.</summary>
    [Params(10, 1000, 10000)]
    public int VariableCount { get; set; }

    /// <summary>Prepares expressions, tokens and metadata outside timed methods.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _source = "sum([" + string.Join(", ", Enumerable.Repeat("amount", Terms)) + "]) + am";
        _tokens = FeelSharpLanguage.Tokenize(_source);
        _variables = Enumerable.Range(0, VariableCount).Select(index => new BpmnFeelVariable("amount " + index)
        {
            Entries = [new("value", "number")],
        }).ToArray();
        _syntax = new();
        if (_syntax.Validate("1 + 2") is not null || string.IsNullOrWhiteSpace(_syntax.Validate("1 + )")))
        {
            throw new InvalidOperationException("Parser benchmark must accept valid syntax and reject invalid syntax.");
        }
    }

    /// <summary>New parser instance in a warmed process, including engine construction and first parse.</summary>
    [Benchmark]
    public object? ColdParse() => new FeelSharpSyntax().Validate(_source);

    /// <summary>Warm parser cost.</summary>
    [Benchmark]
    public object? Parse() => _syntax.Validate(_source);

    /// <summary>Full tokenization, including source text slices.</summary>
    [Benchmark]
    public object Tokenize() => FeelSharpLanguage.Tokenize(_source);

    /// <summary>Completion with cached tokens and current mutable metadata.</summary>
    [Benchmark]
    public object Complete() => FeelSharpLanguage.Complete(_source, _source.Length, _source.Length,
        _variables, true, _options, _tokens);

    /// <summary>Signature guidance inside the final argument.</summary>
    [Benchmark]
    public object? Signature() => FeelSharpAuthoring.Signature(_source, _source.IndexOf(']'), _tokens, _options.Functions);

    /// <summary>Expression-local metadata discovery.</summary>
    [Benchmark]
    public object Locals() => FeelSharpAuthoring.Locals(_source, _source.Length, _tokens);

    /// <summary>Isolated evaluation, including new engine construction.</summary>
    [Benchmark]
    public object Evaluate() => _evaluator.Evaluate("sum([1, 2, 3])");

    /// <summary>Quick repeated-call comparison; use BenchmarkDotNet for statistical conclusions.</summary>
    public static void Report()
    {
        foreach (var terms in new[] { 1, 25, 100 })
        {
            foreach (var count in new[] { 10, 1000, 10000 })
            {
                var benchmark = new FeelSharpBenchmarks { Terms = terms, VariableCount = count };
                benchmark.Setup();
                Console.WriteLine($"terms={terms} variables={count}");
                Measure("cold-parse", benchmark.ColdParse, 20);
                Measure("parse", benchmark.Parse, 100);
                Measure("tokenize", benchmark.Tokenize, 100);
                Measure("complete", benchmark.Complete, 100);
                Measure("signature", benchmark.Signature, 100);
                Measure("locals", benchmark.Locals, 100);
                Measure("evaluate", benchmark.Evaluate, 20);
            }
        }
    }

    private static void Measure(string name, Func<object?> action, int count)
    {
        for (var index = 0; index < 10; index++) _result = action();
        var samples = new double[7];
        long bytes = 0;
        for (var round = 0; round < samples.Length; round++)
        {
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp();
            for (var index = 0; index < count; index++) _result = action();
            samples[round] = Stopwatch.GetElapsedTime(start).TotalMicroseconds / count;
            bytes += GC.GetAllocatedBytesForCurrentThread() - allocated;
        }
        Array.Sort(samples);
        Console.WriteLine($"{name}: median={samples[3]:F2}us allocated={bytes / samples.Length / count}B");
        GC.KeepAlive(_result);
    }
}
