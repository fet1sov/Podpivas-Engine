using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PodpivasEngine.Localization;
using PodpivasEngine.Models;
using PodpivasEngine.Scripting;
using PodpivasEngine.Services;

namespace PodpivasEngine.ViewModels;

public sealed partial class InspectorViewModel : ViewModelBase
{
    private readonly List<SceneNode> _hookedNodes = [];
    private readonly HashSet<ObservableCollection<SceneNode>> _hookedCollections = [];

    private EditorProject? _project;
    private SceneNode? _observedTarget;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTarget), nameof(TypeLabel), nameof(IsCamera), nameof(IsCameraNode),
        nameof(ProjectionIndex), nameof(FovAxisIndex))]
    private SceneNode? _target;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTwoD), nameof(IsThreeD), nameof(IsCamera), nameof(IsModelTarget),
        nameof(ContentAssets), nameof(ContentLabel))]
    private ProjectSpace _space = ProjectSpace.ThreeD;

    public ObservableCollection<string> Scripts { get; } = [];

    public ObservableCollection<string> Models { get; } = [];

    public ObservableCollection<string> Images { get; } = [];

    public ObservableCollection<ScriptComponentViewModel> Components { get; } = [];

    public ObservableCollection<string> Cameras { get; } = [];

    public ObservableCollection<string> SceneObjects { get; } = [];

    public IReadOnlyList<string> BackendLabels { get; } = ["WebGPU", "WebGL", "WebCPU", "Auto"];

    public IReadOnlyList<string> ProjectionLabels =>
    [
        Localizer.Get("Inspector.Projection.Perspective"),
        Localizer.Get("Inspector.Projection.Orthographic"),
    ];

    public IReadOnlyList<string> FovAxisLabels =>
    [
        Localizer.Get("Inspector.FovAxis.Vertical"),
        Localizer.Get("Inspector.FovAxis.Horizontal"),
        Localizer.Get("Inspector.FovAxis.Diagonal"),
    ];

    public EditorProject? Project => _project;

    public bool HasTarget => Target is not null;

    public int BackendIndex
    {
        get => _project is null ? 0 : (int)_project.Backend;
        set
        {
            if (_project is null || value < 0 || value >= BackendLabels.Count)
                return;

            var backend = (RenderBackend)value;
            if (_project.Backend == backend)
                return;

            _project.Backend = backend;
            OnPropertyChanged();
        }
    }

    public string? SelectedCamera
    {
        get => _project?.MainCamera;
        set
        {
            if (_project is null || string.IsNullOrEmpty(value) || !Cameras.Contains(value))
                return;

            if (string.Equals(_project.MainCamera, value, StringComparison.Ordinal))
                return;

            _project.MainCamera = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowCameraWarning));
        }
    }

    public bool ShowCameraWarning =>
        _project is null
        || string.IsNullOrWhiteSpace(_project.MainCamera)
        || !Cameras.Contains(_project.MainCamera);

    public bool HasComponents => Components.Count > 0;

    public bool CanAddComponent => Target is not null && Scripts.Count > 0;

    public bool IsTwoD => Space == ProjectSpace.TwoD;

    public bool IsThreeD => Space == ProjectSpace.ThreeD;

    public bool IsCamera => IsThreeD && Target?.Kind == SceneNodeKind.Camera;

    public bool IsCameraNode => Target?.Kind == SceneNodeKind.Camera;

    public bool IsModelTarget =>
        IsThreeD && Target is { Kind: SceneNodeKind.Entity or SceneNodeKind.Model };

    public IReadOnlyList<string> ContentAssets => IsModelTarget ? Models : Images;

    public string ContentLabel => Localizer.Get(IsModelTarget ? "Inspector.Model" : "Inspector.Asset");

    public int ProjectionIndex
    {
        get => Target is null ? -1 : (int)Target.Projection;
        set
        {
            if (Target is null || value < 0 || value >= ProjectionLabels.Count || (int)Target.Projection == value)
                return;

            Target.Projection = (CameraProjection)value;
            OnPropertyChanged();
        }
    }

    public int FovAxisIndex
    {
        get => Target is null ? -1 : (int)Target.FovAxis;
        set
        {
            if (Target is null || value < 0 || value >= FovAxisLabels.Count || (int)Target.FovAxis == value)
                return;

            Target.FovAxis = (CameraFovAxis)value;
            OnPropertyChanged();
        }
    }

    public string TypeLabel => Target is null ? string.Empty : Localizer.Get($"Kind.{Target.Kind}");

    public void Load(EditorProject project)
    {
        _project = project;
        Space = project.Space;
        HookScene();
        RebuildCameras();

        Sync(Scripts, Directory.Exists(project.ScriptsPath)
            ? Directory.EnumerateFiles(project.ScriptsPath, "*.lua").OrderBy(f => f).Select(Path.GetFileName)
            : Enumerable.Empty<string>());

        var assets = Directory.Exists(project.AssetsPath)
            ? ProjectService.ScanAssets(project, includeModels: !project.IsTwoD)
            : Array.Empty<AssetEntry>();

        Sync(Models, assets.Where(entry => entry.Kind == AssetKind.Model3D).Select(entry => entry.RelativePath));
        Sync(Images, assets.Where(entry => entry.Kind == AssetKind.Image).Select(entry => entry.RelativePath));

        OnPropertyChanged(nameof(ContentAssets));
        OnPropertyChanged(nameof(ContentLabel));
        OnPropertyChanged(nameof(CanAddComponent));
        OnPropertyChanged(nameof(Project));
        OnPropertyChanged(nameof(BackendIndex));
        OnPropertyChanged(nameof(SelectedCamera));
        OnPropertyChanged(nameof(ShowCameraWarning));
        RebuildComponents();
    }

    private void HookScene()
    {
        foreach (var node in _hookedNodes)
            node.PropertyChanged -= OnSceneNodeChanged;

        foreach (var collection in _hookedCollections)
            collection.CollectionChanged -= OnSceneCollectionChanged;

        _hookedNodes.Clear();
        _hookedCollections.Clear();

        if (_project is not null)
            HookCollection(_project.Nodes);
    }

    private void HookCollection(ObservableCollection<SceneNode> nodes)
    {
        if (_hookedCollections.Add(nodes))
            nodes.CollectionChanged += OnSceneCollectionChanged;

        foreach (var node in nodes)
        {
            if (_hookedNodes.Contains(node))
                continue;

            node.PropertyChanged += OnSceneNodeChanged;
            _hookedNodes.Add(node);
            HookCollection(node.Children);
        }
    }

    private void OnSceneNodeChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) =>
        RebuildCameras();

    private void OnSceneCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        HookScene();
        RebuildCameras();
    }

    private void RebuildCameras()
    {
        var all = _project is null ? [] : Enumerate(_project.Nodes).ToList();
        var names = all
            .Where(node => node.Kind == SceneNodeKind.Camera)
            .Select(node => node.Name)
            .ToList();

        if (!names.SequenceEqual(Cameras))
        {
            Cameras.Clear();

            foreach (var name in names)
                Cameras.Add(name);
        }

        var objects = all.Select(node => node.Name).ToList();
        if (!objects.SequenceEqual(SceneObjects))
        {
            SceneObjects.Clear();

            foreach (var name in objects)
                SceneObjects.Add(name);
        }

        OnPropertyChanged(nameof(SelectedCamera));
        OnPropertyChanged(nameof(ShowCameraWarning));
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

    private static void Sync(ObservableCollection<string> target, IEnumerable<string?> values)
    {
        var items = values
            .Where(value => !string.IsNullOrEmpty(value))
            .Select(value => value!)
            .ToList();

        if (items.SequenceEqual(target))
            return;

        target.Clear();

        foreach (var item in items)
            target.Add(item);
    }

    public void Relocalize()
    {
        OnPropertyChanged(nameof(TypeLabel));
        OnPropertyChanged(nameof(ProjectionLabels));
        OnPropertyChanged(nameof(FovAxisLabels));
        OnPropertyChanged(nameof(ContentLabel));
    }

    partial void OnTargetChanged(SceneNode? value)
    {
        if (_observedTarget is not null)
            _observedTarget.Components.CollectionChanged -= OnComponentsChanged;

        _observedTarget = value;

        if (_observedTarget is not null)
            _observedTarget.Components.CollectionChanged += OnComponentsChanged;

        OnPropertyChanged(nameof(CanAddComponent));
        OnPropertyChanged(nameof(IsModelTarget));
        OnPropertyChanged(nameof(ContentAssets));
        OnPropertyChanged(nameof(ContentLabel));
        RebuildComponents();
    }

    private void OnComponentsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildComponents();

    private void RebuildComponents()
    {
        Components.Clear();

        if (Target is not null)
        {
            foreach (var component in Target.Components)
            {
                var viewModel = new ScriptComponentViewModel(component, Scripts, ReadExports, SceneObjects);
                viewModel.Removed += OnComponentRemoved;
                Components.Add(viewModel);
            }
        }

        OnPropertyChanged(nameof(HasComponents));
    }

    private void OnComponentRemoved(ScriptComponentViewModel viewModel) =>
        Target?.Components.Remove(viewModel.Model);

    private IReadOnlyList<LuaExportField> ReadExports(string script)
    {
        if (_project is null || string.IsNullOrWhiteSpace(script))
            return [];

        var path = Path.Combine(_project.ScriptsPath, script);

        try
        {
            return File.Exists(path) ? LuaExports.Read(File.ReadAllText(path)) : [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    [RelayCommand]
    private void AddComponent()
    {
        if (Target is null || Scripts.Count == 0)
            return;

        Target.Components.Add(new ScriptComponent(Scripts[0]));
    }
}
