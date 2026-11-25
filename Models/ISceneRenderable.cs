namespace PodpivasEngine.Models;

public interface ISceneRenderable
{
    string Name { get; }

    SceneNodeKind Kind { get; }

    double X { get; }

    double Y { get; }

    double Z { get; }

    double RotationX { get; }

    double RotationY { get; }

    double RotationZ { get; }

    double ScaleX { get; }

    double ScaleY { get; }

    double ScaleZ { get; }

    bool Visible { get; }

    string Asset { get; }

    CameraProjection Projection { get; }

    double FieldOfView { get; }

    CameraFovAxis FovAxis { get; }

    double Pitch { get; }

    double Yaw { get; }
}
