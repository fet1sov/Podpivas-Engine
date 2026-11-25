using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PodpivasEngine.Models;
using PodpivasEngine.Scripting;

namespace PodpivasEngine.Runtime;

public sealed class ScriptRuntime
{
    private sealed class ScriptInstance
    {
        public required string FileName { get; init; }

        public required LuaTable Globals { get; init; }

        public required LuaInterpreter Interpreter { get; init; }

        public LuaCallable? Start { get; init; }

        public LuaCallable? Update { get; init; }

        public bool Failed { get; set; }
    }

    private readonly EditorProject _project;
    private readonly Action<string, string?> _log;
    private readonly List<ScriptInstance> _scripts = [];
    private readonly LuaTable _inputProxy;
    private readonly LuaTable _math;
    private readonly Random _random = new();

    public ScriptRuntime(EditorProject project, Action<string, string?> log)
    {
        _project = project;
        _log = log;
        _inputProxy = new LuaInputProxy(Input);
        _math = CreateMathTable();
        BuildObjects();
    }

    public List<LiveObject> Objects { get; } = [];

    public RuntimeInput Input { get; } = new();

    public double Time { get; private set; }

    public ProjectSpace Space => _project.Space;

    public string AssetsPath => _project.AssetsPath;

    public ISceneRenderable? MainCamera { get; private set; }

    public bool HasScriptErrors { get; private set; }

    public void RebuildObjects()
    {
        Objects.Clear();
        BuildObjects();
    }

    public void Reset()
    {
        RebuildObjects();
        _scripts.Clear();
        Input.Clear();
        Time = 0;
        HasScriptErrors = false;
    }

    public void Start()
    {
        Reset();

        foreach (var node in EnumerateProjectNodes(_project.Nodes))
        {
            foreach (var component in node.Components)
            {
                if (!string.IsNullOrWhiteSpace(component.Script))
                    LoadScript(node, component);
            }
        }
    }

    public void Update(double deltaSeconds)
    {
        Time += deltaSeconds;

        foreach (var script in _scripts)
        {
            if (script.Failed || script.Update is null)
                continue;

            script.Globals.Set("delta", LuaValue.Of(deltaSeconds));
            script.Globals.Set("time", LuaValue.Of(Time));

            try
            {
                script.Interpreter.CallFunction(script.Update, [LuaValue.Of(deltaSeconds)]);
            }
            catch (LuaRuntimeException exception)
            {
                Fail(script, exception.Message);
            }
            catch (Exception exception)
            {
                Fail(script, exception.Message);
            }
        }

        Input.EndFrame();
    }

    public void SetKey(string key, bool isDown) => Input.SetKey(key, isDown);

    public void SetMouse(double x, double y) => Input.SetMousePosition(x, y);

    public void SetButton(string button, bool isDown) => Input.SetButton(button, isDown);

    public LiveObject? FindObject(string name) =>
        Objects.FirstOrDefault(node => string.Equals(node.Name, name, StringComparison.Ordinal));

    private void LoadScript(SceneNode node, ScriptComponent component)
    {
        var label = $"{node.Name}/{component.Script}";
        var path = Path.Combine(_project.ScriptsPath, component.Script);
        if (!File.Exists(path))
        {
            _log($"script not found: {component.Script}", null);
            HasScriptErrors = true;
            return;
        }

        var target = FindObject(node.Name);
        if (target is null)
            return;

        try
        {
            var globals = new LuaTable();
            globals.Set("self", new LuaNodeProxy(target));
            globals.Set("input", _inputProxy);
            globals.Set("find", new LuaNativeFunction("find", FindFunction));
            globals.Set("print", new LuaNativeFunction("print", PrintFunction));
            globals.Set("log", new LuaNativeFunction("log", LogFunction));
            globals.Set("tostring", new LuaNativeFunction("tostring", args =>
                LuaValue.Of(args.Length > 0 ? args[0].ToLuaString() : "nil")));
            globals.Set("tonumber", new LuaNativeFunction("tonumber", args =>
                args.Length > 0 ? LuaValue.Of(args[0].ToNumber()) : LuaValue.Nil));
            globals.Set("math", _math);
            globals.Set("delta", LuaValue.Of(0));
            globals.Set("time", LuaValue.Of(0));

            var source = LuaExports.ApplyValues(File.ReadAllText(path), component.Values);
            var chunk = new LuaChunk(source);
            var interpreter = new LuaInterpreter(globals);
            chunk.Run(interpreter);

            var script = new ScriptInstance
            {
                FileName = label,
                Globals = globals,
                Interpreter = interpreter,
                Start = globals.Get("start") as LuaCallable,
                Update = globals.Get("update") as LuaCallable,
            };

            _scripts.Add(script);

            if (script.Start is not null)
                interpreter.CallFunction(script.Start, []);
        }
        catch (LuaSyntaxException exception)
        {
            HasScriptErrors = true;
            _log($"{label}: {exception.Message}", null);
        }
        catch (LuaRuntimeException exception)
        {
            HasScriptErrors = true;
            _log($"{label}: {exception.Message}", null);
        }
        catch (Exception exception)
        {
            HasScriptErrors = true;
            _log($"{label}: {exception.Message}", null);
        }
    }

