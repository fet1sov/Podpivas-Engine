using System;
using System.Collections.Generic;

namespace PodpivasEngine.Scripting;

public sealed class LuaScope
{
    private readonly Dictionary<string, LuaValue> _variables = new();
    private readonly LuaScope? _parent;
    private readonly LuaTable? _globals;

    public LuaScope(LuaTable globals) => _globals = globals;

    private LuaScope(LuaScope parent)
    {
        _parent = parent;
        _globals = parent._globals;
    }

    public LuaScope CreateChild() => new(this);

    public LuaValue Get(string name)
    {
        for (var scope = this; scope is not null; scope = scope._parent)
        {
            if (scope._variables.TryGetValue(name, out var value))
                return value;
        }

        return _globals?.Get(name) ?? LuaValue.Nil;
    }

    public void Declare(string name, LuaValue value) => _variables[name] = value;

    public void Assign(string name, LuaValue value)
    {
        for (var scope = this; scope is not null; scope = scope._parent)
        {
            if (scope._variables.ContainsKey(name))
            {
                scope._variables[name] = value;
                return;
            }
        }

        _globals?.Set(name, value);
    }
}

internal sealed class LuaReturnSignal(LuaValue value) : Exception
{
    public LuaValue Value { get; } = value;
}

internal sealed class LuaBreakSignal : Exception;

internal sealed class LuaClosure(LuaInterpreter interpreter, LuaFunctionExpr function, LuaScope closure) : LuaCallable
{
    public LuaInterpreter Interpreter { get; } = interpreter;

    public LuaFunctionExpr Function { get; } = function;

    public LuaScope Closure { get; } = closure;

    public override string ToDisplay() => "function";

    public override LuaValue Invoke(LuaValue[] arguments) => Interpreter.InvokeClosure(this, arguments);
}

public sealed class LuaInterpreter
{
    private const int StepBudget = 400_000;

    private readonly LuaTable _globals;
    private int _budget = StepBudget;

    public LuaInterpreter(LuaTable globals) => _globals = globals;

    public LuaTable Globals => _globals;

    public void RunChunk(LuaBlock block)
    {
        _budget = StepBudget;
        ExecuteBlock(block, new LuaScope(_globals));
    }

    public LuaValue CallFunction(LuaCallable callable, LuaValue[] arguments)
    {
        _budget = StepBudget;
        return callable.Invoke(arguments);
    }

    internal LuaValue InvokeClosure(LuaClosure closure, LuaValue[] arguments)
    {
        var scope = closure.Closure.CreateChild();

        for (var i = 0; i < closure.Function.Parameters.Count; i++)
            scope.Declare(closure.Function.Parameters[i], i < arguments.Length ? arguments[i] : LuaValue.Nil);

        try
        {
            ExecuteBlock(closure.Function.Body, scope);
        }
        catch (LuaReturnSignal signal)
        {
            return signal.Value;
        }
        catch (LuaBreakSignal)
        {
            throw new LuaRuntimeException("break outside of a loop");
        }

        return LuaValue.Nil;
    }

    internal void ExecuteBlock(LuaBlock block, LuaScope scope)
    {
        foreach (var statement in block.Statements)
            ExecuteStatement(statement, scope);
    }

