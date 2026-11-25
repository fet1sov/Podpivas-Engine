using System;
using System.Collections.Generic;

namespace PodpivasEngine.Scripting;

public sealed class LuaParser
{
    private static readonly HashSet<string> BlockTerminators = ["end", "else", "elseif", "until"];

    private readonly List<LuaToken> _tokens;
    private int _index;

    public LuaParser(string source) => _tokens = new LuaLexer(source).Tokenize();

    private LuaToken Current => _tokens[_index];

    private LuaToken Peek(int offset) =>
        _tokens[Math.Min(_index + offset, _tokens.Count - 1)];

    public LuaBlock ParseChunk()
    {
        var block = ParseBlock();
        if (Current.Kind != LuaTokenKind.Eof)
            throw Error($"unexpected '{Current.Text}'");

        return block;
    }

    private LuaBlock ParseBlock()
    {
        var block = new LuaBlock();

        while (true)
        {
            if (Current.Kind == LuaTokenKind.Eof)
                return block;

            if (Current.Kind == LuaTokenKind.Keyword && BlockTerminators.Contains(Current.Text))
                return block;

            if (Current.Kind == LuaTokenKind.Symbol && Current.Text == ";")
            {
                Advance();
                continue;
            }

            block.Statements.Add(ParseStatement());
        }
    }

    private LuaStat ParseStatement()
    {
        if (Current.Kind == LuaTokenKind.Keyword)
        {
            switch (Current.Text)
            {
                case "local":
                    return ParseLocal();
                case "if":
                    return ParseIf();
                case "while":
                    return ParseWhile();
                case "repeat":
                    return ParseRepeat();
                case "do":
                    Advance();
                    var doBlock = ParseBlock();
                    ExpectKeyword("end");
                    return new LuaDoStat(doBlock);
                case "for":
                    return ParseFor();
                case "function":
                    return ParseFunctionStat();
                case "return":
                    return ParseReturn();
                case "break":
                    Advance();
                    return new LuaBreakStat();
            }
        }

        var first = ParseSuffixedExpr();
        if (Current.Kind == LuaTokenKind.Symbol && Current.Text == "=")
        {
            var targets = new List<LuaExpr> { first };
            while (MatchSymbol(","))
                targets.Add(ParseSuffixedExpr());

            ExpectSymbol("=");
            return new LuaAssignStat(targets, ParseExprList());
        }

        if (Current.Kind == LuaTokenKind.Symbol && Current.Text == ",")
        {
            var targets = new List<LuaExpr> { first };
            while (MatchSymbol(","))
                targets.Add(ParseSuffixedExpr());

            ExpectSymbol("=");
            return new LuaAssignStat(targets, ParseExprList());
        }

        if (first is LuaCallExpr call)
            return new LuaCallStat(call);

        throw Error("assignment or function call expected");
    }

    private LuaStat ParseLocal()
    {
        Advance();

        if (MatchKeyword("function"))
        {
            var name = ExpectName();
            var function = ParseFunctionBody();
            return new LuaLocalStat([name], [function]);
        }

        var names = new List<string> { ExpectName() };
        while (MatchSymbol(","))
            names.Add(ExpectName());

        var values = MatchSymbol("=") ? ParseExprList() : [];
        return new LuaLocalStat(names, values);
    }

    private LuaStat ParseIf()
    {
        Advance();
        var branches = new List<(LuaExpr Condition, LuaBlock Body)>
        {
            (ParseExpr(), ParseThenBlock()),
        };

        LuaBlock? elseBody = null;

        while (Current.Kind == LuaTokenKind.Keyword && Current.Text == "elseif")
        {
            Advance();
            branches.Add((ParseExpr(), ParseThenBlock()));
        }

        if (MatchKeyword("else"))
            elseBody = ParseBlock();

        ExpectKeyword("end");
        return new LuaIfStat(branches, elseBody);
    }

    private LuaBlock ParseThenBlock()
    {
        ExpectKeyword("then");
        return ParseBlock();
    }

    private LuaStat ParseWhile()
    {
        Advance();
        var condition = ParseExpr();
        ExpectKeyword("do");
        var body = ParseBlock();
        ExpectKeyword("end");
        return new LuaWhileStat(condition, body);
    }

