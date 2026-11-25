using System.Collections.Generic;

namespace PodpivasEngine.Scripting;

public sealed class LuaBlock
{
    public List<LuaStat> Statements { get; } = [];
}

public abstract class LuaExpr;

public sealed class LuaLiteralExpr(LuaValue value) : LuaExpr
{
    public LuaValue Value { get; } = value;
}

public sealed class LuaNameExpr(string name) : LuaExpr
{
    public string Name { get; } = name;
}

public sealed class LuaIndexExpr(LuaExpr target, LuaExpr key) : LuaExpr
{
    public LuaExpr Target { get; } = target;

    public LuaExpr Key { get; } = key;
}

public sealed class LuaUnaryExpr(string op, LuaExpr operand) : LuaExpr
{
    public string Op { get; } = op;

    public LuaExpr Operand { get; } = operand;
}

public sealed class LuaBinaryExpr(string op, LuaExpr left, LuaExpr right) : LuaExpr
{
    public string Op { get; } = op;

    public LuaExpr Left { get; } = left;

    public LuaExpr Right { get; } = right;
}

public sealed class LuaCallExpr(LuaExpr target, List<LuaExpr> arguments) : LuaExpr
{
    public LuaExpr Target { get; } = target;

    public List<LuaExpr> Arguments { get; } = arguments;
}

public sealed class LuaFieldExpr(LuaExpr value) : LuaExpr
{
    public LuaExpr Value { get; } = value;
}

public sealed class LuaTableExpr : LuaExpr
{
    public List<(LuaExpr Key, LuaExpr Value)> Fields { get; } = [];

    public List<LuaExpr> Items { get; } = [];
}

public sealed class LuaFunctionExpr(List<string> parameters, LuaBlock body) : LuaExpr
{
    public List<string> Parameters { get; } = parameters;

    public LuaBlock Body { get; } = body;
}

public abstract class LuaStat;

public sealed class LuaLocalStat(List<string> names, List<LuaExpr> values) : LuaStat
{
    public List<string> Names { get; } = names;

    public List<LuaExpr> Values { get; } = values;
}

public sealed class LuaAssignStat(List<LuaExpr> targets, List<LuaExpr> values) : LuaStat
{
    public List<LuaExpr> Targets { get; } = targets;

    public List<LuaExpr> Values { get; } = values;
}

public sealed class LuaCallStat(LuaCallExpr call) : LuaStat
{
    public LuaCallExpr Call { get; } = call;
}

public sealed class LuaIfStat(List<(LuaExpr Condition, LuaBlock Body)> branches, LuaBlock? elseBody) : LuaStat
{
    public List<(LuaExpr Condition, LuaBlock Body)> Branches { get; } = branches;

    public LuaBlock? ElseBody { get; } = elseBody;
}

public sealed class LuaWhileStat(LuaExpr condition, LuaBlock body) : LuaStat
{
    public LuaExpr Condition { get; } = condition;

    public LuaBlock Body { get; } = body;
}

public sealed class LuaRepeatStat(LuaBlock body, LuaExpr condition) : LuaStat
{
    public LuaBlock Body { get; } = body;

    public LuaExpr Condition { get; } = condition;
}

public sealed class LuaNumericForStat(string name, LuaExpr start, LuaExpr limit, LuaExpr? step, LuaBlock body) : LuaStat
{
    public string Name { get; } = name;

    public LuaExpr Start { get; } = start;

    public LuaExpr Limit { get; } = limit;

    public LuaExpr? Step { get; } = step;

    public LuaBlock Body { get; } = body;
}

public sealed class LuaFunctionStat(LuaExpr target, LuaFunctionExpr function) : LuaStat
{
    public LuaExpr Target { get; } = target;

    public LuaFunctionExpr Function { get; } = function;
}

public sealed class LuaReturnStat(List<LuaExpr> values) : LuaStat
{
    public List<LuaExpr> Values { get; } = values;
}

public sealed class LuaDoStat(LuaBlock body) : LuaStat
{
    public LuaBlock Body { get; } = body;
}

public sealed class LuaBreakStat : LuaStat;
