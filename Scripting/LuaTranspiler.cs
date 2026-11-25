using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace PodpivasEngine.Scripting;

public sealed class LuaTranspiler
{
    private sealed class Scope
    {
        public HashSet<string> Names { get; } = [];

        public Scope? Parent { get; init; }

        public bool Has(string name) => Names.Contains(name) || Parent?.Has(name) == true;
    }

    private readonly List<Scope> _scopes = [];
    private StringBuilder _builder = new();
    private int _indent;
    private int _temporary;

    public static string Transpile(string source) =>
        new LuaTranspiler().TranspileChunk(new LuaParser(source).ParseChunk());

    private string TranspileChunk(LuaBlock block)
    {
        PushScope();
        EmitBlock(block);
        PopScope();
        return _builder.ToString();
    }

    private void PushScope(Scope? explicitScope = null) =>
        _scopes.Add(explicitScope ?? new Scope { Parent = _scopes.Count > 0 ? _scopes[^1] : null });

    private void PopScope() => _scopes.RemoveAt(_scopes.Count - 1);

    private bool IsLocal(string name) => _scopes.Count > 0 && _scopes[^1].Has(name);

    private void Declare(string name) => _scopes[^1].Names.Add(name);

    private void Line(string text) =>
        _builder.Append(' ', _indent * 2).Append(text).Append('\n');

    private string Capture(Action action)
    {
        var outer = _builder;
        _builder = new StringBuilder();
        action();
        var result = _builder.ToString();
        _builder = outer;
        return result;
    }

    private void EmitBlock(LuaBlock block)
    {
        foreach (var statement in block.Statements)
            EmitStatement(statement);
    }

    private void EmitStatement(LuaStat statement)
    {
        switch (statement)
        {
            case LuaLocalStat local:
                EmitLocal(local);
                break;

            case LuaAssignStat assign:
                EmitAssign(assign);
                break;

            case LuaCallStat call:
                Line($"{Expression(call.Call)};");
                break;

            case LuaIfStat ifStat:
                EmitIf(ifStat);
                break;

            case LuaWhileStat whileStat:
                Line($"while ({Truthy(whileStat.Condition)}) {{");
                EmitNested(whileStat.Body);
                Line("}");
                break;

            case LuaRepeatStat repeatStat:
                Line("do {");
                PushScope();
                _indent++;
                EmitBlock(repeatStat.Body);
                var condition = Truthy(repeatStat.Condition);
                _indent--;
                PopScope();
                Line($"}} while (!{condition});");
                break;

            case LuaNumericForStat forStat:
                EmitNumericFor(forStat);
                break;

            case LuaFunctionStat functionStat:
                var function = FunctionExpression(functionStat.Function);
                EmitAssignment(functionStat.Target, function);
                break;

            case LuaReturnStat returnStat:
                if (returnStat.Values.Count == 0)
                    Line("return;");
                else
                    Line($"return {Expression(returnStat.Values[0])};");
                break;

            case LuaDoStat doStat:
                Line("{");
                EmitNested(doStat.Body);
                Line("}");
                break;

            case LuaBreakStat:
                Line("break;");
                break;
        }
    }

    private void EmitNested(LuaBlock body)
    {
        PushScope();
        _indent++;
        EmitBlock(body);
        _indent--;
        PopScope();
    }

    private string Truthy(LuaExpr expression) => $"_L.truthy({Expression(expression)})";

    private void EmitIf(LuaIfStat ifStat)
    {
        for (var index = 0; index < ifStat.Branches.Count; index++)
        {
            var (condition, body) = ifStat.Branches[index];
            var keyword = index == 0 ? "if" : "} else if";
            Line($"{keyword} ({Truthy(condition)}) {{");
            EmitNested(body);
        }

        if (ifStat.ElseBody is not null)
        {
            Line("} else {");
            EmitNested(ifStat.ElseBody);
        }

        Line("}");
    }

    private void EmitNumericFor(LuaNumericForStat forStat)
    {
        var limit = NextTemporary("_l");
        var step = NextTemporary("_s");
        var stepExpression = forStat.Step is null ? "1" : Expression(forStat.Step);

        Line(
            $"for (var {forStat.Name} = {Expression(forStat.Start)}, {limit} = {Expression(forStat.Limit)}, {step} = {stepExpression}; " +
            $"{step} >= 0 ? {forStat.Name} <= {limit} : {forStat.Name} >= {limit}; {forStat.Name} += {step}) {{");

        PushScope();
        Declare(forStat.Name);
        _indent++;
        EmitBlock(forStat.Body);
        _indent--;
        PopScope();
        Line("}");
    }

