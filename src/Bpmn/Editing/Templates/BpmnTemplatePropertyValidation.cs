using System.Text.RegularExpressions;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions.Templates;
using Hasseware.AspNetCore.Components.Bpmn.Validation;

namespace Hasseware.AspNetCore.Components.Bpmn.Editing;

/// <summary>Shared inline and profile validation of stored template property values.</summary>
public static class BpmnTemplatePropertyValidation
{
    /// <summary>Returns constraint and required-FEEL failures. Regex execution is bounded.</summary>
    public static IReadOnlyList<string> Errors(BpmnTemplateProperty property, string? value)
        => [.. LocalizedErrors(property, value).Select(error => error.Message)];

    internal static IReadOnlyList<BpmnDiagnosticText> LocalizedErrors(BpmnTemplateProperty property, string? value)
    {
        var errors = new List<BpmnDiagnosticText>();
        var label = property.Label ?? property.Id ?? "Value";
        var constraints = property.Constraints;
        if (constraints?.NotEmpty == true && string.IsNullOrWhiteSpace(value)) errors.Add(BpmnDiagnosticText.Create("PROF-101.Detail", "{0} must not be empty.", label));
        if (constraints?.MinLength is { } min && (value?.Length ?? 0) < min) errors.Add(BpmnDiagnosticText.Create("PROF-101.Detail.2", "{0} must contain at least {1} characters.", label, min));
        if (constraints?.MaxLength is { } max && (value?.Length ?? 0) > max) errors.Add(BpmnDiagnosticText.Create("PROF-101.Detail.3", "{0} must contain at most {1} characters.", label, max));
        if (constraints?.Pattern is { } pattern && !string.IsNullOrEmpty(value))
        {
            try
            {
                if (!Regex.IsMatch(value, pattern.Value, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
                    errors.Add(pattern.Message is { } custom
                        ? BpmnDiagnosticText.Create("PROF-101.Detail.Custom", "{0}", custom)
                        : BpmnDiagnosticText.Create("PROF-101.Detail.Pattern", "{0} does not match the required pattern.", label));
            }
            catch (Exception exception) when (exception is ArgumentException or RegexMatchTimeoutException)
            {
                errors.Add(BpmnDiagnosticText.Create("PROF-101.Detail.4", "{0} has an invalid or excessively complex pattern.", label));
            }
        }
        if (property.Feel == BpmnTemplateFeel.Required && value?.StartsWith('=') != true)
            errors.Add(BpmnDiagnosticText.Create("PROF-102.Detail", "{0} requires a FEEL expression starting with '='.", label));
        return errors;
    }
}
