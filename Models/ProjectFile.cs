using System.Collections.Generic;

namespace PodpivasEngine.Models;

public sealed class ProjectFile
{
    public string Name { get; set; } = string.Empty;

    public string GameName { get; set; } = string.Empty;

    public string GameVersion { get; set; } = "0.1.0";

    public string MainCamera { get; set; } = string.Empty;

    public EditorCameraState? EditorCamera { get; set; }

    public string Renderer { get; set; } = nameof(RenderBackend.WebGPU);

    public string Space { get; set; } = "3D";

    public int Version { get; set; } = 1;

    public List<SceneNodeData> Nodes { get; set; } = [];
}

public sealed class SceneNodeData
{
    public string Name { get; set; } = "node";

    public string Kind { get; set; } = nameof(SceneNodeKind.Entity);

    public double X { get; set; }

    public double Y { get; set; }

    public double Z { get; set; }

    public double Rotation { get; set; }

    public double RotationX { get; set; }

    public double RotationY { get; set; }

    public double RotationZ { get; set; }

    public double Scale { get; set; } = 1;

    public double ScaleX { get; set; }

    public double ScaleY { get; set; }

    public double ScaleZ { get; set; }

    public bool Visible { get; set; } = true;

    public string Asset { get; set; } = string.Empty;

    public string Projection { get; set; } = nameof(CameraProjection.Perspective);

    public double FieldOfView { get; set; } = 50;

    public string FovAxis { get; set; } = nameof(CameraFovAxis.Vertical);

    public double Pitch { get; set; }

    public double Yaw { get; set; }

    public string Script { get; set; } = string.Empty;

    public List<ScriptComponentData> Components { get; set; } = [];

    public List<SceneNodeData> Children { get; set; } = [];
}

public sealed class ScriptComponentData
{
    public string Script { get; set; } = string.Empty;

    public Dictionary<string, string> Values { get; set; } = [];
}
