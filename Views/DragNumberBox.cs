using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace PodpivasEngine.Views;

public sealed class DragNumberBox : TextBox
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<DragNumberBox, double>(nameof(Value), 1, defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<double> StepProperty =
        AvaloniaProperty.Register<DragNumberBox, double>(nameof(Step), 0.05);

    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<DragNumberBox, double>(nameof(Minimum), 0.01);

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<DragNumberBox, double>(nameof(Maximum), 1000);

    private bool _pressed;
    private bool _scrubbed;
    private Point _origin;
    private double _startValue;

    static DragNumberBox()
    {
        ValueProperty.Changed.AddClassHandler<DragNumberBox>((box, _) => box.SyncText());
    }

    public DragNumberBox()
    {
        Text = "1";
        LostFocus += (_, _) => Commit();
    }

    protected override Type StyleKeyOverride => typeof(TextBox);

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Step
    {
        get => GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    public double Minimum
    {
        get => GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            base.OnPointerPressed(e);
            return;
        }

        Focus();
        _pressed = true;
        _scrubbed = false;
        _origin = e.GetPosition(this);
        _startValue = Value;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (!_pressed)
        {
            base.OnPointerMoved(e);
            return;
        }

        var position = e.GetPosition(this);
        var dx = position.X - _origin.X;

        if (!_scrubbed && Math.Abs(dx) < 3 && Math.Abs(position.Y - _origin.Y) < 3)
            return;

        _scrubbed = true;
        var step = Step * (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 0.1 : 1);
        Value = Math.Clamp(_startValue + dx * step, Minimum, Maximum);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (!_pressed)
        {
            base.OnPointerReleased(e);
            return;
        }

        _pressed = false;
        e.Pointer.Capture(null);

        if (!_scrubbed)
            SelectAll();

        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        _pressed = false;
        base.OnPointerCaptureLost(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            Commit();

        base.OnKeyDown(e);
    }

    private void Commit()
    {
        if (double.TryParse(Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var parsed)
            || double.TryParse(Text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
        {
            Value = parsed;
            return;
        }

        SyncText();
    }

    private void SyncText()
    {
        var text = Value.ToString("0.###", CultureInfo.CurrentCulture);
        if (!string.Equals(Text, text, StringComparison.Ordinal))
            Text = text;
    }
}