    private void ExecuteStatement(LuaStat statement, LuaScope scope)
    {
        if (--_budget < 0)
            throw new LuaRuntimeException("script exceeded the execution limit");

        switch (statement)
        {
            case LuaLocalStat local:
                var values = EvaluateList(local.Values, scope);
                for (var i = 0; i < local.Names.Count; i++)
                    scope.Declare(local.Names[i], i < values.Count ? values[i] : LuaValue.Nil);
                break;

            case LuaAssignStat assign:
                AssignTargets(assign, scope);
                break;

            case LuaCallStat call:
                Evaluate(call.Call, scope);
                break;

            case LuaIfStat ifStat:
                foreach (var (condition, body) in ifStat.Branches)
                {
                    if (!Evaluate(condition, scope).Truthy)
                        continue;

                    ExecuteBlock(body, scope.CreateChild());
                    return;
                }

                if (ifStat.ElseBody is not null)
                    ExecuteBlock(ifStat.ElseBody, scope.CreateChild());
                break;

            case LuaWhileStat whileStat:
                while (Evaluate(whileStat.Condition, scope).Truthy)
                {
                    if (--_budget < 0)
                        throw new LuaRuntimeException("script exceeded the execution limit");

                    try
                    {
                        ExecuteBlock(whileStat.Body, scope.CreateChild());
                    }
                    catch (LuaBreakSignal)
                    {
                        break;
                    }
                }
                break;

            case LuaRepeatStat repeatStat:
                var repeatScope = scope.CreateChild();
                while (true)
                {
                    if (--_budget < 0)
                        throw new LuaRuntimeException("script exceeded the execution limit");

                    try
                    {
                        ExecuteBlock(repeatStat.Body, repeatScope);
                    }
                    catch (LuaBreakSignal)
                    {
                        break;
                    }

                    if (Evaluate(repeatStat.Condition, repeatScope).Truthy)
                        break;
                }
                break;

            case LuaNumericForStat forStat:
                ExecuteNumericFor(forStat, scope);
                break;

            case LuaFunctionStat functionStat:
                var closure = new LuaClosure(this, functionStat.Function, scope);
                Assign(functionStat.Target, closure, scope);
                break;

            case LuaReturnStat returnStat:
                var result = EvaluateList(returnStat.Values, scope);
                throw new LuaReturnSignal(result.Count > 0 ? result[0] : LuaValue.Nil);

            case LuaDoStat doStat:
                ExecuteBlock(doStat.Body, scope.CreateChild());
                break;

            case LuaBreakStat:
                throw new LuaBreakSignal();
        }
    }

    private void ExecuteNumericFor(LuaNumericForStat forStat, LuaScope scope)
    {
        var start = Evaluate(forStat.Start, scope).ToNumber();
        var limit = Evaluate(forStat.Limit, scope).ToNumber();
        var step = forStat.Step is null ? 1 : Evaluate(forStat.Step, scope).ToNumber();

        if (step == 0)
            throw new LuaRuntimeException("'for' step is zero");

        for (var i = start; step >= 0 ? i <= limit : i >= limit; i += step)
        {
            if (--_budget < 0)
                throw new LuaRuntimeException("script exceeded the execution limit");

            var iterationScope = scope.CreateChild();
            iterationScope.Declare(forStat.Name, LuaValue.Of(i));

            try
            {
                ExecuteBlock(forStat.Body, iterationScope);
            }
            catch (LuaBreakSignal)
            {
                break;
            }
        }
    }

    private void AssignTargets(LuaAssignStat assign, LuaScope scope)
    {
        var values = EvaluateList(assign.Values, scope);

        for (var i = 0; i < assign.Targets.Count; i++)
        {
            var value = i < values.Count ? values[i] : LuaValue.Nil;
            Assign(assign.Targets[i], value, scope);
        }
    }

    private void Assign(LuaExpr target, LuaValue value, LuaScope scope)
    {
        switch (target)
        {
            case LuaNameExpr name:
                scope.Assign(name.Name, value);
                return;
            case LuaIndexExpr index:
                var container = Evaluate(index.Target, scope);
                if (container is not LuaTable table)
                    throw new LuaRuntimeException($"attempt to index a {container.TypeName} value");

                table.Set(Evaluate(index.Key, scope), value);
                return;
            default:
                throw new LuaRuntimeException("invalid assignment target");
        }
    }

    private List<LuaValue> EvaluateList(List<LuaExpr> expressions, LuaScope scope)
    {
        var values = new List<LuaValue>(expressions.Count);
        foreach (var expression in expressions)
            values.Add(Evaluate(expression, scope));
        return values;
    }

