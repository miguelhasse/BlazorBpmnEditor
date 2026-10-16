namespace Hasseware.AspNetCore.Components.Bpmn.Validation;

/// <summary>Pure managed FEEL and JUEL syntax validation; does not resolve names, types, or engine functions.</summary>
public sealed class BpmnExpressionValidator : IBpmnExpressionValidator
{
    /// <inheritdoc />
    public IReadOnlyList<BpmnExpressionDiagnostic> Validate(BpmnExpressionLanguage language, string expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        try
        {
            if (language == BpmnExpressionLanguage.Feel)
            {
                var start = expression.StartsWith('=') ? 1 : 0;
                new Parser(expression[start..], start, true).Parse();
            }
            else if (language == BpmnExpressionLanguage.Juel) ValidateJuel(expression);
            return [];
        }
        catch (SyntaxFailure error)
        {
            return [new(error.Message, error.Offset, error.Length)];
        }
    }

    private static void ValidateJuel(string source)
    {
        for (var index = 0; index < source.Length; index++)
        {
            if (source[index] == '\\') { index++; continue; }
            if (source[index] is not ('$' or '#') || index + 1 >= source.Length || source[index + 1] != '{') continue;
            var start = index + 2;
            var quote = '\0';
            index = start;
            for (; index < source.Length; index++)
            {
                var character = source[index];
                if (quote != '\0')
                {
                    if (character == '\\') index++;
                    else if (character == quote) quote = '\0';
                }
                else if (character is '\'' or '"') quote = character;
                else if (character == '}') break;
            }
            if (index >= source.Length) throw new SyntaxFailure("Unterminated JUEL expression; expected '}'.", start - 2, 2);
            new Parser(source[start..index], start, false).Parse();
        }
    }

    private sealed class SyntaxFailure(string message, int offset, int length) : Exception(message)
    {
        public int Offset { get; } = offset;
        public int Length { get; } = length;
    }

    private readonly record struct Token(string Text, int Offset, int Length, bool IsName = false, bool IsLiteral = false);

    private sealed class Parser
    {
        private readonly List<Token> _tokens = [];
        private readonly bool _feel;
        private readonly int _baseOffset;
        private int _position;
        private int _depth;
        private bool _intervalEnd;
        private Token Current => _tokens[_position];
        private string Text => Current.Text;
        private static readonly string[] ReservedNames = ["date and time", "years and months duration", "days and time duration", "get or else"];
        private static readonly HashSet<string> FeelKeywords = new(StringComparer.Ordinal)
        { "if", "then", "else", "for", "return", "some", "every", "satisfies", "in", "between", "and", "or", "not", "instance", "of", "function" };

        public Parser(string source, int offset, bool feel)
        {
            _feel = feel;
            _baseOffset = offset;
            Lex(source);
        }

        public void Parse()
        {
            Expression();
            while (_feel && Take(",")) Expression(); // FEEL unary-test alternatives.
            Expect("");
        }

        private bool Expression(int minimum = 0)
        {
            if (++_depth > 128) Fail("Expression nesting exceeds the syntax validator's limit (128).");
            var range = false;
            try
            {
                Prefix(minimum);
                Postfix();
                while (true)
                {
                    var operation = Text;
                    var precedence = Precedence(operation);
                    if (precedence < minimum) break;
                    _position++;
                    range |= operation == "..";
                    if (_feel && operation == "between")
                    {
                        Expression(precedence + 1);
                        Expect("and");
                        Expression(precedence + 1);
                    }
                    else if (_feel && operation == "instance")
                    {
                        Expect("of");
                        TypeName();
                    }
                    else Expression(precedence + (operation == "**" ? 0 : 1));
                }
                if (!_feel && minimum == 0 && Take("?"))
                {
                    Expression(); Expect(":"); Expression();
                }
                return range;
            }
            finally { _depth--; }
        }

        private int Precedence(string operation) => operation switch
        {
            "or" => 1,
            "||" when !_feel => 1,
            "and" => 2,
            "&&" when !_feel => 2,
            "=" when _feel => 3,
            "==" or "eq" or "ne" when !_feel => 3,
            "!=" or "<" or ">" or "<=" or ">=" => 3,
            "lt" or "gt" or "le" or "ge" when !_feel => 3,
            "in" or "between" or "instance" when _feel => 3,
            ".." when _feel => 4,
            "+" or "-" => 5,
            "*" or "/" => 6,
            "%" or "div" or "mod" when !_feel => 6,
            "**" when _feel => 7,
            _ => -1,
        };

