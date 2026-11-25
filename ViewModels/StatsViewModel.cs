using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using PodpivasEngine.Models;

namespace PodpivasEngine.ViewModels;

public sealed partial class StatsViewModel : ViewModelBase
{
    private const int IntervalWindow = 60;

    private readonly Queue<double> _frameIntervals = new();
    private long _lastFrameTimestamp;

    [ObservableProperty]
    private string _backend = nameof(RenderBackend.WebGPU);

    [ObservableProperty]
    private string _fps = "—";

    [ObservableProperty]
    private string _frame = "—";

    [ObservableProperty]
    private string _drawCalls = "0";

    [ObservableProperty]
    private string _triangles = "0";

    [ObservableProperty]
    private string _vertices = "0";

    [ObservableProperty]
    private string _vram = "0.0 MB";

    [ObservableProperty]
    private string _textures = "0";

    [ObservableProperty]
    private string _buffers = "0";

    [ObservableProperty]
    private string _resolution = "—";

    public void Update(EditorProject project)
    {
        Backend = project.Backend.ToString();
        _frameIntervals.Clear();
        _lastFrameTimestamp = 0;
        Fps = "—";
    }

    public void Report(SceneStats stats, double pixelWidth, double pixelHeight, double renderMilliseconds)
    {
        var now = Stopwatch.GetTimestamp();
        if (_lastFrameTimestamp != 0)
        {
            _frameIntervals.Enqueue((now - _lastFrameTimestamp) * 1000.0 / Stopwatch.Frequency);
            while (_frameIntervals.Count > IntervalWindow)
                _frameIntervals.Dequeue();

            var total = 0.0;
            foreach (var interval in _frameIntervals)
                total += interval;

            if (total > 0)
                Fps = (1000.0 * _frameIntervals.Count / total).ToString("0", CultureInfo.InvariantCulture);
        }

        _lastFrameTimestamp = now;
        Frame = renderMilliseconds.ToString("0.0", CultureInfo.InvariantCulture) + " ms";
        DrawCalls = FormatCount(stats.DrawCalls);
        Triangles = FormatCount(stats.Triangles);
        Vertices = FormatCount(stats.Vertices);
        Buffers = stats.Buffers.ToString(CultureInfo.InvariantCulture);
        Textures = stats.Textures.ToString(CultureInfo.InvariantCulture);
        Vram = (stats.TextureBytes / (1024.0 * 1024.0)).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
        Resolution = $"{Math.Round(pixelWidth)}x{Math.Round(pixelHeight)}";
    }

    private static string FormatCount(int value) =>
        value >= 1000
            ? (value / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + "K"
            : value.ToString(CultureInfo.InvariantCulture);
}
