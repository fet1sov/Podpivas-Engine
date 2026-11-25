using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using PodpivasEngine.Services;
using PodpivasEngine.ViewModels;

namespace PodpivasEngine.Views;

public enum DockSlot
{
    Left,
    Right,
    Bottom,
}

public partial class MainWindow
{
    private const double SplitterSize = 4;
    private const double MinSlotSize = 120;
    private const double MaxSlotSize = 900;
    private const double DragThreshold = 4;

    private readonly List<Control> _order = [];
    private readonly Dictionary<Control, DockSlot> _slots = [];
    private readonly Dictionary<Control, string> _names = [];
    private readonly Dictionary<string, Control> _panels = [];

    private Control? _dragPanel;
    private Point _dragStart;
    private bool _isDragging;
    private MainViewModel? _panelSource;

    private double _leftWidth = 250;
    private double _rightWidth = 300;
    private double _bottomHeight = 170;

    private void InitializeDocking()
    {
        RegisterPanel(HierarchyPanel, "Hierarchy", DockSlot.Left);
        RegisterPanel(AssetsPanel, "Assets", DockSlot.Left);
        RegisterPanel(InspectorPanel, "Inspector", DockSlot.Right);
        RegisterPanel(ConsolePanel, "Console", DockSlot.Bottom);

        RestoreLayout();

        PointerMoved += OnDockPointerMoved;
        PointerReleased += OnDockPointerReleased;
        Closing += (_, _) => SaveLayout();
    }

    private void RegisterPanel(Control panel, string name, DockSlot slot)
    {
        _order.Add(panel);
        _names[panel] = name;
        _panels[name] = panel;
        _slots[panel] = slot;

        panel.AddHandler(PointerPressedEvent, OnPanelPointerPressed, RoutingStrategies.Tunnel);
    }

    private void ObservePanelVisibility()
    {
        if (_panelSource is not null)
            _panelSource.PropertyChanged -= OnPanelVisibilityChanged;

        _panelSource = DataContext as MainViewModel;

        if (_panelSource is null)
            return;

        _panelSource.PropertyChanged += OnPanelVisibilityChanged;
        BuildDockLayout();
    }

