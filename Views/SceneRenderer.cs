using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using PodpivasEngine.Models;
using PodpivasEngine.Services;

namespace PodpivasEngine.Views;

public static class SceneRenderer
{
    public const double WorldWidth = 24;
    public const double WorldHeight = 14;
    public const double DragScale3D = 40;
    private const int EllipseTriangles = 32;
    private const double DefaultFov = 50;
    private const float OrthoReferenceDepth = 10;
    private static readonly IBrush BackgroundBrush = MakeBrush("#080808");
    private static readonly IBrush FloorBrush = MakeBrush("#0B0B0F");
    private static readonly IPen WorldBorderPen = MakePen("#262626");
    private static readonly IPen GridPen = MakePen("#171717");
    private static readonly IPen FloorPen = MakePen("#1B1B1B");
    private static readonly IPen AxisXPen = MakePen("#E5484D", 1.5);
    private static readonly IPen AxisZPen = MakePen("#4E79D5", 1.5);
    private static readonly IPen EdgePen = MakePen("#0B0B0B");
    private static readonly IPen CameraPen = MakePen("#4E79D5");
    private static readonly IPen SelectionPen = MakePen("#0EA800", 2);
    private static readonly IBrush LightBrush = MakeBrush("#2AB7D34A");
    private static readonly IBrush LightCoreBrush = MakeBrush("#B7D34A");
    private static readonly IBrush AudioBrush = MakeBrush("#2A8A8A8A");
    private static readonly IBrush TileBrush = MakeBrush("#14201A");
    private static readonly IPen TilePen = MakePen("#1F3A2C");
    private const int MeshShadeSteps = 16;
    private static readonly IBrush[] MeshShades = CreateMeshShades();
    private static readonly Vector3 MeshLight = Vector3.Normalize(new Vector3(0.4f, 0.7f, 0.55f));
    private static readonly Dictionary<string, Bitmap?> ImageCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object ImageLock = new();

    public static SceneStats Render(
        DrawingContext context,
        ProjectSpace space,
        IReadOnlyList<ISceneRenderable> objects,
        ISceneRenderable? selected,
        string assetsPath,
        double width,
        double height,
        bool showGrid = true,
        ISceneRenderable? camera = null)
    {
        if (width <= 1 || height <= 1)
            return default;

        var metrics = new Metrics();
        context.DrawRectangle(BackgroundBrush, null, new Rect(0, 0, width, height));
        metrics.Draw(2, 4);

        if (space == ProjectSpace.TwoD)
        {
            Render2D(context, objects, selected, assetsPath, width, height, metrics, showGrid, camera);
        }
        else
        {
            using (context.PushRenderOptions(new RenderOptions { EdgeMode = EdgeMode.Aliased }))
                Render3D(context, objects, selected, assetsPath, width, height, metrics, showGrid, camera);
        }

        return metrics.Snapshot();
    }

    public static (double Scale, Point Origin) Map2D(double width, double height, ISceneRenderable? camera = null)
    {
        var scale = Math.Min(width / WorldWidth, height / WorldHeight);
        var centerX = camera?.X ?? 0;
        var centerY = camera?.Y ?? 0;
        return (scale, new Point(width / 2 - centerX * scale, height / 2 - centerY * scale));
    }

    public static ISceneRenderable? HitTest(
        ProjectSpace space,
        IReadOnlyList<ISceneRenderable> objects,
        double x,
        double y,
        double width,
        double height)
    {
        if (space == ProjectSpace.TwoD)
        {
            var (scale, origin) = Map2D(width, height);
            ISceneRenderable? best = null;
            var bestArea = 0.0;
            foreach (var node in objects)
            {
                if (!node.Visible || node.Kind == SceneNodeKind.Group)
                    continue;
                var centerX = origin.X + node.X * scale;
                var centerY = origin.Y + node.Y * scale;
                var halfX = Math.Max(3, scale * 0.55 * Math.Max(0.1, node.ScaleX));
                var halfY = Math.Max(3, scale * 0.55 * Math.Max(0.1, node.ScaleY));
                if (Math.Abs(x - centerX) > halfX || Math.Abs(y - centerY) > halfY)
                    continue;
                var area = halfX * halfY;
                if (area < bestArea)
                    continue;
                bestArea = area;
                best = node;
            }
            return best;
        }

        var camera = Camera3D.Create(width, height);
        ISceneRenderable? best3D = null;
        var bestDistance3D = 24.0;
        foreach (var node in objects)
        {
            if (!node.Visible || node.Kind == SceneNodeKind.Group)
                continue;
            var size = 1.6 * Math.Max(0.1, node.ScaleY);
            var point = new Vector3((float)node.X, (float)(node.Y + size / 2), (float)node.Z);
            if (!camera.TryProject(point, width, height, out var screen, out _))
                continue;
            var distance = Math.Sqrt((screen.X - x) * (screen.X - x) + (screen.Y - y) * (screen.Y - y));
            if (distance >= bestDistance3D)
                continue;
            bestDistance3D = distance;
            best3D = node;
        }
        return best3D;
    }

