using global::FeelSharp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Hasseware.AspNetCore.Components.Bpmn.Configuration;

namespace Hasseware.AspNetCore.Components.Bpmn.FeelSharp.Tests;

public sealed class EvaluationTests
{
    [Fact]
    public void RegistrationIsIndependentAndIdempotent()
    {
        var services = new ServiceCollection();
        services.AddBpmnFeelSharpEvaluation().AddBpmnFeelSharpEvaluation();
        using var provider = services.BuildServiceProvider();
        Assert.Single(provider.GetServices<IBpmnFeelSharpEvaluation>());
        Assert.Null(provider.GetService<IOptions<BpmnOptions>>());

        var editorServices = new ServiceCollection().AddBpmnFeelSharpEditor();
        using var editorProvider = editorServices.BuildServiceProvider();
        Assert.Null(editorProvider.GetService<IBpmnFeelSharpEvaluation>());
    }

    [Fact]
    public void InterpretationPreservesTypedValuesAndContext()
    {
        var evaluator = new BpmnFeelSharpEvaluation();
        var result = evaluator.Evaluate("= amount + 3", new Dictionary<string, object?> { ["amount"] = 4 }, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(7m, Assert.IsType<NumberValue>(result.Value).Value);
        Assert.True(evaluator.Evaluate("amount > 3", new Dictionary<string, object?> { ["amount"] = 4 }, TestContext.Current.CancellationToken).IsSuccess);
        var nested = evaluator.Evaluate("customer.age > 21", new Dictionary<string, object?>
        {
            ["customer"] = new Dictionary<string, object?> { ["age"] = 25 },
        }, TestContext.Current.CancellationToken);
        Assert.True(Assert.IsType<BooleanValue>(nested.Value).Value);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("1 / 0")]
    public void SuccessfulFeelNullIsNotFailure(string expression)
    {
        var result = new BpmnFeelSharpEvaluation().Evaluate(expression, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess, result.Error);
        Assert.IsType<NullValue>(result.Value);
        Assert.Null(result.Error);
    }

    [Fact]
    public void NullContextAndMalformedExpressionsRemainDistinct()
    {
        var evaluator = new BpmnFeelSharpEvaluation();
        var result = evaluator.Evaluate("x", new Dictionary<string, object?> { ["x"] = null }, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess, result.Error);
        Assert.IsType<NullValue>(result.Value);
        var failure = evaluator.Evaluate("1 + )", cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(failure.IsFailure);
        Assert.False(string.IsNullOrWhiteSpace(failure.Error));
    }

    [Fact]
    public void CancellationAndConcurrentRequestsDoNotLeakContext()
    {
        var evaluator = new BpmnFeelSharpEvaluation();
        Assert.Throws<OperationCanceledException>(() => evaluator.Evaluate("1", cancellationToken: new(true)));
        Parallel.For(0, 20, new ParallelOptions { CancellationToken = TestContext.Current.CancellationToken }, index =>
        {
            var result = evaluator.Evaluate("x + 1", new Dictionary<string, object?> { ["x"] = index }, TestContext.Current.CancellationToken);
            Assert.Equal(index + 1m, Assert.IsType<NumberValue>(result.Value).Value);
        });
    }
}
