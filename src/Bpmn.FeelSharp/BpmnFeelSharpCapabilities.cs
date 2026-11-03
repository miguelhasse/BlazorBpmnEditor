namespace Hasseware.AspNetCore.Components.Bpmn.FeelSharp;

/// <summary>Verified limitations of the pinned interpreter and conservative authoring layer.</summary>
public sealed class BpmnFeelSharpCapabilities
{
    /// <summary>Engine/profile identifier, not a Camunda conformance claim.</summary>
    public string Profile { get; } = "FeelSharp 1.3.0 restricted interpreter";
    /// <summary>No public parse-only unary-test API has been verified.</summary>
    public bool UnaryTestParsing { get; }
    /// <summary>Semantic formatting requires a verified syntax tree; only conservative editing actions are provided.</summary>
    public bool SemanticFormatting { get; }
    /// <summary>Cancellation cannot forcibly interrupt synchronous interpretation.</summary>
    public bool HardExecutionTimeout { get; }
    /// <summary>Known upstream unary-test differences; comparison tests alone do not establish dialect compatibility.</summary>
    public string UnaryTestLimitations { get; } = "Wildcard '-' fails; '[1..10]' with input 3 returns false. No parse-only validation.";
}
