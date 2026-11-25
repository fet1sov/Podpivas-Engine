using System;
using System.IO;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using PodpivasEngine.Localization;
using PodpivasEngine.Models;
using PodpivasEngine.Services;

namespace PodpivasEngine.ViewModels;

public sealed partial class PreviewViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private string _kindLabel = string.Empty;

    [ObservableProperty]
    private string _details = string.Empty;

    [ObservableProperty]
    private Bitmap? _image;

    [ObservableProperty]
    private string? _modelPath;

    [ObservableProperty]
    private bool _hasImage;

    [ObservableProperty]
    private bool _hasModel;

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private bool _hasMessage;

    public bool IsEmpty => !HasImage && !HasModel && !HasMessage;

    public void Show(AssetEntry entry)
    {
        Clear();

        FileName = entry.Name;

        if (entry.Kind == AssetKind.Model3D)
        {
            KindLabel = Localizer.Get("Kind.Model");

            if (MeshLibrary.TryLoad(entry.FullPath, out _))
            {
                ModelPath = entry.FullPath;
                HasModel = true;
                Details = $"{entry.Extension.TrimStart('.').ToUpperInvariant()} · {FileSize(entry.FullPath)}";
            }
            else
            {
                Message = Localizer.Format("Preview.ModelUnsupported", entry.Extension);
                HasMessage = true;
            }

            OnPropertyChanged(nameof(IsEmpty));
            return;
        }

        KindLabel = Localizer.Get("Preview.Kind.Image");

        try
        {
            Image = new Bitmap(entry.FullPath);
            HasImage = true;
            Details = Localizer.Format("Preview.Dimensions", Image.PixelSize.Width, Image.PixelSize.Height);
        }
        catch (Exception)
        {
            Message = Localizer.Format("Preview.Unsupported", entry.Extension);
            HasMessage = true;
        }

        OnPropertyChanged(nameof(IsEmpty));
    }

    public void Clear()
    {
        FileName = string.Empty;
        KindLabel = string.Empty;
        Details = string.Empty;
        Image = null;
        HasImage = false;
        ModelPath = null;
        HasModel = false;
        Message = string.Empty;
        HasMessage = false;
        OnPropertyChanged(nameof(IsEmpty));
    }

    private static string FileSize(string path)
    {
        try
        {
            var bytes = new FileInfo(path).Length;
            return bytes >= 1024 * 1024
                ? $"{bytes / (1024.0 * 1024.0):0.##} MB"
                : $"{Math.Max(1, bytes / 1024)} KB";
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }
}
