using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PodpivasEngine.Models;

public sealed partial class EditorProject : ObservableObject
{
    private readonly HashSet<SceneNode> _watchedNodes = [];
    private readonly HashSet<ScriptComponent> _watchedComponents = [];
    private readonly HashSet<ObservableCollection<SceneNode>> _watchedCollections = [];

    public EditorProject(string name, RenderBackend backend, ProjectSpace space, string rootPath)
    {
        _name = name;
        _gameName = name;
        _backend = backend;
        Space = space;
        RootPath = rootPath;
    }

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _gameName;

    [ObservableProperty]
    private string _version = "0.1.0";

    [ObservableProperty]
    private RenderBackend _backend;

    [ObservableProperty]
    private string _mainCamera = string.Empty;

    [ObservableProperty]
    private bool _isDirty;

    public ProjectSpace Space { get; }

    public string RootPath { get; }

    public string ProjectFilePath => Path.Combine(RootPath, "project.json");

    public string AssetsPath => Path.Combine(RootPath, "assets");

    public string ScriptsPath => Path.Combine(RootPath, "scripts");

    public EditorCameraState? EditorCamera { get; set; }

    public ObservableCollection<SceneNode> Nodes { get; } = [];

    public event EventHandler? SceneChanged;

    public bool IsTwoD => Space == ProjectSpace.TwoD;

    public string SpaceLabel => IsTwoD ? "2D" : "3D";

    public void TrackChanges()
    {
        _watchedNodes.Clear();
        _watchedComponents.Clear();
        _watchedCollections.Clear();
        HookCollection(Nodes);
        IsDirty = false;
    }

    private void HookCollection(ObservableCollection<SceneNode> nodes)
    {
        if (_watchedCollections.Add(nodes))
            nodes.CollectionChanged += OnCollectionChanged;

        foreach (var node in nodes)
        {
            if (_watchedNodes.Add(node))
            {
                node.PropertyChanged += OnStateChanged;
                node.Components.CollectionChanged += OnCollectionChanged;
            }

            foreach (var component in node.Components)
            {
                if (_watchedComponents.Add(component))
                    component.PropertyChanged += OnStateChanged;
            }

            HookCollection(node.Children);
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        HookCollection(Nodes);
        IsDirty = true;
        SceneChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        IsDirty = true;
        SceneChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnNameChanged(string value) => IsDirty = true;

    partial void OnGameNameChanged(string value) => IsDirty = true;

    partial void OnVersionChanged(string value) => IsDirty = true;

    partial void OnBackendChanged(RenderBackend value) => IsDirty = true;

    partial void OnMainCameraChanged(string value) => IsDirty = true;
}
