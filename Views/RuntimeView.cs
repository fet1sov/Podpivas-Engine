using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using PodpivasEngine.Models;
using PodpivasEngine.ViewModels;

namespace PodpivasEngine.Views;

public sealed class RuntimeView : Control
{
    private RuntimeViewModel? _runtime;

    public RuntimeView()
    {
        ClipToBounds = true;
        DataContextChanged += (_, _) => BindRuntime();
        PointerMoved += OnPointerMoved;
        PointerPressed += OnPointerPressed;
        PointerReleased += OnPointerReleased;
        PointerExited += OnPointerExited;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        BindRuntime();
        if (_runtime is not null)
        {
            _runtime.Frame -= OnFrame;
            _runtime.Frame += OnFrame;
        }
        InvalidateVisual();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_runtime is not null)
            _runtime.Frame -= OnFrame;
    }

    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 1 || height <= 1)
            return;

        context.DrawRectangle(new SolidColorBrush(Color.Parse("#080808")), null, new Rect(0, 0, width, height));

        if (_runtime is null)
            return;

        SceneRenderer.Render(
            context,
            _runtime.Space,
            _runtime.Objects,
            null,
            _runtime.AssetsPath,
            width,
            height,
            showGrid: false,
            camera: _runtime.MainCamera);
    }

    private void BindRuntime()
    {
        var runtime = DataContext as RuntimeViewModel;
        if (ReferenceEquals(runtime, _runtime))
            return;

        if (_runtime is not null)
            _runtime.Frame -= OnFrame;

        _runtime = runtime;

        if (_runtime is not null)
            _runtime.Frame += OnFrame;

        InvalidateVisual();
    }

    private void OnFrame(object? sender, EventArgs e) => InvalidateVisual();

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var point = e.GetPosition(this);
        _runtime?.SetMouse(point.X, Math.Max(0, Bounds.Height - point.Y));
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var properties = e.GetCurrentPoint(this).Properties;
        if (properties.IsLeftButtonPressed)
            _runtime?.SetButton("left", true);
        if (properties.IsRightButtonPressed)
            _runtime?.SetButton("right", true);
        if (properties.IsMiddleButtonPressed)
            _runtime?.SetButton("middle", true);
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _runtime?.SetButton(e.InitialPressMouseButton switch
        {
            MouseButton.Left => "left",
            MouseButton.Right => "right",
            _ => "middle",
        }, false);
    }

    private void OnPointerExited(object? sender, PointerEventArgs e) => _runtime?.ReleaseInput();
}