        private void Prefix(int minimum)
        {
            if (_feel && Take("not"))
            {
                if (Take("(")) Arguments();
                else Expression(7);
                return;
            }
            if (Take("+") || Take("-") || (!_feel && (Take("not") || Take("!") || Take("empty"))))
            {
                if (_feel && minimum == 0 && _tokens[_position - 1].Text == "-" && Text is "" or "," or ")") return;
                Expression(7); return;
            }
            if (_feel && minimum == 0 && Text is "<" or ">" or "<=" or ">=" or "!=") { _position++; Expression(4); return; }
            if (_feel && Take("if"))
            {
                Expression(); Expect("then"); Expression(); Expect("else"); Expression(); return;
            }
            if (_feel && Text is "for" or "some" or "every")
            {
                var keyword = Text; _position++;
                do { Name(); Expect("in"); Expression(4); } while (Take(","));
                Expect(keyword == "for" ? "return" : "satisfies"); Expression(); return;
            }
            if (_feel && Take("function"))
            {
                Expect("(");
                if (!Take(")"))
                {
                    do { Name(); if (Take(":")) TypeName(); } while (Take(","));
                    Expect(")");
                }
                Expression(); return;
            }
            if (_feel && Take("@"))
            {
                if (!Current.IsLiteral || !Text.StartsWith('"')) Fail("Expected a quoted temporal literal after '@'.");
                _position++; return;
            }
            if (Text == "(" || _feel && Text is "[" or "]")
            {
                var opening = Text; _position++;
                var closing = opening == "(" ? ")" : "]";
                if (opening == "[" && Take("]")) return;
                var previous = _intervalEnd;
                _intervalEnd = _feel;
                bool range;
                try { range = Expression(); } finally { _intervalEnd = previous; }
                if (_feel && range)
                {
                    if (Text is not ("]" or "[" or ")")) Fail("Expected a closing interval delimiter.");
                    _position++; return;
                }
                if (opening == "]") Fail("Expected a range and closing interval delimiter.");
                if (_feel) while (Take(",")) Expression();
                Expect(closing); return;
            }
            if (_feel && Take("{"))
            {
                if (Take("}")) return;
                do
                {
                    if (Current.IsLiteral && Text.StartsWith('"')) _position++;
                    else KeyName();
                    Expect(":"); Expression();
                } while (Take(","));
                Expect("}"); return;
            }
            if (Current.IsLiteral) { _position++; return; }
            if (Current.IsName && (_feel ? !FeelKeywords.Contains(Text) : Text is not ("and" or "or" or "eq" or "ne" or "lt" or "gt" or "le" or "ge" or "div" or "mod")))
            {
                Name();
                if (!_feel && Text == ":" && _position + 2 < _tokens.Count && _tokens[_position + 1].IsName && _tokens[_position + 2].Text == "(")
                { _position++; Name(); Expect("("); Arguments(); }
                return;
            }
            Fail("Expected an expression.");
        }

        private void Postfix()
        {
            while (true)
            {
                if (Take(".")) PropertyName();
                else if (Text == "[" && (!_intervalEnd || !IsIntervalClose())) { _position++; Expression(); Expect("]"); }
                else if (Take("(")) Arguments();
                else break;
            }
        }

        private bool IsIntervalClose() => _tokens[_position + 1].Text is "" or "," or ")" or "]" or "}" or "."
            or "and" or "or" or "then" or "else" or "return" or "satisfies" || Precedence(_tokens[_position + 1].Text) >= 0;

        private void Arguments()
        {
            if (Take(")")) return;
            do
            {
                if (_feel)
                {
                    var saved = _position;
                    if (Current.IsName)
                    {
                        Name();
                        if (Take(":")) { Expression(); continue; }
                        _position = saved;
                    }
                }
                Expression();
            } while (Take(","));
            Expect(")");
        }

        private void TypeName()
        {
            if (++_depth > 128) Fail("Expression nesting exceeds the syntax validator's limit (128).");
            try
            {
                if (_feel && Take("function"))
                {
                    // function<T1, T2> -> R
                    if (Take("<") && !Take(">"))
                    {
                        do TypeName(); while (Take(","));
                        Expect(">");
                    }
                    Expect("-"); Expect(">");
                    TypeName();
                    return;
                }
                var context = _feel && Text == "context";
                Name();
                if (!Take("<")) return;
                if (context)
                {
                    // context<k1: T1, k2: T2>
                    do { KeyName(); Expect(":"); TypeName(); } while (Take(","));
                }
                else
                {
                    do TypeName(); while (_feel && Take(","));
                }
                Expect(">");
            }
            finally { _depth--; }
        }

        private bool IsWord => Current.IsName || Current.IsLiteral && Text is "true" or "false" or "null";

        // Context keys may be any name, including FEEL keywords and literal words ({if: 1, true: 2}).
        private void KeyName()
        {
            if (!IsWord) Fail("Expected a name.");
            _position++;
            if (_feel)
                while (IsWord && Text != ":") _position++;
        }

        // Property access may name any context key ({if: 1}.if); later words must not be keywords so
        // that "x.a and y" still parses as a conjunction.
        private void PropertyName()
        {
            if (!_feel) { Name(); return; }
            if (!IsWord) Fail("Expected a name.");
            _position++;
            while (Current.IsName && !FeelKeywords.Contains(Text)) _position++;
        }

