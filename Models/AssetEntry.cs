using System.IO;

namespace PodpivasEngine.Models;

public enum AssetKind
{
    Folder,
    Image,
    Audio,
    Model3D,
    Script,
    Other,
}

public sealed record AssetEntry(
    string Name,
    string RelativePath,
    string FullPath,
    bool IsFolder,
    AssetKind Kind)
{
    public string Extension => Path.GetExtension(Name).ToLowerInvariant();

    public string Glyph => Kind switch
    {
        AssetKind.Folder => "▸",
        AssetKind.Image => "▤",
        AssetKind.Audio => "♫",
        AssetKind.Model3D => "◈",
        AssetKind.Script => "◆",
        _ => "•",
    };

    public bool IsModel => Kind == AssetKind.Model3D;

    public static AssetKind DetectKind(string extension) => extension.ToLowerInvariant() switch
    {
        ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp" or ".svg" => AssetKind.Image,
        ".wav" or ".mp3" or ".ogg" or ".flac" => AssetKind.Audio,
        ".obj" or ".fbx" or ".blend" or ".gltf" or ".glb" or ".dae" or ".3ds" or ".stl" => AssetKind.Model3D,
        ".lua" => AssetKind.Script,
        _ => AssetKind.Other,
    };
}
