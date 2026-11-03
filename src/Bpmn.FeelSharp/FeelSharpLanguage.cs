namespace Hasseware.AspNetCore.Components.Bpmn.FeelSharp;

internal sealed record FeelSharpToken(string Text, string Kind, int Offset)
{
    internal int End => Offset + Text.Length;
}

internal sealed record FeelSharpCompletion(string Label, string Text, string? Detail, int Start, int Length,
    string? Info = null, string? Example = null, string? Note = null, bool HostFunction = false,
    int? PlaceholderStart = null, int PlaceholderLength = 0);

internal sealed class FeelSharpLanguage
{
    private static readonly string[] Keywords =
        ["if", "then", "else", "for", "return", "some", "every", "satisfies", "in", "and", "or", "not", "between",
            "instance of", "function", "true", "false", "null"];
    private static readonly FeelSharpFunction[] FunctionsByLength =
        FeelSharpFunctionCatalog.Functions.OrderByDescending(function => function.Name.Length).ToArray();

    internal static IReadOnlyList<FeelSharpToken> Tokenize(string source)
    {
        var tokens = new List<FeelSharpToken>();
        for (var offset = 0; offset < source.Length;)
        {
            var start = offset;
            var kind = "plain";
            var character = source[offset++];
            if (character == '"' || character == '@' && offset < source.Length && source[offset] == '"')
            {
                kind = character == '@' ? "temporal" : "string";
                if (character == '@')
                {
                    offset++;
                }
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
                if (offset < source.Length && source[offset] is 'e' or 'E')
                {
                    var exponent = offset + 1;
                    if (exponent < source.Length && source[exponent] is '+' or '-')
                    {
                        exponent++;
                    }
                    if (exponent < source.Length && char.IsDigit(source[exponent]))
                    {
                        offset = exponent + 1;
                        while (offset < source.Length && char.IsDigit(source[offset]))
                        {
                            offset++;
                        }
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
                if (source.AsSpan(start).StartsWith("instance of", StringComparison.Ordinal) &&
                    (start + 11 == source.Length || !IsName(source[start + 11])))
                {
                    offset = start + 11;
                    kind = "keyword";
                }
                foreach (var function in FunctionsByLength)
                {
                    if (!source.AsSpan(start).StartsWith(function.Name, StringComparison.Ordinal))
                    {
                        continue;
                    }
                    var next = start + function.Name.Length;
                    while (next < source.Length && char.IsWhiteSpace(source[next]))
                    {
                        next++;
                    }
                    if (next < source.Length && source[next] == '(')
                    {
                        offset = start + function.Name.Length;
                        kind = "function";
                        break;
                    }
                }
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
        IReadOnlyList<BpmnFeelVariable> variables, bool explicitlyRequested = false,
        BpmnFeelSharpEditorOptions? options = null, IReadOnlyList<FeelSharpToken>? tokens = null)
    {
        if (start < 0 || end < start || end > source.Length)
        {
            return [];
        }
        tokens ??= Tokenize(source);
        if (tokens.Any(token => token.Kind is "string" or "comment" or "temporal" && start > token.Offset && start <= token.End))
        {
            return [];
        }
        var candidates = new List<(string Name, string Text, string? Detail, string? Info, string? Example, string? Note)>();
        if (options?.LocalVariables == true)
        {
            AddVariables(FeelSharpAuthoring.Locals(source, start, tokens), "", candidates);
        }
        AddVariables(variables, "", candidates);
        foreach (var function in options?.Functions ?? [])
        {
            candidates.Add((function.Name, function.Name + "()", function.Signature, function.Description, function.Example, "Feel.HostFunction"));
        }
        foreach (var keyword in Keywords)
        {
            candidates.Add((keyword, keyword, "FEEL", null, null, null));
        }
        foreach (var function in FeelSharpFunctionCatalog.Functions)
        {
            candidates.Add((function.Name, function.Name + "()", function.Signature, function.Description, function.Example, function.Note));
        }
        if (options?.Snippets == true)
        {
            candidates.Add(("if expression", "if condition then value else null", "if ... then ... else", "Feel.Snippet.Conditional", null, null));
            candidates.Add(("for expression", "for item in items return item", "for ... in ... return", "Feel.Snippet.Iteration", null, null));
            candidates.Add(("some expression", "some item in items satisfies item != null", "some ... satisfies", "Feel.Snippet.Existential", null, null));
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
        if (prefix.Length == 0 && start == end && !explicitlyRequested)
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
            .OrderBy(candidate => options?.RankCompletions == true
                ? candidate.Text == candidate.Name && candidate.Detail != "FEEL" ? 0 : candidate.Text.EndsWith("()", StringComparison.Ordinal) ? 1 : 2
                : 0)
            .ThenBy(candidate => candidate.Name, StringComparer.Ordinal)
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
                var placeholder = candidate.Name switch
                {
                    "if expression" when candidate.Text == "if condition then value else null" => "condition",
                    "for expression" when candidate.Text == "for item in items return item" => "item",
                    "some expression" when candidate.Text == "some item in items satisfies item != null" => "item",
                    _ => null,
                };
                return new FeelSharpCompletion(candidate.Name,
                    hasArguments && candidate.Text.EndsWith("()", StringComparison.Ordinal) ? candidate.Name : candidate.Text,
                    candidate.Detail, replacementStart, candidateEnd - replacementStart, candidate.Info, candidate.Example, candidate.Note,
                    options?.Functions.Any(function => function.Name == candidate.Name) == true &&
                    !variables.Any(variable => variable.Name == candidate.Name),
                    placeholder is null ? null : candidate.Text.IndexOf(placeholder, StringComparison.Ordinal), placeholder?.Length ?? 0);
            }).ToArray();
    }

    private static void AddVariables(IReadOnlyList<BpmnFeelVariable> variables, string prefix,
        List<(string Name, string Text, string? Detail, string? Info, string? Example, string? Note)> candidates)
    {
        foreach (var variable in variables)
        {
            var name = prefix + variable.Name;
            candidates.Add((name, name, variable.Detail, variable.Info, null, null));
            AddVariables(variable.Entries, name + ".", candidates);
        }
    }

    internal static bool IsName(char character) => char.IsLetterOrDigit(character) || character is '_' or '\'';
}
