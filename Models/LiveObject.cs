namespace PodpivasEngine.Models;

public sealed class LiveObject : ISceneRenderable
{
    public required string Name { get; init; }

    public required SceneNodeKind Kind { get; init; }

    public double X { get; set; }

    public double Y { get; set; }

    public double Z { get; set; }

    public double RotationX { get; set; }

    public double RotationY { get; set; }

    public double RotationZ { get; set; }

    public double ScaleX { get; set; } = 1;

    public double ScaleY { get; set; } = 1;

    public double ScaleZ { get; set; } = 1;

    public bool Visible { get; set; } = true;

    public string Asset { get; set; } = string.Empty;

    public CameraProjection Projection { get; set; } = CameraProjection.Perspective;

    public double FieldOfView { get; set; } = 50;

    public CameraFovAxis FovAxis { get; set; } = CameraFovAxis.Vertical;

    public double Pitch { get; set; }

    public double Yaw { get; set; }

    public LiveObject Clone() => new()
    {
        Name = Name,
        Kind = Kind,
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
}
