using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using PodpivasEngine.Services;

namespace PodpivasEngine.Views;

public sealed class ModelPreview : Control
{
    private const int ShadeSteps = 16;

    public static readonly StyledProperty<string?> ModelPathProperty =
        AvaloniaProperty.Register<ModelPreview, string?>(nameof(ModelPath));

    private static readonly IBrush[] Shades = CreateShades();

    private Vector3[] _triangles = [];
    private Vector3 _center;
    private float _radius = 1;
    private string? _loadedPath;
    private float _yaw = 0.7f;
    private float _pitch = 0.35f;
    private Point _dragStart;
    private bool _dragging;

    static ModelPreview()
    {
        AffectsRender<ModelPreview>(ModelPathProperty);
    }

    public string? ModelPath
    {
        get => GetValue(ModelPathProperty);
        set => SetValue(ModelPathProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ModelPathProperty)
        {
            LoadMesh();
            InvalidateVisual();
        }
    }

    private void LoadMesh()
    {
        var path = ModelPath;

        if (string.Equals(path, _loadedPath, StringComparison.OrdinalIgnoreCase))
            return;

        _loadedPath = path;
        _triangles = [];

        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return;

        try
        {
            if (!MeshLibrary.TryLoad(path, out var geometry) || geometry.Triangles.Length < 3)
                return;

            _triangles = geometry.Triangles;
            _center = geometry.Center;
            _radius = Math.Max(geometry.Radius, 0.0001f);
        }
        catch (Exception)
        {
            _triangles = [];
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        _dragging = true;
        _dragStart = e.GetPosition(this);
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (!_dragging)
            return;

        var position = e.GetPosition(this);
        _yaw += (float)((position.X - _dragStart.X) * 0.01);
        _pitch = Math.Clamp(_pitch - (float)((position.Y - _dragStart.Y) * 0.01), -1.4f, 1.4f);
        _dragStart = position;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragging = false;
        e.Pointer.Capture(null);
    }

    public override void Render(DrawingContext context)
    {
        if (_triangles.Length < 3 || Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        var scale = (float)(Math.Min(Bounds.Width, Bounds.Height) * 0.42 / _radius);
        var origin = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var light = Vector3.Normalize(new Vector3(0.4f, 0.7f, 0.55f));

        var faces = new List<(float Depth, Point[] Points, int Shade)>(_triangles.Length / 3);

        for (var index = 0; index + 2 < _triangles.Length; index += 3)
        {
            var a = Rotate(_triangles[index] - _center);
            var b = Rotate(_triangles[index + 1] - _center);
            var c = Rotate(_triangles[index + 2] - _center);

            var normal = Vector3.Cross(b - a, c - a);
            var length = normal.Length();
            if (!float.IsFinite(length) || length < 0.000001f)
                continue;

            normal /= length;

            var intensity = Math.Clamp(0.25f + 0.85f * Vector3.Dot(normal, light), 0f, 1f);
            var shade = Math.Clamp((int)(intensity * (ShadeSteps - 1)), 0, ShadeSteps - 1);

            faces.Add((
                (a.Z + b.Z + c.Z) / 3f,
                [
                    Project(a, origin, scale),
                    Project(b, origin, scale),
                    Project(c, origin, scale),
                ],
                shade));
        }

        faces.Sort((left, right) => right.Depth.CompareTo(left.Depth));

        foreach (var (_, points, shade) in faces)
        {
            var geometry = new StreamGeometry();
            using (var stream = geometry.Open())
            {
                stream.BeginFigure(points[0], true);
                stream.LineTo(points[1]);
                stream.LineTo(points[2]);
                stream.EndFigure(true);
            }

            context.DrawGeometry(Shades[shade], null, geometry);
        }
    }

    private static Point Project(Vector3 point, Point origin, float scale) =>
        new(origin.X + point.X * scale, origin.Y - point.Y * scale);

    private Vector3 Rotate(Vector3 point)
    {
        var x = point.X * MathF.Cos(_yaw) - point.Z * MathF.Sin(_yaw);
        var z = point.X * MathF.Sin(_yaw) + point.Z * MathF.Cos(_yaw);
        var y = point.Y * MathF.Cos(_pitch) - z * MathF.Sin(_pitch);
        var depth = point.Y * MathF.Sin(_pitch) + z * MathF.Cos(_pitch);

        return new Vector3(x, y, depth);
    }

    private static IBrush[] CreateShades()
    {
        var brushes = new IBrush[ShadeSteps];

        for (var index = 0; index < ShadeSteps; index++)
        {
            var value = (byte)(48 + index * 11);
            brushes[index] = new SolidColorBrush(Color.FromRgb(value, (byte)(value + 4), (byte)(value + 12)));
        }

        return brushes;
    }
}
