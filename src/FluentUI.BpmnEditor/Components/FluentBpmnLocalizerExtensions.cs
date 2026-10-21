using Microsoft.Extensions.Localization;

namespace Hasseware.FluentUI.AspNetCore.Components.Bpmn;

internal static class FluentBpmnLocalizerExtensions
{
    public static string EditorText(this IStringLocalizer localizer, string key, params object[] arguments) =>
        arguments.Length == 0 ? localizer[key] : localizer[key, arguments];
}
