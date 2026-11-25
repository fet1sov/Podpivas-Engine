using System.Collections.Generic;

namespace PodpivasEngine.Runtime;

public sealed class RuntimeInput
{
    private readonly HashSet<string> _held = [];
    private readonly HashSet<string> _pressed = [];
    private readonly HashSet<string> _released = [];
    private readonly HashSet<string> _buttonsHeld = [];
    private readonly HashSet<string> _buttonsPressed = [];

    public double MouseX { get; private set; }

    public double MouseY { get; private set; }

    public bool IsDown(string key) => _held.Contains(key);

    public bool WasPressed(string key) => _pressed.Contains(key);

    public bool WasReleased(string key) => _released.Contains(key);

    public bool IsButtonDown(string button) => _buttonsHeld.Contains(button);

    public bool WasButtonPressed(string button) => _buttonsPressed.Contains(button);

    public void SetKey(string key, bool isDown)
    {
        if (isDown)
        {
            if (_held.Add(key))
                _pressed.Add(key);
        }
        else if (_held.Remove(key))
        {
            _released.Add(key);
        }
    }

    public void SetButton(string button, bool isDown)
    {
        if (isDown)
        {
            if (_buttonsHeld.Add(button))
                _buttonsPressed.Add(button);
        }
        else
        {
            _buttonsHeld.Remove(button);
        }
    }

    public void SetMousePosition(double x, double y)
    {
        MouseX = x;
        MouseY = y;
    }

    public void EndFrame()
    {
        _pressed.Clear();
        _released.Clear();
        _buttonsPressed.Clear();
    }

    public void Clear()
    {
        _held.Clear();
        _pressed.Clear();
        _released.Clear();
        _buttonsHeld.Clear();
        _buttonsPressed.Clear();
    }
}