    private void Fail(ScriptInstance script, string message)
    {
        script.Failed = true;
        HasScriptErrors = true;
        _log($"{script.FileName}: {message}", null);
    }

    private LuaValue FindFunction(LuaValue[] arguments)
    {
        if (arguments.Length == 0)
            return LuaValue.Nil;

        var found = FindObject(arguments[0].ToLuaString());
        return found is null ? LuaValue.Nil : new LuaNodeProxy(found);
    }

    private LuaValue PrintFunction(LuaValue[] arguments)
    {
        var text = string.Join("\t", arguments.Select(argument => argument.ToDisplay()));
        _log(text, null);
        return LuaValue.Nil;
    }

    private LuaValue LogFunction(LuaValue[] arguments)
    {
        if (arguments.Length == 0)
            return LuaValue.Nil;

        var color = ResolveColor(arguments, out var textCount) ?? "#FFFFFF";
        var text = string.Join("\t", arguments.Take(textCount).Select(argument => argument.ToDisplay()));
        _log(text, color);
        return LuaValue.Nil;
    }

    private static string? ResolveColor(LuaValue[] arguments, out int textCount)
    {
        textCount = arguments.Length;

        if (arguments.Length == 4
            && arguments[1] is LuaNumber red
            && arguments[2] is LuaNumber green
            && arguments[3] is LuaNumber blue)
        {
            textCount = 1;
            return $"#{Channel(red.Value):X2}{Channel(green.Value):X2}{Channel(blue.Value):X2}";
        }

        if (arguments.Length == 2 && arguments[1] is LuaString hex && TryParseHex(hex.Value, out var normalized))
        {
            textCount = 1;
            return normalized;
        }

        return null;
    }

    private static int Channel(double value) => (int)Math.Clamp(value, 0, 255);

    private static bool TryParseHex(string raw, out string hex)
    {
        hex = string.Empty;

        var text = raw.Trim().TrimStart('#');
        if (text.Length is not (3 or 6) || !text.All(char.IsAsciiHexDigit))
            return false;

        if (text.Length == 3)
            text = string.Concat(text.Select(character => new string(character, 2)));

        hex = "#" + text.ToUpperInvariant();
        return true;
    }