    private LuaStat ParseRepeat()
    {
        Advance();
        var body = ParseBlock();
        ExpectKeyword("until");
        return new LuaRepeatStat(body, ParseExpr());
    }

    private LuaStat ParseFor()
    {
        Advance();
        var name = ExpectName();

        if (MatchSymbol("="))
        {
            var start = ParseExpr();
            ExpectSymbol(",");
            var limit = ParseExpr();
            LuaExpr? step = null;
            if (MatchSymbol(","))
                step = ParseExpr();

            ExpectKeyword("do");
            var body = ParseBlock();
            ExpectKeyword("end");
            return new LuaNumericForStat(name, start, limit, step, body);
        }

        throw Error("generic for is not supported, use a numeric for loop");
    }

    private LuaStat ParseFunctionStat()
    {
        Advance();
        LuaExpr target = new LuaNameExpr(ExpectName());

        while (MatchSymbol("."))
            target = new LuaIndexExpr(target, new LuaLiteralExpr(LuaValue.Of(ExpectName())));

        return new LuaFunctionStat(target, ParseFunctionBody());
    }

    private LuaFunctionExpr ParseFunctionBody()
    {
        ExpectSymbol("(");
        var parameters = new List<string>();

        if (!MatchSymbol(")"))
        {
            if (Current.Kind == LuaTokenKind.Symbol && Current.Text == "...")
                throw Error("varargs are not supported");

            parameters.Add(ExpectName());
            while (MatchSymbol(","))
                parameters.Add(ExpectName());

            ExpectSymbol(")");
        }

        var body = ParseBlock();
        ExpectKeyword("end");
        return new LuaFunctionExpr(parameters, body);
    }

    private LuaStat ParseReturn()
    {
        Advance();
        var values = new List<LuaExpr>();

        if (!IsStatementEnd())
            values = ParseExprList();

        MatchSymbol(";");
        return new LuaReturnStat(values);
    }

    private bool IsStatementEnd()
    {
        if (Current.Kind == LuaTokenKind.Eof)
            return true;

        if (Current.Kind == LuaTokenKind.Keyword && BlockTerminators.Contains(Current.Text))
            return true;

        return Current.Kind == LuaTokenKind.Symbol && Current.Text == ";";
    }

    private List<LuaExpr> ParseExprList()
    {
        var values = new List<LuaExpr> { ParseExpr() };
        while (MatchSymbol(","))
            values.Add(ParseExpr());
        return values;
    }

    private LuaExpr ParseExpr(int minimumPrecedence = 0)
    {
        var left = ParseUnary();

        while (true)
        {
            var op = Current.Text;
            var isBinary = Current.Kind is LuaTokenKind.Symbol or LuaTokenKind.Keyword;
            if (!isBinary)
                break;

            var precedence = GetBinaryPrecedence(op);
            if (precedence < minimumPrecedence)
                break;

            Advance();
            var rightAssociative = op is ".." or "^";
            var right = ParseExpr(rightAssociative ? precedence : precedence + 1);
            left = new LuaBinaryExpr(op, left, right);
        }

        return left;
    }

    private static int GetBinaryPrecedence(string op) => op switch
    {
        "or" => 1,
        "and" => 2,
        "<" or ">" or "<=" or ">=" or "~=" or "==" => 3,
        ".." => 4,
        "+" or "-" => 5,
        "*" or "/" or "%" or "//" => 6,
        "^" => 8,
        _ => -1,
    };

    private LuaExpr ParseUnary()
    {
        if ((Current.Kind == LuaTokenKind.Symbol && Current.Text is "-" or "#")
            || (Current.Kind == LuaTokenKind.Keyword && Current.Text == "not"))
        {
            var op = Current.Text;
            Advance();
            return new LuaUnaryExpr(op, ParseExpr(7));
        }

        return ParseSuffixedExpr();
    }

