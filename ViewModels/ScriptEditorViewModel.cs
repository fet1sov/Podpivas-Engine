using System;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PodpivasEngine.Localization;
using PodpivasEngine.Models;
using PodpivasEngine.Scripting;

namespace PodpivasEngine.ViewModels;

public sealed partial class ScriptEditorViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private string _text = string.Empty;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private bool _isModified;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private int _errorLine;

    public EditorProject? Project { get; private set; }

    public Action<string>? Log { get; set; }

    public void Load(EditorProject project)
    {
        Project = project;
        Close();
    }

    public void Close()
    {
        IsOpen = false;
        FilePath = string.Empty;
        FileName = string.Empty;
        Text = string.Empty;
        IsModified = false;
        Status = string.Empty;
        HasError = false;
        ErrorLine = 0;
    }

    public bool TryOpen(string path)
    {
        if (Project is null)
            return Reject(Localizer.Get("Script.NoProject"));

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception exception)
        {
            return Reject(exception.Message);
        }

        if (!string.Equals(Path.GetExtension(fullPath), ".lua", StringComparison.OrdinalIgnoreCase))
            return Reject(Localizer.Format("Script.Unsupported", Path.GetFileName(fullPath)));

        if (!File.Exists(fullPath))
            return Reject(Localizer.Format("Script.NotFound", Path.GetFileName(fullPath)));

        if (!IsInsideProject(fullPath))
            return Reject(Localizer.Format("Script.OutsideProject", Path.GetFileName(fullPath)));

        if (IsOpen && IsModified && !Save())
            return false;

        try
        {
            Text = File.ReadAllText(fullPath);
        }
        catch (Exception exception)
        {
            return Reject(exception.Message);
        }

        FilePath = fullPath;
        FileName = Path.GetFileName(fullPath);
        IsOpen = true;
        IsModified = false;
        Status = string.Empty;
        HasError = false;
        Validate();
        Log?.Invoke(Localizer.Format("Script.Opened", FileName));
        return true;
    }

    public bool Save()
    {
        if (!IsOpen)
            return false;

        try
        {
            File.WriteAllText(FilePath, Text, new UTF8Encoding(false));
        }
        catch (Exception exception)
        {
            Status = exception.Message;
            HasError = true;
            Log?.Invoke(Localizer.Format("Script.SaveFailed", exception.Message));
            return false;
        }

        IsModified = false;
        Validate();
        Log?.Invoke(HasError
            ? Localizer.Format("Script.SavedWithError", FileName, Status)
            : Localizer.Format("Script.Saved", FileName));
        return true;
    }

    public void Validate()
    {
        try
        {
            _ = new LuaParser(Text).ParseChunk();
            Status = string.Empty;
            HasError = false;
            ErrorLine = 0;
        }
        catch (LuaSyntaxException exception)
        {
            Status = exception.Message;
            HasError = true;
            ErrorLine = exception.Line;
        }
    }

    private bool IsInsideProject(string path)
    {
        var root = Project?.RootPath;
        if (string.IsNullOrEmpty(root))
            return false;

        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private bool Reject(string message)
    {
        Log?.Invoke(message);
        return false;
    }

    [RelayCommand]
    private void SaveScript() => Save();
}