    public static bool TryProject3D(double width, double height, Vector3 point, out Point screen) =>
        Camera3D.Create(width, height).TryProject(point, width, height, out screen, out _);

    public static bool TryProjectToGround(double width, double height, double x, double y, out double worldX, out double worldZ)
    {
        var camera = Camera3D.Create(width, height);
        var direction = camera.Forward * camera.Focal
                        + camera.Right * (float)(x - width / 2)
                        + camera.Up * (float)(height / 2 - y);
        worldX = 0;
        worldZ = 0;

        if (Math.Abs(direction.Y) < 1e-4f)
            return false;

        var distance = -camera.Eye.Y / direction.Y;
        if (distance <= 0)
            return false;

        var point = camera.Eye + direction * distance;
        worldX = point.X;
        worldZ = point.Z;
        return true;
    }

    public static bool TryProjectAxis3D(
        double width,
        double height,
        Vector3 origin,
        Vector3 axis,
        out Point from,
        out Point to,
        out double pixelsPerUnit)
    {
        var camera = Camera3D.Create(width, height);
        pixelsPerUnit = 0;
        to = default;
        if (!camera.TryProject(origin, width, height, out from, out _) ||
            !camera.TryProject(origin + Vector3.Normalize(axis), width, height, out to, out _))
            return false;
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        pixelsPerUnit = Math.Sqrt(dx * dx + dy * dy);
        return pixelsPerUnit > 0.5;
    }

