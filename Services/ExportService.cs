using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using PodpivasEngine.Models;
using PodpivasEngine.Scripting;

namespace PodpivasEngine.Services;

public static class ExportService
{
    public static string Export(EditorProject project, string target)
    {
        Directory.CreateDirectory(target);

        var scriptsDirectory = Path.Combine(target, "js");
        Directory.CreateDirectory(scriptsDirectory);

        CopyDirectory(project.AssetsPath, Path.Combine(target, "assets"));

        File.WriteAllText(Path.Combine(target, "index.html"), BuildHtml(project));
        File.WriteAllText(Path.Combine(scriptsDirectory, "config.js"), BuildConfig(project));
        File.WriteAllText(Path.Combine(scriptsDirectory, "lua.js"), LuaRuntime);
        File.WriteAllText(Path.Combine(scriptsDirectory, "meshes.js"), BuildMeshes(project));
        File.WriteAllText(Path.Combine(scriptsDirectory, "engine.js"), EngineRuntime);
        File.WriteAllText(Path.Combine(scriptsDirectory, "scripts.js"), BuildScripts(project));

        return target;
    }

    private static string BuildHtml(EditorProject project)
    {
        var name = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(project.GameName) ? project.Name : project.GameName);

        return $$"""
                 <!DOCTYPE html>
                 <html lang="ru">
                 <head>
                   <meta charset="utf-8" />
                   <meta name="viewport" content="width=device-width, initial-scale=1" />
                   <title>{{name}}</title>
                   <style>
                     html, body { margin: 0; height: 100%; background: #080808; overflow: hidden; }
                     body { font-family: Inter, "Segoe UI", system-ui, sans-serif; }
                     canvas { display: block; width: 100%; height: 100%; }
                   </style>
                 </head>
                 <body>
                   <canvas id="game"></canvas>
                   <script src="js/lua.js"></script>
                   <script src="js/config.js"></script>
                   <script src="js/meshes.js"></script>
                   <script src="js/engine.js"></script>
                   <script src="js/scripts.js"></script>
                 </body>
                 </html>
                 """;
    }

    private static string BuildConfig(EditorProject project)
    {
        var counter = new ComponentCounter();
        var config = new Dictionary<string, object?>
        {
            ["name"] = string.IsNullOrWhiteSpace(project.GameName) ? project.Name : project.GameName,
            ["version"] = project.Version,
            ["renderer"] = project.Backend.ToString(),
            ["space"] = project.IsTwoD ? "2D" : "3D",
            ["mainCamera"] = project.MainCamera,
            ["scene"] = project.Nodes.Select(node => ToNode(node, counter)).ToList(),
        };

        return "window.GAME = "
               + JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true })
               + ";\n";
    }

    private sealed class ComponentCounter
    {
        public int Value { get; set; }
    }

    private static Dictionary<string, object?> ToNode(SceneNode node, ComponentCounter counter)
    {
        var components = new List<Dictionary<string, object?>>();
        foreach (var component in node.Components)
        {
            components.Add(new Dictionary<string, object?>
            {
                ["id"] = "c" + counter.Value++,
                ["script"] = component.Script,
            });
        }

        return new Dictionary<string, object?>
        {
            ["name"] = node.Name,
            ["kind"] = node.Kind.ToString(),
            ["x"] = node.X,
            ["y"] = node.Y,
            ["z"] = node.Z,
            ["rotation"] = node.RotationY,
            ["rotationX"] = node.RotationX,
            ["rotationY"] = node.RotationY,
            ["rotationZ"] = node.RotationZ,
            ["scale"] = node.ScaleX,
            ["scaleX"] = node.ScaleX,
            ["scaleY"] = node.ScaleY,
            ["scaleZ"] = node.ScaleZ,
            ["visible"] = node.Visible,
            ["asset"] = node.Asset,
            ["projection"] = node.Projection.ToString(),
            ["fov"] = node.FieldOfView,
            ["fovAxis"] = node.FovAxis.ToString(),
            ["pitch"] = node.Pitch,
            ["yaw"] = node.Yaw,

            ["components"] = components,
            ["children"] = node.Children.Select(child => ToNode(child, counter)).ToList(),
        };
    }

    private static string BuildMeshes(EditorProject project)
    {
        var meshes = new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase);

        if (!project.IsTwoD)
        {
            foreach (var node in Enumerate(project.Nodes))
            {
                if (string.IsNullOrWhiteSpace(node.Asset) || meshes.ContainsKey(node.Asset))
                    continue;

                if (AssetEntry.DetectKind(Path.GetExtension(node.Asset)) != AssetKind.Model3D)
                    continue;

                var source = Path.Combine(project.AssetsPath, node.Asset.Replace('/', Path.DirectorySeparatorChar));
                if (!MeshLibrary.TryLoad(source, out var geometry))
                    continue;

                var values = new float[geometry.Triangles.Length * 3];
                for (var index = 0; index < geometry.Triangles.Length; index++)
                {
                    var vertex = geometry.Triangles[index];
                    values[index * 3] = MathF.Round(vertex.X, 3);
                    values[index * 3 + 1] = MathF.Round(vertex.Y, 3);
                    values[index * 3 + 2] = MathF.Round(vertex.Z, 3);
                }

                meshes[node.Asset] = values;
            }
        }

        return "window.MESHES = " + JsonSerializer.Serialize(meshes) + ";\n";
    }

    private static IEnumerable<SceneNode> Enumerate(IEnumerable<SceneNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;

            foreach (var child in Enumerate(node.Children))
                yield return child;
        }
    }

    private static string BuildScripts(EditorProject project)
    {
        var builder = new StringBuilder();
        builder.AppendLine("window.SCRIPTS = window.SCRIPTS || {};");

        var counter = new ComponentCounter();
        foreach (var node in project.Nodes)
            AppendScripts(project, node, counter, builder);

        return builder.ToString();
    }

    private static void AppendScripts(EditorProject project, SceneNode node, ComponentCounter counter, StringBuilder builder)
    {
        foreach (var component in node.Components)
        {
            var id = "c" + counter.Value++;
            if (string.IsNullOrWhiteSpace(component.Script))
                continue;

            var path = Path.Combine(project.ScriptsPath, component.Script);

            try
            {
                if (!File.Exists(path))
                    throw new FileNotFoundException($"script not found: {component.Script}");

                var source = LuaExports.ApplyValues(File.ReadAllText(path), component.Values);
                var body = LuaTranspiler.Transpile(source);

                builder.AppendLine($"window.SCRIPTS[{JsonSerializer.Serialize(id)}] = function () {{");
                builder.AppendLine("  \"use strict\";");
                builder.AppendLine("  var _L = window.LUA;");
                builder.AppendLine("  var _G = { self: null, input: window.INPUT, find: window.FIND, math: window.LUA.math, print: window.PRINT, log: window.LOG, delta: 0, time: 0 };");
                builder.AppendLine(body);
                builder.AppendLine("  return {");
                builder.AppendLine("    setSelf: function (value) { _G.self = value; },");
                builder.AppendLine("    setTime: function (delta, clock) { _G.delta = delta; _G.time = clock; },");
                builder.AppendLine("    start: _G[\"start\"],");
                builder.AppendLine("    update: _G[\"update\"]");
                builder.AppendLine("  };");
                builder.AppendLine("};");
            }
            catch (Exception exception)
            {
                var message = $"{node.Name}/{component.Script}: {exception.Message}";
                builder.AppendLine($"console.error({JsonSerializer.Serialize(message)});");
            }
        }

        foreach (var child in node.Children)
            AppendScripts(project, child, counter, builder);
    }

    private static void CopyDirectory(string source, string target)
    {
        if (!Directory.Exists(source))
            return;

        if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
            return;

        Directory.CreateDirectory(target);

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).ToList())
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, true);
        }
    }

    private const string LuaRuntime = """
                                      (function () {
                                        function truthy(value) { return value !== null && value !== undefined && value !== false; }
                                        function luaToString(value) { return value === null || value === undefined ? "nil" : String(value); }

                                        window.LUA = {
                                          truthy: truthy,
                                          not: function (value) { return !truthy(value); },
                                          and: function (a, b) { return truthy(a) ? b : a; },
                                          or: function (a, b) { return truthy(a) ? a : b; },
                                          eq: function (a, b) { return a === b; },
                                          concat: function (a, b) { return luaToString(a) + luaToString(b); },
                                          mod: function (a, b) { return a - Math.floor(a / b) * b; },
                                          len: function (value) {
                                            if (typeof value === "string") return value.length;
                                            if (value === null || value === undefined) return 0;
                                            var count = 0;
                                            while (Object.prototype.hasOwnProperty.call(value, count + 1)) count++;
                                            return count;
                                          },
                                          index: function (target, key) {
                                            if (target === null || target === undefined) throw new Error("attempt to index a nil value");
                                            return target[key];
                                          },
                                          setIndex: function (target, key, value) {
                                            if (target === null || target === undefined) throw new Error("attempt to index a nil value");
                                            target[key] = value;
                                          },
                                          call: function (fn, args) {
                                            if (typeof fn !== "function") throw new Error("attempt to call a non-function value");
                                            return fn.apply(null, args);
                                          },
                                          math: {
                                            pi: Math.PI,
                                            floor: Math.floor,
                                            ceil: Math.ceil,
                                            abs: Math.abs,
                                            sqrt: Math.sqrt,
                                            sin: Math.sin,
                                            cos: Math.cos,
                                            tan: Math.tan,
                                            atan2: function (y, x) { return Math.atan2(y, x); },
                                            min: function () { return Math.min.apply(null, arguments); },
                                            max: function () { return Math.max.apply(null, arguments); },
                                            random: function (a, b) {
                                              if (a === undefined) return Math.random();
                                              if (b === undefined) return Math.floor(Math.random() * a) + 1;
                                              return Math.floor(Math.random() * (b - a + 1)) + a;
                                            }
                                          }
                                        };

                                        window.PRINT = function () {
                                          var parts = [];
                                          for (var i = 0; i < arguments.length; i++) parts.push(luaToString(arguments[i]));
                                          console.log(parts.join("\t"));
                                        };

                                        function channel(value) {
                                          var clamped = Math.max(0, Math.min(255, Math.round(Number(value)) || 0));
                                          return (clamped < 16 ? "0" : "") + clamped.toString(16);
                                        }

                                        function hexColor(value) {
                                          var text = String(value).trim().replace(/^#/, "");
                                          if (!/^(?:[0-9a-f]{3}|[0-9a-f]{6})$/i.test(text)) return null;
                                          if (text.length === 3) {
                                            text = text.split("").map(function (digit) { return digit + digit; }).join("");
                                          }
                                          return "#" + text;
                                        }

                                        window.LOG = function () {
                                          if (arguments.length === 0) return;

                                          var text = luaToString(arguments[0]);
                                          var color = null;

                                          if (arguments.length === 4
                                              && typeof arguments[1] === "number"
                                              && typeof arguments[2] === "number"
                                              && typeof arguments[3] === "number") {
                                            color = "#" + channel(arguments[1]) + channel(arguments[2]) + channel(arguments[3]);
                                          } else if (arguments.length === 2 && typeof arguments[1] === "string") {
                                            color = hexColor(arguments[1]);
                                          }

                                          if (color) console.log("%c" + text, "color:" + color);
                                          else console.log(text);
                                        };
                                      })();
                                      """;

    private static string? _engineRuntime;

    private static string EngineRuntime => _engineRuntime ??= LoadEngineRuntime();

    private static string LoadEngineRuntime()
    {
        var assembly = typeof(ExportService).Assembly;
        using var stream = assembly.GetManifestResourceStream("PodpivasEngine.Runtime.engine.js")
                           ?? throw new InvalidOperationException("engine.js resource is missing");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

}
