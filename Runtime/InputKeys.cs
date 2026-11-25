using Avalonia.Input;

namespace PodpivasEngine.Runtime;

public static class InputKeys
{
    public static string? FromKey(Key key)
    {
        if (key is >= Key.A and <= Key.Z)
            return key.ToString().ToLowerInvariant();

        if (key is >= Key.D0 and <= Key.D9)
            return ((char)('0' + (key - Key.D0))).ToString();

        if (key is >= Key.NumPad0 and <= Key.NumPad9)
            return ((char)('0' + (key - Key.NumPad0))).ToString();

        return key switch
        {
            Key.Up => "up",
            Key.Down => "down",
            Key.Left => "left",
            Key.Right => "right",
            Key.Space => "space",
            Key.Enter => "enter",
            Key.Escape => "escape",
            Key.Tab => "tab",
            Key.Back => "backspace",
            Key.LeftShift or Key.RightShift => "shift",
            Key.LeftCtrl or Key.RightCtrl => "control",
            Key.LeftAlt or Key.RightAlt => "alt",
            _ => null,
        };
    }
}
