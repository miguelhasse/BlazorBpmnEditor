using Hasseware.AspNetCore.Components.Bpmn.FeelSharp;

namespace Hasseware.AspNetCore.Components.Bpmn.FeelSharp.Tests;

public sealed class LanguageTests
{
    [Fact]
    public void LargeCompletionMetadataAvoidsFullCandidateSortAndBufferAllocations()
    {
        const string source = "sum([amount, amount]) + am";
        var variables = Enumerable.Range(0, 1000).Select(index => new BpmnFeelVariable("amount " + index)
        {
            Entries = [new("value", "number")],
        }).ToArray();
        var options = new BpmnFeelSharpEditorOptions { LocalVariables = true, Snippets = true, RankCompletions = true };
        var tokens = FeelSharpLanguage.Tokenize(source);
        FeelSharpLanguage.Complete(source, source.Length, source.Length, variables, true, options, tokens);
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var completions = FeelSharpLanguage.Complete(source, source.Length, source.Length, variables, true, options, tokens);
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Assert.Equal(12, completions.Count);
        Assert.True(allocated < 600_000, $"Completion allocated {allocated} bytes; budget is 600,000.");
        Assert.Equal("amount 0", completions[0].Label);
        Assert.Equal("amount 0.value", completions[1].Label);
    }

    [Fact]
    public void CompletionObservesMutationsToHostMetadataAndCustomSignatures()
    {
        var entries = new List<BpmnFeelVariable> { new("before") };
        var variables = new[] { new BpmnFeelVariable("customer") { Entries = entries } };
        Assert.Contains(FeelSharpLanguage.Complete("customer.", 9, 9, variables, true), item => item.Label == "customer.before");
        entries.Clear();
        entries.Add(new("after"));
        var changed = FeelSharpLanguage.Complete("customer.", 9, 9, variables, true);
        Assert.Contains(changed, item => item.Label == "customer.after");
        Assert.DoesNotContain(changed, item => item.Label == "customer.before");
        var functions = new List<BpmnFeelSharpFunction> { new("sum", "custom sum(first)", "Custom sum") };
        const string source = "sum(";
        var tokens = FeelSharpLanguage.Tokenize(source);
        Assert.Equal("custom sum(first)", FeelSharpAuthoring.Signature(source, source.Length, tokens, functions)!.Signature);
        functions[0] = new("sum", "custom sum(changed)", "Changed");
        Assert.Equal("custom sum(changed)", FeelSharpAuthoring.Signature(source, source.Length, tokens, functions)!.Signature);
    }

    [Fact]
    public void ParserEngineIsCreatedOnlyForNonemptyValidation()
    {
        var syntax = new FeelSharpSyntax();
        Assert.False(syntax.IsInitialized);
        Assert.Null(syntax.Validate(" \r\n"));
        Assert.False(syntax.IsInitialized);
        Assert.Null(syntax.Validate("1 + 2"));
        Assert.True(syntax.IsInitialized);
    }

    [Theory]
    [InlineData("unknownVariable + 3")]
    [InlineData("if amount > 10 then \"yes\" else \"no\"")]
    [InlineData("1 / 0")]
    [InlineData("sum([1, 2, 3])")]
    [InlineData("/* comment */ 1")]
    public void SyntaxValidationDoesNotEvaluate(string expression)
    {
        Assert.Null(new FeelSharpSyntax().Validate(expression));
    }

    [Theory]
    [InlineData("1 + )")]
    [InlineData("if true then")]
    [InlineData("\"unterminated")]
    public void SyntaxErrorsAreExplicit(string expression)
    {
        Assert.False(string.IsNullOrWhiteSpace(new FeelSharpSyntax().Validate(expression)));
    }

