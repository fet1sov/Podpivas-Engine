using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PodpivasEngine.Scripting;

public enum LuaExportKind
{
    Number,
    Text,
    Boolean,
    Object,
}

public sealed record LuaExportField(string Name, string Label, LuaExportKind Kind, string DefaultValue);

// Поля компонента объявляются комментарием -- @export. Можно отдельной строкой над однострочным local:
//   -- @export Скорость
//   local speed = 3
// либо прямо на строке local:
//   local speed = 3 -- @export Скорость
// Значение подставляется в исходник перед выполнением, поэтому expression-значения не поддерживаются.
public static partial class LuaExports
{
    private sealed record Declaration(int LineIndex, LuaExportField Field, string Prefix, string Suffix);

    public static IReadOnlyList<LuaExportField> Read(string source) =>
        Scan(source).Select(declaration => declaration.Field).ToList();

    public static string ApplyValues(string source, IReadOnlyDictionary<string, string> values)
    {
        if (values.Count == 0)
            return source;

        var declarations = Scan(source);
        if (declarations.Count == 0)
            return source;

        var newline = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = source.Replace("\r\n", "\n").Split('\n');

        foreach (var declaration in declarations)
        {
            if (!values.TryGetValue(declaration.Field.Name, out var raw))
                continue;

            if (!TryFormat(declaration.Field.Kind, raw, out var literal))
                continue;

            lines[declaration.LineIndex] = declaration.Prefix + literal + declaration.Suffix;
        }

        return string.Join(newline, lines);
    }

    private static List<Declaration> Scan(string source)
    {
        var declarations = new List<Declaration>();
        var lines = source.Replace("\r\n", "\n").Split('\n');

        for (var index = 0; index < lines.Length; index++)
        {
            if (TryReadDeclaration(lines[index], out var name, out var kind, out var value, out var prefix, out var suffix, out var inlineLabel)
                && !string.IsNullOrWhiteSpace(inlineLabel))
            {
                declarations.Add(new Declaration(
                    index,
                    new LuaExportField(name, inlineLabel, kind, value),
                    prefix,
                    suffix));
                continue;
            }

            if (!TryReadLabel(lines[index], out var label))
                continue;

            var declarationIndex = index + 1;
            while (declarationIndex < lines.Length && string.IsNullOrWhiteSpace(lines[declarationIndex]))
                declarationIndex++;

            if (declarationIndex >= lines.Length)
                break;

            if (!TryReadDeclaration(lines[declarationIndex], out name, out kind, out value, out prefix, out suffix, out _))
                continue;

            declarations.Add(new Declaration(
                declarationIndex,
                new LuaExportField(name, string.IsNullOrWhiteSpace(label) ? name : label, kind, value),
                prefix,
                suffix));
        }

        return declarations;
    }

    private static bool TryReadLabel(string line, out string label)
    {
        label = string.Empty;

        var text = line.TrimStart();
        if (!text.StartsWith("--", StringComparison.Ordinal))
            return false;

        text = text.TrimStart('-').Trim();
        if (!text.StartsWith("@export", StringComparison.OrdinalIgnoreCase))
            return false;

        label = text["@export".Length..].Trim().Trim('"', '\'');
        return true;
    }

    private static bool TryReadDeclaration(
        string line,
        out string name,
        out LuaExportKind kind,
        out string value,
        out string prefix,
        out string suffix,
        out string inlineLabel)
    {
        name = string.Empty;
        kind = LuaExportKind.Number;
        value = string.Empty;
        prefix = string.Empty;
        suffix = string.Empty;
        inlineLabel = string.Empty;

        var match = LocalDeclaration().Match(line);
        if (!match.Success)
            return false;

        name = match.Groups["name"].Value;
        var tail = match.Groups["value"].Value;

        var comment = FindComment(tail);
        var literal = (comment < 0 ? tail : tail[..comment]).Trim();
        suffix = comment < 0 ? string.Empty : " " + tail[comment..].TrimEnd();
        if (comment >= 0 && TryReadLabel(suffix, out var label))
            inlineLabel = label;

        if (!TryParseLiteral(literal, out kind, out value))
            return false;

        prefix = line[..(line.Length - tail.Length)];
        return true;
    }

    private static int FindComment(string text)
    {
        var quote = '\0';

        for (var i = 0; i < text.Length; i++)
        {
            var current = text[i];

            if (quote != '\0')
            {
                if (current == '\\')
                    i++;
                else if (current == quote)
                    quote = '\0';

                continue;
            }

            if (current is '"' or '\'')
            {
                quote = current;
                continue;
            }

            if (current == '-' && i + 1 < text.Length && text[i + 1] == '-')
                return i;
        }

        return -1;
    }

    private static bool TryParseLiteral(string literal, out LuaExportKind kind, out string value)
    {
        kind = LuaExportKind.Text;
        value = literal;

        if (literal is "true" or "false")
        {
            kind = LuaExportKind.Boolean;
            return true;
        }

        if (literal.Replace(" ", string.Empty) is "{}")
        {
            kind = LuaExportKind.Object;
            value = string.Empty;
            return true;
        }

        if (double.TryParse(literal, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        {
            kind = LuaExportKind.Number;
            return true;
        }

        if (literal.Length >= 2 && (literal[0] is '"' or '\'') && literal[^1] == literal[0])
        {
            kind = LuaExportKind.Text;
            value = Unescape(literal[1..^1], literal[0]);
            return true;
        }

        return false;
    }

    private static bool TryFormat(LuaExportKind kind, string raw, out string literal)
    {
        literal = string.Empty;

        switch (kind)
        {
            case LuaExportKind.Number:
                if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                    return false;

                literal = number.ToString("0.############", CultureInfo.InvariantCulture);
                return true;

            case LuaExportKind.Boolean:
                if (!bool.TryParse(raw, out var flag))
                    return false;

                literal = flag ? "true" : "false";
                return true;

            case LuaExportKind.Object:
                literal = string.IsNullOrWhiteSpace(raw) ? "{}" : "find(\"" + Escape(raw.Trim()) + "\")";
                return true;

            default:
                literal = "\"" + Escape(raw) + "\"";
                return true;
        }
    }

    private static string Unescape(string text, char quote)
    {
        var builder = new StringBuilder(text.Length);

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\\' || i + 1 >= text.Length)
            {
                builder.Append(text[i]);
                continue;
            }

            var escape = text[++i];
            builder.Append(escape switch
            {
                'n' => '\n',
                't' => '\t',
                'r' => '\r',
                '\\' => '\\',
                '"' => '"',
                '\'' => '\'',
                _ => escape,
            });
        }

        return builder.ToString();
    }

    private static string Escape(string text)
    {
        var builder = new StringBuilder(text.Length);

        foreach (var character in text)
        {
            builder.Append(character switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ => character.ToString(),
            });
        }

        return builder.ToString();
    }

    [GeneratedRegex(@"^\s*local\s+(?<name>[A-Za-z_]\w*)\s*=\s*(?<value>.+)$")]
    private static partial Regex LocalDeclaration();
}
