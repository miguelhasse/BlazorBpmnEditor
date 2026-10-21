using System.Globalization;
using System.Text;

namespace Hasseware.AspNetCore.Components.Bpmn.RulesGenerator;

/// <summary>
/// A minimal, dependency-free JSON parser for this generator's own use. Source generators load
/// into whatever Roslyn host the consuming SDK ships (netstandard2.0, no built-in JSON support),
/// and a generator-only dependency on System.Text.Json/Newtonsoft.Json is a real packaging
/// footgun (the compiler process must then also load that exact assembly) — for a file this
/// small and this stable (rules/bpmn-rules.json, checked into the repo), a self-contained parser
/// is the more robust choice. It only needs to produce object/array/string/number/bool/null
/// values; the generator itself only reads a handful of flat string fields per rule and skips the
/// nested `predicate` value entirely.
/// </summary>
internal static class MiniJson
{
    public static object? Parse(string json)
    {
        var index = 0;
        var value = ParseValue(json, ref index);
        SkipWhitespace(json, ref index);
        return value;
    }

    private static object? ParseValue(string json, ref int index)
    {
        SkipWhitespace(json, ref index);
        var c = json[index];
        switch (c)
        {
            case '{': return ParseObject(json, ref index);
            case '[': return ParseArray(json, ref index);
            case '"': return ParseString(json, ref index);
            case 't':
                Expect(ref index, "true");
                return true;
            case 'f':
                Expect(ref index, "false");
                return false;
            case 'n':
                Expect(ref index, "null");
                return null;
            default:
                return ParseNumber(json, ref index);
        }
    }

    private static Dictionary<string, object?> ParseObject(string json, ref int index)
    {
        var result = new Dictionary<string, object?>();
        index++; // '{'
        SkipWhitespace(json, ref index);
        if (json[index] == '}')
        {
            index++;
            return result;
        }
        while (true)
        {
            SkipWhitespace(json, ref index);
            var key = ParseString(json, ref index);
            SkipWhitespace(json, ref index);
            index++; // ':'
            var value = ParseValue(json, ref index);
            result[key] = value;
            SkipWhitespace(json, ref index);
            if (json[index] == ',')
            {
                index++;
                continue;
            }
            index++; // '}'
            break;
        }
        return result;
    }

    private static List<object?> ParseArray(string json, ref int index)
    {
        var result = new List<object?>();
        index++; // '['
        SkipWhitespace(json, ref index);
        if (json[index] == ']')
        {
            index++;
            return result;
        }
        while (true)
        {
            var value = ParseValue(json, ref index);
            result.Add(value);
            SkipWhitespace(json, ref index);
            if (json[index] == ',')
            {
                index++;
                continue;
            }
            index++; // ']'
            break;
        }
        return result;
    }

    private static string ParseString(string json, ref int index)
    {
        index++; // opening quote
        var sb = new StringBuilder();
        while (json[index] != '"')
        {
            var c = json[index];
            if (c == '\\')
            {
                index++;
                var escaped = json[index];
                sb.Append(escaped switch
                {
                    'n' => '\n',
                    't' => '\t',
                    'r' => '\r',
                    'b' => '\b',
                    'f' => '\f',
                    '"' => '"',
                    '\\' => '\\',
                    '/' => '/',
                    'u' => ParseUnicodeEscape(json, ref index),
                    _ => escaped,
                });
            }
            else
            {
                sb.Append(c);
            }
            index++;
        }
        index++; // closing quote
        return sb.ToString();
    }

    private static char ParseUnicodeEscape(string json, ref int index)
    {
        var hex = json.Substring(index + 1, 4);
        index += 4;
        return (char)int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    private static double ParseNumber(string json, ref int index)
    {
        var start = index;
        while (index < json.Length && (char.IsDigit(json[index]) || json[index] is '-' or '+' or '.' or 'e' or 'E'))
        {
            index++;
        }
        return double.Parse(json.Substring(start, index - start), CultureInfo.InvariantCulture);
    }

    private static void Expect(ref int index, string literal)
    {
        index += literal.Length;
    }

    private static void SkipWhitespace(string json, ref int index)
    {
        while (index < json.Length && char.IsWhiteSpace(json[index]))
        {
            index++;
        }
    }
}
