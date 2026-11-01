namespace Hasseware.AspNetCore.Components.Bpmn.FeelSharp;

internal sealed record FeelSharpToken(string Text, string Kind, int Offset)
{
    internal int End => Offset + Text.Length;
}

internal sealed record FeelSharpCompletion(string Label, string Text, string? Detail, int Start, int Length);

internal sealed class FeelSharpLanguage
{
    private static readonly string[] Keywords =
        ["if", "then", "else", "for", "return", "some", "every", "satisfies", "in", "and", "or", "not", "between",
            "instance of", "function", "true", "false", "null"];
    private static readonly string[] Functions =
        ["abs", "append", "ceiling", "concatenate", "contains", "count", "date", "date and time", "day of week",
            "day of year", "distinct values", "duration", "ends with", "floor", "get entries", "get value",
            "index of", "insert before", "list contains", "lower case", "max", "mean", "min", "now", "number",
            "reverse", "round", "sort", "split", "starts with", "string", "string join", "string length",
            "substring", "sum", "time", "today", "upper case"];

    internal static IReadOnlyList<FeelSharpToken> Tokenize(string source)
    {
        var tokens = new List<FeelSharpToken>();
        for (var offset = 0; offset < source.Length;)
        {
            var start = offset;
            var kind = "plain";
            var character = source[offset++];
            if (character == '"')
            {
                kind = "string";
                while (offset < source.Length)
                {
                    var next = source[offset++];
                    if (next == '\\' && offset < source.Length)
                    {
                        offset++;
                    }
                    else if (next == '"')
                    {
                        break;
                    }
                }
            }
            else if (character == '/' && offset < source.Length && source[offset] is '/' or '*')
            {
                kind = "comment";
                var block = source[offset++] == '*';
                while (offset < source.Length)
                {
                    if (!block && source[offset] is '\r' or '\n')
                    {
                        break;
                    }
                    if (block && source[offset] == '*' && offset + 1 < source.Length && source[offset + 1] == '/')
                    {
                        offset += 2;
                        break;
                    }
                    offset++;
                }
            }
            else if (char.IsDigit(character))
            {
                kind = "number";
                while (offset < source.Length && char.IsDigit(source[offset]))
                {
                    offset++;
                }
                if (offset + 1 < source.Length && source[offset] == '.' && char.IsDigit(source[offset + 1]))
                {
                    offset++;
                    while (offset < source.Length && char.IsDigit(source[offset]))
                    {
                        offset++;
                    }
                }
            }
            else if (IsName(character))
            {
                while (offset < source.Length && IsName(source[offset]))
                {
                    offset++;
                }
                var word = source[start..offset];
                kind = Keywords.Contains(word, StringComparer.Ordinal) ? "keyword" : "name";
            }
            else if ("+-*/=<>!.,:()[]{}".Contains(character, StringComparison.Ordinal))
            {
                kind = "operator";
            }
            tokens.Add(new(source[start..offset], kind, start));
        }
        return tokens;
    }

    internal static IReadOnlyList<FeelSharpCompletion> Complete(string source, int start, int end,
        IReadOnlyList<BpmnFeelVariable> variables)
    {
        if (start < 0 || end < start || end > source.Length)
        {
            return [];
        }
        if (Tokenize(source).Any(token => token.Kind is "string" or "comment" && start > token.Offset && start <= token.End))
        {
            return [];
        }
        var candidates = new List<(string Name, string Text, string? Detail)>();
        AddVariables(variables, "", candidates);
        foreach (var keyword in Keywords)
        {
            candidates.Add((keyword, keyword, "FEEL"));
        }
        foreach (var function in Functions)
        {
            candidates.Add((function, function + "()", "FEEL"));
        }
        var replacementStart = start;
        while (replacementStart > 0 && (IsName(source[replacementStart - 1]) || source[replacementStart - 1] == '.'))
        {
            replacementStart--;
        }
        var prefix = source[replacementStart..start];
        // Prefer a longer suffix when it completes a FEEL name containing spaces.
        var scan = replacementStart;
        while (scan > 0 && (IsName(source[scan - 1]) || source[scan - 1] is ' ' or '.'))
        {
            scan--;
            var suffix = source[scan..start];
            if ((scan == 0 || !IsName(source[scan - 1])) && !suffix.StartsWith(' ') &&
                candidates.Any(candidate => candidate.Name.StartsWith(suffix, StringComparison.OrdinalIgnoreCase)))
            {
                replacementStart = scan;
                prefix = suffix;
            }
        }
        if (prefix.Length == 0 && start == end)
        {
            return [];
        }
        var replacementEnd = end;
        while (replacementEnd < source.Length && IsName(source[replacementEnd]))
        {
            replacementEnd++;
        }
        return candidates.Where(candidate => candidate.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .DistinctBy(candidate => candidate.Name, StringComparer.Ordinal)
            .OrderBy(candidate => candidate.Name, StringComparer.Ordinal)
            .Take(12)
            .Select(candidate =>
            {
                var candidateEnd = replacementEnd;
                var nameEnd = replacementStart + candidate.Name.Length;
                if (nameEnd > candidateEnd && nameEnd <= source.Length &&
                    source.AsSpan(replacementStart, candidate.Name.Length)
                        .Equals(candidate.Name.AsSpan(), StringComparison.OrdinalIgnoreCase) &&
                    (nameEnd == source.Length || !IsName(source[nameEnd])))
                {
                    candidateEnd = nameEnd;
                }
                var next = candidateEnd;
                while (next < source.Length && char.IsWhiteSpace(source[next]))
                {
                    next++;
                }
                var hasArguments = next < source.Length && source[next] == '(';
                return new FeelSharpCompletion(candidate.Name,
                    hasArguments && candidate.Text.EndsWith("()", StringComparison.Ordinal) ? candidate.Name : candidate.Text,
                    candidate.Detail, replacementStart, candidateEnd - replacementStart);
            }).ToArray();
    }

    private static void AddVariables(IReadOnlyList<BpmnFeelVariable> variables, string prefix,
        List<(string Name, string Text, string? Detail)> candidates)
    {
        foreach (var variable in variables)
        {
            var name = prefix + variable.Name;
            candidates.Add((name, name, variable.Detail ?? variable.Info));
            AddVariables(variable.Entries, name + ".", candidates);
        }
    }

    private static bool IsName(char character) => char.IsLetterOrDigit(character) || character is '_' or '\'';
}
