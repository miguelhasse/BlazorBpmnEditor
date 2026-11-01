using global::FeelSharp;

namespace Hasseware.AspNetCore.Components.Bpmn.FeelSharp;

/// <summary>Opt-in in-process FEEL interpretation. Not a sandbox or BPMN executor.</summary>
public interface IBpmnFeelSharpEvaluation
{
    /// <summary>Interprets an expression, accepting one optional BPMN '=' prefix and explicit context variables.</summary>
    /// <remarks>Cancellation is checked before and after interpretation, not during it.
    /// Inspect IsSuccess and Error separately from Value: FEEL null is a successful value.</remarks>
    public FeelResult<FeelValue> Evaluate(string expression, IReadOnlyDictionary<string, object?>? variables = null,
        CancellationToken cancellationToken = default);
}