    private void OnPanelVisibilityChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.IsLeftPanelVisible)
            or nameof(MainViewModel.IsRightPanelVisible)
            or nameof(MainViewModel.IsConsoleVisible)
            or nameof(MainViewModel.IsAssetBrowserVisible))
            BuildDockLayout();
    }

    private void SyncPanelVisibility()
    {
        if (DataContext is not MainViewModel viewModel)
            return;

        HierarchyPanel.IsVisible = viewModel.IsLeftPanelVisible;
        AssetsPanel.IsVisible = viewModel.IsAssetBrowserVisible;
        InspectorPanel.IsVisible = viewModel.IsRightPanelVisible;
        ConsolePanel.IsVisible = viewModel.IsConsoleVisible;
    }

    private void RestoreLayout()
    {
        var state = LayoutStore.Load();
        _leftWidth = ClampSize(state.LeftWidth, 250);
        _rightWidth = ClampSize(state.RightWidth, 300);
        _bottomHeight = ClampSize(state.BottomHeight, 170);

        var ordered = new List<Control>();
        foreach (var placement in state.Panels)
        {
            if (!_panels.TryGetValue(placement.Panel, out var panel)
                || !Enum.TryParse<DockSlot>(placement.Slot, out var slot)
                || ordered.Contains(panel))
                continue;

            _slots[panel] = slot;
            ordered.Add(panel);
        }

        foreach (var panel in _order.Where(panel => !ordered.Contains(panel)))
            ordered.Add(panel);

        _order.Clear();
        _order.AddRange(ordered);

        BuildDockLayout();
    }

    private void SaveLayout()
    {
        CaptureSlotSizes();

        LayoutStore.Save(new DockLayoutState
        {
            LeftWidth = _leftWidth,
            RightWidth = _rightWidth,
            BottomHeight = _bottomHeight,
            Panels = _order.Select(panel => new PanelPlacement
            {
                Panel = _names[panel],
                Slot = _slots[panel].ToString(),
            }).ToList(),
        });
    }

    private static double ClampSize(double value, double fallback) =>
        !double.IsFinite(value) || value < MinSlotSize ? fallback : Math.Min(value, MaxSlotSize);

    private void CaptureSlotSizes()
    {
        var left = DockRoot.ColumnDefinitions[0].Width;
        if (left.IsAbsolute && left.Value >= MinSlotSize)
            _leftWidth = left.Value;

        var right = DockRoot.ColumnDefinitions[4].Width;
        if (right.IsAbsolute && right.Value >= MinSlotSize)
            _rightWidth = right.Value;

        var bottom = DockRoot.RowDefinitions[2].Height;
        if (bottom.IsAbsolute && bottom.Value >= MinSlotSize)
            _bottomHeight = bottom.Value;
    }

    private void BuildDockLayout()
    {
        CaptureSlotSizes();
        SyncPanelVisibility();

        LeftDock.Children.Clear();
        RightDock.Children.Clear();
        BottomDock.Children.Clear();
        LeftDock.RowDefinitions.Clear();
        RightDock.RowDefinitions.Clear();
        BottomDock.RowDefinitions.Clear();

        FillDock(LeftDock, DockSlot.Left);
        FillDock(RightDock, DockSlot.Right);
        FillDock(BottomDock, DockSlot.Bottom);

        DockRoot.ColumnDefinitions[0].Width = new GridLength(LeftDock.IsVisible ? _leftWidth : 0);
        DockRoot.ColumnDefinitions[1].Width = new GridLength(LeftDock.IsVisible ? SplitterSize : 0);
        DockRoot.ColumnDefinitions[3].Width = new GridLength(RightDock.IsVisible ? SplitterSize : 0);
        DockRoot.ColumnDefinitions[4].Width = new GridLength(RightDock.IsVisible ? _rightWidth : 0);
        DockRoot.RowDefinitions[1].Height = new GridLength(BottomDock.IsVisible ? SplitterSize : 0);
        DockRoot.RowDefinitions[2].Height = new GridLength(BottomDock.IsVisible ? _bottomHeight : 0);

        LeftSplitter.IsVisible = LeftDock.IsVisible;
        RightSplitter.IsVisible = RightDock.IsVisible;
        BottomSplitter.IsVisible = BottomDock.IsVisible;
    }

    private void FillDock(Grid dock, DockSlot slot)
    {
        var panels = _order.Where(panel => _slots[panel] == slot && panel.IsVisible).ToList();

        for (var index = 0; index < panels.Count; index++)
        {
            if (index > 0)
            {
                dock.RowDefinitions.Add(new RowDefinition(new GridLength(SplitterSize)));
                var splitter = new GridSplitter
                {
                    ResizeDirection = GridResizeDirection.Rows,
                    ResizeBehavior = GridResizeBehavior.PreviousAndNext,
                    Background = SplitterBrush,
                };
                Grid.SetRow(splitter, index * 2 - 1);
                dock.Children.Add(splitter);
            }

            dock.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            Grid.SetRow(panels[index], index *  2);
            dock.Children.Add(panels[index]);
        }

        dock.IsVisible = panels.Count > 0;
    }

    private IBrush? SplitterBrush =>
        this.TryFindResource("Editor.Border", out var value) ? value as IBrush : null;

    private void MovePanel(Control panel, DockSlot slot)
    {
        if (_slots[panel] == slot)
            return;

        _slots[panel] = slot;
        BuildDockLayout();
        SaveLayout();
    }

    private void OnPanelPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_isDragging || sender is not Control panel)
            return;

        if (!e.GetCurrentPoint(panel).Properties.IsLeftButtonPressed)
            return;

        if (e.Source is not Visual source
            || source.FindAncestorOfType<Button>() is not null
            || !IsHeader(source, panel))
            return;

        _dragPanel = panel;
        _dragStart = e.GetPosition(this);
        e.Pointer.Capture(panel);
    }

    private static bool IsHeader(Visual source, Control panel)
    {
        for (var visual = source as Visual; visual is not null && visual != panel; visual = visual.GetVisualParent())
        {
            if (visual is Border border && border.Classes.Contains("panelHeader"))
                return true;
        }

        return false;
    }

    private void OnDockPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragPanel is null)
            return;

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            CancelDrag();
            return;
        }

        if (!_isDragging)
        {
            var offset = e.GetPosition(this) - _dragStart;
            if (Math.Abs(offset.X) < DragThreshold && Math.Abs(offset.Y) < DragThreshold)
                return;

            _isDragging = true;
            ShowDropZones();
        }

        HighlightZone(ZoneAt(e.GetPosition(DockRoot)));
    }

    private void OnDockPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragPanel is null)
            return;

        if (_isDragging)
        {
            if (ZoneAt(e.GetPosition(DockRoot)) is { } slot)
                MovePanel(_dragPanel, slot);
        }

        CancelDrag();
        e.Pointer.Capture(null);
    }

    private void CancelDrag()
    {
        DropOverlay.IsVisible = false;
        _dragPanel = null;
        _isDragging = false;
    }

    private DockSlot? ZoneAt(Point position)
    {
        var size = DockRoot.Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0)
            return null;

        if (position.Y > size.Height * 0.78)
            return DockSlot.Bottom;

        if (position.X < size.Width * 0.22)
            return DockSlot.Left;

        return position.X > size.Width * 0.78 ? DockSlot.Right : null;
    }

    private void ShowDropZones()
    {
        var width = DockRoot.Bounds.Width;
        var height = DockRoot.Bounds.Height;
        var side = Math.Max(80, width * 0.22);
        var bottom = Math.Max(60, height * 0.22);

        PlaceZone(DropLeftZone, 0, 0, side, height);
        PlaceZone(DropRightZone, width - side, 0, side, height);
        PlaceZone(DropBottomZone, 0, height - bottom, width, bottom);

        DropOverlay.IsVisible = true;
        HighlightZone(null);
    }

    private void HighlightZone(DockSlot? slot)
    {
        DropLeftZone.Opacity = slot == DockSlot.Left ? 0.8 : 0.3;
        DropRightZone.Opacity = slot == DockSlot.Right ? 0.8 : 0.3;
        DropBottomZone.Opacity = slot == DockSlot.Bottom ? 0.8 : 0.3;
    }

    private static void PlaceZone(Control zone, double x, double y, double width, double height)
    {
        Canvas.SetLeft(zone, x);
        Canvas.SetTop(zone, y);
        zone.Width = width;
        zone.Height = height;
    }
}