    private readonly record struct Camera3D(Vector3 Eye, Vector3 Forward, Vector3 Right, Vector3 Up, float Focal, bool Orthographic)
    {
        private const float NearPlane = 0.1f;

        public static Camera3D Create(double width, double height, ISceneRenderable? node = null)
        {
            var fov = Math.Clamp(node?.FieldOfView ?? DefaultFov, 1, 179);
            var span = (node?.FovAxis ?? CameraFovAxis.Vertical) switch
            {
                CameraFovAxis.Horizontal => width,
                CameraFovAxis.Diagonal => Math.Sqrt(width * width + height * height),
                _ => height,
            };
            var focal = (float)(span / (2 * Math.Tan(fov * Math.PI / 360)));
            var orthographic = node?.Projection == CameraProjection.Orthographic;

            if (node is not null)
            {
                var eye = new Vector3((float)node.X, (float)node.Y, (float)node.Z);
                var rotation = new Vector3(
                    (float)((node.RotationX + node.Pitch) * Math.PI / 180),
                    (float)((node.RotationY + node.Yaw) * Math.PI / 180),
                    (float)(node.RotationZ * Math.PI / 180));
                var forward = Vector3.Normalize(RotatePoint(-Vector3.UnitZ, rotation));
                var upAxis = RotatePoint(Vector3.UnitY, rotation);
                var right = Vector3.Normalize(Vector3.Cross(forward, upAxis));
                var up = Vector3.Cross(right, forward);
                return new Camera3D(eye, forward, right, up, focal, orthographic);
            }

            return new Camera3D(SceneCamera.Eye, SceneCamera.Forward, SceneCamera.Right, SceneCamera.Up, focal, orthographic);
        }

        public bool TryProject(Vector3 point, double width, double height, out Point screen, out float depth)
        {
            var view = ToView(point);
            depth = view.Z;
            screen = default;
            if (depth < NearPlane)
                return false;
            screen = ProjectView(view, width, height);
            return true;
        }

        public bool TryProjectSegment(Vector3 from, Vector3 to, double width, double height, out Point fromScreen, out Point toScreen)
        {
            var a = ToView(from);
            var b = ToView(to);
            fromScreen = default;
            toScreen = default;

            if (a.Z < NearPlane && b.Z < NearPlane)
                return false;

            if (a.Z < NearPlane)
                a = Vector3.Lerp(a, b, (NearPlane - a.Z) / (b.Z - a.Z));
            else if (b.Z < NearPlane)
                b = Vector3.Lerp(b, a, (NearPlane - b.Z) / (a.Z - b.Z));

            fromScreen = ProjectView(a, width, height);
            toScreen = ProjectView(b, width, height);
            return true;
        }

        public bool TryProjectPolygon(Vector3[] points, double width, double height, out Point[] screen, out float depth, out bool clipped)
        {
            depth = 0;
            clipped = false;
            Span<Vector3> view = stackalloc Vector3[points.Length];
            var visible = 0;

            for (var i = 0; i < points.Length; i++)
            {
                view[i] = ToView(points[i]);
                if (view[i].Z >= NearPlane)
                    visible++;
            }

            if (visible == 0)
            {
                screen = [];
                return false;
            }

            if (visible == points.Length)
            {
                screen = new Point[points.Length];
                for (var i = 0; i < points.Length; i++)
                {
                    screen[i] = ProjectView(view[i], width, height);
                    depth += view[i].Z;
                }

                depth /= points.Length;
                return !IsScreenGarbage(screen, width, height);
            }

            Span<Vector3> polygon = stackalloc Vector3[points.Length + 1];
            var count = 0;

            for (var i = 0; i < points.Length; i++)
            {
                var current = view[i];
                var next = view[(i + 1) % points.Length];
                var currentInside = current.Z >= NearPlane;

                if (currentInside)
                    polygon[count++] = current;

                if (currentInside != (next.Z >= NearPlane))
                    polygon[count++] = Vector3.Lerp(current, next, (NearPlane - current.Z) / (next.Z - current.Z));
            }

            if (count < 3)
            {
                screen = [];
                return false;
            }

            screen = new Point[count];
            for (var i = 0; i < count; i++)
            {
                screen[i] = ProjectView(polygon[i], width, height);
                depth += polygon[i].Z;
            }

            depth /= count;
            clipped = true;

            return !IsScreenGarbage(screen, width, height);
        }

        // Грань вплотную к камере проецируется на десятки экранов: в painter-рендере
        // без буфера глубины такие «шипы» рисуются поверх сцены, поэтому их пропускаем.
        private static bool IsScreenGarbage(Point[] points, double width, double height)
        {
            var minX = points[0].X;
            var maxX = minX;
            var minY = points[0].Y;
            var maxY = minY;

            for (var i = 1; i < points.Length; i++)
            {
                var current = points[i];
                minX = Math.Min(minX, current.X);
                maxX = Math.Max(maxX, current.X);
                minY = Math.Min(minY, current.Y);
                maxY = Math.Max(maxY, current.Y);
            }

            return maxX - minX > width * 3 || maxY - minY > height * 3;
        }

        private Vector3 ToView(Vector3 point)
        {
            var relative = point - Eye;
            return new Vector3(Vector3.Dot(relative, Right), Vector3.Dot(relative, Up), Vector3.Dot(relative, Forward));
        }

        private Point ProjectView(Vector3 view, double width, double height)
        {
            var divisor = Orthographic ? OrthoReferenceDepth : view.Z;
            return new Point(
                width / 2 + view.X / divisor * Focal,
                height / 2 - view.Y / divisor * Focal);
        }
    }

