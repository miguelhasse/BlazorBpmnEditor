namespace Hasseware.AspNetCore.Components.Bpmn.FeelSharp;

internal sealed record FeelSharpSignature(string Signature, int Parameter);

internal sealed class FeelSharpAuthoring
{
    internal static FeelSharpSignature? Signature(string source, int caret, IReadOnlyList<FeelSharpToken> tokens,
        IReadOnlyList<BpmnFeelSharpFunction> custom)
    {
        var stack = new Stack<(char Open, int Offset, int Parameter)>();
        foreach (var token in tokens)
        {
            if (token.Offset >= caret)
            {
                break;
            }
            if (token.Kind is "string" or "comment" or "temporal")
            {
                continue;
            }
            for (var index = 0; index < token.Text.Length && token.Offset + index < caret; index++)
            {
                var character = token.Text[index];
                if (character is '(' or '[' or '{')
                {
                    stack.Push((character, token.Offset + index, 0));
                }
                else if (character is ')' or ']' or '}')
                {
                    if (stack.Count == 0 || !Matches(stack.Pop().Open, character))
                    {
                        return null;
                    }
                }
                else if (character == ',' && stack.TryPop(out var frame))
                {
                    stack.Push(frame with { Parameter = frame.Parameter + 1 });
                }
            }
        }
        foreach (var frame in stack.Where(frame => frame.Open == '('))
        {
            var prefix = source[..frame.Offset].TrimEnd();
            var functions = custom.Select(function => (function.Name, function.Signature))
                .Concat(FeelSharpFunctionCatalog.Functions.Select(function => (function.Name, function.Signature)));
            var function = functions.OrderByDescending(function => function.Name.Length).FirstOrDefault(function =>
                prefix.EndsWith(function.Name, StringComparison.Ordinal) &&
                (prefix.Length == function.Name.Length || !FeelSharpLanguage.IsName(prefix[prefix.Length - function.Name.Length - 1])));
            if (function.Name is not null)
            {
                return new(function.Signature, frame.Parameter + 1);
            }
        }
        return null;
    }

    internal static IReadOnlyList<BpmnFeelVariable> Locals(string source, int caret, IReadOnlyList<FeelSharpToken> tokens)
    {
        var variables = new Dictionary<string, BpmnFeelVariable>(StringComparer.Ordinal);
        var visible = tokens.Where(token => token.Offset < caret && token.Kind is not ("comment" or "string" or "temporal"))
            .Where(token => !string.IsNullOrWhiteSpace(token.Text)).ToArray();
        var scopes = new Stack<int>();
        var owners = new int[visible.Length];
        var closed = new HashSet<int>();
        for (var index = 0; index < visible.Length; index++)
        {
            owners[index] = scopes.TryPeek(out var owner) ? owner : -1;
            if (visible[index].Text is "(" or "[" or "{")
            {
                scopes.Push(index);
            }
            else if (visible[index].Text is ")" or "]" or "}")
            {
                if (scopes.TryPop(out var scope) && Matches(visible[scope].Text[0], visible[index].Text[0]))
                {
                    closed.Add(scope);
                }
                else
                {
                    return [];
                }
            }
        }
        void Add(string name)
        {
            if (name.Length > 0)
            {
                variables[name] = new(name, "unknown", "Lexically inferred binding; type unknown.")
                { Source = "FEEL local binding", IsInferred = true };
            }
        }
        int Next(int start, int owner, string text)
        {
            for (var item = start; item < visible.Length; item++)
            {
                if (owners[item] == owner && visible[item].Text == text)
                {
                    return item;
                }
            }
            return -1;
        }
        for (var index = 0; index + 3 < visible.Length; index++)
        {
            if (closed.Contains(owners[index]))
            {
                continue;
            }
            if (visible[index].Text is "for" or "some" or "every")
            {
                var binding = index + 1;
                var body = Next(binding, owners[index], visible[index].Text == "for" ? "return" : "satisfies");
                if (body < 0 || Next(body + 1, owners[index], ",") >= 0)
                {
                    continue;
                }
                while (binding < body)
                {
                    var inIndex = Next(binding, owners[index], "in");
                    if (inIndex < binding || inIndex >= body)
                    {
                        break;
                    }
                    if (visible[binding..inIndex].All(token => token.Kind == "name"))
                    {
                        Add(source[visible[binding].Offset..visible[inIndex].Offset].Trim());
                    }
                    var comma = Next(inIndex + 1, owners[index], ",");
                    if (comma < 0 || comma >= body)
                    {
                        break;
                    }
                    binding = comma + 1;
                }
            }
            else if (visible[index].Text == "function" && visible[index + 1].Text == "(")
            {
                var end = Array.FindIndex(visible, index + 2, token => token.Text == ")");
                if (end > index + 2 && end < visible.Length - 1 && Next(end + 1, owners[index], ",") < 0)
                {
                    foreach (var parameter in source[visible[index + 1].End..visible[end].Offset].Split(','))
                    {
                        var name = parameter.Trim();
                        if (name.All(character => char.IsLetterOrDigit(character) || character is ' ' or '_' or '\''))
                        {
                            Add(name);
                        }
                    }
                }
            }
            else if (visible[index].Text == "{" && !closed.Contains(index))
            {
                var keyStart = index + 1;
                for (var item = keyStart; item + 1 < visible.Length; item++)
                {
                    if (owners[item] != index)
                    {
                        continue;
                    }
                    if (visible[item].Text == ":")
                    {
                        var end = Next(item + 1, index, ",");
                        if (end > item && visible[keyStart..item].All(token => token.Kind == "name"))
                        {
                            Add(source[visible[keyStart].Offset..visible[item].Offset].Trim());
                            keyStart = end + 1;
                        }
                    }
                }
            }
        }
        return variables.Values.ToArray();
    }

    internal static bool Matches(char open, char close) =>
        open == '(' && close == ')' || open == '[' && close == ']' || open == '{' && close == '}';
}
