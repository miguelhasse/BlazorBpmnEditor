using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Validation;

namespace Hasseware.AspNetCore.Components.Bpmn;

/// <summary>
/// Checks the value typed into an expression-bearing property entry, for entry editors that do not
/// derive from <see cref="BpmnPropertyEntryComponent"/>.
/// </summary>
public static class BpmnExpressionEntryValidation
{
    /// <summary>Validates <paramref name="value"/> as the expression <paramref name="entry"/> declares.</summary>
    /// <param name="entry">The property's metadata, which decides the expression language and FEEL mode.</param>
    /// <param name="value">The value typed by the user.</param>
    /// <param name="validator">The expression validator to check it with.</param>
    /// <returns>A warning to show beside the editor, or <see langword="null"/> when the value is not an expression or is valid.</returns>
    public static string? Error(BpmnPropertyEntry entry, string? value, IBpmnExpressionValidator validator)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(validator);
        if (value is null) return null;
        var language = entry.ExpressionLanguage;
        if (language == BpmnExpressionLanguage.None && entry.Feel is BpmnFeelMode.Optional or BpmnFeelMode.Required)
            language = BpmnExpressionLanguage.Feel;
        if (language == BpmnExpressionLanguage.None) return null;
        if (language == BpmnExpressionLanguage.Feel && (entry.Feel == BpmnFeelMode.Static
            || entry.Feel == BpmnFeelMode.Optional && !value.StartsWith('='))) return null;
        var diagnostics = validator.Validate(language, value);
        return diagnostics.Count == 0 ? null : $"{(language == BpmnExpressionLanguage.Feel ? "FEEL" : "JUEL")} warning: {diagnostics[0].Message}";
    }
}
