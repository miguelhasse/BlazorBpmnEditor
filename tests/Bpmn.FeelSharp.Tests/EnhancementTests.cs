using System.Diagnostics;
using System.Text.Json;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Hasseware.AspNetCore.Components.Bpmn.FeelSharp.Tests;

public sealed class EnhancementTests
{
    [Fact]
    public async Task ScopedDiscoveryExcludesOtherProcessChildScopeAndSiblingInputs()
    {
        const string uri = "http://camunda.org/schema/zeebe/1.0";
        static BpmnFlowNode Node(string id, string scope, string input, string output) =>
            new(id, BpmnFlowNodeType.Task, scope)
            {
                Extras = BpmnElementExtras.None with
                {
                    Extensions = [new(uri, "ioMapping", new Dictionary<string, string>(),
                        [new(uri, "input", new Dictionary<string, string> { ["target"] = input }, []),
                            new(uri, "output", new Dictionary<string, string> { ["target"] = output }, [])])],
                },
            };
        var document = new BpmnDocument
        {
            Definitions = new("definitions", "urn:test"),
            FlowNodes = [Node("selected", "child", "ownInput", "ownOutput"),
                new("child", BpmnFlowNodeType.SubProcess, "process"),
                Node("sibling", "child", "siblingInput", "siblingOutput"),
                Node("parent", "process", "parentInput", "parentOutput"),
                Node("unrelated", "other", "foreignInput", "foreignOutput"),
                Node("grandchild", "selected", "hiddenInput", "hiddenOutput")],
        };
        var hints = await new BpmnFeelVariableProvider(true).GetVariablesAsync(document, "selected",
            TestContext.Current.CancellationToken);
        Assert.Equal(["ownInput", "ownOutput", "parentOutput", "siblingOutput"], hints.Select(hint => hint.Name));
        Assert.All(hints, hint => Assert.True(hint.IsInferred));
        var defaults = await new BpmnFeelVariableProvider().GetVariablesAsync(document, "selected",
            TestContext.Current.CancellationToken);
        Assert.Contains(defaults, hint => hint.Name == "foreignInput");
    }

    [Fact]
    public void SampleInferencePreservesStringsNullAndCaseSensitiveNames()
    {
        var variables = FeelSharpSampleVariables.FromJson("""{"date":"2025-01-01","x":null,"X":1,"customer":{"name":"Ada"}}""");
        Assert.Equal("string", Assert.Single(variables, item => item.Name == "date").Detail);
        Assert.Equal("null", Assert.Single(variables, item => item.Name == "x").Detail);
        Assert.Equal("number", Assert.Single(variables, item => item.Name == "X").Detail);
        Assert.Equal("name", Assert.Single(Assert.Single(variables, item => item.Name == "customer").Entries).Name);
        Assert.Throws<JsonException>(() => FeelSharpSampleVariables.FromJson("[]"));
        Assert.Throws<JsonException>(() => FeelSharpSampleVariables.FromSchema("""{"$ref":"https://example.test/schema"}"""));
        var schema = FeelSharpSampleVariables.FromSchema("""{"type":"object","properties":{"value":{"type":"number"}}}""");
        Assert.Equal("number", Assert.Single(schema).Detail);
    }