    private void EmitLocal(LuaLocalStat local)
    {
        if (local.Names.Count == 1
            && local.Values.Count == 1
            && local.Values[0] is LuaFunctionExpr recursive)
        {
            Declare(local.Names[0]);
            Line($"var {local.Names[0]} = {FunctionExpression(recursive)};");
            return;
        }

        var temporaries = local.Values.Select(value =>
        {
            var name = NextTemporary("_t");
            Line($"var {name} = {Expression(value)};");
            return name;
        }).ToList();

        for (var index = 0; index < local.Names.Count; index++)
        {
            Declare(local.Names[index]);
            var value = index < temporaries.Count ? temporaries[index] : "null";
            Line($"var {local.Names[index]} = {value};");
        }
    }

    private void EmitAssign(LuaAssignStat assign)
    {
        var temporaries = assign.Values.Select(value =>
        {
            var name = NextTemporary("_t");
            Line($"var {name} = {Expression(value)};");
            return name;
        }).ToList();

        for (var index = 0; index < assign.Targets.Count; index++)
        {
            var value = index < temporaries.Count ? temporaries[index] : "null";
            EmitAssignment(assign.Targets[index], value);
        }
    }

    private void EmitAssignment(LuaExpr target, string value)
    {
        switch (target)
        {
            case LuaNameExpr name:
                Line(IsLocal(name.Name)
                    ? $"{name.Name} = {value};"
                    : $"_G[{Quote(name.Name)}] = {value};");
                break;

            case LuaIndexExpr index:
                Line($"_L.setIndex({Expression(index.Target)}, {Expression(index.Key)}, {value});");
                break;

            default:
                throw new InvalidOperationException("invalid assignment target");
        }
    }

    private string FunctionExpression(LuaFunctionExpr function)
    {
        return Capture(() =>
        {
            _builder.Append("function(").Append(string.Join(", ", function.Parameters)).Append(") {\n");
            PushScope();
            foreach (var parameter in function.Parameters)
                Declare(parameter);

            _indent++;
            EmitBlock(function.Body);
            _indent--;
            PopScope();
            _builder.Append(' ', _indent * 2).Append('}');
        });
    }

    private string Expression(LuaExpr expression)
    {
        switch (expression)
        {
            case LuaLiteralExpr literal:
                return Literal(literal.Value);

            case LuaNameExpr name:
                return IsLocal(name.Name) ? name.Name : $"_G[{Quote(name.Name)}]";

            case LuaFieldExpr field:
                return $"({Expression(field.Value)})";

            case LuaIndexExpr index:
                return $"_L.index({Expression(index.Target)}, {Expression(index.Key)})";

            case LuaUnaryExpr unary:
                return unary.Op switch
                {
                    "-" => $"(-{Expression(unary.Operand)})",
                    "not" => $"_L.not({Expression(unary.Operand)})",
                    "#" => $"_L.len({Expression(unary.Operand)})",
                    _ => "null",
                };

            case LuaBinaryExpr binary:
                return BinaryExpression(binary);

            case LuaCallExpr call:
                var arguments = string.Join(", ", call.Arguments.Select(Expression));
                return $"_L.call({Expression(call.Target)}, [{arguments}])";

            case LuaTableExpr table:
                return TableExpression(table);

            case LuaFunctionExpr function:
                return FunctionExpression(function);
        }

        throw new InvalidOperationException("unsupported expression");
    }

    private string BinaryExpression(LuaBinaryExpr binary)
    {
        var left = Expression(binary.Left);
        var right = Expression(binary.Right);

        return binary.Op switch
        {
            "and" => $"_L.and({left}, {right})",
            "or" => $"_L.or({left}, {right})",
            "==" => $"_L.eq({left}, {right})",
            "~=" => $"(!_L.eq({left}, {right}))",
            ".." => $"_L.concat({left}, {right})",
            "%" => $"_L.mod({left}, {right})",
            "//" => $"Math.floor({left} / {right})",
            "^" => $"Math.pow({left}, {right})",
            "<" or ">" or "<=" or ">=" or "+" or "-" or "*" or "/" => $"({left} {binary.Op} {right})",
            _ => "null",
        };
    }

    private string TableExpression(LuaTableExpr table)
    {
        var entries = new List<string>();
        var position = 1;

        foreach (var item in table.Items)
            entries.Add($"{position++}: {Expression(item)}");

        foreach (var (key, value) in table.Fields)
        {
            var keyCode = key is LuaLiteralExpr { Value: LuaString text }
                ? Quote(text.Value)
                : $"[{Expression(key)}]";

            entries.Add($"{keyCode}: {Expression(value)}");
        }

        return "{ " + string.Join(", ", entries) + " }";
    }

    private static string Literal(LuaValue value) => value switch
    {
        LuaString text => Quote(text.Value),
        LuaNumber number => number.ToDisplay(),
        LuaBool boolean => boolean.Truthy ? "true" : "false",
        _ => "null",
    };

    private static string Quote(string value) => JsonSerializer.Serialize(value);

    private string NextTemporary(string prefix) => prefix + (++_temporary).ToString(CultureInfo.InvariantCulture);
}