    [Fact]
    public void TokenizationPreservesExactTextAndOffsets()
    {
        const string source = " \t\r\nif x >= 2 then \"<script>😀\\\"\" else /* a\nb */ null\n  ";
        var tokens = FeelSharpLanguage.Tokenize(source);
        Assert.Equal(source, string.Concat(tokens.Select(token => token.Text)));
        Assert.All(tokens, token => Assert.Equal(token.Text, source[token.Offset..token.End]));
        Assert.Equal(" \t\r\n", tokens[0].Text);
        Assert.Contains(tokens, token => token.Kind == "string" && token.Text.Contains("<script>", StringComparison.Ordinal));
        Assert.Contains(tokens, token => token.Kind == "comment");
    }

    [Theory]
    [InlineData("amount + cus + 1", 12, 12, "customer", 9, 3)]
    [InlineData("customer.na + 1", 11, 11, "customer.name", 0, 11)]
    [InlineData("string j", 8, 8, "string join", 0, 8)]
    [InlineData("😀 + amouZZ + 1", 9, 11, "amount", 5, 6)]
    public void CompletionUsesCaretAndExactReplacementSpans(string source, int start, int end,
        string label, int expectedStart, int expectedLength)
    {
        BpmnFeelVariable[] variables =
        [
            new("amount"),
            new("customer") { Entries = [new("name")] },
        ];
        var completion = Assert.Single(FeelSharpLanguage.Complete(source, start, end, variables),
            item => item.Label == label);
        Assert.Equal(expectedStart, completion.Start);
        Assert.Equal(expectedLength, completion.Length);
    }

    [Theory]
    [InlineData("\"amo", 4)]
    [InlineData("/* amo", 6)]
    [InlineData("// amo", 6)]
    public void CompletionIsSuppressedInStringsAndComments(string source, int caret)
    {
        Assert.Empty(FeelSharpLanguage.Complete(source, caret, caret, [new("amount")]));
    }

    [Theory]
    [InlineData("su([1, 2])", 2, "sum([1, 2])")]
    [InlineData("su ([1, 2])", 2, "sum ([1, 2])")]
    [InlineData("sum([1, 2])", 2, "sum([1, 2])")]
    public void CompleteExistingFunctionArgumentsPreservesCall(string source, int caret, string expected)
    {
        var completion = Assert.Single(FeelSharpLanguage.Complete(source, caret, caret, []),
            candidate => candidate.Label == "sum");

        var result = source.Remove(completion.Start, completion.Length)
            .Insert(completion.Start, completion.Text);

        Assert.Equal(expected, result);
        Assert.Null(new FeelSharpSyntax().Validate(result));
    }

    [Fact]
    public void CompleteVariableNamedLikeFunctionInsertsVariable()
    {
        var completion = Assert.Single(FeelSharpLanguage.Complete("cou", 3, 3,
            [new("count") { Detail = "Item count" }]), candidate => candidate.Label == "count");

        Assert.Equal("count", completion.Text);
        Assert.Equal("Item count", completion.Detail);
    }

    [Theory]
    [InlineData("string join([\"a\", \"b\"], \",\")", 3, "string join", "string join([\"a\", \"b\"], \",\")")]
    [InlineData("STRING JOIN([\"a\", \"b\"], \",\")", 3, "string join", "string join([\"a\", \"b\"], \",\")")]
    [InlineData("customer.full name + 1", 11, "customer.full name", "customer.full name + 1")]
    public void CompletionInsideMultiwordNamePreservesRemainingWords(string source, int caret, string label, string expected)
    {
        var completion = Assert.Single(FeelSharpLanguage.Complete(source, caret, caret,
            [new("customer") { Entries = [new("full name")] }]), candidate => candidate.Label == label);

        var result = source.Remove(completion.Start, completion.Length).Insert(completion.Start, completion.Text);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("customer.full nickname + 1")]
    [InlineData("customer.full names + 1")]
    public void MultiwordCompletionDoesNotConsumeDifferentTrailingName(string source)
    {
        var completion = Assert.Single(FeelSharpLanguage.Complete(source, 11, 11,
            [new("customer") { Entries = [new("full name")] }]), candidate => candidate.Label == "customer.full name");

        Assert.Equal(0, completion.Start);
        Assert.Equal("customer.full".Length, completion.Length);
    }

