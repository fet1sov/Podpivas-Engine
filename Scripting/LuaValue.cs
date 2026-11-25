using System.Globalization;

namespace PodpivasEngine.Scripting;

public class LuaRuntimeException(string message) : System.Exception(message);

public abstract class LuaValue
{
    public static readonly LuaValue Nil = new LuaNil();
    public static readonly LuaValue True = new LuaBool(true);
    public static readonly LuaValue False = new LuaBool(false);

    public abstract string TypeName { get; }

    public virtual bool Truthy => true;

    public static LuaValue Of(double value) => new LuaNumber(value);

    public static LuaValue Of(string value) => new LuaString(value);

    public static LuaValue Of(bool value) => value ? True : False;

    public virtual double ToNumber() =>
        throw new LuaRuntimeException($"attempt to perform arithmetic on a {TypeName} value");

    public virtual string ToDisplay() => TypeName;

    public string ToLuaString() => this is LuaString s ? s.Value : ToDisplay();
}

internal sealed class LuaNil : LuaValue
{
    public override string TypeName => "nil";

    public override bool Truthy => false;

    public override string ToDisplay() => "nil";
}

internal sealed class LuaBool(bool value) : LuaValue
{
    public override string TypeName => "boolean";

    public override bool Truthy => value;

    public override string ToDisplay() => value ? "true" : "false";
}

internal sealed class LuaNumber(double value) : LuaValue
{
    public double Value { get; } = value;

    public override string TypeName => "number";

    public override double ToNumber() => Value;

    public override string ToDisplay()
    {
        if (Value == System.Math.Floor(Value) && System.Math.Abs(Value) < 1e15)
            return ((long)Value).ToString(CultureInfo.InvariantCulture);

        return Value.ToString("R", CultureInfo.InvariantCulture);
    }
}

internal sealed class LuaString(string value) : LuaValue
{
    public string Value { get; } = value;

    public override string TypeName => "string";

    public override string ToDisplay() => Value;

    public override double ToNumber() =>
        double.TryParse(Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new LuaRuntimeException($"attempt to perform arithmetic on a string value ('{Value}')");
}

public abstract class LuaCallable : LuaValue
{
    public override string TypeName => "function";

    public abstract LuaValue Invoke(LuaValue[] arguments);
}

public sealed class LuaNativeFunction(string name, System.Func<LuaValue[], LuaValue> body) : LuaCallable
{
    public override string ToDisplay() => $"function: {name}";

    public override LuaValue Invoke(LuaValue[] arguments) => body(arguments);
}

public class LuaTable : LuaValue
{
    private readonly System.Collections.Generic.Dictionary<string, LuaValue> _fields = new();
    private readonly System.Collections.Generic.List<LuaValue> _items = [];

    public override string TypeName => "table";

    public int Length
    {
        get
        {
            var count = 0;
            while (count < _items.Count && _items[count] is not LuaNil)
                count++;
            return count;
        }
    }

    public virtual LuaValue Get(LuaValue key)
    {
        switch (key)
        {
            case LuaNumber number:
                var index = (int)number.Value;
                if (index >= 1 && index <= _items.Count)
                    return _items[index - 1];
                return LuaValue.Nil;
            case LuaString text:
                return _fields.TryGetValue(text.Value, out var value) ? value : LuaValue.Nil;
            default:
                return LuaValue.Nil;
        }
    }

    public virtual void Set(LuaValue key, LuaValue value)
    {
        switch (key)
        {
            case LuaNumber number:
                var index = (int)number.Value;
                if (index < 1)
                    return;
                while (_items.Count < index)
                    _items.Add(LuaValue.Nil);
                _items[index - 1] = value;
                return;
            case LuaString text:
                _fields[text.Value] = value;
                return;
        }
    }

    public void Set(string key, LuaValue value) => _fields[key] = value;

    public LuaValue Get(string key) => _fields.TryGetValue(key, out var value) ? value : LuaValue.Nil;

    public void Set(double index, LuaValue value) => Set(LuaValue.Of(index), value);

    public void Add(LuaValue value) => _items.Add(value);

    public override string ToDisplay() => "table";

    public System.Collections.Generic.IEnumerable<LuaValue> Items => _items;

    public System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<string, LuaValue>> Fields =>
        _fields;
}
