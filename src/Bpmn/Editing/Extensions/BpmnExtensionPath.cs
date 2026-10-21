namespace Hasseware.AspNetCore.Components.Bpmn.Editing;

/// <summary>One namespace-qualified element name and optional selector in an extension path.</summary>
/// <param name="NamespaceUri">Namespace URI. Prefixes never participate in identity.</param>
/// <param name="LocalName">Element local name.</param>
/// <param name="Selector">Optional selector within same-name siblings.</param>
public sealed record BpmnExtensionPathStep(
    string NamespaceUri,
    string LocalName,
    BpmnExtensionSelector? Selector = null);

/// <summary>Selects an extension child by ordinal or by an attribute value.</summary>
/// <param name="Ordinal">Zero-based ordinal among same-name siblings.</param>
/// <param name="AttributeName">Attribute key, plain local name or expanded <c>{uri}local</c> name.</param>
/// <param name="AttributeValue">Required exact attribute value for a keyed selector.</param>
public sealed record BpmnExtensionSelector(
    int? Ordinal = null,
    string? AttributeName = null,
    string? AttributeValue = null);

/// <summary>A parsed extension path addressed by namespace URI and local name.</summary>
/// <remarks>
/// A path consists of slash-separated <c>{uri}localName</c> steps, each optionally followed by
/// <c>[n]</c> or <c>[attribute=value]</c>. A terminal <c>@attribute</c> addresses an attribute;
/// <c>#text</c> addresses text. Selector values escape backslash, closing bracket, slash and equals
/// with a backslash. Namespace URIs are delimited by braces and are not escaped.
/// </remarks>
/// <param name="Steps">Element steps, from extension root to target.</param>
/// <param name="AttributeName">Terminal attribute name, or null.</param>
/// <param name="IsText">Whether the terminal target is element text.</param>
public sealed record BpmnExtensionPath(
    IReadOnlyList<BpmnExtensionPathStep> Steps,
    string? AttributeName = null,
    bool IsText = false)
{
    /// <summary>Parses an expanded-name extension path.</summary>
    /// <param name="path">Path to parse.</param>
    /// <exception cref="FormatException">Path does not follow the extension path syntax.</exception>
    public static BpmnExtensionPath Parse(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!TryParse(path, out var parsed)) throw new FormatException($"Invalid extension path '{path}'.");
        return parsed!;
    }

    /// <summary>Tries to parse an expanded-name extension path.</summary>
    /// <param name="path">Path to parse.</param>
    /// <param name="result">Parsed path when successful.</param>
    /// <returns>True when path is valid.</returns>
    public static bool TryParse(string? path, out BpmnExtensionPath? result)
    {
        result = null;
        if (string.IsNullOrEmpty(path)) return false;

        var segments = Split(path);
        if (segments is null || segments.Count == 0) return false;
        string? attribute = null;
        var isText = false;
        var last = segments[^1];
        if (last == "#text")
        {
            isText = true;
            segments.RemoveAt(segments.Count - 1);
        }
        else if (last.StartsWith('@'))
        {
            attribute = last[1..];
            if (!ValidAttributeName(attribute)) return false;
            segments.RemoveAt(segments.Count - 1);
        }

        if (segments.Count == 0) return false;
        var steps = new List<BpmnExtensionPathStep>(segments.Count);
        foreach (var segment in segments)
        {
            if (!TryStep(segment, out var step)) return false;
            steps.Add(step!);
        }

        result = new BpmnExtensionPath(steps, attribute, isText);
        return true;
    }

    /// <inheritdoc />
    public override string ToString()
    {
        var path = string.Join('/', Steps.Select(FormatStep));
        if (AttributeName is not null) return path + "@" + AttributeName;
        if (IsText) return path + "#text";
        return path;
    }

    private static List<string>? Split(string path)
    {
        var segments = new List<string>();
        var start = 0;
        var braces = 0;
        var brackets = 0;
        var escaped = false;
        for (var i = 0; i < path.Length; i++)
        {
            var c = path[i];
            if (escaped) { escaped = false; continue; }
            if (brackets > 0 && c == '\\') { escaped = true; continue; }
            if (c == '{') braces++;
            else if (c == '}') { if (--braces < 0) return null; }
            else if (braces == 0 && c == '[') brackets++;
            else if (braces == 0 && c == ']') { if (--brackets < 0) return null; }
            else if (c == '/' && braces == 0 && brackets == 0)
            {
                if (i == start) return null;
                segments.Add(path[start..i]);
                start = i + 1;
            }
        }
        if (braces != 0 || brackets != 0 || escaped || start == path.Length) return null;
        segments.Add(path[start..]);

        // A terminal marker belongs to its own final segment. Markers inside expanded names and
        // selectors are ordinary characters.
        var final = segments[^1];
        var marker = -1;
        braces = brackets = 0;
        escaped = false;
        for (var i = 0; i < final.Length; i++)
        {
            var c = final[i];
            if (escaped) { escaped = false; continue; }
            if (brackets > 0 && c == '\\') { escaped = true; continue; }
            if (c == '{') braces++;
            else if (c == '}') braces--;
            else if (braces == 0 && c == '[') brackets++;
            else if (braces == 0 && c == ']') brackets--;
            else if (braces == 0 && brackets == 0 && (c == '@' || c == '#')) marker = i;
        }
        if (marker >= 0)
        {
            var tail = final[marker..];
            if (tail != "#text" && !tail.StartsWith('@')) return null;
            if (marker == 0) return segments;
            segments[^1] = final[..marker];
            segments.Add(tail);
        }
        return segments;
    }

    private static bool TryStep(string segment, out BpmnExtensionPathStep? step)
    {
        step = null;
        if (segment.Length < 3 || segment[0] != '{') return false;
        var close = segment.IndexOf('}');
        if (close < 0 || close == segment.Length - 1) return false;
        var uri = segment[1..close];
        var rest = segment[(close + 1)..];
        var selectorStart = rest.IndexOf('[');
        string local;
        BpmnExtensionSelector? selector = null;
        if (selectorStart < 0)
        {
            local = rest;
        }
        else
        {
            var selectorEnd = FindSelectorEnd(rest, selectorStart);
            if (selectorEnd != rest.Length - 1) return false;
            local = rest[..selectorStart];
            var contents = rest[(selectorStart + 1)..selectorEnd];
            if (!TrySelector(contents, out selector)) return false;
        }
        if (local.Length == 0 || local.Contains('/') || local.Contains('@') || local.Contains('#')) return false;
        try { System.Xml.XmlConvert.VerifyNCName(local); }
        catch (System.Xml.XmlException) { return false; }
        step = new BpmnExtensionPathStep(uri, local, selector);
        return true;
    }

    private static int FindSelectorEnd(string value, int start)
    {
        var escaped = false;
        for (var i = start + 1; i < value.Length; i++)
        {
            if (escaped) { escaped = false; continue; }
            if (value[i] == '\\') { escaped = true; continue; }
            if (value[i] == ']') return i;
        }
        return -1;
    }

    private static bool TrySelector(string text, out BpmnExtensionSelector? selector)
    {
        selector = null;
        if (text.Length == 0) return false;
        if (text.All(char.IsAsciiDigit))
        {
            if (!int.TryParse(text, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var ordinal)) return false;
            selector = new BpmnExtensionSelector(Ordinal: ordinal);
            return true;
        }
        var equals = UnescapedEquals(text);
        if (equals <= 0) return false;
        string name;
        string value;
        try
        {
            name = Unescape(text[..equals]);
            value = Unescape(text[(equals + 1)..]);
        }
        catch (FormatException)
        {
            return false;
        }
        if (!ValidAttributeName(name)) return false;
        selector = new BpmnExtensionSelector(AttributeName: name, AttributeValue: value);
        return true;
    }

    private static int UnescapedEquals(string value)
    {
        var escaped = false;
        for (var i = 0; i < value.Length; i++)
        {
            if (escaped) { escaped = false; continue; }
            if (value[i] == '\\') { escaped = true; continue; }
            if (value[i] == '=') return i;
        }
        return -1;
    }

    private static string Unescape(string value)
    {
        var result = new System.Text.StringBuilder(value.Length);
        var escaped = false;
        foreach (var c in value)
        {
            if (escaped)
            {
                if (c is not (']' or '/' or '=' or '\\')) throw new FormatException("Invalid selector escape.");
                result.Append(c);
                escaped = false;
            }
            else if (c == '\\') escaped = true;
            else result.Append(c);
        }
        if (escaped) throw new FormatException("Trailing selector escape.");
        return result.ToString();
    }

    private static bool ValidAttributeName(string value)
    {
        if (value.Length == 0 || value.Any(char.IsWhiteSpace) || value.Contains('=')) return false;
        if (value[0] == '{')
        {
            var close = value.IndexOf('}');
            if (close < 0 || close == value.Length - 1) return false;
            try { System.Xml.XmlConvert.VerifyNCName(value[(close + 1)..]); }
            catch (System.Xml.XmlException) { return false; }
            return true;
        }
        if (value.Contains('/')) return false;
        try { System.Xml.XmlConvert.VerifyNCName(value); return true; }
        catch (System.Xml.XmlException) { return false; }
    }

    private static string FormatStep(BpmnExtensionPathStep step)
    {
        var value = $"{{{step.NamespaceUri}}}{step.LocalName}";
        if (step.Selector is { Ordinal: { } ordinal }) return value + $"[{ordinal}]";
        if (step.Selector is { AttributeName: { } name, AttributeValue: { } selectorValue })
            return value + $"[{Escape(name)}={Escape(selectorValue)}]";
        return value;
    }

    private static string Escape(string value)
    {
        var result = new System.Text.StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (c is ']' or '/' or '=' or '\\') result.Append('\\');
            result.Append(c);
        }
        return result.ToString();
    }
}
