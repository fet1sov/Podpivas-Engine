using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using PodpivasEngine.ViewModels;

namespace PodpivasEngine.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        SizeChanged += (_, e) =>
        {
            if (DataContext is MainViewModel viewModel)
                viewModel.ApplyWindowSize(e.NewSize.Width, e.NewSize.Height);
        };

        DataContextChanged += (_, _) =>
        {
            WireFolderPicker();
            ObservePanelVisibility();
        };
        WireFolderPicker();

        InitializeDocking();

        DragDrop.SetAllowDrop(WorkspaceArea, true);
        WorkspaceArea.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        WorkspaceArea.AddHandler(DragDrop.DropEvent, OnDrop);

        KeyDown += OnKeyDown;
        KeyUp += OnKeyUp;
    }

    private void WireFolderPicker()
    {
        if (DataContext is MainViewModel viewModel)
            viewModel.PickFolder = PickFolderAsync;
    }

    private async Task<string?> PickFolderAsync(string title)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });

        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    private static IEnumerable<string> DroppedPaths(DragEventArgs e)
    {
        foreach (var item in e.DataTransfer.TryGetFiles() ?? [])
        {
            if (item.TryGetLocalPath() is { } path)
                yield return path;
        }

        if (e.DataTransfer.TryGetText() is { Length: > 0 } text)
            yield return text.Trim().Trim('"');
    }

    private static bool IsLua(string path) =>
        path.EndsWith(".lua", StringComparison.OrdinalIgnoreCase);

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = DroppedPaths(e).Any(IsLua) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            foreach (var path in DroppedPaths(e).Where(IsLua))
            {
                if (viewModel.OpenScript(path))
                    break;
            }
        }

        e.Handled = true;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || viewModel.IsNewProjectOpen || IsTextInput(e.Source))
            return;

        if (viewModel.Runtime.SetKey(e.Key, true))
            e.Handled = true;
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || IsTextInput(e.Source))
            return;

        if (viewModel.Runtime.SetKey(e.Key, false))
            e.Handled = true;
    }

    private static bool IsTextInput(object? source) =>
        source is TextBox or AvaloniaEdit.TextEditor or AvaloniaEdit.Editing.TextArea
        || (source is Visual visual && visual.FindAncestorOfType<AvaloniaEdit.TextEditor>() is not null);
}
