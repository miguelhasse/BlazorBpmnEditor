using Hasseware.AspNetCore.Components.Bpmn.FeelSharp;

namespace Hasseware.AspNetCore.Components.Bpmn.FeelSharp.Tests;

public sealed class LanguageTests
{
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
        const string source = "if x >= 2 then \"<script>😀\\\"\" else /* a\nb */ null\n";
        var tokens = FeelSharpLanguage.Tokenize(source);
        Assert.Equal(source, string.Concat(tokens.Select(token => token.Text)));
        Assert.All(tokens, token => Assert.Equal(token.Text, source[token.Offset..token.End]));
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
}
