using global::FeelSharp;
using global::FeelSharp.Api;
using Microsoft.Extensions.Options;

namespace Hasseware.AspNetCore.Components.Bpmn.FeelSharp;

internal sealed class BpmnFeelSharpEvaluation(IOptions<BpmnFeelSharpEvaluationOptions>? options = null) : IBpmnFeelSharpEvaluation
{
    public string Profile => "FeelSharp 1.3.0 restricted interpreter; explicit host providers/mappers only";

    public FeelResult<FeelValue> Evaluate(string expression, IReadOnlyDictionary<string, object?>? variables = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expression);
        cancellationToken.ThrowIfCancellationRequested();
        // Each request owns its engine; no assumptions about upstream engine thread safety.
        var engine = BuildEngine();
        // The object-context overload maps null dictionary entries correctly; the typed
        // dictionary overload cannot express nulls in its nullable annotations.
        object context = variables is null ? new Dictionary<string, object?>() :
            new Dictionary<string, object?>(variables, StringComparer.Ordinal);
        var result = engine.EvaluateExpression(FeelSharpSyntax.Normalize(expression), context);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    public FeelResult<bool?> EvaluateUnaryTests(string tests, object? input,
        IReadOnlyDictionary<string, object?>? variables = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tests);
        cancellationToken.ThrowIfCancellationRequested();
        object context = variables is null ? new Dictionary<string, object?>() :
            new Dictionary<string, object?>(variables, StringComparer.Ordinal);
        var result = BuildEngine().EvaluateUnaryTests(tests, input!, context);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    private IFeelEngine BuildEngine()
    {
        var builder = new FeelEngineBuilder().WithAutoDiscovery(false).WithExternalFunctions(false);
        if (options is not null)
        {
            builder.WithCustomFunctionProviders(options.Value.FunctionProviders);
            builder.WithCustomValueMappers(options.Value.ValueMappers);
        }
        return builder.Build();
    }
}
