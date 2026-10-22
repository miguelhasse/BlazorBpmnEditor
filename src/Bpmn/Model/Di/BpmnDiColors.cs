using System.Buffers;

using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions;

namespace Hasseware.AspNetCore.Components.Bpmn.Model.Di;

/// <summary>MIWG BPMN in Color attributes and bpmn.io compatibility aliases.</summary>
public static class BpmnDiColors
{
    private static readonly SearchValues<char> HexDigits = SearchValues.Create("0123456789abcdefABCDEF");

    /// <summary>The MIWG color namespace.</summary>
    public const string Namespace = "http://www.omg.org/spec/BPMN/non-normative/color/1.0";

    /// <summary>The bpmn.io color namespace.</summary>
    public const string BiocNamespace = "http://bpmn.io/schema/bpmn/biocolor/1.0";

    /// <summary>Whether a value is an opaque RGB hex triplet.</summary>
    public static bool IsValid(string? value) =>
        value is { Length: 7 } && value[0] == '#' && value.AsSpan(1).IndexOfAnyExcept(
            HexDigits) < 0;

    /// <summary>Resolves a valid MIWG color, then its optional compatibility alias.</summary>
    public static string? Resolve(BpmnElementExtras extras, string attribute, string? alias = null)
    {
        if (extras.ForeignAttributes.TryGetValue($"{{{Namespace}}}{attribute}", out var value)
            && IsValid(value)) return value;
        if (alias is not null
            && extras.ForeignAttributes.TryGetValue($"{{{BiocNamespace}}}{alias}", out value)
            && IsValid(value)) return value;
        return null;
    }

    internal static bool IsApplicable(string element, string namespaceName, string attribute) =>
        (element, namespaceName, attribute) switch
        {
            ("BPMNShape", Namespace, "background-color" or "border-color") => true,
            ("BPMNEdge", Namespace, "border-color") => true,
            ("BPMNLabel", Namespace, "color") => true,
            ("BPMNShape", BiocNamespace, "fill" or "stroke") => true,
            ("BPMNEdge", BiocNamespace, "stroke") => true,
            _ => false,
        };

    /// <summary>Sets or removes one MIWG override and removes its legacy alias.</summary>
    internal static BpmnElementExtras Set(BpmnElementExtras extras, string attribute, string? alias, string? value)
    {
        var attributes = new Dictionary<string, string>(extras.ForeignAttributes, StringComparer.Ordinal);
        var key = $"{{{Namespace}}}{attribute}";
        if (value is null) attributes.Remove(key);
        else attributes[key] = value;
        if (alias is not null) attributes.Remove($"{{{BiocNamespace}}}{alias}");
        return extras with { ForeignAttributes = attributes };
    }
}
