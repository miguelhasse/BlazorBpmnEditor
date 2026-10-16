using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Hasseware.AspNetCore.Components.Bpmn.Resources;
using Microsoft.Extensions.Localization;

namespace Hasseware.AspNetCore.Components.Bpmn.Validation;

/// <summary>Renders validation findings in the current UI culture without changing their invariant message.</summary>
public sealed class BpmnDiagnosticLocalizer(IStringLocalizer<BpmnRuleMessages> localizer)
{
    /// <summary>Localizes a finding, retaining its original English message when a resource is unavailable.</summary>
    /// <param name="diagnostic">The finding to display.</param>
    /// <returns>The localized message, including any detail and profile name.</returns>
    [SuppressMessage("Globalization", "CA1305:Specify IFormatProvider", Justification = "Diagnostic presentation follows the requested UI culture, including formatted arguments.")]
    public string Localize(BpmnDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);

        var message = localizer[diagnostic.MessageResourceKey ?? diagnostic.RuleId];
        if (message.ResourceNotFound) return diagnostic.Message;

        try
        {
            var rendered = diagnostic.DetailResourceKey is null
                ? string.Format(CultureInfo.CurrentUICulture, message.Value, diagnostic.MessageArguments?.ToArray() ?? [])
                : message.Value;

            if (diagnostic.MessageDetail is { } detail)
            {
                if (diagnostic.DetailResourceKey is { } key)
                {
                    var translatedDetail = localizer[key];
                    if (!translatedDetail.ResourceNotFound)
                    {
                        detail = string.Format(CultureInfo.CurrentUICulture, translatedDetail.Value,
                            diagnostic.MessageArguments?.ToArray() ?? []);
                    }
                }
                rendered = $"{rendered} {detail}";
            }

            return diagnostic.ProfileName is { } profile ? $"{rendered} ({profile})" : rendered;
        }
        catch (FormatException)
        {
            // A malformed host translation must not hide a validation finding.
            return diagnostic.Message;
        }
    }
}
