using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Resources;
using System.Text;
using System.Text.RegularExpressions;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Resources;
using Microsoft.Extensions.Localization;

namespace Hasseware.AspNetCore.Components.Bpmn.Validation;

/// <summary>Renders validation findings in the current UI culture without changing their invariant message.</summary>
public sealed class BpmnDiagnosticLocalizer(IStringLocalizer<BpmnRuleMessages> localizer)
{
    private static readonly ResourceManager English = new(typeof(BpmnRuleMessages));

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

    /// <summary>Localizes an edit finding from its resource metadata or built-in message, retaining invariant text otherwise.</summary>
    /// <param name="diagnostic">The edit finding to display.</param>
    /// <returns>The localized message, or its invariant English message when no resource is available.</returns>
    [SuppressMessage("Globalization", "CA1305:Specify IFormatProvider", Justification = "Diagnostic presentation follows the requested UI culture, including formatted arguments.")]
    public string Localize(BpmnEditDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);

        var key = diagnostic.MessageResourceKey;
        var arguments = diagnostic.MessageArguments;
        if (key is null)
        {
            (key, arguments) = FindBuiltInEditMessage(diagnostic);
        }

        if (key is null)
        {
            key = diagnostic.Code;
            if (!string.Equals(English.GetString(key, CultureInfo.InvariantCulture), diagnostic.Message, StringComparison.Ordinal))
                return diagnostic.Message;
        }

        var message = localizer[key];
        if (message.ResourceNotFound)
            return diagnostic.Message;

        try
        {
            return string.Format(
                CultureInfo.CurrentUICulture,
                message.Value,
                LocalizeArguments(arguments));
        }
        catch (FormatException)
        {
            // A malformed host translation must not hide an edit finding.
            return diagnostic.Message;
        }
    }

    private object?[] LocalizeArguments(IReadOnlyList<object?>? arguments) =>
        arguments?.Select(argument => argument is BpmnDiagnosticText text ? Localize(text) : argument).ToArray() ?? [];

    [SuppressMessage("Globalization", "CA1305:Specify IFormatProvider", Justification = "Diagnostic presentation follows the requested UI culture, including formatted arguments.")]
    private string Localize(BpmnDiagnosticText text)
    {
        var message = localizer[text.ResourceKey];
        if (message.ResourceNotFound) return text.Message;

        try
        {
            return string.Format(CultureInfo.CurrentUICulture, message.Value, text.Arguments.ToArray());
        }
        catch (FormatException)
        {
            return text.Message;
        }
    }

    private static (string? Key, IReadOnlyList<object?>? Arguments) FindBuiltInEditMessage(BpmnEditDiagnostic diagnostic)
    {
        var resources = English.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: true);
        if (resources is null)
            return (null, null);

        var expectedPrefix = $"EDITMSG.{diagnostic.Code}.";
        var candidates = new List<(string Key, string Template)>();
        var entries = resources.GetEnumerator();
        while (entries.MoveNext())
        {
            if (entries.Current is DictionaryEntry { Key: string key, Value: string message }
                && string.Equals(message, diagnostic.Message, StringComparison.Ordinal))
                return (key, null);

            if (entries.Key is string candidateKey
                && entries.Value is string candidateMessage
                && candidateKey.StartsWith(expectedPrefix, StringComparison.Ordinal))
            {
                if (candidateMessage.Contains('{', StringComparison.Ordinal))
                    candidates.Add((candidateKey, candidateMessage));
            }
        }

        foreach (var (key, template) in candidates.OrderByDescending(candidate => candidate.Template.Length))
        {
            var pattern = new StringBuilder("^");
            var argumentIndexes = new List<int>();
            var position = 0;
            foreach (Match placeholder in Regex.Matches(template, @"\{(?<index>\d+)(?:,[^}:]+)?(?::[^}]+)?\}"))
            {
                pattern.Append(Regex.Escape(template[position..placeholder.Index]));
                pattern.Append("(?<arg").Append(argumentIndexes.Count).Append(@">.+?)");
                argumentIndexes.Add(int.Parse(placeholder.Groups["index"].Value, CultureInfo.InvariantCulture));
                position = placeholder.Index + placeholder.Length;
            }
            pattern.Append(Regex.Escape(template[position..])).Append('$');

            var match = Regex.Match(diagnostic.Message, pattern.ToString(), RegexOptions.CultureInvariant);
            if (!match.Success)
                continue;

            var arguments = new object?[argumentIndexes.Count];
            for (var index = 0; index < argumentIndexes.Count; index++)
                arguments[argumentIndexes[index]] = match.Groups[$"arg{index}"].Value;

            return (key, arguments);
        }

        return (null, null);
    }
}
