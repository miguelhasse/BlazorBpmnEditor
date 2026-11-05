using System.Text.Json;

namespace Hasseware.AspNetCore.Components.Bpmn.FeelSharp.Tests;

public sealed class ResultJsonTests
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    [Theory]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("12.5")]
    [InlineData("\"hello\"")]
    [InlineData("[1, null, \"two\", true]")]
    [InlineData("{customer: {name: \"Ada\"}, items: [1, 2]}")]
    [InlineData("date(\"2026-03-01\")")]
    [InlineData("time(\"12:30:45\")")]
    [InlineData("date and time(\"2026-03-01T12:30:45Z\")")]
    [InlineData("duration(\"P1DT2H\")")]
    [InlineData("duration(\"P1Y2M\")")]
    [InlineData("[1..5]")]
    [InlineData("function(x) x + 1")]
    public void PreservesEvaluationResultJson(string expression)
    {
        var result = new BpmnFeelSharpEvaluation().Evaluate(expression,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess, result.Error);
        var value = result.Value!.ToObject();

        var expected = JsonSerializer.Serialize(value, Options);
        var actual = FeelSharpResultJson.Serialize(value);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void CyclicContextsReportJsonErrorsInsteadOfOverflowing()
    {
        var context = new Dictionary<string, object?>();
        context["self"] = context;

        Assert.Throws<JsonException>(() => FeelSharpResultJson.Serialize(context));
    }
}
