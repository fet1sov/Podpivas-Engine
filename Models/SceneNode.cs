using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PodpivasEngine.Models;

public enum SceneNodeKind
{
    Group,
    Entity,
    Sprite,
    Model,
    Camera,
    Light,
    Audio,
    Tileset,
}

public sealed partial class SceneNode : ObservableObject, ISceneRenderable
{
    public SceneNode(string name, SceneNodeKind kind, params SceneNode[] children)
    {
        _name = name;
        Kind = kind;
        Children = new ObservableCollection<SceneNode>(children);
    }

    [ObservableProperty]
    private string _name;

    public SceneNodeKind Kind { get; }

    public ObservableCollection<SceneNode> Children { get; }

    [ObservableProperty]
    private double _x;

    [ObservableProperty]
    private double _y;

    [ObservableProperty]
    private double _z;

    [ObservableProperty]
    private double _rotationX;

    [ObservableProperty]
    private double _rotationY;

    [ObservableProperty]
    private double _rotationZ;

    [ObservableProperty]
    private double _scaleX = 1;

    [ObservableProperty]
    private double _scaleY = 1;

    [ObservableProperty]
    private double _scaleZ = 1;

    [ObservableProperty]
    private bool _visible = true;

    private string _asset = string.Empty;

    public string Asset
    {
        get => _asset;
        set
        {
            // ComboBox writes null when its item list is refreshed or the node loses focus.
            if (value is null || string.Equals(_asset, value, StringComparison.Ordinal))
                return;

            _asset = value;
            OnPropertyChanged();
        }
    }

    [ObservableProperty]
    private CameraProjection _projection = CameraProjection.Perspective;

    [ObservableProperty]
    private double _fieldOfView = 50;

    [ObservableProperty]
    private CameraFovAxis _fovAxis = CameraFovAxis.Vertical;

    [ObservableProperty]
    private double _pitch;

    [ObservableProperty]
    private double _yaw;

    public ObservableCollection<ScriptComponent> Components { get; } = [];

    public string Glyph => Kind switch
    {
        SceneNodeKind.Group => "▣",
        SceneNodeKind.Entity => "◆",
        SceneNodeKind.Sprite => "▢",
        SceneNodeKind.Model => "◈",
        SceneNodeKind.Camera => "▤",
        SceneNodeKind.Light => "◐",
        SceneNodeKind.Audio => "♫",
        SceneNodeKind.Tileset => "▦",
        _ => "•",
    };

    public SceneNode Clone()
    {
        var clone = new SceneNode(Name, Kind)
        {
            X = X,
            Y = Y,
            Z = Z,
            RotationX = RotationX,
            RotationY = RotationY,
            RotationZ = RotationZ,
            ScaleX = ScaleX,
            ScaleY = ScaleY,
            ScaleZ = ScaleZ,
            Visible = Visible,
            Asset = Asset,
            Projection = Projection,
            FieldOfView = FieldOfView,
            FovAxis = FovAxis,
            Pitch = Pitch,
            Yaw = Yaw,
        };

        foreach (var component in Components)
            clone.Components.Add(component.Clone());

        foreach (var child in Children)
            clone.Children.Add(child.Clone());

        return clone;
    }

    public SceneNode? FindByName(string name)
    {
        if (Name == name)
            return this;

        foreach (var child in Children)
        {
            if (child.FindByName(name) is { } found)
                return found;
        }

        return null;
    }
}
