namespace PodpivasEngine.Models;

public readonly record struct SceneStats(
    int DrawCalls,
    int Triangles,
    int Vertices,
    int Buffers,
    int Textures,
    long TextureBytes);
