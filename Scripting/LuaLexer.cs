using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PodpivasEngine.Scripting;

public enum LuaTokenKind
{
    Name,
    Number,
    String,
    Keyword,
    Symbol,
    Eof,
}

public readonly record struct LuaToken(LuaTokenKind Kind, string Text, double Number, int Line);

public sealed class LuaSyntaxException(string message, int line)
    : Exception($"line {line}: {message}")
{
    public int Line { get; } = line;
}

public sealed class LuaLexer
{
    private static readonly HashSet<string> Keywords =
    [
        "and", "break", "do", "else", "elseif", "end", "false", "for", "function",
        "if", "in", "local", "nil", "not", "or", "repeat", "return", "then",
        "true", "until", "while",
    ];

    private readonly string _source;
    private int _position;
    private int _line = 1;

    public LuaLexer(string source) => _source = source;

    public List<LuaToken> Tokenize()
    {
        var tokens = new List<LuaToken>();

        while (true)
        {
            SkipWhitespaceAndComments();

            if (_position >= _source.Length)
            {
                tokens.Add(new LuaToken(LuaTokenKind.Eof, string.Empty, 0, _line));
                return tokens;
            }

            var startLine = _line;
            var current = _source[_position];

            if (char.IsLetter(current) || current == '_')
            {
                var name = ReadName();
                tokens.Add(new LuaToken(
                    Keywords.Contains(name) ? LuaTokenKind.Keyword : LuaTokenKind.Name,
                    name,
                    0,
                    startLine));
                continue;
            }

            if (char.IsDigit(current) || (current == '.' && _position + 1 < _source.Length && char.IsDigit(_source[_position + 1])))
            {
                var number = ReadNumber();
                tokens.Add(new LuaToken(LuaTokenKind.Number, string.Empty, number, startLine));
                continue;
            }

            if (current is '"' or '\'')
            {
                var text = ReadString(current);
                tokens.Add(new LuaToken(LuaTokenKind.String, text, 0, startLine));
                continue;
            }

            if (TryReadSymbol(out var symbol))
            {
                tokens.Add(new LuaToken(LuaTokenKind.Symbol, symbol, 0, startLine));
                continue;
            }

            throw new LuaSyntaxException($"unexpected character '{current}'", _line);
        }
    }

    private void SkipWhitespaceAndComments()
    {
        while (_position < _source.Length)
        {
            var current = _source[_position];

            if (current == '\n')
            {
                _line++;
                _position++;
                continue;
            }

            if (char.IsWhiteSpace(current))
            {
                _position++;
                continue;
            }

            if (current == '-' && Peek(1) == '-')
            {
                _position += 2;

                if (_source.AsSpan(_position).StartsWith("[[".AsSpan(), StringComparison.Ordinal))
                {
                    var close = _source.IndexOf("]]", _position + 2, StringComparison.Ordinal);
                    if (close < 0)
                        throw new LuaSyntaxException("unfinished long comment", _line);

                    _line += CountLines(_position, close);
                    _position = close + 2;
                    continue;
                }

                while (_position < _source.Length && _source[_position] != '\n')
                    _position++;
                continue;
            }

            return;
        }
    }

    private int CountLines(int from, int to)
    {
        var count = 0;
        for (var i = from; i < to && i < _source.Length; i++)
        {
            if (_source[i] == '\n')
                count++;
        }

        return count;
    }

    private string ReadName()
    {
        var start = _position;
        while (_position < _source.Length && (char.IsLetterOrDigit(_source[_position]) || _source[_position] == '_'))
            _position++;
        return _source[start.._position];
    }

    private double ReadNumber()
    {
        var start = _position;
        var hasDot = false;

        while (_position < _source.Length)
        {
            var current = _source[_position];
            if (char.IsDigit(current))
            {
                _position++;
                continue;
            }

            if (current == '.' && !hasDot)
            {
                hasDot = true;
                _position++;
                continue;
            }

            if (current is 'e' or 'E')
            {
                _position++;
                if (_position < _source.Length && _source[_position] is '+' or '-')
                    _position++;
                continue;
            }

            break;
        }

        var text = _source[start.._position];
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            throw new LuaSyntaxException($"malformed number '{text}'", _line);

        return value;
    }

    private string ReadString(char quote)
    {
        _position++;
        var builder = new StringBuilder();

        while (_position < _source.Length)
        {
            var current = _source[_position];

            if (current == quote)
            {
                _position++;
                return builder.ToString();
            }

            if (current == '\n')
                throw new LuaSyntaxException("unfinished string", _line);

            if (current != '\\')
            {
                builder.Append(current);
                _position++;
                continue;
            }

            _position++;
            if (_position >= _source.Length)
                break;

            var escape = _source[_position++];
            builder.Append(escape switch
            {
                'n' => '\n',
                't' => '\t',
                'r' => '\r',
                'a' => '\a',
                '\\' => '\\',
                '"' => '"',
                '\'' => '\'',
                '0' => '\0',
                _ => escape,
            });
        }

        throw new LuaSyntaxException("unfinished string", _line);
    }

    private bool TryReadSymbol(out string symbol)
    {
        if (_position + 1 < _source.Length)
        {
            var pair = _source.Substring(_position, 2);
            if (pair is "==" or "~=" or "<=" or ">=" or ".." or "//" or "::")
            {
                _position += 2;
                symbol = pair;
                return true;
            }
        }

        var single = _source[_position];
        if ("+-*/%^#<>=(){}[];:,.".Contains(single))
        {
            _position++;
            symbol = single.ToString();
            return true;
        }

        symbol = string.Empty;
        return false;
    }

    private char Peek(int offset)
    {
        var index = _position + offset;
        return index < _source.Length ? _source[index] : '\0';
    }
}
