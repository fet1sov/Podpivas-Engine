using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PodpivasEngine.Localization;
using PodpivasEngine.Models;
using PodpivasEngine.Services;

namespace PodpivasEngine.ViewModels;

public sealed partial class AssetBrowserViewModel : ViewModelBase
{
    private const string ScriptTemplate = """
        -- @export Speed
        local speed = 3

        function start()
          log("ready", "#FFFFFF")
        end

        function update(dt)
        end
        """;

    [ObservableProperty]
    private AssetEntry? _selected;

    [ObservableProperty]
    private string _rootPath = string.Empty;

    public ObservableCollection<AssetEntry> Items { get; } = [];

    public bool HasItems => Items.Count > 0;

    public EditorProject? Project { get; private set; }

    public bool CanCreate => Project is { } project && !string.IsNullOrEmpty(project.RootPath);

    public bool CanCreateModels => CanCreate && Project is { IsTwoD: false };

    public Action<string>? Log { get; set; }

    public event Action<AssetEntry>? Activated;

    public event Action<AssetEntry>? ApplyRequested;

    public event Action<AssetEntry>? PreviewRequested;

    public event Action<AssetEntry>? AddToSceneRequested;

    public void Load(EditorProject project)
    {
        Project = project;
        RootPath = project.AssetsPath;
        Reload();
    }

    public void Reload()
    {
        Items.Clear();
        Selected = null;

        if (Project is not null)
        {
            foreach (var entry in ProjectService.ScanAssets(Project, includeModels: !Project.IsTwoD))
            {
                if (!entry.IsFolder)
                    Items.Add(entry);
            }
        }

        OnPropertyChanged(nameof(HasItems));
    }

    public AssetEntry? FindByPath(string path) =>
        Items.FirstOrDefault(item => string.Equals(item.FullPath, path, StringComparison.OrdinalIgnoreCase));

    public void Report(string message) => Log?.Invoke(message);

    public void Activate(AssetEntry entry)
    {
        Selected = entry;
        Activated?.Invoke(entry);
    }

    public void RequestApply(AssetEntry entry) => ApplyRequested?.Invoke(entry);

    public void RequestPreview(AssetEntry entry) => PreviewRequested?.Invoke(entry);

    public void RequestAddToScene(AssetEntry entry) => AddToSceneRequested?.Invoke(entry);

    public IReadOnlyList<AssetEntry> ImportFiles(IEnumerable<string> paths)
    {
        var imported = new List<AssetEntry>();

        if (Project is not { } project || string.IsNullOrEmpty(project.RootPath))
            return imported;

        var assetsPath = project.AssetsPath;
        Directory.CreateDirectory(assetsPath);

        foreach (var path in paths.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var source = Path.GetFullPath(path);
                if (IsInside(assetsPath, source))
                    continue;

                var target = UniqueTarget(assetsPath, Path.GetFileName(source));
                File.Copy(source, target);

                imported.Add(new AssetEntry(
                    Path.GetFileName(target),
                    Path.GetFileName(target),
                    target,
                    false,
                    AssetEntry.DetectKind(Path.GetExtension(target))));
            }
            catch (Exception exception)
            {
                Report(Localizer.Format("Assets.CreateFailed", exception.Message));
            }
        }

        if (imported.Count == 0)
            return imported;

        Reload();
        Report(Localizer.Format("Assets.Imported", imported.Count));
        Selected = FindByPath(imported[0].FullPath);
        return imported;
    }

    public AssetEntry? CreateScript(string name)
    {
        if (Project is not { } project || string.IsNullOrEmpty(project.RootPath))
        {
            Report(Localizer.Get("Assets.NoProject"));
            return null;
        }

        var stem = string.IsNullOrWhiteSpace(name) ? "script" : ProjectService.SanitizeName(name);
        var fileName = stem.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)
            ? stem
            : $"{Path.GetFileNameWithoutExtension(stem)}.lua";

        try
        {
            Directory.CreateDirectory(project.ScriptsPath);
            var path = Path.Combine(project.ScriptsPath, fileName);

            if (File.Exists(path))
            {
                Report(Localizer.Format("Assets.ScriptExists", fileName));
                Selected = FindByPath(path);
                return null;
            }

            File.WriteAllText(path, ScriptTemplate.ReplaceLineEndings("\n"), new UTF8Encoding(false));
            Reload();
            Report(Localizer.Format("Assets.ScriptCreated", fileName));
            return FindByPath(path);
        }
        catch (Exception exception)
        {
            Report(Localizer.Format("Assets.CreateFailed", exception.Message));
            return null;
        }
    }

    private static bool IsInside(string folder, string path)
    {
        var root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return path.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static string UniqueTarget(string folder, string fileName)
    {
        var target = Path.Combine(folder, fileName);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);

        for (var index = 2; File.Exists(target); index++)
            target = Path.Combine(folder, $"{stem} ({index}){extension}");

        return target;
    }

    [RelayCommand]
    private void Refresh() => Reload();
}