        private void Name()
        {
            if (!Current.IsName || _feel && FeelKeywords.Contains(Text)) Fail("Expected a name.");
            _position++;
            if (_feel)
                while (Current.IsName && !FeelKeywords.Contains(Text)) _position++;
        }

        private bool Take(string text)
        {
            if (Text != text) return false;
            _position++; return true;
        }

        private void Expect(string text)
        {
            if (Text != text) Fail(text.Length == 0 ? $"Unexpected token '{Text}'." : $"Expected '{text}'.");
            if (text.Length != 0) _position++;
        }

        private void Fail(string message) => throw new SyntaxFailure(message, Current.Offset + _baseOffset, Current.Length);

        private void Lex(string source)
        {
            for (var index = 0; index < source.Length;)
            {
                var start = index;
                var character = source[index];
                if (char.IsWhiteSpace(character)) { index++; continue; }
                if (_feel && index + 1 < source.Length && source[index..(index + 2)] == "//")
                { while (index < source.Length && source[index] != '\n') index++; continue; }
                if (_feel && index + 1 < source.Length && source[index..(index + 2)] == "/*")
                {
                    var end = source.IndexOf("*/", index + 2, StringComparison.Ordinal);
                    if (end < 0) throw new SyntaxFailure("Unterminated comment.", start + _baseOffset, 2);
                    index = end + 2; continue;
                }
                if (character == '"' || !_feel && character == '\'')
                {
                    index++;
                    var closed = false;
                    while (index < source.Length)
                    {
                        if (source[index] == character) { index++; closed = true; break; }
                        if (source[index] is '\n' or '\r' && _feel) throw new SyntaxFailure("Unescaped newline in string.", index + _baseOffset, 1);
                        if (source[index++] != '\\') continue;
                        if (index >= source.Length) break;
                        var escape = source[index++];
                        if (_feel && escape == 'u')
                        {
                            if (index + 4 > source.Length || !source.AsSpan(index, 4).ToString().All(Uri.IsHexDigit))
                                throw new SyntaxFailure("Expected four hexadecimal digits in Unicode escape.", index - 2 + _baseOffset, 2);
                            index += 4;
                        }
                        else if (_feel && escape is not ('"' or '\\' or 'n' or 'r' or 't' or 'b' or 'f')
                            || !_feel && escape is not ('"' or '\'' or '\\'))
                            throw new SyntaxFailure("Invalid string escape.", index - 2 + _baseOffset, 2);
                    }
                    if (!closed) throw new SyntaxFailure("Unterminated quoted string.", start + _baseOffset, source.Length - start);
                    _tokens.Add(new(source[start..index], start, index - start, IsLiteral: true)); continue;
                }
                if (char.IsDigit(character) || character == '.' && index + 1 < source.Length && char.IsDigit(source[index + 1]))
                {
                    while (index < source.Length && char.IsDigit(source[index])) index++;
                    if (index < source.Length && source[index] == '.' && (index + 1 == source.Length || source[index + 1] != '.'))
                    { index++; while (index < source.Length && char.IsDigit(source[index])) index++; }
                    if (index < source.Length && source[index] is 'e' or 'E')
                    {
                        index++; if (index < source.Length && source[index] is '+' or '-') index++;
                        var digits = index;
                        while (index < source.Length && char.IsDigit(source[index])) index++;
                        if (digits == index) throw new SyntaxFailure("Expected exponent digits.", start + _baseOffset, index - start);
                    }
                    _tokens.Add(new(source[start..index], start, index - start, IsLiteral: true)); continue;
                }
                if (_feel)
                {
                    var phrase = ReservedNames
                        .FirstOrDefault(name => source.AsSpan(index).StartsWith(name, StringComparison.Ordinal)
                            && (index + name.Length == source.Length || !char.IsLetterOrDigit(source[index + name.Length])));
                    if (phrase is not null)
                    { _tokens.Add(new(phrase, index, phrase.Length, IsName: true)); index += phrase.Length; continue; }
                }
                if (char.IsLetter(character) || character is '_' or '$' or '?')
                {
                    if (character == '?' && !_feel) { _tokens.Add(new("?", index++, 1)); continue; }
                    index++;
                    while (index < source.Length && (char.IsLetterOrDigit(source[index]) || source[index] is '_' or '$' || _feel && source[index] == '?')) index++;
                    var word = source[start..index];
                    _tokens.Add(new(word, start, index - start, IsName: word is not ("true" or "false" or "null"), IsLiteral: word is "true" or "false" or "null")); continue;
                }
                var pair = index + 1 < source.Length ? source[index..(index + 2)] : "";
                if (pair is "<=" or ">=" or "!=" || _feel && pair is ".." or "**" || !_feel && pair is "==" or "&&" or "||") index += 2;
                else if ("+-*/%=<>()[]{}.,:@!".Contains(character, StringComparison.Ordinal)) index++;
                else throw new SyntaxFailure($"Unexpected character '{character}'.", index + _baseOffset, 1);
                _tokens.Add(new(source[start..index], start, index - start));
            }
            _tokens.Add(new("", source.Length, 0));
        }
    }
}
