using CompletionCandidate = (string Name, string Text, string? Detail, string? Info, string? Example, string? Note);

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
    private static readonly IReadOnlyDictionary<char, FeelSharpFunction[]> FunctionsByInitial =
        FunctionsByLength.GroupBy(function => function.Name[0]).ToDictionary(group => group.Key, group => group.ToArray());
    private static readonly CompletionCandidate[] BuiltInCandidates = Keywords
        .Select(keyword => new CompletionCandidate(keyword, keyword, "FEEL", null, null, null))
        .Concat(FeelSharpFunctionCatalog.Functions.Select(function => new CompletionCandidate(
            function.Name, function.Name + "()", function.Signature, function.Description, function.Example, function.Note)))
        .ToArray();
    private static readonly CompletionCandidate[] SnippetCandidates =
    [
        new("if expression", "if condition then value else null", "if ... then ... else", "Feel.Snippet.Conditional", null, null),
        new("for expression", "for item in items return item", "for ... in ... return", "Feel.Snippet.Iteration", null, null),
        new("some expression", "some item in items satisfies item != null", "some ... satisfies", "Feel.Snippet.Existential", null, null),
    ];

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
                foreach (var function in FunctionsByInitial.GetValueOrDefault(character) ?? [])
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
            else if (char.IsWhiteSpace(character))
            {
                while (offset < source.Length && char.IsWhiteSpace(source[offset])) offset++;
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
        var replacementStart = start;
        while (replacementStart > 0 && (IsName(source[replacementStart - 1]) || source[replacementStart - 1] == '.'))
        {
            replacementStart--;
        }
        var wordStart = replacementStart;
        var candidates = new List<CompletionCandidate>(12);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in Candidates(source, start, tokens, variables, options))
        {
            var candidateStart = candidate.Name.AsSpan().StartsWith(source.AsSpan(wordStart, start - wordStart),
                StringComparison.OrdinalIgnoreCase) ? wordStart : -1;
            // Find the longest matching multiword suffix without allocating a string per suffix/candidate.
            for (var scan = wordStart - 1; scan >= 0 && start - scan <= candidate.Name.Length &&
                (IsName(source[scan]) || source[scan] is ' ' or '.'); scan--)
            {
                if ((scan == 0 || !IsName(source[scan - 1])) && source[scan] != ' ' &&
                    candidate.Name.AsSpan().StartsWith(source.AsSpan(scan, start - scan), StringComparison.OrdinalIgnoreCase))
                {
                    candidateStart = scan;
                }
            }
            if (candidateStart < 0 || candidateStart > replacementStart) continue;
            if (candidateStart < replacementStart)
            {
                replacementStart = candidateStart;
                candidates.Clear();
                names.Clear();
            }
            if (!names.Add(candidate.Name)) continue;
            var rank = Rank(candidate, options?.RankCompletions == true);
            var index = 0;
            while (index < candidates.Count && (Rank(candidates[index], options?.RankCompletions == true) < rank ||
                Rank(candidates[index], options?.RankCompletions == true) == rank &&
                string.CompareOrdinal(candidates[index].Name, candidate.Name) < 0)) index++;
            if (index >= 12) continue;
            candidates.Insert(index, candidate);
            if (candidates.Count > 12) candidates.RemoveAt(12);
        }
        if (replacementStart == start && start == end && !explicitlyRequested)
        {
            return [];
        }
        var replacementEnd = end;
        while (replacementEnd < source.Length && IsName(source[replacementEnd]))
        {
            replacementEnd++;
        }
        return candidates.Select(candidate =>
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

    private static int Rank(CompletionCandidate candidate, bool ranked) => !ranked ? 0 :
        candidate.Text == candidate.Name && candidate.Detail != "FEEL" ? 0 :
        candidate.Text.EndsWith("()", StringComparison.Ordinal) ? 1 : 2;

    private static IEnumerable<CompletionCandidate> Candidates(string source, int caret,
        IReadOnlyList<FeelSharpToken> tokens, IReadOnlyList<BpmnFeelVariable> variables, BpmnFeelSharpEditorOptions? options)
    {
        if (options?.LocalVariables == true)
        {
            foreach (var candidate in VariableCandidates(FeelSharpAuthoring.Locals(source, caret, tokens), "")) yield return candidate;
        }
        foreach (var candidate in VariableCandidates(variables, "")) yield return candidate;
        foreach (var function in options?.Functions ?? [])
            yield return new(function.Name, function.Name + "()", function.Signature, function.Description, function.Example, "Feel.HostFunction");
        foreach (var candidate in BuiltInCandidates) yield return candidate;
        if (options?.Snippets == true)
        {
            foreach (var candidate in SnippetCandidates) yield return candidate;
        }
    }

    private static IEnumerable<CompletionCandidate> VariableCandidates(IReadOnlyList<BpmnFeelVariable> variables, string prefix)
    {
        foreach (var variable in variables)
        {
            var name = prefix + variable.Name;
            yield return new(name, name, variable.Detail, variable.Info, null, null);
            if (variable.Entries.Count > 0)
            {
                foreach (var candidate in VariableCandidates(variable.Entries, name + ".")) yield return candidate;
            }
        }
    }

    internal static bool IsName(char character) => char.IsLetterOrDigit(character) || character is '_' or '\'';
}
