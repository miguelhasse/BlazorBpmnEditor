using global::FeelSharp;

namespace Hasseware.AspNetCore.Components.Bpmn.FeelSharp;

/// <summary>Opt-in in-process FEEL interpretation. Not a sandbox or BPMN executor.</summary>
public interface IBpmnFeelSharpEvaluation
{
    /// <summary>Host-visible engine/profile description. Implementations must not imply Camunda compatibility.</summary>
    public string Profile => "Host-supplied evaluator; profile unspecified";

    /// <summary>Interprets an expression, accepting one optional BPMN '=' prefix and explicit context variables.</summary>
    /// <remarks>Cancellation is checked before and after interpretation, not during it.
    /// Inspect IsSuccess and Error separately from Value: FEEL null is a successful value.</remarks>
    public FeelResult<FeelValue> Evaluate(string expression, IReadOnlyDictionary<string, object?>? variables = null,
        CancellationToken cancellationToken = default);

    /// <summary>Evaluates unary tests explicitly against an input; this is not parse-only validation.</summary>
    public FeelResult<bool?> EvaluateUnaryTests(string tests, object? input,
        IReadOnlyDictionary<string, object?>? variables = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This host evaluator does not support unary tests.");
}
