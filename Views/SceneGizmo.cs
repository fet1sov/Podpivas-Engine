using System;
using System.Collections.Generic;
using System.Numerics;
using Avalonia;
using Avalonia.Media;
using PodpivasEngine.Models;
using PodpivasEngine.ViewModels;
using Vector = Avalonia.Vector;

namespace PodpivasEngine.Views;

public enum GizmoHandle
{
    None,
    X,
    Y,
    Z,
    RotateX,
    RotateY,
    RotateZ,
}

public enum GizmoStyle
{
    Arrow,
    Box,
    Ring,
}

public readonly record struct GizmoSegment(
    Point From,
    Point To,
    GizmoHandle Handle,
    Color Color,
    GizmoStyle Style,
    double PixelsPerUnit);

public static class SceneGizmo
{
    private const double AxisLength = 58;
    private const double RingRadius = 46;
    private const double HitRadius = 8;
    private const int RingSteps = 36;

    private static readonly Color AxisXColor = Color.Parse("#E5484D");
    private static readonly Color AxisYColor = Color.Parse("#5FD97A");
    private static readonly Color AxisZColor = Color.Parse("#4E79D5");
    private static readonly Color RingColor = Color.Parse("#E4E4E4");

    public static IReadOnlyList<GizmoSegment> Build(
        ProjectSpace space,
        ISceneRenderable node,
        EditorTool tool,
        double width,
        double height)
    {
        var segments = new List<GizmoSegment>();
        if (tool == EditorTool.Select || width <= 1 || height <= 1)
            return segments;

        if (space == ProjectSpace.TwoD)
        {
            var (scale, origin) = SceneRenderer.Map2D(width, height);
            var center = new Point(origin.X + node.X * scale, origin.Y + node.Y * scale);

            if (tool == EditorTool.Rotate)
            {
                AddRing(segments, center);
                return segments;
            }

            var style = tool == EditorTool.Scale ? GizmoStyle.Box : GizmoStyle.Arrow;
            segments.Add(new GizmoSegment(center, center + new Vector(AxisLength, 0), GizmoHandle.X, AxisXColor, style, scale));
            segments.Add(new GizmoSegment(center, center + new Vector(0, AxisLength), GizmoHandle.Y, AxisYColor, style, scale));
            return segments;
        }

        var size = new Vector3(
            1.6f * (float)Math.Max(0.1, node.ScaleX),
            1.6f * (float)Math.Max(0.1, node.ScaleY),
            1.6f * (float)Math.Max(0.1, node.ScaleZ));
        var centerWorld = new Vector3((float)node.X, (float)node.Y + size.Y / 2, (float)node.Z);

        if (!SceneRenderer.TryProject3D(width, height, centerWorld, out _))
            return segments;

        if (tool == EditorTool.Rotate)
        {
            var radius = 0.85f * Math.Max(size.X, Math.Max(size.Y, size.Z));
            AddWorldRing(segments, width, height, centerWorld, Vector3.UnitX, radius, GizmoHandle.RotateX, AxisXColor);
            AddWorldRing(segments, width, height, centerWorld, Vector3.UnitY, radius, GizmoHandle.RotateY, AxisYColor);
            AddWorldRing(segments, width, height, centerWorld, Vector3.UnitZ, radius, GizmoHandle.RotateZ, AxisZColor);
            return segments;
        }

        var boxStyle = tool == EditorTool.Scale ? GizmoStyle.Box : GizmoStyle.Arrow;
        AddAxis(segments, width, height, centerWorld, Vector3.UnitX, GizmoHandle.X, AxisXColor, boxStyle);
        AddAxis(segments, width, height, centerWorld, Vector3.UnitY, GizmoHandle.Y, AxisYColor, boxStyle);
        AddAxis(segments, width, height, centerWorld, Vector3.UnitZ, GizmoHandle.Z, AxisZColor, boxStyle);

        return segments;
    }