    private LuaExpr ParseSuffixedExpr()
    {
        var expr = ParsePrimary();

        while (true)
        {
            if (MatchSymbol("."))
            {
                expr = new LuaIndexExpr(expr, new LuaLiteralExpr(LuaValue.Of(ExpectName())));
                continue;
            }

            if (MatchSymbol("["))
            {
                var key = ParseExpr();
                ExpectSymbol("]");
                expr = new LuaIndexExpr(expr, key);
                continue;
            }

            if (Current.Kind == LuaTokenKind.Symbol && Current.Text == ":")
                throw Error("method calls (':') are not supported");

            if (Current.Kind == LuaTokenKind.Symbol && Current.Text == "(")
            {
                expr = new LuaCallExpr(expr, ParseCallArguments());
                continue;
            }

            if (Current.Kind == LuaTokenKind.String)
            {
                var argument = new LuaLiteralExpr(LuaValue.Of(Current.Text));
                Advance();
                expr = new LuaCallExpr(expr, [argument]);
                continue;
            }

            break;
        }

        return expr;
    }

    private List<LuaExpr> ParseCallArguments()
    {
        ExpectSymbol("(");
        var arguments = new List<LuaExpr>();

        if (MatchSymbol(")"))
            return arguments;

        arguments.Add(ParseExpr());
        while (MatchSymbol(","))
            arguments.Add(ParseExpr());

        ExpectSymbol(")");
        return arguments;
    }

    private LuaExpr ParsePrimary()
    {
        var token = Current;

        switch (token.Kind)
        {
            case LuaTokenKind.Number:
                Advance();
                return new LuaLiteralExpr(LuaValue.Of(token.Number));
            case LuaTokenKind.String:
                Advance();
                return new LuaLiteralExpr(LuaValue.Of(token.Text));
            case LuaTokenKind.Name:
                Advance();
                return new LuaNameExpr(token.Text);
            case LuaTokenKind.Keyword when token.Text is "nil" or "true" or "false":
                Advance();
                return new LuaLiteralExpr(token.Text switch
                {
                    "nil" => LuaValue.Nil,
                    "true" => LuaValue.True,
                    _ => LuaValue.False,
                });
            case LuaTokenKind.Keyword when token.Text == "function":
                Advance();
                return ParseFunctionBody();
            case LuaTokenKind.Symbol when token.Text == "(":
                Advance();
                var inner = ParseExpr();
                ExpectSymbol(")");
                return new LuaFieldExpr(inner);
            case LuaTokenKind.Symbol when token.Text == "{":
                return ParseTable();
        }

        throw Error($"unexpected '{token.Text}'");
    }

    private LuaExpr ParseTable()
    {
        ExpectSymbol("{");
        var table = new LuaTableExpr();

        while (!(Current.Kind == LuaTokenKind.Symbol && Current.Text == "}"))
        {
            if (MatchSymbol("["))
            {
                var key = ParseExpr();
                ExpectSymbol("]");
                ExpectSymbol("=");
                table.Fields.Add((key, ParseExpr()));
            }
            else if (Current.Kind == LuaTokenKind.Name
                     && Peek(1).Kind == LuaTokenKind.Symbol
                     && Peek(1).Text == "=")
            {
                var key = Current.Text;
                Advance();
                Advance();
                table.Fields.Add((new LuaLiteralExpr(LuaValue.Of(key)), ParseExpr()));
            }
            else
            {
                table.Items.Add(ParseExpr());
            }

            if (!MatchSymbol(",") && !MatchSymbol(";"))
                break;
        }

        ExpectSymbol("}");
        return table;
    }

    private void Advance() => _index = Math.Min(_index + 1, _tokens.Count - 1);

    private bool MatchSymbol(string symbol)
    {
        if (Current.Kind != LuaTokenKind.Symbol || Current.Text != symbol)
            return false;

        Advance();
        return true;
    }

    private bool MatchKeyword(string keyword)
    {
        if (Current.Kind != LuaTokenKind.Keyword || Current.Text != keyword)
            return false;

        Advance();
        return true;
    }

    private void ExpectSymbol(string symbol)
    {
        if (!MatchSymbol(symbol))
            throw Error($"'{symbol}' expected");
    }

    private void ExpectKeyword(string keyword)
    {
        if (!MatchKeyword(keyword))
            throw Error($"'{keyword}' expected");
    }

    private string ExpectName()
    {
        if (Current.Kind != LuaTokenKind.Name)
            throw Error("name expected");

        var name = Current.Text;
        Advance();
        return name;
    }

    private LuaSyntaxException Error(string message) => new(message, Current.Line);
}