    internal LuaValue Evaluate(LuaExpr expression, LuaScope scope)
    {
        if (--_budget < 0)
            throw new LuaRuntimeException("script exceeded the execution limit");

        switch (expression)
        {
            case LuaLiteralExpr literal:
                return literal.Value;

            case LuaNameExpr name:
                return scope.Get(name.Name);

            case LuaFieldExpr field:
                return Evaluate(field.Value, scope);

            case LuaIndexExpr index:
                var container = Evaluate(index.Target, scope);
                if (container is not LuaTable indexedTable)
                    throw new LuaRuntimeException($"attempt to index a {container.TypeName} value");

                return indexedTable.Get(Evaluate(index.Key, scope));

            case LuaUnaryExpr unary:
                return EvaluateUnary(unary, scope);

            case LuaBinaryExpr binary:
                return EvaluateBinary(binary, scope);

            case LuaCallExpr call:
                var target = Evaluate(call.Target, scope);
                if (target is not LuaCallable callable)
                    throw new LuaRuntimeException($"attempt to call a {target.TypeName} value");

                return callable.Invoke(EvaluateList(call.Arguments, scope).ToArray());

            case LuaTableExpr table:
                var result = new LuaTable();
                foreach (var item in table.Items)
                    result.Add(Evaluate(item, scope));

                foreach (var (key, value) in table.Fields)
                    result.Set(Evaluate(key, scope), Evaluate(value, scope));

                return result;

            case LuaFunctionExpr function:
                return new LuaClosure(this, function, scope);
        }

        throw new LuaRuntimeException("unsupported expression");
    }

    private LuaValue EvaluateUnary(LuaUnaryExpr unary, LuaScope scope)
    {
        var operand = Evaluate(unary.Operand, scope);

        return unary.Op switch
        {
            "-" => LuaValue.Of(-operand.ToNumber()),
            "not" => LuaValue.Of(!operand.Truthy),
            "#" => operand switch
            {
                LuaTable table => LuaValue.Of(table.Length),
                LuaString text => LuaValue.Of(text.Value.Length),
                _ => throw new LuaRuntimeException($"attempt to get length of a {operand.TypeName} value"),
            },
            _ => throw new LuaRuntimeException($"unknown unary operator '{unary.Op}'"),
        };
    }

    private LuaValue EvaluateBinary(LuaBinaryExpr binary, LuaScope scope)
    {
        var left = Evaluate(binary.Left, scope);

        if (binary.Op == "and")
            return left.Truthy ? Evaluate(binary.Right, scope) : left;

        if (binary.Op == "or")
            return left.Truthy ? left : Evaluate(binary.Right, scope);

        var right = Evaluate(binary.Right, scope);

        switch (binary.Op)
        {
            case "..":
                return LuaValue.Of(left.ToLuaString() + right.ToLuaString());

            case "==":
                return LuaValue.Of(AreEqual(left, right));

            case "~=":
                return LuaValue.Of(!AreEqual(left, right));

            case "<" or ">" or "<=" or ">=":
                return LuaValue.Of(Compare(left, right, binary.Op));

            case "+":
                return LuaValue.Of(left.ToNumber() + right.ToNumber());

            case "-":
                return LuaValue.Of(left.ToNumber() - right.ToNumber());

            case "*":
                return LuaValue.Of(left.ToNumber() * right.ToNumber());

            case "/":
                return LuaValue.Of(left.ToNumber() / right.ToNumber());

            case "//":
                return LuaValue.Of(Math.Floor(left.ToNumber() / right.ToNumber()));

            case "%":
                var divisor = right.ToNumber();
                return LuaValue.Of(left.ToNumber() - Math.Floor(left.ToNumber() / divisor) * divisor);

            case "^":
                return LuaValue.Of(Math.Pow(left.ToNumber(), right.ToNumber()));
        }

        throw new LuaRuntimeException($"unknown binary operator '{binary.Op}'");
    }

    private static bool AreEqual(LuaValue left, LuaValue right) => (left, right) switch
    {
        (LuaNil, LuaNil) => true,
        (LuaBool a, LuaBool b) => a.Truthy == b.Truthy,
        (LuaNumber a, LuaNumber b) => a.Value.Equals(b.Value),
        (LuaString a, LuaString b) => a.Value == b.Value,
        _ => ReferenceEquals(left, right),
    };

    private static bool Compare(LuaValue left, LuaValue right, string op)
    {
        int comparison;

        if (left is LuaNumber a && right is LuaNumber b)
            comparison = a.Value.CompareTo(b.Value);
        else if (left is LuaString x && right is LuaString y)
            comparison = string.CompareOrdinal(x.Value, y.Value);
        else
            throw new LuaRuntimeException($"cannot compare {left.TypeName} with {right.TypeName}");

        return op switch
        {
            "<" => comparison < 0,
            ">" => comparison > 0,
            "<=" => comparison <= 0,
            _ => comparison >= 0,
        };
    }
}