    public static void Render(DrawingContext context, IReadOnlyList<GizmoSegment> segments)
    {
        foreach (var segment in segments)
        {
            var brush = new SolidColorBrush(segment.Color);
            var pen = new Pen(brush, 2);
            context.DrawLine(pen, segment.From, segment.To);

            switch (segment.Style)
            {
                case GizmoStyle.Arrow:
                    var direction = Direction(segment.From, segment.To);
                    var normal = new Vector(-direction.Y, direction.X);
                    var back = segment.To - direction * 9;
                    context.DrawLine(pen, segment.To, back + normal * 4.5);
                    context.DrawLine(pen, segment.To, back - normal * 4.5);
                    break;

                case GizmoStyle.Box:
                    context.DrawRectangle(brush, null, new Rect(segment.To.X - 4.5, segment.To.Y - 4.5, 9, 9));
                    break;
            }
        }
    }

    public static GizmoHandle HitTest(IReadOnlyList<GizmoSegment> segments, Point point)
    {
        var handle = GizmoHandle.None;
        var best = HitRadius;

        foreach (var segment in segments)
        {
            var distance = DistanceToSegment(point, segment.From, segment.To);
            if (distance > best)
                continue;

            best = distance;
            handle = segment.Handle;
        }

        return handle;
    }

    public static Vector Direction(Point from, Point to)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        return length < 0.001 ? default : new Vector(dx / length, dy / length);
    }

    private static void AddAxis(
        List<GizmoSegment> segments,
        double width,
        double height,
        Vector3 center,
        Vector3 axis,
        GizmoHandle handle,
        Color color,
        GizmoStyle style)
    {
        if (!SceneRenderer.TryProjectAxis3D(width, height, center, axis, out var from, out var to, out var pixelsPerUnit))
            return;

        var direction = Direction(from, to);
        segments.Add(new GizmoSegment(from, from + direction * AxisLength, handle, color, style, pixelsPerUnit));
    }

    private static void AddRing(List<GizmoSegment> segments, Point center)
    {
        var previous = center + new Vector(RingRadius, 0);

        for (var i = 1; i <= RingSteps; i++)
        {
            var angle = i * 2 * Math.PI / RingSteps;
            var point = center + new Vector(Math.Cos(angle) * RingRadius, Math.Sin(angle) * RingRadius);
            segments.Add(new GizmoSegment(previous, point, GizmoHandle.RotateZ, RingColor, GizmoStyle.Ring, 0));
            previous = point;
        }
    }

    private static void AddWorldRing(
        List<GizmoSegment> segments,
        double width,
        double height,
        Vector3 center,
        Vector3 axis,
        float radius,
        GizmoHandle handle,
        Color color)
    {
        var normal = Vector3.Normalize(axis);
        var reference = Math.Abs(normal.Y) > 0.9f ? Vector3.UnitX : Vector3.UnitY;
        var side = Vector3.Normalize(Vector3.Cross(normal, reference));
        var up = Vector3.Cross(normal, side);

        Point? previous = null;

        for (var i = 0; i <= RingSteps; i++)
        {
            var angle = (float)(i * 2 * Math.PI / RingSteps);
            var point = center + (side * MathF.Cos(angle) + up * MathF.Sin(angle)) * radius;

            if (!SceneRenderer.TryProject3D(width, height, point, out var screen))
            {
                previous = null;
                continue;
            }

            if (previous is { } from)
                segments.Add(new GizmoSegment(from, screen, handle, color, GizmoStyle.Ring, 0));

            previous = screen;
        }
    }

    private static double DistanceToSegment(Point point, Point from, Point to)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var lengthSquared = dx * dx + dy * dy;

        if (lengthSquared < 0.001)
            return Distance(point, from);

        var t = ((point.X - from.X) * dx + (point.Y - from.Y) * dy) / lengthSquared;
        t = Math.Clamp(t, 0, 1);
        return Distance(point, new Point(from.X + dx * t, from.Y + dy * t));
    }

    private static double Distance(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
