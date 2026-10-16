namespace Hasseware.AspNetCore.Components.Bpmn.Validation;

/// <summary>The expression syntax used by an engine.</summary>
public enum BpmnExpressionLanguage
{
    /// <summary>A literal value without expression validation.</summary>
    None,
    /// <summary>Camunda 8 FEEL syntax.</summary>
    Feel,
    /// <summary>Camunda 7 Unified Expression Language, including composite strings.</summary>
    Juel,
}

/// <summary>A syntax diagnostic. Offsets and lengths use UTF-16 code units in the supplied value.</summary>
public sealed record BpmnExpressionDiagnostic(string Message, int Offset, int Length);

/// <summary>Replaceable, server-side expression syntax validation. Validation never evaluates expressions.</summary>
public interface IBpmnExpressionValidator
{
    /// <summary>Validates an expression value. FEEL accepts an optional leading '='; JUEL accepts literal text.</summary>
    public IReadOnlyList<BpmnExpressionDiagnostic> Validate(BpmnExpressionLanguage language, string expression);
}
