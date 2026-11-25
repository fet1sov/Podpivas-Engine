using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Assimp;

namespace PodpivasEngine.Services;

public sealed class MeshGeometry
{
    public Vector3[] Triangles { get; init; } = [];

    public Vector3 Min { get; init; }

    public Vector3 Max { get; init; }

    public Vector3 Center => (Min + Max) / 2f;

    public float Radius => (Max - Min).Length() / 2f;
}

public static class MeshLibrary
{
    public const int MaxTriangles = 20000;

    private const float TargetSize = 1.5f;

    private static readonly PostProcessSteps Steps =
        PostProcessSteps.Triangulate
        | PostProcessSteps.PreTransformVertices
        | PostProcessSteps.JoinIdenticalVertices
        | PostProcessSteps.SortByPrimitiveType;

    private static readonly Dictionary<string, CacheEntry> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object CacheLock = new();
    private static AssimpContext? _context;

    private sealed record CacheEntry(DateTime Stamp, MeshGeometry? Geometry);

    public static bool TryLoad(string path, out MeshGeometry geometry)
    {
        geometry = null!;

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;

        var stamp = File.GetLastWriteTimeUtc(path);

        lock (CacheLock)
        {
            if (Cache.TryGetValue(path, out var cached) && cached.Stamp == stamp)
            {
                if (cached.Geometry is null)
                    return false;

                geometry = cached.Geometry;
                return true;
            }
        }

        MeshGeometry? loaded;
        try
        {
            loaded = Load(path);
        }
        catch (Exception)
        {
            loaded = null;
        }

        lock (CacheLock)
            Cache[path] = new CacheEntry(stamp, loaded);

        if (loaded is null)
            return false;

        geometry = loaded;
        return true;
    }

    private static MeshGeometry? Load(string path)
    {
        lock (CacheLock)
            _context ??= new AssimpContext();

        var scene = _context!.ImportFile(path, Steps);
        var points = new List<Vector3>(1024);

        foreach (var mesh in scene.Meshes)
        {
            if (mesh.PrimitiveType != PrimitiveType.Triangle)
                continue;

            foreach (var face in mesh.Faces)
            {
                if (face.IndexCount < 3)
                    continue;

                for (var index = 1; index + 1 < face.IndexCount; index++)
                {
                    AddVertex(mesh, face.Indices[0], points);
                    AddVertex(mesh, face.Indices[index], points);
                    AddVertex(mesh, face.Indices[index + 1], points);

                    if (points.Count >= MaxTriangles * 3)
                        break;
                }

                if (points.Count >= MaxTriangles * 3)
                    break;
            }

            if (points.Count >= MaxTriangles * 3)
                break;
        }

        if (points.Count < 3)
            return null;

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);

        foreach (var point in points)
        {
            min = Vector3.Min(min, point);
            max = Vector3.Max(max, point);
        }

        var size = max - min;
        var extent = MathF.Max(size.X, MathF.Max(size.Y, size.Z));
        var scale = extent > 1e-6f ? TargetSize / extent : 1f;
        var centerX = (min.X + max.X) * 0.5f;
        var centerZ = (min.Z + max.Z) * 0.5f;

        for (var index = 0; index < points.Count; index++)
        {
            var point = points[index];
            points[index] = new Vector3(
                (point.X - centerX) * scale,
                (point.Y - min.Y) * scale,
                (point.Z - centerZ) * scale);
        }

        return new MeshGeometry
        {
            Triangles = points.ToArray(),
            Min = new Vector3((min.X - centerX) * scale, 0, (min.Z - centerZ) * scale),
            Max = new Vector3((max.X - centerX) * scale, (max.Y - min.Y) * scale, (max.Z - centerZ) * scale),
        };
    }

    private static void AddVertex(Mesh mesh, int index, List<Vector3> points)
    {
        if (index < 0 || index >= mesh.VertexCount)
            return;

        var vertex = mesh.Vertices[index];
        points.Add(new Vector3(vertex.X, vertex.Y, vertex.Z));
    }
}