    private void BuildObjects()
    {
        foreach (var node in EnumerateProjectNodes(_project.Nodes))
        {
            Objects.Add(new LiveObject
            {
                Name = node.Name,
                Kind = node.Kind,
                X = node.X,
                Y = node.Y,
                Z = node.Z,
                RotationX = node.RotationX,
                RotationY = node.RotationY,
                RotationZ = node.RotationZ,
                ScaleX = node.ScaleX,
                ScaleY = node.ScaleY,
                ScaleZ = node.ScaleZ,
                Visible = node.Visible,
                Asset = node.Asset,
                Projection = node.Projection,
                FieldOfView = node.FieldOfView,
                FovAxis = node.FovAxis,
                Pitch = node.Pitch,
                Yaw = node.Yaw,
            });
        }

        MainCamera = string.IsNullOrWhiteSpace(_project.MainCamera)
            ? null
            : Objects.FirstOrDefault(node => node.Kind == SceneNodeKind.Camera
                                             && string.Equals(node.Name, _project.MainCamera, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<SceneNode> EnumerateProjectNodes(IEnumerable<SceneNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;

            foreach (var child in EnumerateProjectNodes(node.Children))
                yield return child;
        }
    }

    private LuaTable CreateMathTable()
    {
        var math = new LuaTable();
        math.Set("pi", LuaValue.Of(Math.PI));
        math.Set("floor", new LuaNativeFunction("math.floor", args => LuaValue.Of(Math.Floor(Arg(args)))));
        math.Set("ceil", new LuaNativeFunction("math.ceil", args => LuaValue.Of(Math.Ceiling(Arg(args)))));
        math.Set("abs", new LuaNativeFunction("math.abs", args => LuaValue.Of(Math.Abs(Arg(args)))));
        math.Set("sqrt", new LuaNativeFunction("math.sqrt", args => LuaValue.Of(Math.Sqrt(Arg(args)))));
        math.Set("sin", new LuaNativeFunction("math.sin", args => LuaValue.Of(Math.Sin(Arg(args)))));
        math.Set("cos", new LuaNativeFunction("math.cos", args => LuaValue.Of(Math.Cos(Arg(args)))));
        math.Set("tan", new LuaNativeFunction("math.tan", args => LuaValue.Of(Math.Tan(Arg(args)))));
        math.Set("atan2", new LuaNativeFunction("math.atan2", args =>
            LuaValue.Of(Math.Atan2(args.Length > 0 ? args[0].ToNumber() : 0, args.Length > 1 ? args[1].ToNumber() : 0))));
        math.Set("min", new LuaNativeFunction("math.min", args =>
            LuaValue.Of(args.Length == 0 ? 0 : args.Min(argument => argument.ToNumber()))));
        math.Set("max", new LuaNativeFunction("math.max", args =>
            LuaValue.Of(args.Length == 0 ? 0 : args.Max(argument => argument.ToNumber()))));
        math.Set("random", new LuaNativeFunction("math.random", args =>
        {
            if (args.Length == 0)
                return LuaValue.Of(_random.NextDouble());

            var min = args.Length >= 2 ? (int)args[0].ToNumber() : 1;
            var max = (int)args[^1].ToNumber();
            return LuaValue.Of(min > max ? min : _random.Next(min, max + 1));
        }));

        return math;
    }

    private static double Arg(LuaValue[] arguments) => arguments.Length > 0 ? arguments[0].ToNumber() : 0;

    private sealed class LuaNodeProxy(LiveObject node) : LuaTable
    {
        public override LuaValue Get(LuaValue key)
        {
            if (key is LuaString name)
            {
                return name.Value switch
                {
                    "x" => LuaValue.Of(node.X),
                    "y" => LuaValue.Of(node.Y),
                    "z" => LuaValue.Of(node.Z),
                    "rotation" => LuaValue.Of(node.RotationY),
                    "rotationX" => LuaValue.Of(node.RotationX),
                    "rotationY" => LuaValue.Of(node.RotationY),
                    "rotationZ" => LuaValue.Of(node.RotationZ),
                    "scale" => LuaValue.Of(node.ScaleX),
                    "scaleX" => LuaValue.Of(node.ScaleX),
                    "scaleY" => LuaValue.Of(node.ScaleY),
                    "scaleZ" => LuaValue.Of(node.ScaleZ),
                    "visible" => LuaValue.Of(node.Visible),
                    "name" => LuaValue.Of(node.Name),
                    "asset" => LuaValue.Of(node.Asset),
                    "pitch" => LuaValue.Of(node.Pitch),
                    "yaw" => LuaValue.Of(node.Yaw),
                    _ => base.Get(key),
                };
            }

            return base.Get(key);
        }

        public override void Set(LuaValue key, LuaValue value)
        {
            if (key is LuaString name)
            {
                switch (name.Value)
                {
                    case "x":
                        node.X = value.ToNumber();
                        return;
                    case "y":
                        node.Y = value.ToNumber();
                        return;
                    case "z":
                        node.Z = value.ToNumber();
                        return;
                    case "rotation":
                        node.RotationY = value.ToNumber();
                        return;
                    case "rotationX":
                        node.RotationX = value.ToNumber();
                        return;
                    case "rotationY":
                        node.RotationY = value.ToNumber();
                        return;
                    case "rotationZ":
                        node.RotationZ = value.ToNumber();
                        return;
                    case "scale":
                        node.ScaleX = value.ToNumber();
                        node.ScaleY = node.ScaleX;
                        node.ScaleZ = node.ScaleX;
                        return;
                    case "scaleX":
                        node.ScaleX = value.ToNumber();
                        return;
                    case "scaleY":
                        node.ScaleY = value.ToNumber();
                        return;
                    case "scaleZ":
                        node.ScaleZ = value.ToNumber();
                        return;
                    case "visible":
                        node.Visible = value.Truthy;
                        return;
                    case "asset":
                        node.Asset = value.ToLuaString();
                        return;
                    case "pitch":
                        node.Pitch = value.ToNumber();
                        return;
                    case "yaw":
                        node.Yaw = value.ToNumber();
                        return;
                }
            }

            base.Set(key, value);
        }
    }

    private sealed class LuaInputProxy(RuntimeInput input) : LuaTable
    {
        public override LuaValue Get(LuaValue key)
        {
            if (key is not LuaString name)
                return base.Get(key);

            return name.Value switch
            {
                "key" => new LuaNativeFunction("input.key", args => LuaValue.Of(input.IsDown(Key(args)))),
                "pressed" => new LuaNativeFunction("input.pressed", args => LuaValue.Of(input.WasPressed(Key(args)))),
                "released" => new LuaNativeFunction("input.released", args => LuaValue.Of(input.WasReleased(Key(args)))),
                "mouseDown" => new LuaNativeFunction("input.mouseDown", args => LuaValue.Of(input.IsButtonDown(Key(args)))),
                "click" => new LuaNativeFunction("input.click", args => LuaValue.Of(input.WasButtonPressed(Key(args)))),
                "mouseX" => LuaValue.Of(input.MouseX),
                "mouseY" => LuaValue.Of(input.MouseY),
                "left" => LuaValue.Of(input.IsButtonDown("left")),
                "right" => LuaValue.Of(input.IsButtonDown("right")),
                "middle" => LuaValue.Of(input.IsButtonDown("middle")),
                _ => base.Get(key),
            };
        }

        private static string Key(LuaValue[] arguments) =>
            arguments.Length > 0 ? arguments[0].ToLuaString().Trim().ToLowerInvariant() : string.Empty;
    }
}
