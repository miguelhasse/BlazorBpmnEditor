using Microsoft.Extensions.Localization;

namespace Hasseware.AspNetCore.Components.Bpmn;

internal static class BpmnEditorLocalizerExtensions
{
    public static string EditorText(this IStringLocalizer localizer, string key, params object[] arguments) =>
        arguments.Length == 0 ? localizer[key] : localizer[key, arguments];
}
