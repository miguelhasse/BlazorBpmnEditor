using System.Collections;
using System.Globalization;
using System.Resources;

using Hasseware.AspNetCore.Components.Bpmn.Resources;

using Microsoft.Extensions.Localization;

namespace Hasseware.AspNetCore.Components.Bpmn;

/// <summary>Reads the built-in editor strings for the current UI culture when no host localizer is available.</summary>
internal sealed class BpmnResourceStringLocalizer : IStringLocalizer<BpmnStrings>
{
    private static readonly ResourceManager Resources = new(typeof(BpmnStrings));

    /// <inheritdoc />
    public LocalizedString this[string name]
    {
        get
        {
            var value = Resources.GetString(name, CultureInfo.CurrentUICulture);
            return new LocalizedString(name, value ?? name, value is null);
        }
    }

    /// <inheritdoc />
    public LocalizedString this[string name, params object[] arguments]
    {
        get
        {
            var format = this[name];
            return format.ResourceNotFound
                ? format
                : new LocalizedString(name, string.Format(CultureInfo.CurrentCulture, format.Value, arguments));
        }
    }

    /// <inheritdoc />
    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) =>
        Resources.GetResourceSet(CultureInfo.CurrentUICulture, true, includeParentCultures) is not { } set
            ? []
            : set.Cast<DictionaryEntry>()
                .Select(entry => new LocalizedString((string)entry.Key, entry.Value as string ?? string.Empty));
}
