using System.Globalization;

namespace Hasseware.AspNetCore.Components.Bpmn.Validation;

internal sealed record BpmnDiagnosticText(string ResourceKey, string Message, IReadOnlyList<object?> Arguments)
{
    internal static BpmnDiagnosticText Create(string resourceKey, string format, params object?[] arguments) =>
        new(resourceKey, string.Format(CultureInfo.CurrentCulture, format, arguments), Array.AsReadOnly(arguments));
}