    private static void Render2D(
        DrawingContext context,
        IReadOnlyList<ISceneRenderable> objects,
        ISceneRenderable? selected,
        string assetsPath,
        double width,
        double height,
        Metrics metrics,
        bool showGrid,
        ISceneRenderable? camera)
    {
        var (scale, origin) = Map2D(width, height, camera);
        var world = new Rect(
            origin.X - WorldWidth / 2 * scale,
            origin.Y - WorldHeight / 2 * scale,
            WorldWidth * scale,
            WorldHeight * scale);
        context.DrawRectangle(FloorBrush, showGrid ? WorldBorderPen : null, world);
        metrics.Draw(2, 4);

        if (showGrid)
        {
            for (var i = -11; i <= 11; i++)
            {
                context.DrawLine(GridPen, new Point(origin.X + i * scale, world.Top), new Point(origin.X + i * scale, world.Bottom));
                metrics.Line();
            }
            for (var i = -6; i <= 6; i++)
            {
                context.DrawLine(GridPen, new Point(world.Left, origin.Y + i * scale), new Point(world.Right, origin.Y + i * scale));
                metrics.Line();
            }

            context.DrawLine(AxisXPen, new Point(world.Left, origin.Y), new Point(world.Right, origin.Y));
            metrics.Line();
            context.DrawLine(AxisZPen, new Point(origin.X, world.Top), new Point(origin.X, world.Bottom));
            metrics.Line();
        }

        var ordered = new List<ISceneRenderable>(objects);
        ordered.Sort((a, b) => a.Y.CompareTo(b.Y));
        foreach (var node in ordered)
        {
            if (!node.Visible)
                continue;
            var center = new Point(origin.X + node.X * scale, origin.Y + node.Y * scale);
            var sizeX = Math.Max(6, scale * 1.1 * Math.Max(0.1, node.ScaleX));
            var sizeY = Math.Max(6, scale * 1.1 * Math.Max(0.1, node.ScaleY));
            var size = Math.Max(sizeX, sizeY);
            switch (node.Kind)
            {
                case SceneNodeKind.Light:
                    context.DrawEllipse(LightBrush, null, center, size * 1.8, size * 1.8);
                    context.DrawEllipse(LightCoreBrush, null, center, size * 0.16, size * 0.16);
                    break;
                case SceneNodeKind.Audio:
                    context.DrawEllipse(AudioBrush, null, center, size * 0.5, size * 0.5);
                    break;
                case SceneNodeKind.Camera:
                    context.DrawRectangle(null, CameraPen, new Rect(center.X - sizeX, center.Y - sizeY * 0.75, sizeX * 2, sizeY * 1.5));
                    break;
                case SceneNodeKind.Tileset:
                    var tileWidth = Math.Max(8, scale * 4 * Math.Max(0.1, node.ScaleX));
                    var tileHeight = Math.Max(8, scale * 4 * Math.Max(0.1, node.ScaleY));
                    context.DrawRectangle(TileBrush, TilePen, new Rect(center.X - tileWidth / 2, center.Y - tileHeight / 2, tileWidth, tileHeight));
                    break;
                case SceneNodeKind.Group:
                    break;
                default:
                    var rect = new Rect(center.X - sizeX / 2, center.Y - sizeY / 2, sizeX, sizeY);
                    var rotation = Matrix.CreateTranslation(-center.X, -center.Y)
                                   * Matrix.CreateRotation(node.RotationZ * Math.PI / 180)
                                   * Matrix.CreateTranslation(center.X, center.Y);
                    using (context.PushTransform(rotation))
                    {
                        if (TryLoadImage(assetsPath, node.Asset) is { } bitmap)
                        {
                            context.DrawImage(
                                bitmap,
                                new Rect(0, 0, bitmap.PixelSize.Width, bitmap.PixelSize.Height),
                                rect);
                        }
                        else
                        {
                            var radius = 3 * Math.Max(0.1, Math.Max(node.ScaleX, node.ScaleY));
                            context.DrawRectangle(MakeBrush(FillFor(node.Kind)), EdgePen, rect, radius, radius);
                            context.DrawRectangle(MakeBrush(AccentFor(node.Kind)), null,
                                new Rect(rect.X, rect.Y, rect.Width, rect.Height * 0.24));
                        }
                    }
                    break;
            }

            if (ReferenceEquals(node, selected))
                DrawSelection2D(context, center, size, metrics);
        }
    }

    private static void DrawSelection2D(DrawingContext context, Point center, double size, Metrics metrics)
    {
        var padding = size * 0.8 + 4;
        var selectionRect = new Rect(center.X - padding / 2, center.Y - padding / 2, padding, padding);
        context.DrawRectangle(null, SelectionPen, selectionRect, 2, 2);
        metrics.Draw(2, 4);
        context.DrawEllipse(null, SelectionPen, center, 2.5, 2.5);
        metrics.Draw(EllipseTriangles, EllipseTriangles);
    }