    [Theory]
    [InlineData("> 18", 19, true)]
    [InlineData("> 18", 18, false)]
    public void UnaryEvaluationIsExplicitAndNotExpressionValidation(string tests, int input, bool expected)
    {
        var result = new BpmnFeelSharpEvaluation().EvaluateUnaryTests(tests, input,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(expected, result.Value);
        Assert.DoesNotContain(typeof(global::FeelSharp.IFeelEngine).GetMethods(), method => method.Name == "ParseUnaryTests");
    }

    [Fact]
    public void UnaryCorpusRecordsUpstreamRangeAndWildcardLimitations()
    {
        var evaluator = new BpmnFeelSharpEvaluation();
        var range = evaluator.EvaluateUnaryTests("[1..10]", 3, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(range.IsSuccess, range.Error);
        Assert.False(range.Value);
        var wildcard = evaluator.EvaluateUnaryTests("-", 3, cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(wildcard.IsSuccess);
        Assert.NotNull(wildcard.Error);
    }

    [Theory]
    [InlineData("substring(\"a,b\", count([1,2]), ", 3)]
    [InlineData("substring(\"a,b\", ", 2)]
    [InlineData("sum([1,2], ", 2)]
    public void SignatureHelpCountsOnlyCurrentCallParameters(string source, int parameter)
    {
        var signature = FeelSharpAuthoring.Signature(source, source.Length, FeelSharpLanguage.Tokenize(source), []);
        Assert.NotNull(signature);
        Assert.Equal(parameter, signature.Parameter);
    }

    [Theory]
    [InlineData("my_sum(")]
    [InlineData("value'count(")]
    public void SignatureHelpDoesNotMatchSuffixOfUnrelatedFunctionName(string source)
    {
        Assert.Null(FeelSharpAuthoring.Signature(source, source.Length, FeelSharpLanguage.Tokenize(source), []));
    }

    [Theory]
    [InlineData("for invoice amount in [1] return invoice a", "invoice amount")]
    [InlineData("some item in [1] satisfies ite", "item")]
    [InlineData("function(x, y) x + ", "x")]
    [InlineData("{ first: 1, second: fir", "first")]
    public void LocalBindingInferenceIsSharedAndConservative(string source, string name)
    {
        var locals = FeelSharpAuthoring.Locals(source, source.Length, FeelSharpLanguage.Tokenize(source));
        Assert.Contains(locals, variable => variable.Name == name);
    }

    [Fact]
    public void ClosedLocalScopeDoesNotLeak()
    {
        const string source = "sum(for item in [1] return item) + ite";
        Assert.Empty(FeelSharpAuthoring.Locals(source, source.Length, FeelSharpLanguage.Tokenize(source)));
    }

    [Theory]
    [InlineData("for x in [1], y in [2] return x + ", "y")]
    [InlineData("{ first: { value: 1 }, second: fir", "first")]
    [InlineData("sum(for x in [1] return function(y) x + ", "y")]
    public void NestedLocalHintsRetainOnlyVisibleBindings(string source, string name)
    {
        Assert.Contains(FeelSharpAuthoring.Locals(source, source.Length, FeelSharpLanguage.Tokenize(source)),
            variable => variable.Name == name);
    }

    [Fact]
    public void MalformedDelimitersSuppressLocalHints()
    {
        const string source = "(for x in [1] return x] + ";
        Assert.Empty(FeelSharpAuthoring.Locals(source, source.Length, FeelSharpLanguage.Tokenize(source)));
    }

    [Fact]
    public void SampleInferenceMergesNestedListsAndRejectsAmbiguousOrOversizedData()
    {
        var samples = FeelSharpSampleVariables.FromJson("""{"rows":[{"address":{"city":"A"}},{"address":{"city":"B","zip":1}}]}""");
        Assert.Equal(["city", "zip"], Assert.Single(Assert.Single(samples).Entries).Entries.Select(variable => variable.Name));
        var mixed = FeelSharpSampleVariables.FromJson("""{"rows":[{"x":1},{"x":"a"}]}""");
        Assert.Equal("unknown", Assert.Single(Assert.Single(mixed).Entries).Detail);
        const string duplicate = """{"x":1,"x":2}""";
        Assert.Throws<JsonException>(() => FeelSharpSampleVariables.FromJson(duplicate));
        Assert.Throws<JsonException>(() => FeelSharpJsonContext.Read(duplicate));
        Assert.Throws<JsonException>(() => FeelSharpSampleVariables.FromSchema("""{"description":1}"""));
        var large = "{\"rows\":[" + string.Join(',', Enumerable.Repeat("1", 1000)) + "]}";
        Assert.Throws<JsonException>(() => FeelSharpSampleVariables.FromJson(large));
        var deep = "{\"x\":" + new string('[', 33) + "1" + new string(']', 33) + "}";
        Assert.ThrowsAny<JsonException>(() => FeelSharpSampleVariables.FromJson(deep));
    }

    [Fact]
    public void DiagnosticsDelayRejectsValuesUnsupportedByScheduler()
    {
        var options = new BpmnFeelSharpEditorOptions();
        Assert.Throws<ArgumentOutOfRangeException>(() => options.DiagnosticsDelay = TimeSpan.FromMilliseconds(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => options.DiagnosticsDelay = TimeSpan.MaxValue);
    }

    [Fact]
    public void RankingAndSnippetsAreOptIn()
    {
        BpmnFeelVariable[] variables = [new("zebra", "number")];
        Assert.Equal("abs", FeelSharpLanguage.Complete("", 0, 0, variables, true)[0].Label);
        var options = new BpmnFeelSharpEditorOptions { RankCompletions = true, Snippets = true };
        Assert.Equal("zebra", FeelSharpLanguage.Complete("", 0, 0, variables, true, options)[0].Label);
        var snippet = Assert.Single(FeelSharpLanguage.Complete("if", 2, 2, [], true, options),
            candidate => candidate.Label == "if expression");
        Assert.Null(new FeelSharpSyntax().Validate(snippet.Text));
    }

    [Fact]
    public void ExplicitProviderAllowlistWorksWithoutAutomaticDiscovery()
    {
        var services = new ServiceCollection().AddBpmnFeelSharpEvaluation(options =>
            options.FunctionProviders.Add(new TestFunctions()));
        using var provider = services.BuildServiceProvider();
        var evaluator = provider.GetRequiredService<IBpmnFeelSharpEvaluation>();
        var result = evaluator.Evaluate("trustedAdd(2)", cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(3m, Assert.IsType<global::FeelSharp.NumberValue>(result.Value).Value);
        var unknown = new BpmnFeelSharpEvaluation().Evaluate("trustedAdd(2)",
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(!unknown.IsSuccess || unknown.Value is global::FeelSharp.NullValue);
    }

    private sealed class TestFunctions : global::FeelSharp.Api.ICustomFunctionProvider
    {
        private readonly global::FeelSharp.Api.FeelFunction _function = new("trustedAdd", ["number"], values =>
            new global::FeelSharp.NumberValue(Assert.IsType<global::FeelSharp.NumberValue>(values[0]).Value + 1), false);
        public IEnumerable<string> FunctionNames => ["trustedAdd"];
        public global::FeelSharp.Api.FeelFunction? GetFunction(string name) => name == _function.Name ? _function : null;
        public IEnumerable<global::FeelSharp.Api.FeelFunction> GetFunctions(string name) =>
            name == _function.Name ? [_function] : [];
    }

    [Theory]
    [InlineData("1e3", "number")]
    [InlineData("@\"2025-01-01\"", "temporal")]
    [InlineData("date and time(\"2025-01-01T12:00:00\")", "function")]
    public void VerifiedLexicalFormsPreserveExactText(string source, string kind)
    {
        Assert.Null(new FeelSharpSyntax().Validate(source));
        var tokens = FeelSharpLanguage.Tokenize(source);
        Assert.Equal(source, string.Concat(tokens.Select(token => token.Text)));
        Assert.Equal(kind, tokens[0].Kind);
    }

    [Theory]
    [InlineData(1024)]
    [InlineData(10240)]
    [InlineData(102400)]
    public void LargeDraftBaselinePreservesTextAndCompletes(int size)
    {
        var source = "[" + string.Join(",", Enumerable.Repeat("1", size / 2)) + "]";
        BpmnFeelVariable[] variables = Enumerable.Range(0, 1000).Select(index => new BpmnFeelVariable($"item{index}")).ToArray();
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        var tokens = FeelSharpLanguage.Tokenize(source);
        var completion = FeelSharpLanguage.Complete(source, 1, 1, variables, true, tokens: tokens);
        var completionDuration = watch.Elapsed;
        var syntax = new FeelSharpSyntax();
        watch.Restart();
        Assert.Null(syntax.Validate(source));
        Assert.Equal(source, string.Concat(tokens.Select(token => token.Text)));
        Assert.NotEmpty(completion);
        Console.WriteLine($"FEEL baseline {size} bytes: tokenize/complete {completionDuration.TotalMilliseconds:F2} ms, parse {watch.Elapsed.TotalMilliseconds:F2} ms, allocated {GC.GetAllocatedBytesForCurrentThread() - allocated} bytes.");
    }
}
