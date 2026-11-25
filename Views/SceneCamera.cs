using System;
using System.Numerics;
using PodpivasEngine.Models;

namespace PodpivasEngine.Views;

public static class SceneCamera
{
    public const double RotateSensitivity = 0.0025;
    public const float MoveSpeed = 8f;
    public const float WheelStep = 1.5f;

    private const float MaxPitch = 1.5f;

    private static Vector3 _eye;
    private static float _yaw;
    private static float _pitch;

    static SceneCamera() => Reset();

    public static Vector3 Eye => _eye;

    public static Vector3 Forward
    {
        get
        {
            var cosPitch = MathF.Cos(_pitch);
            return new Vector3(cosPitch * MathF.Sin(_yaw), MathF.Sin(_pitch), cosPitch * MathF.Cos(_yaw));
        }
    }

    public static Vector3 Right => Vector3.Normalize(Vector3.Cross(Forward, Vector3.UnitY));

    public static Vector3 Up => Vector3.Cross(Right, Forward);

    public static void Reset()
    {
        _eye = new Vector3(0, 6.5f, 12);
        var direction = Vector3.Normalize(new Vector3(0, 0.8f, 0) - _eye);
        _pitch = MathF.Asin(direction.Y);
        _yaw = MathF.Atan2(direction.X, direction.Z);
    }

    public static EditorCameraState Capture() => new()
    {
        X = _eye.X,
        Y = _eye.Y,
        Z = _eye.Z,
        Yaw = _yaw,
        Pitch = _pitch,
    };

    public static void Apply(EditorCameraState? state)
    {
        if (state is null || !IsValid(state))
        {
            Reset();
            return;
        }

        _eye = new Vector3(state.X, state.Y, state.Z);
        _yaw = state.Yaw;
        _pitch = Math.Clamp(state.Pitch, -MaxPitch, MaxPitch);
    }

    private static bool IsValid(EditorCameraState state) =>
        float.IsFinite(state.X) && float.IsFinite(state.Y) && float.IsFinite(state.Z)
        && float.IsFinite(state.Yaw) && float.IsFinite(state.Pitch);

    public static void Rotate(double deltaX, double deltaY)
    {
        _yaw -= (float)(deltaX * RotateSensitivity);
        _pitch = Math.Clamp(_pitch - (float)(deltaY * RotateSensitivity), -MaxPitch, MaxPitch);
    }

    public static void Move(Vector3 direction, float distance)
    {
        var length = direction.Length();
        if (length < 0.0001f)
            return;

        _eye += direction / length * distance;
    }
}