    private static void Render3D(
        DrawingContext context,
        IReadOnlyList<ISceneRenderable> objects,
        ISceneRenderable? selected,
        string assetsPath,
        double width,
        double height,
        Metrics metrics,
        bool showGrid,
        ISceneRenderable? cameraNode)
    {
        var camera = Camera3D.Create(width, height, cameraNode);
        (Point Point, float Depth)? Project(Vector3 point) =>
            camera.TryProject(point, width, height, out var screen, out var depth) ? (screen, depth) : null;

        void Segment(Vector3 from, Vector3 to, IPen pen)
        {
            if (camera.TryProjectSegment(from, to, width, height, out var fromScreen, out var toScreen))
            {
                context.DrawLine(pen, fromScreen, toScreen);
                metrics.Line();
            }
        }

        if (showGrid)
        {
            for (var i = -8; i <= 8; i++)
            {
                Segment(new Vector3(i, 0, -8), new Vector3(i, 0, 8), FloorPen);
                Segment(new Vector3(-8, 0, i), new Vector3(8, 0, i), FloorPen);
            }
            Segment(new Vector3(-8, 0.01f, 0), new Vector3(8, 0.01f, 0), AxisXPen);
            Segment(new Vector3(0, 0.01f, -8), new Vector3(0, 0.01f, 8), AxisZPen);
        }

        var faces = new List<Face>(64);
        var lines = new List<(Vector3 From, Vector3 To, IPen Pen)>();

        void AddFace(Vector3[] points, IBrush fill, IPen? pen)
        {
            if (!camera.TryProjectPolygon(points, width, height, out var projected, out var depth, out var clipped))
                return;
            faces.Add(new Face(projected, clipped ? float.MaxValue : depth, fill, pen));
            metrics.Draw(points.Length - 2, points.Length);
        }

        Vector3[] Corners(Vector3 center, Vector3 size, Vector3 rotation)
        {
            var halfX = size.X / 2;
            var halfY = size.Y / 2;
            var halfZ = size.Z / 2;
            var corners = new Vector3[8];

            for (var i = 0; i < 8; i++)
            {
                var x = i is 1 or 2 or 5 or 6 ? halfX : -halfX;
                var y = i < 4 ? -halfY : halfY;
                var z = i is 2 or 3 or 6 or 7 ? halfZ : -halfZ;
                corners[i] = center + RotatePoint(new Vector3(x, y, z), rotation);
            }

            return corners;
        }

        void AddBox(Vector3 center, Vector3 size, Vector3 rotation, IBrush topFace, IBrush leftFace, IBrush rightFace)
        {
            var corners = Corners(center, size, rotation);
            AddFace([corners[4], corners[5], corners[6], corners[7]], topFace, EdgePen);
            AddFace([corners[0], corners[1], corners[5], corners[4]], leftFace, EdgePen);
            AddFace([corners[1], corners[2], corners[6], corners[5]], rightFace, EdgePen);
            AddFace([corners[2], corners[3], corners[7], corners[6]], leftFace, EdgePen);
            AddFace([corners[3], corners[0], corners[4], corners[7]], rightFace, EdgePen);
        }

        void AddWireframe(Vector3[] corners, IPen pen)
        {
            int[][] edges =
            [
                [0, 1], [1, 2], [2, 3], [3, 0],
                [4, 5], [5, 6], [6, 7], [7, 4],
                [0, 4], [1, 5], [2, 6], [3, 7],
            ];
            foreach (var edge in edges)
                lines.Add((corners[edge[0]], corners[edge[1]], pen));
        }

        Vector3 Transform(Vector3 point, ISceneRenderable node, Vector3 rotation, Vector3 scale) =>
            new Vector3((float)node.X, (float)node.Y, (float)node.Z) + RotatePoint(point * scale, rotation);

        void AddMesh(MeshGeometry mesh, ISceneRenderable node, Vector3 rotation)
        {
            var scale = new Vector3(
                (float)Math.Max(0.01, node.ScaleX),
                (float)Math.Max(0.01, node.ScaleY),
                (float)Math.Max(0.01, node.ScaleZ));
            var triangles = mesh.Triangles;

            for (var index = 0; index + 2 < triangles.Length; index += 3)
            {
                var a = Transform(triangles[index], node, rotation, scale);
                var b = Transform(triangles[index + 1], node, rotation, scale);
                var c = Transform(triangles[index + 2], node, rotation, scale);

                var normal = Vector3.Cross(b - a, c - a);
                var length = normal.Length();
                if (!float.IsFinite(length) || length < 1e-6f)
                    continue;

                if (!camera.TryProjectPolygon([a, b, c], width, height, out var projected, out var depth, out var clipped))
                    continue;

                normal /= length;
                var intensity = Math.Clamp(0.25f + 0.85f * Vector3.Dot(normal, MeshLight), 0f, 1f);
                var shade = Math.Clamp((int)(intensity * (MeshShadeSteps - 1)), 0, MeshShadeSteps - 1);

                faces.Add(new Face(projected, clipped ? float.MaxValue : depth, MeshShades[shade], null));
                metrics.Draw(1, 3);
            }
        }

        Vector3[] MeshCorners(MeshGeometry mesh, ISceneRenderable node, Vector3 rotation)
        {
            var scale = new Vector3(
                (float)Math.Max(0.01, node.ScaleX),
                (float)Math.Max(0.01, node.ScaleY),
                (float)Math.Max(0.01, node.ScaleZ));
            var size = mesh.Max - mesh.Min;
            var center = new Vector3((float)node.X, (float)node.Y, (float)node.Z) + RotatePoint(mesh.Center * scale, rotation);

            return Corners(center, new Vector3(
                Math.Max(size.X * scale.X, 0.02f),
                Math.Max(size.Y * scale.Y, 0.02f),
                Math.Max(size.Z * scale.Z, 0.02f)), rotation);
        }

        foreach (var node in objects)
        {
            if (!node.Visible || node.Kind == SceneNodeKind.Group || node.Kind == SceneNodeKind.Audio)
                continue;
            if (ReferenceEquals(node, cameraNode))
                continue;
            var size = new Vector3(
                1.6f * (float)Math.Max(0.1, node.ScaleX),
                1.6f * (float)Math.Max(0.1, node.ScaleY),
                1.6f * (float)Math.Max(0.1, node.ScaleZ));
            var center = new Vector3((float)node.X, (float)node.Y + size.Y / 2, (float)node.Z);
            var rotation = new Vector3(
                (float)(node.RotationX * Math.PI / 180),
                (float)(node.RotationY * Math.PI / 180),
                (float)(node.RotationZ * Math.PI / 180));

            if (node.Kind is SceneNodeKind.Entity or SceneNodeKind.Model
                && TryLoadMesh(assetsPath, node.Asset) is { } mesh)
            {
                AddMesh(mesh, node, rotation);

                if (ReferenceEquals(node, selected))
                    AddWireframe(MeshCorners(mesh, node, rotation), SelectionPen);

                continue;
            }

            var (top, left, rightFace) = Palette(node.Kind);
            AddBox(center, size, rotation, top, left, rightFace);
            if (node.Kind == SceneNodeKind.Light)
            {
                if (Project(center + new Vector3(0, size.Y * 0.8f, 0)) is { } glow)
                {
                    faces.Add(new Face(
                        [
                            new Point(glow.Point.X - 7, glow.Point.Y - 7),
                            new Point(glow.Point.X + 7, glow.Point.Y - 7),
                            new Point(glow.Point.X + 7, glow.Point.Y + 7),
                            new Point(glow.Point.X - 7, glow.Point.Y + 7),
                        ],
                        glow.Depth - 0.01f,
                        LightCoreBrush,
                        EdgePen));
                    metrics.Draw(2, 4);
                }
            }
            if (ReferenceEquals(node, selected))
                AddWireframe(Corners(center, size * 1.08f, rotation), SelectionPen);
        }

        faces.Sort((a, b) => b.Depth.CompareTo(a.Depth));
        foreach (var face in faces)
            DrawFace(context, face, metrics);
        foreach (var (from, to, pen) in lines)
            Segment(from, to, pen);
    }