    [Fact]
    public void MultiwordCompletionDoesNotShrinkExplicitSelection()
    {
        const string source = "customer.full name + 1";
        var completion = Assert.Single(FeelSharpLanguage.Complete(source, 11, source.Length,
            [new("customer") { Entries = [new("full name")] }]), candidate => candidate.Label == "customer.full name");

        Assert.Equal(0, completion.Start);
        Assert.Equal(source.Length, completion.Length);
    }

    [Fact]
    public void CatalogFunctionsAreAcceptedByPublishedParser()
    {
        var syntax = new FeelSharpSyntax();
        // Suggestions insert syntax only; argument count and runtime values are not evaluated.
        foreach (var prefix in new[] { "a", "c", "d", "f", "g", "i", "l", "m", "n", "r", "s", "t", "u" })
        {
            foreach (var candidate in FeelSharpLanguage.Complete(prefix, prefix.Length, prefix.Length, [])
                         .Where(candidate => candidate.Text.EndsWith("()", StringComparison.Ordinal)))
            {
                Assert.Null(syntax.Validate(candidate.Text));
            }
        }

    }

    [Theory]
    [InlineData("abs", "number")]
    [InlineData("append", "list")]
    [InlineData("ceiling", "number")]
    [InlineData("concatenate", "list")]
    [InlineData("contains", "boolean")]
    [InlineData("count", "number")]
    [InlineData("date", "temporal")]
    [InlineData("date and time", "temporal")]
    [InlineData("day of week", "string")]
    [InlineData("day of year", "number")]
    [InlineData("distinct values", "list")]
    [InlineData("duration", "temporal")]
    [InlineData("ends with", "boolean")]
    [InlineData("floor", "number")]
    [InlineData("get entries", "list")]
    [InlineData("get value", "number")]
    [InlineData("index of", "list")]
    [InlineData("insert before", "list")]
    [InlineData("list contains", "boolean")]
    [InlineData("lower case", "string")]
    [InlineData("max", "number")]
    [InlineData("mean", "number")]
    [InlineData("min", "number")]
    [InlineData("now", "temporal")]
    [InlineData("number", "number")]
    [InlineData("reverse", "list")]
    [InlineData("round", "number")]
    [InlineData("sort", "list")]
    [InlineData("split", "list")]
    [InlineData("starts with", "boolean")]
    [InlineData("string", "string")]
    [InlineData("string join", "string")]
    [InlineData("string length", "number")]
    [InlineData("substring", "string")]
    [InlineData("sum", "number")]
    [InlineData("time", "temporal")]
    [InlineData("today", "temporal")]
    [InlineData("upper case", "string")]
    public void DocumentedFunctionExamplesEvaluateWithRestrictedEngine(string name, string kind)
    {
        var function = Assert.Single(FeelSharpFunctionCatalog.Functions, function => function.Name == name);
        Assert.Null(new FeelSharpSyntax().Validate(function.Example));
        var result = new BpmnFeelSharpEvaluation().Evaluate(function.Example,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess, result.Error);
        Assert.NotNull(result.Value);
        Assert.IsNotType<global::FeelSharp.NullValue>(result.Value);
        switch (kind)
        {
            case "number": Assert.IsType<global::FeelSharp.NumberValue>(result.Value); break;
            case "list": Assert.IsType<global::FeelSharp.ListValue>(result.Value); break;
            case "boolean": Assert.IsType<global::FeelSharp.BooleanValue>(result.Value); break;
            case "string": Assert.IsType<global::FeelSharp.StringValue>(result.Value); break;
            case "temporal":
                var temporal = Assert.IsType<global::FeelSharp.StringValue>(result.Value).Value;
                Assert.Equal("Feel.TemporalResult", function.Note);
                switch (name)
                {
                    case "date":
                        Assert.Equal("ValDate 2025-01-01 00:00:00", temporal);
                        break;
                    case "date and time":
                        Assert.Equal("ValLocalDateTime 2025-01-01 12:00:00", temporal);
                        break;
                    case "duration":
                        Assert.Equal("ValDayTimeDuration 1.00:00:00", temporal);
                        break;
                    case "time":
                        Assert.StartsWith("ValTime", temporal, StringComparison.Ordinal);
                        Assert.Contains("Hour = 12", temporal, StringComparison.Ordinal);
                        Assert.Contains("Minute = 0", temporal, StringComparison.Ordinal);
                        break;
                    case "today": Assert.StartsWith("ValDate ", temporal, StringComparison.Ordinal); break;
                    case "now": Assert.StartsWith("ValDateTime ", temporal, StringComparison.Ordinal); break;
                }
                break;
        }
        var expected = name switch
        {
            "abs" => 3m,
            "ceiling" => 2m,
            "count" => 2m,
            "day of year" => 1m,
            "floor" => 1m,
            "get value" => 1m,
            "max" => 3m,
            "mean" => 2m,
            "min" => 1m,
            "number" => 12m,
            "round" => 1.23m,
            "string length" => 5m,
            "sum" => 6m,
            _ => (decimal?)null,
        };
        if (expected is { } number)
        {
            Assert.Equal(number, Assert.IsType<global::FeelSharp.NumberValue>(result.Value).Value);
        }
        if (kind == "boolean")
        {
            Assert.True(Assert.IsType<global::FeelSharp.BooleanValue>(result.Value).Value);
        }
        var text = name switch
        {
            "day of week" => "Wednesday",
            "lower case" => "hello",
            "string" => "12",
            "string join" => "a,b",
            "substring" => "ell",
            "upper case" => "HELLO",
            _ => null,
        };
        if (text is not null)
        {
            Assert.Equal(text, Assert.IsType<global::FeelSharp.StringValue>(result.Value).Value);
        }
        decimal[]? numbers = name switch
        {
            "append" or "insert before" or "sort" => [1m, 2m, 3m],
            "concatenate" or "distinct values" => [1m, 2m],
            "index of" => [1m, 3m],
            "reverse" => [2m, 1m],
            _ => null,
        };
        if (numbers is not null)
        {
            Assert.Equal(numbers, Assert.IsType<global::FeelSharp.ListValue>(result.Value).Items
                .Select(value => Assert.IsType<global::FeelSharp.NumberValue>(value).Value));
        }
        if (name == "split")
        {
            Assert.Equal(["a", "b"], Assert.IsType<global::FeelSharp.ListValue>(result.Value).Items
                .Select(value => Assert.IsType<global::FeelSharp.StringValue>(value).Value));
        }
    }

    [Fact]
    public void ExplicitCompletionAndDocumentationPreserveVariablePrecedence()
    {
        Assert.Empty(FeelSharpLanguage.Complete("", 0, 0, []));
        var completion = Assert.Single(FeelSharpLanguage.Complete("", 0, 0,
            [new("abs", "Business value", "<script>documentation</script>")], true), item => item.Label == "abs");
        Assert.Equal("abs", completion.Text);
        Assert.Equal("Business value", completion.Detail);
        Assert.Equal("<script>documentation</script>", completion.Info);
        Assert.Null(completion.Example);
        Assert.Empty(FeelSharpLanguage.Complete("\"abc", 4, 4, [], true));
        Assert.Empty(FeelSharpLanguage.Complete("// abc", 6, 6, [], true));
        var builtin = Assert.Single(FeelSharpLanguage.Complete("su", 2, 2, []), item => item.Label == "sum");
        Assert.Equal("sum(list)", builtin.Detail);
        Assert.Equal("sum([1, 2, 3])", builtin.Example);
    }
}
