using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using PodpivasEngine.Models;
using PodpivasEngine.Runtime;

namespace PodpivasEngine.ViewModels;

public sealed partial class RuntimeViewModel : ViewModelBase
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private EditorProject? _project;
    private ScriptRuntime? _runtime;
    private long _lastTick;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowStartHint))]
    private bool _isPlaying;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowStartHint), nameof(ShowPausedHint))]
    private bool _isPaused;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SpaceLabel))]
    private ProjectSpace _space = ProjectSpace.ThreeD;

    [ObservableProperty]
    private RenderBackend _backend = RenderBackend.WebGPU;

    public RuntimeViewModel()
    {
        _timer.Tick += OnTick;
    }

    public Action<string, string?>? Logger { get; set; }

    public ScriptRuntime? World => _runtime;

    public IReadOnlyList<LiveObject> Objects => _runtime?.Objects ?? [];

    public ISceneRenderable? MainCamera => _runtime?.MainCamera;

    public string AssetsPath => _runtime?.AssetsPath ?? string.Empty;

    public string SpaceLabel => Space == ProjectSpace.TwoD ? "2D" : "3D";

    public bool ShowStartHint => !IsPlaying && !IsPaused;

    public bool ShowPausedHint => IsPaused;

    public event EventHandler? Frame;

    public void Load(EditorProject project)
    {
        if (_project is not null)
        {
            _project.SceneChanged -= OnSceneChanged;
            _project.PropertyChanged -= OnProjectPropertyChanged;
        }

        _project = project;
        project.SceneChanged += OnSceneChanged;
        project.PropertyChanged += OnProjectPropertyChanged;

        _timer.Stop();
        IsPlaying = false;
        IsPaused = false;
        Space = project.Space;
        Backend = project.Backend;
        _runtime = new ScriptRuntime(project, (message, color) => Logger?.Invoke(message, color));
        Frame?.Invoke(this, EventArgs.Empty);
    }

    private void OnSceneChanged(object? sender, EventArgs e) => RefreshObjects();

    private void OnProjectPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditorProject.MainCamera))
            RefreshObjects();
    }

    private void RefreshObjects()
    {
        if (_runtime is null || IsPlaying || IsPaused)
            return;

        _runtime.RebuildObjects();
        Frame?.Invoke(this, EventArgs.Empty);
    }

    public void Play()
    {
        if (_runtime is null || IsPlaying)
            return;

        if (!IsPaused)
            _runtime.Start();

        IsPaused = false;
        IsPlaying = true;
        _lastTick = Stopwatch.GetTimestamp();
        _timer.Start();
        Frame?.Invoke(this, EventArgs.Empty);
    }

    public void Pause()
    {
        if (!IsPlaying)
            return;

        _timer.Stop();
        IsPlaying = false;
        IsPaused = true;
        ReleaseInput();
        Frame?.Invoke(this, EventArgs.Empty);
    }

    public void Stop()
    {
        if (_runtime is null)
            return;

        _timer.Stop();
        IsPlaying = false;
        IsPaused = false;
        ReleaseInput();
        _runtime.Reset();
        Frame?.Invoke(this, EventArgs.Empty);
    }

    public bool SetKey(Key key, bool isDown)
    {
        if (!IsPlaying || _runtime is null)
            return false;

        if (InputKeys.FromKey(key) is not { } name)
            return false;

        _runtime.SetKey(name, isDown);
        return true;
    }

    public void SetMouse(double x, double y) => _runtime?.SetMouse(x, y);

    public void SetButton(string button, bool isDown) => _runtime?.SetButton(button, isDown);

    public void ReleaseInput()
    {
        if (_runtime is null)
            return;

        foreach (var button in new[] { "left", "right", "middle" })
            _runtime.SetButton(button, false);

        _runtime.Input.Clear();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_runtime is null)
            return;

        var now = Stopwatch.GetTimestamp();
        var delta = (now - _lastTick) / (double)Stopwatch.Frequency;
        _lastTick = now;

        _runtime.Update(Math.Min(delta, 0.05));
        Frame?.Invoke(this, EventArgs.Empty);
    }
}