    private static Vector3 RotatePoint(Vector3 point, Vector3 rotation)
    {
        var cos = MathF.Cos(rotation.Z);
        var sin = MathF.Sin(rotation.Z);
        var x = point.X * cos - point.Y * sin;
        var y = point.X * sin + point.Y * cos;
        var z = point.Z;

        cos = MathF.Cos(rotation.X);
        sin = MathF.Sin(rotation.X);
        var y2 = y * cos - z * sin;
        var z2 = y * sin + z * cos;

        cos = MathF.Cos(rotation.Y);
        sin = MathF.Sin(rotation.Y);
        var x3 = x * cos + z2 * sin;
        var z3 = -x * sin + z2 * cos;

        return new Vector3(x3, y2, z3);
    }

    private static void DrawFace(DrawingContext context, Face face, Metrics metrics)
    {
        var geometry = new StreamGeometry();
        using (var geometryContext = geometry.Open())
        {
            geometryContext.BeginFigure(face.Points[0], true);
            for (var i = 1; i < face.Points.Length; i++)
                geometryContext.LineTo(face.Points[i]);
            geometryContext.EndFigure(true);
        }
        context.DrawGeometry(face.Fill, face.Pen, geometry);
        metrics.DrawGeometry();
    }

    private static (IBrush Top, IBrush Left, IBrush Right) Palette(SceneNodeKind kind) => kind switch
    {
        SceneNodeKind.Sprite => (MakeBrush("#C7E05A"), MakeBrush("#8FA638"), MakeBrush("#5C6E20")),
        SceneNodeKind.Model => (MakeBrush("#8B7BE8"), MakeBrush("#6A5FBF"), MakeBrush("#4A4290")),
        SceneNodeKind.Tileset => (MakeBrush("#2F5A45"), MakeBrush("#244737"), MakeBrush("#183228")),
        SceneNodeKind.Light => (MakeBrush("#E8D66A"), MakeBrush("#B7A83F"), MakeBrush("#8A7E2A")),
        SceneNodeKind.Camera => (MakeBrush("#5E7FB8"), MakeBrush("#46618F"), MakeBrush("#2F4468")),
        _ => (MakeBrush("#16C200"), MakeBrush("#0A7500"), MakeBrush("#0C2A09")),
    };

