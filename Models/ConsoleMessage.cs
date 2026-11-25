using System;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using PodpivasEngine.Localization;

namespace PodpivasEngine.Models;

public sealed class ConsoleMessage : ObservableObject
{
    private static readonly IBrush DefaultAccent = new SolidColorBrush(Color.Parse("#22C57C"));
    private readonly object?[] _arguments;
    private readonly string? _raw;

    public ConsoleMessage(string key, params object?[] arguments)
    {
        Key = key;
        _arguments = arguments;
        Time = DateTime.Now.ToString("HH:mm:ss");
        Accent = DefaultAccent;
    }

    private ConsoleMessage(string raw, IBrush accent)
    {
        Key = string.Empty;
        _arguments = [];
        _raw = raw;
        Time = DateTime.Now.ToString("HH:mm:ss");
        Accent = accent;
    }

    public static ConsoleMessage Raw(string text, string? color = null) =>
        new(text, color is not null && Color.TryParse(color, out var parsed)
            ? new SolidColorBrush(parsed)
            : DefaultAccent);

    public string Key { get; }

    public string Time { get; }

    public IBrush Accent { get; }

    public string Text => _raw ?? Localizer.Format(Key, _arguments);

    public void Relocalize()
    {
        if (_raw is null)
            OnPropertyChanged(nameof(Text));
    }
}
