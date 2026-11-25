using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using PodpivasEngine.Localization;
using PodpivasEngine.Models;
using PodpivasEngine.ViewModels;

namespace PodpivasEngine.Views;

public partial class AssetBrowserView : UserControl
{
    private static readonly FilePickerFileType ModelFiles = new("3D")
    {
        Patterns = ["*.obj", "*.fbx", "*.gltf", "*.glb", "*.dae", "*.3ds", "*.stl", "*.blend"],
    };

    private static readonly FilePickerFileType ImageFiles = new("Images")
    {
        Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp"],
    };

    private Point _dragStart;
    private PointerPressedEventArgs? _dragTrigger;
    private AssetEntry? _dragEntry;

    public AssetBrowserView()
    {
        InitializeComponent();

        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        PointerMoved += OnPointerMoved;

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private AssetBrowserViewModel? ViewModel => DataContext as AssetBrowserViewModel;

    private static IEnumerable<string> DroppedFiles(DragEventArgs e)
    {
        foreach (var item in e.DataTransfer.TryGetFiles() ?? [])
        {
            if (item.TryGetLocalPath() is { } path)
                yield return path;
        }

        if (e.DataTransfer.TryGetText() is { Length: > 0 } text)
            yield return text.Trim().Trim('"');
    }

    private bool CanAccept(DragEventArgs e) =>
        ViewModel?.CanCreate == true && DroppedFiles(e).Any(File.Exists);

    private void SetDropHighlight(bool active) =>
        PanelRoot.Classes.Set("dropTarget", active);

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var accepted = CanAccept(e);
        SetDropHighlight(accepted);
        e.DragEffects = accepted ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDragLeave(object? sender, DragEventArgs e) => SetDropHighlight(false);

    private void OnDrop(object? sender, DragEventArgs e)
    {
        SetDropHighlight(false);
        e.Handled = true;

        ViewModel?.ImportFiles(DroppedFiles(e));
    }

    private void OnItemActivated(object? sender, TappedEventArgs e)
    {
        if (DataContext is not AssetBrowserViewModel viewModel || ResolveEntry(e.Source as Visual) is not { } entry)
            return;

        viewModel.Activate(entry);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _dragEntry = (e.Source as Control)?.DataContext is AssetEntry { Kind: AssetKind.Script or AssetKind.Model3D } entry
            ? entry
            : null;
        _dragTrigger = _dragEntry is null ? null : e;
        _dragStart = e.GetPosition(this);
    }

    private async void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragEntry is null || _dragTrigger is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        var delta = e.GetPosition(this) - _dragStart;
        if (Math.Abs(delta.X) < 6 && Math.Abs(delta.Y) < 6)
            return;

        var entry = _dragEntry;
        var trigger = _dragTrigger;
        _dragEntry = null;
        _dragTrigger = null;

        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.CreateText(entry.FullPath));
        await DragDrop.DoDragDropAsync(trigger, transfer, DragDropEffects.Copy);
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Right || DataContext is not AssetBrowserViewModel viewModel)
            return;

        var entry = ResolveEntry(e.Source as Visual);
        if (entry is not null)
            viewModel.Selected = entry;

        BuildMenu(viewModel, entry).Open(this);
        e.Handled = true;
    }

    private static AssetEntry? ResolveEntry(Visual? source)
    {
        for (var visual = source; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is Control { DataContext: AssetEntry entry })
                return entry;
        }

        return null;
    }

    private ContextMenu BuildMenu(AssetBrowserViewModel viewModel, AssetEntry? entry)
    {
        var menu = new ContextMenu();
        var create = new MenuItem { Header = Localizer.Get("Assets.Create") };

        var model = new MenuItem
        {
            Header = Localizer.Get("Assets.Create.Model"),
            IsEnabled = viewModel.CanCreateModels,
        };
        ToolTip.SetTip(model, Localizer.Get(viewModel.Project is { IsTwoD: true } ? "Assets.ModelIn2D" : "Assets.ImportModel"));
        model.Click += async (_, _) => await ImportAsync(viewModel, ModelFiles, "Assets.ImportModel");
        create.Items.Add(model);

        var texture = new MenuItem { Header = Localizer.Get("Assets.Create.Texture"), IsEnabled = viewModel.CanCreate };
        texture.Click += async (_, _) => await ImportAsync(viewModel, ImageFiles, "Assets.ImportTexture");
        create.Items.Add(texture);

        var sprite = new MenuItem { Header = Localizer.Get("Assets.Create.Sprite"), IsEnabled = viewModel.CanCreate };
        sprite.Click += async (_, _) => await ImportAsync(viewModel, ImageFiles, "Assets.ImportSprite");
        create.Items.Add(sprite);

        var script = new MenuItem { Header = Localizer.Get("Assets.Create.Script"), IsEnabled = viewModel.CanCreate };
        script.Click += async (_, _) => await CreateScriptAsync(viewModel);
        create.Items.Add(script);

        menu.Items.Add(create);

        if (entry is null)
            return menu;

        menu.Items.Add(new Separator());

        if (entry.Kind == AssetKind.Model3D && viewModel.CanCreateModels)
        {
            var toScene = new MenuItem { Header = Localizer.Get("Assets.AddToScene") };
            toScene.Click += (_, _) => viewModel.RequestAddToScene(entry);
            menu.Items.Add(toScene);
        }

        var apply = new MenuItem
        {
            Header = Localizer.Get("Assets.Apply"),
            IsEnabled = entry.Kind is AssetKind.Image or AssetKind.Model3D or AssetKind.Script,
        };
        apply.Click += (_, _) => viewModel.RequestApply(entry);
        menu.Items.Add(apply);

        return menu;
    }

    private async Task ImportAsync(AssetBrowserViewModel viewModel, FilePickerFileType type, string titleKey)
    {
        if (!viewModel.CanCreate)
        {
            viewModel.Report(Localizer.Get("Assets.NoProject"));
            return;
        }

        if (TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Localizer.Get(titleKey),
            AllowMultiple = true,
            FileTypeFilter = [type],
        });

        var paths = files.Select(file => file.TryGetLocalPath()).OfType<string>().ToList();
        if (paths.Count > 0)
            viewModel.ImportFiles(paths);
    }

    private async Task CreateScriptAsync(AssetBrowserViewModel viewModel)
    {
        if (!viewModel.CanCreate)
        {
            viewModel.Report(Localizer.Get("Assets.NoProject"));
            return;
        }

        if (TopLevel.GetTopLevel(this) is not Window window)
            return;

        var name = await PromptDialog.ShowAsync(window, Localizer.Get("Assets.NewScriptName"), "script.lua");
        if (string.IsNullOrWhiteSpace(name))
            return;

        if (viewModel.CreateScript(name) is { } created)
            viewModel.Activate(created);
    }
}