    private static string FillFor(SceneNodeKind kind) => kind switch
    {
        SceneNodeKind.Sprite => "#22C57C",
        SceneNodeKind.Model => "#8B7BE8",
        SceneNodeKind.Tileset => "#24503C",
        _ => "#0EA800",
    };

    private static string AccentFor(SceneNodeKind kind) => kind switch
    {
        SceneNodeKind.Sprite => "#45D893",
        SceneNodeKind.Model => "#A99BF5",
        SceneNodeKind.Tileset => "#3B7A5C",
        _ => "#2AE914",
    };

    public static Bitmap? TryLoadImage(string assetsPath, string relativeAsset)
    {
        if (string.IsNullOrWhiteSpace(relativeAsset) || string.IsNullOrWhiteSpace(assetsPath))
            return null;
        var fullPath = Path.Combine(assetsPath, relativeAsset.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
            return null;
        lock (ImageLock)
        {
            if (ImageCache.TryGetValue(fullPath, out var cached))
                return cached;
            try
            {
                var bitmap = new Bitmap(fullPath);
                ImageCache[fullPath] = bitmap;
                return bitmap;
            }
            catch (Exception)
            {
                ImageCache[fullPath] = null;
                return null;
            }
        }
    }

    private static MeshGeometry? TryLoadMesh(string assetsPath, string asset)
    {
        if (string.IsNullOrWhiteSpace(asset) || string.IsNullOrWhiteSpace(assetsPath))
            return null;

        if (AssetEntry.DetectKind(Path.GetExtension(asset)) != AssetKind.Model3D)
            return null;

        var fullPath = Path.Combine(assetsPath, asset.Replace('/', Path.DirectorySeparatorChar));
        return MeshLibrary.TryLoad(fullPath, out var geometry) ? geometry : null;
    }

    private static IBrush[] CreateMeshShades()
    {
        var brushes = new IBrush[MeshShadeSteps];

        for (var index = 0; index < MeshShadeSteps; index++)
        {
            var value = (byte)(48 + index * 11);
            brushes[index] = new SolidColorBrush(Color.FromRgb(value, (byte)(value + 4), (byte)(value + 12)));
        }

        return brushes;
    }


    private static IBrush MakeBrush(string hex) => new SolidColorBrush(Color.Parse(hex));

    private static IPen MakePen(string hex, double thickness = 1) => new Pen(MakeBrush(hex), thickness);

    private readonly record struct Face(Point[] Points, float Depth, IBrush Fill, IPen? Pen);

    private sealed class Metrics
    {
        private readonly HashSet<string> _textures = new(StringComparer.OrdinalIgnoreCase);
        private int _drawCalls;
        private int _triangles;
        private int _vertices;
        private int _buffers;
        private long _textureBytes;

        public void Draw(int triangles, int vertices)
        {
            _drawCalls++;
            _triangles += triangles;
            _vertices += vertices;
        }

        public void Line() => Draw(2, 2);

        public void DrawGeometry()
        {
            _drawCalls++;
            _buffers++;
        }

        public void AddTexture(string path, Bitmap bitmap)
        {
            if (!_textures.Add(path))
                return;
            _textureBytes += (long)bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4;
        }

        public SceneStats Snapshot() =>
            new(_drawCalls, _triangles, _vertices, _buffers, _textures.Count, _textureBytes);
    }
}




