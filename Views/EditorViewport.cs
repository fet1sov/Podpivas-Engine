using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PodpivasEngine.Models;
using PodpivasEngine.ViewModels;
using Vector = Avalonia.Vector;

namespace PodpivasEngine.Views;

public sealed class EditorViewport : Control
{
    private static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.Parse("#080808"));

    private readonly List<SceneNode> _hookedNodes = [];
    private readonly HashSet<ObservableCollection<SceneNode>> _hookedCollections = [];
    private readonly HashSet<Key> _cameraKeys = [];
    private readonly DispatcherTimer _cameraTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly DispatcherTimer _renderTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };

    private MainViewModel? _viewModel;
    private RuntimeViewModel? _runtime;
    private SceneNode? _dragNode;
    private Point _lastPoint;
    private bool _cameraLooking;
    private TopLevel? _topLevel;

    private SceneNode? _gizmoNode;
    private GizmoHandle _gizmoHandle;
    private EditorTool _gizmoTool;
    private Point _gizmoStart;
    private Vector _gizmoAxis;
    private double _gizmoPixelsPerUnit;
    private double _gizmoStartValue;

    public EditorViewport()
    {
        ClipToBounds = true;
        Focusable = true;
        DataContextChanged += (_, _) => Bind();
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerWheelChanged += OnPointerWheelChanged;
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        _cameraTimer.Tick += OnCameraTick;
        _renderTimer.Tick += OnRenderTick;
    }

    private static bool IsModelPath(string path) =>
        AssetEntry.DetectKind(Path.GetExtension(path)) == AssetKind.Model3D;

    private static IEnumerable<string> DroppedPaths(DragEventArgs e)
    {
        foreach (var item in e.DataTransfer.TryGetFiles() ?? [])
        {
            if (item.TryGetLocalPath() is { } path)
                yield return path;
        }

        if (e.DataTransfer.TryGetText() is { Length: > 0 } text)
            yield return text.Trim().Trim('"');
    }

    private bool CanAcceptModels(DragEventArgs e)
    {
        if (_viewModel is not { } viewModel
            || viewModel.Project.IsTwoD
            || string.IsNullOrEmpty(viewModel.Project.RootPath)
            || viewModel.Runtime.IsPlaying
            || viewModel.Runtime.IsPaused)
            return false;

        return DroppedPaths(e).Any(path => File.Exists(path) && IsModelPath(path));
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var accepted = CanAcceptModels(e);
        e.DragEffects = accepted ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;

        if (!CanAcceptModels(e) || _viewModel is not { } viewModel)
            return;

        var point = e.GetPosition(this);
        if (!SceneRenderer.TryProjectToGround(Bounds.Width, Bounds.Height, point.X, point.Y, out var x, out var z))
        {
            x = 0;
            z = 0;
        }

        var external = new List<string>();

        foreach (var path in DroppedPaths(e).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (viewModel.Assets.FindByPath(path) is { IsModel: true } entry)
                viewModel.AddModelToScene(entry, x, z);
            else if (File.Exists(path) && IsModelPath(path))
                external.Add(path);
        }

        if (external.Count == 0)
            return;

        foreach (var entry in viewModel.Assets.ImportFiles(external))
        {
            if (entry.IsModel)
                viewModel.AddModelToScene(entry, x, z);
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _topLevel = TopLevel.GetTopLevel(this);
        if (_topLevel is null)
            return;

        _topLevel.AddHandler(KeyDownEvent, OnCameraKeyDown, RoutingStrategies.Tunnel);
        _topLevel.AddHandler(KeyUpEvent, OnCameraKeyUp, RoutingStrategies.Tunnel);

        _renderTimer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_topLevel is not null)
        {
            _topLevel.RemoveHandler(KeyDownEvent, OnCameraKeyDown);
            _topLevel.RemoveHandler(KeyUpEvent, OnCameraKeyUp);
            _topLevel = null;
        }

        StopCameraMove();
        _renderTimer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 1 || height <= 1)
            return;

        var viewModel = _viewModel;
        if (viewModel is null)
        {
            context.DrawRectangle(BackgroundBrush, null, new Rect(0, 0, width, height));
            return;
        }

        var runtime = viewModel.Runtime;
        var live = runtime.IsPlaying || runtime.IsPaused;
        IReadOnlyList<ISceneRenderable> objects = live ? runtime.Objects : Flatten(viewModel.Project.Nodes);
        var selection = live ? null : viewModel.Hierarchy.SelectedNode;

        var start = Stopwatch.GetTimestamp();
        var stats = SceneRenderer.Render(
            context,
            viewModel.Project.Space,
            objects,
            selection,
            viewModel.Project.AssetsPath,
            width,
            height,
            showGrid: !live);

        if (!live && viewModel.Tool != EditorTool.Select && selection is { Visible: true })
            SceneGizmo.Render(context, SceneGizmo.Build(viewModel.Project.Space, selection, viewModel.Tool, width, height));

        var elapsed = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        var pixelWidth = width * scaling;
        var pixelHeight = height * scaling;

        Dispatcher.UIThread.Post(
            () => viewModel.Stats.Report(stats, pixelWidth, pixelHeight, elapsed),
            DispatcherPriority.Background);
    }

    private void Bind()
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.Hierarchy.PropertyChanged -= OnHierarchyPropertyChanged;
        }

        if (_runtime is not null)
            _runtime.Frame -= OnRuntimeFrame;

        _viewModel = DataContext as MainViewModel;
        _runtime = _viewModel?.Runtime;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.Hierarchy.PropertyChanged += OnHierarchyPropertyChanged;
        }

        if (_runtime is not null)
            _runtime.Frame += OnRuntimeFrame;

        HookScene();
        RestoreCamera();
        InvalidateVisual();
    }

    private void OnRuntimeFrame(object? sender, EventArgs e) => InvalidateVisual();

    private void RestoreCamera()
    {
        if (_viewModel is null)
            return;

        SceneCamera.Apply(_viewModel.Project.EditorCamera);
        _viewModel.Project.EditorCamera = SceneCamera.Capture();
    }

    private void SaveCamera()
    {
        if (_viewModel is not null)
            _viewModel.Project.EditorCamera = SceneCamera.Capture();
    }

    private void OnRenderTick(object? sender, EventArgs e) => InvalidateVisual();

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Project))
        {
            RestoreCamera();
            HookScene();
        }

        if (e.PropertyName is nameof(MainViewModel.Project) or nameof(MainViewModel.Tool))
            InvalidateVisual();
    }

    private void OnHierarchyPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HierarchyViewModel.SelectedNode))
            InvalidateVisual();
    }

    private void HookScene()
    {
        foreach (var node in _hookedNodes)
            node.PropertyChanged -= OnNodePropertyChanged;

        foreach (var collection in _hookedCollections)
            collection.CollectionChanged -= OnCollectionChanged;

        _hookedNodes.Clear();
        _hookedCollections.Clear();

        if (_viewModel is not null)
            HookCollection(_viewModel.Project.Nodes);
    }

    private void HookCollection(ObservableCollection<SceneNode> nodes)
    {
        if (_hookedCollections.Add(nodes))
            nodes.CollectionChanged += OnCollectionChanged;

        foreach (var node in nodes)
        {
            if (_hookedNodes.Contains(node))
                continue;

            node.PropertyChanged += OnNodePropertyChanged;
            _hookedNodes.Add(node);
            HookCollection(node.Children);
        }
    }

    private void OnNodePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) =>
        InvalidateVisual();

    private void OnCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        HookScene();
        InvalidateVisual();
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var viewModel = _viewModel;
        if (viewModel is null)
            return;

        if (viewModel.Runtime.IsPlaying || viewModel.Runtime.IsPaused)
            return;

        var point = e.GetPosition(this);
        var hit = HitTest(viewModel, point);
        var properties = e.GetCurrentPoint(this).Properties;

        if (properties.IsRightButtonPressed)
        {
            if (hit is not null)
                viewModel.Hierarchy.SelectedNode = hit;

            SceneMenu.Create(this, viewModel.Hierarchy, hit, hit is null ? [] : [hit]).Open(this);
            e.Handled = true;
            return;
        }

        if (!properties.IsLeftButtonPressed)
            return;

        if (viewModel.Tool != EditorTool.Select && viewModel.Hierarchy.SelectedNode is { Visible: true } selected)
        {
            var segments = SceneGizmo.Build(viewModel.Project.Space, selected, viewModel.Tool, Bounds.Width, Bounds.Height);
            var handle = SceneGizmo.HitTest(segments, point);

            if (handle != GizmoHandle.None)
            {
                BeginGizmo(selected, handle, segments, point, viewModel.Tool);
                e.Pointer.Capture(this);
                e.Handled = true;
                return;
            }
        }

        if (viewModel.Project.Space == ProjectSpace.ThreeD &&
            (hit is null || viewModel.Tool == EditorTool.Select))
        {
            viewModel.Hierarchy.SelectedNode = hit;

            if (hit is not null)
            {
                InvalidateVisual();
                e.Handled = true;
                return;
            }

            _cameraLooking = true;
            _lastPoint = point;
            _cameraTimer.Start();
            e.Pointer.Capture(this);
            e.Handled = true;
            InvalidateVisual();
            return;
        }

        viewModel.Hierarchy.SelectedNode = hit;

        if (hit is not null && viewModel.Tool == EditorTool.Move)
        {
            _dragNode = hit;
            _lastPoint = point;
            e.Pointer.Capture(this);
        }

        InvalidateVisual();
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        if (_viewModel is { } liveModel && (liveModel.Runtime.IsPlaying || liveModel.Runtime.IsPaused))
            return;

        if (_cameraLooking)
        {
            var moved = e.GetPosition(this);
            SceneCamera.Rotate(moved.X - _lastPoint.X, moved.Y - _lastPoint.Y);
            SaveCamera();
            _lastPoint = moved;
            InvalidateVisual();
            return;
        }

        if (_gizmoNode is not null)
        {
            ApplyGizmo(e.GetPosition(this));
            InvalidateVisual();
            return;
        }

        if (_dragNode is null || _viewModel is null)
            return;

        var point = e.GetPosition(this);
        var dx = point.X - _lastPoint.X;
        var dy = point.Y - _lastPoint.Y;
        _lastPoint = point;

        if (_viewModel.Project.Space == ProjectSpace.TwoD)
        {
            var (scale, _) = SceneRenderer.Map2D(Bounds.Width, Bounds.Height);
            _dragNode.X += dx / scale;
            _dragNode.Y += dy / scale;
        }
        else
        {
            _dragNode.X += dx / SceneRenderer.DragScale3D;
            _dragNode.Z += dy / SceneRenderer.DragScale3D;
        }

        InvalidateVisual();
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_cameraLooking)
        {
            StopCameraMove();
            e.Pointer.Capture(null);
            InvalidateVisual();
            return;
        }

        if (_gizmoNode is not null)
        {
            _gizmoNode = null;
            e.Pointer.Capture(null);
            InvalidateVisual();
            return;
        }

        if (_dragNode is null)
            return;

        _dragNode = null;
        e.Pointer.Capture(null);
        InvalidateVisual();
    }

    private void StopCameraMove()
    {
        _cameraLooking = false;
        _cameraKeys.Clear();
        _cameraTimer.Stop();
    }

    private void OnCameraKeyDown(object? sender, KeyEventArgs e)
    {
        if (!_cameraLooking || !IsCameraKey(e.Key))
            return;

        _cameraKeys.Add(e.Key);
        e.Handled = true;
    }

    private void OnCameraKeyUp(object? sender, KeyEventArgs e)
    {
        if (_cameraKeys.Remove(e.Key))
            e.Handled = true;
    }

    private static bool IsCameraKey(Key key) =>
        key is Key.W or Key.A or Key.S or Key.D;

    private void OnCameraTick(object? sender, EventArgs e)
    {
        if (!_cameraLooking || _cameraKeys.Count == 0)
            return;

        var forward = SceneCamera.Forward;
        var right = SceneCamera.Right;
        var direction = Vector3.Zero;

        if (_cameraKeys.Contains(Key.W))
            direction += forward;
        if (_cameraKeys.Contains(Key.S))
            direction -= forward;
        if (_cameraKeys.Contains(Key.D))
            direction += right;
        if (_cameraKeys.Contains(Key.A))
            direction -= right;

        if (direction == Vector3.Zero)
            return;

        SceneCamera.Move(direction, SceneCamera.MoveSpeed * (float)_cameraTimer.Interval.TotalSeconds);
        SaveCamera();
        InvalidateVisual();
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (_viewModel is null || _viewModel.Runtime.IsPlaying || _viewModel.Runtime.IsPaused)
            return;

        if (_viewModel.Project.Space != ProjectSpace.ThreeD || Math.Abs(e.Delta.Y) < 0.0001)
            return;

        SceneCamera.Move(SceneCamera.Forward, (float)e.Delta.Y * SceneCamera.WheelStep);
        SaveCamera();
        InvalidateVisual();
        e.Handled = true;
    }

    private void BeginGizmo(SceneNode node, GizmoHandle handle, IReadOnlyList<GizmoSegment> segments, Point point, EditorTool tool)
    {
        _gizmoNode = node;
        _gizmoHandle = handle;
        _gizmoTool = tool;
        _gizmoStart = point;
        _gizmoAxis = default;
        _gizmoPixelsPerUnit = 1;

        foreach (var segment in segments)
        {
            if (segment.Handle != handle)
                continue;

            _gizmoAxis = SceneGizmo.Direction(segment.From, segment.To);
            _gizmoPixelsPerUnit = segment.PixelsPerUnit;
            break;
        }

        _gizmoStartValue = tool switch
        {
            EditorTool.Scale => handle switch
            {
                GizmoHandle.X => node.ScaleX,
                GizmoHandle.Y => node.ScaleY,
                GizmoHandle.Z => node.ScaleZ,
                _ => 1,
            },
            EditorTool.Rotate => handle switch
            {
                GizmoHandle.RotateX => node.RotationX,
                GizmoHandle.RotateY => node.RotationY,
                GizmoHandle.RotateZ => node.RotationZ,
                _ => 0,
            },
            _ => handle switch
            {
                GizmoHandle.X => node.X,
                GizmoHandle.Y => node.Y,
                GizmoHandle.Z => node.Z,
                _ => 0,
            },
        };
    }

    private void ApplyGizmo(Point point)
    {
        if (_gizmoNode is not { } node)
            return;

        var deltaX = point.X - _gizmoStart.X;
        var deltaY = point.Y - _gizmoStart.Y;
        var along = deltaX * _gizmoAxis.X + deltaY * _gizmoAxis.Y;

        switch (_gizmoTool)
        {
            case EditorTool.Rotate:
                var angle = _gizmoStartValue + deltaX * 0.5;

                if (_gizmoHandle == GizmoHandle.RotateX)
                    node.RotationX = angle;
                else if (_gizmoHandle == GizmoHandle.RotateY)
                    node.RotationY = angle;
                else if (_gizmoHandle == GizmoHandle.RotateZ)
                    node.RotationZ = angle;

                return;

            case EditorTool.Scale:
                var factor = Math.Clamp(1 + along / 90, 0.01, 100);
                var scale = Math.Clamp(_gizmoStartValue * factor, 0.01, 100);

                if (_gizmoHandle == GizmoHandle.X)
                    node.ScaleX = scale;
                else if (_gizmoHandle == GizmoHandle.Y)
                    node.ScaleY = scale;
                else if (_gizmoHandle == GizmoHandle.Z)
                    node.ScaleZ = scale;

                return;

            default:
                var world = along / Math.Max(0.001, _gizmoPixelsPerUnit);

                if (_gizmoHandle == GizmoHandle.X)
                    node.X = _gizmoStartValue + world;
                else if (_gizmoHandle == GizmoHandle.Y)
                    node.Y = _gizmoStartValue + world;
                else if (_gizmoHandle == GizmoHandle.Z)
                    node.Z = _gizmoStartValue + world;

                return;
        }
    }

    private SceneNode? HitTest(MainViewModel viewModel, Point point) =>
        SceneRenderer.HitTest(
            viewModel.Project.Space,
            Flatten(viewModel.Project.Nodes),
            point.X,
            point.Y,
            Bounds.Width,
            Bounds.Height) as SceneNode;

    private static List<ISceneRenderable> Flatten(IEnumerable<SceneNode> nodes)
    {
        var result = new List<ISceneRenderable>();

        void Walk(IEnumerable<SceneNode> branch)
        {
            foreach (var node in branch)
            {
                result.Add(node);
                Walk(node.Children);
            }
        }

        Walk(nodes);
        return result;
    }
}
