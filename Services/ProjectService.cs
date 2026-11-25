using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using PodpivasEngine.Models;

namespace PodpivasEngine.Services;

public static class ProjectService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string ProjectsRoot => Path.Combine(AppContext.BaseDirectory, "Projects");

    public static string SanitizeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "project" : cleaned;
    }

    public static EditorProject Create(string name, RenderBackend backend, ProjectSpace space)
    {
        var safeName = SanitizeName(name);
        var root = Path.Combine(ProjectsRoot, safeName);
        var suffix = 2;

        while (File.Exists(Path.Combine(root, "project.json")))
        {
            safeName = $"{SanitizeName(name)} {suffix++}";
            root = Path.Combine(ProjectsRoot, safeName);
        }

        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "assets"));
        Directory.CreateDirectory(Path.Combine(root, "scripts"));

        var project = new EditorProject(safeName, backend, space, root);
        BuildDefaultScene(project);
        WriteDefaultScript(project);
        Save(project);
        return project;
    }

    public static EditorProject CreateInMemory(string name, RenderBackend backend, ProjectSpace space)
    {
        var project = new EditorProject(name, backend, space, string.Empty);
        BuildDefaultScene(project, withScript: false);
        return project;
    }

    public static EditorProject SaveAs(EditorProject source, string directory)
    {
        Directory.CreateDirectory(directory);

        var assetsTarget = Path.Combine(directory, "assets");
        var scriptsTarget = Path.Combine(directory, "scripts");
        CopyFolder(source.AssetsPath, assetsTarget);
        CopyFolder(source.ScriptsPath, scriptsTarget);

        var target = new EditorProject(source.Name, source.Backend, source.Space, directory)
        {
            GameName = source.GameName,
            Version = source.Version,
            MainCamera = source.MainCamera,
            EditorCamera = source.EditorCamera,
        };
        foreach (var node in source.Nodes)
            target.Nodes.Add(node);

        Save(target);
        return target;
    }

    private static void CopyFolder(string source, string target)
    {
        Directory.CreateDirectory(target);

        if (!Directory.Exists(source))
            return;

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, true);
        }
    }

    public static void Save(EditorProject project)
    {
        Directory.CreateDirectory(project.RootPath);
        Directory.CreateDirectory(project.AssetsPath);
        Directory.CreateDirectory(project.ScriptsPath);

        var file = new ProjectFile
        {
            Name = project.Name,
            GameName = project.GameName,
            GameVersion = project.Version,
            MainCamera = project.MainCamera,
            EditorCamera = project.EditorCamera,
            Renderer = project.Backend.ToString(),
            Space = project.IsTwoD ? "2D" : "3D",
            Nodes = project.Nodes.Select(ToData).ToList(),
        };

        File.WriteAllText(project.ProjectFilePath, JsonSerializer.Serialize(file, JsonOptions));
    }

    public static EditorProject Open(string directory)
    {
        var filePath = Path.Combine(directory, "project.json");
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"project.json not found in {directory}", filePath);

        var file = JsonSerializer.Deserialize<ProjectFile>(File.ReadAllText(filePath), JsonOptions)
                   ?? throw new InvalidDataException("project.json is empty");

        var backend = Enum.TryParse<RenderBackend>(file.Renderer, true, out var parsedBackend)
            ? parsedBackend
            : RenderBackend.WebGPU;

        var space = string.Equals(file.Space, "2D", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(file.Space, nameof(ProjectSpace.TwoD), StringComparison.OrdinalIgnoreCase)
            ? ProjectSpace.TwoD
            : ProjectSpace.ThreeD;

        var project = new EditorProject(
            string.IsNullOrWhiteSpace(file.Name) ? Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar)) : file.Name,
            backend,
            space,
            directory);

        foreach (var data in file.Nodes)
            project.Nodes.Add(FromData(data));

        project.GameName = string.IsNullOrWhiteSpace(file.GameName) ? project.Name : file.GameName;
        project.Version = string.IsNullOrWhiteSpace(file.GameVersion) ? "0.1.0" : file.GameVersion;
        project.MainCamera = file.MainCamera;
        project.EditorCamera = file.EditorCamera;

        Directory.CreateDirectory(project.AssetsPath);
        Directory.CreateDirectory(project.ScriptsPath);
        return project;
    }

    public static IReadOnlyList<AssetEntry> ScanAssets(EditorProject project, bool includeModels)
    {
        var results = new List<AssetEntry>();
        ScanFolder(project.AssetsPath, project.AssetsPath, results, includeModels);
        ScanFolder(project.ScriptsPath, project.ScriptsPath, results, includeModels);
        return results;
    }

    private static void ScanFolder(string folder, string assetsRoot, List<AssetEntry> results, bool includeModels)
    {
        if (!Directory.Exists(folder))
            return;

        foreach (var directory in Directory.EnumerateDirectories(folder).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            results.Add(new AssetEntry(
                Path.GetFileName(directory),
                RelativeTo(assetsRoot, directory),
                directory,
                true,
                AssetKind.Folder));
            ScanFolder(directory, assetsRoot, results, includeModels);
        }

        foreach (var file in Directory.EnumerateFiles(folder).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var kind = AssetEntry.DetectKind(Path.GetExtension(file));
            if (!includeModels && kind == AssetKind.Model3D)
                continue;

            results.Add(new AssetEntry(
                Path.GetFileName(file),
                RelativeTo(assetsRoot, file),
                file,
                false,
                kind));
        }
    }

    private static string RelativeTo(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');

    private static void BuildDefaultScene(EditorProject project, bool withScript = true)
    {
        var player = new SceneNode("player", project.IsTwoD ? SceneNodeKind.Sprite : SceneNodeKind.Entity)
        {
            X = 0,
            Y = 0,
            Z = 0,
        };

        if (withScript)
            player.Components.Add(new ScriptComponent("player.lua"));

        var camera = new SceneNode("camera", SceneNodeKind.Camera)
        {
            X = project.IsTwoD ? 0 : 0,
            Y = project.IsTwoD ? 0 : 5,
            Z = project.IsTwoD ? 0 : 10,
            RotationX = project.IsTwoD ? 0 : -25,
        };

        var light = new SceneNode("sun", SceneNodeKind.Light)
        {
            X = -4,
            Y = project.IsTwoD ? -2 : 6,
        };

        project.Nodes.Add(player);
        project.Nodes.Add(camera);
        project.Nodes.Add(light);
        project.MainCamera = camera.Name;

        if (project.IsTwoD)
            project.Nodes.Add(new SceneNode("background", SceneNodeKind.Tileset) { ScaleX = 2, ScaleY = 2 });
        else
            project.Nodes.Add(new SceneNode("ground", SceneNodeKind.Model) { ScaleX = 4, ScaleY = 4, ScaleZ = 4 });
    }

    private static void WriteDefaultScript(EditorProject project)
    {
        var path = Path.Combine(project.ScriptsPath, "player.lua");
        if (File.Exists(path))
            return;

        var body = project.IsTwoD
            ? """
              -- @export Скорость
              local speed = 3

              function start()
                print("player ready")
              end

              function update(dt)
                if input.key("d") then self.x = self.x + speed * dt end
                if input.key("a") then self.x = self.x - speed * dt end
                if input.key("s") then self.y = self.y + speed * dt end
                if input.key("w") then self.y = self.y - speed * dt end
              end
              """
            : """
              -- @export Скорость
              local speed = 3

              function start()
                print("player ready")
              end

              function update(dt)
                if input.key("d") then self.x = self.x + speed * dt end
                if input.key("a") then self.x = self.x - speed * dt end
                if input.key("s") then self.z = self.z + speed * dt end
                if input.key("w") then self.z = self.z - speed * dt end
                if input.mouseDown("left") then self.rotation = self.rotation + 45 * dt end
              end
              """;

        File.WriteAllText(path, body.ReplaceLineEndings("\n"));
    }

    private static SceneNodeData ToData(SceneNode node) => new()
    {
        Name = node.Name,
        Kind = node.Kind.ToString(),
        X = node.X,
        Y = node.Y,
        Z = node.Z,
        Rotation = node.RotationY,
        RotationX = node.RotationX,
        RotationY = node.RotationY,
        RotationZ = node.RotationZ,
        Scale = node.ScaleX,
        ScaleX = node.ScaleX,
        ScaleY = node.ScaleY,
        ScaleZ = node.ScaleZ,
        Visible = node.Visible,
        Asset = node.Asset,
        Projection = node.Projection.ToString(),
        FieldOfView = node.FieldOfView,
        FovAxis = node.FovAxis.ToString(),
        Pitch = node.Pitch,
        Yaw = node.Yaw,
        Components = node.Components.Select(component => new ScriptComponentData
        {
            Script = component.Script,
            Values = new Dictionary<string, string>(component.Values),
        }).ToList(),
        Children = node.Children.Select(ToData).ToList(),
    };

    private static SceneNode FromData(SceneNodeData data)
    {
        var kind = Enum.TryParse<SceneNodeKind>(data.Kind, true, out var parsedKind)
            ? parsedKind
            : SceneNodeKind.Entity;

        var uniform = data.Scale > 0 ? data.Scale : 1;
        var node = new SceneNode(data.Name, kind)
        {
            X = data.X,
            Y = data.Y,
            Z = data.Z,
            RotationX = data.RotationX,
            RotationY = data.RotationY != 0 ? data.RotationY : data.Rotation,
            RotationZ = data.RotationZ,
            ScaleX = data.ScaleX > 0 ? data.ScaleX : uniform,
            ScaleY = data.ScaleY > 0 ? data.ScaleY : uniform,
            ScaleZ = data.ScaleZ > 0 ? data.ScaleZ : uniform,
            Visible = data.Visible,
            Asset = data.Asset,
            Projection = Enum.TryParse<CameraProjection>(data.Projection, true, out var projection)
                ? projection
                : CameraProjection.Perspective,
            FieldOfView = data.FieldOfView is > 0 and < 180 ? data.FieldOfView : 50,
            FovAxis = Enum.TryParse<CameraFovAxis>(data.FovAxis, true, out var fovAxis)
                ? fovAxis
                : CameraFovAxis.Vertical,
            Pitch = data.Pitch,
            Yaw = data.Yaw,
        };

        foreach (var component in data.Components)
        {
            if (string.IsNullOrWhiteSpace(component.Script))
                continue;

            var restored = new ScriptComponent(component.Script);
            foreach (var (name, value) in component.Values)
                restored.Values[name] = value;

            node.Components.Add(restored);
        }

        if (node.Components.Count == 0 && !string.IsNullOrWhiteSpace(data.Script))
            node.Components.Add(new ScriptComponent(data.Script));

        foreach (var child in data.Children)
            node.Children.Add(FromData(child));

        return node;
    }
}
