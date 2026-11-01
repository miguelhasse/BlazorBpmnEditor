using global::FeelSharp;
using global::FeelSharp.Api;

namespace Hasseware.AspNetCore.Components.Bpmn.FeelSharp;

internal sealed class BpmnFeelSharpEvaluation : IBpmnFeelSharpEvaluation
{
    public FeelResult<FeelValue> Evaluate(string expression, IReadOnlyDictionary<string, object?>? variables = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expression);
        cancellationToken.ThrowIfCancellationRequested();
        // Each request owns its engine; no assumptions about upstream engine thread safety.
        var engine = new FeelEngineBuilder().WithAutoDiscovery(false).WithExternalFunctions(false).Build();
        // The object-context overload maps null dictionary entries correctly; the typed
        // dictionary overload cannot express nulls in its nullable annotations.
        object context = variables is null ? new Dictionary<string, object?>() :
            new Dictionary<string, object?>(variables, StringComparer.Ordinal);
        var result = engine.EvaluateExpression(FeelSharpSyntax.Normalize(expression), context);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }
}
