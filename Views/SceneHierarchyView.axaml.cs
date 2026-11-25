using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using PodpivasEngine.Models;
using PodpivasEngine.ViewModels;

namespace PodpivasEngine.Views;

public partial class SceneHierarchyView : UserControl
{
    public SceneHierarchyView()
    {
        InitializeComponent();

        Tree.AddHandler(PointerReleasedEvent, OnTreePointerReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        Tree.AddHandler(PointerPressedEvent, OnTreePointerPressed, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private void OnTreePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not HierarchyViewModel hierarchy)
            return;

        if (!e.GetCurrentPoint(Tree).Properties.IsLeftButtonPressed)
            return;

        if (ResolveNode(e.Source as Visual) is not null)
            return;

        hierarchy.SelectedNode = null;
    }

    private void OnTreePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (DataContext is not HierarchyViewModel hierarchy)
            return;

        if (e.InitialPressMouseButton != MouseButton.Right)
            return;

        var node = ResolveNode(e.Source as Visual);
        var selection = Tree.SelectedItems.OfType<SceneNode>().ToList();

        if (node is null)
        {
            hierarchy.SelectedNode = null;
            selection.Clear();
        }
        else if (!selection.Contains(node))
        {
            hierarchy.SelectedNode = node;
            selection = [node];
        }

        SceneMenu.Create(this, hierarchy, node, selection).Open(this);
        e.Handled = true;
    }

    private static SceneNode? ResolveNode(Visual? source)
    {
        if (source is null)
            return null;

        if (source is TreeViewItem { DataContext: SceneNode own })
            return own;

        foreach (var ancestor in source.GetVisualAncestors())
        {
            if (ancestor is TreeViewItem { DataContext: SceneNode node })
                return node;
        }

        return null;
    }
}
