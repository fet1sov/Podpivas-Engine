using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.VisualTree;
using PodpivasEngine.Localization;
using PodpivasEngine.Models;
using PodpivasEngine.ViewModels;

namespace PodpivasEngine.Views;

public static class SceneMenu
{
    public static ContextMenu Create(
        Control owner,
        HierarchyViewModel hierarchy,
        SceneNode? target,
        IReadOnlyList<SceneNode> selection)
    {
        var menu = new ContextMenu();
        var add = new MenuItem { Header = Localizer.Get("Context.Add") };

        if (selection.Count > 1)
        {
            var group = new MenuItem { Header = Localizer.Get("Kind.Group") };
            group.Click += (_, _) => hierarchy.GroupNodes(selection);
            add.Items.Add(group);
            menu.Items.Add(add);
            return menu;
        }

        var parent = target is { Kind: SceneNodeKind.Group } ? target : null;

        foreach (var kind in hierarchy.AvailableKinds)
        {
            var item = new MenuItem { Header = Localizer.Get($"Kind.{kind}") };
            item.Click += (_, _) => hierarchy.AddNode(kind, parent);
            add.Items.Add(item);
        }

        menu.Items.Add(add);

        if (target is null)
            return menu;

        menu.Items.Add(new Separator());

        var rename = new MenuItem { Header = Localizer.Get("Context.Rename") };
        rename.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(owner) is not Window window)
                return;

            var name = await PromptDialog.ShowAsync(window, Localizer.Get("Context.Rename"), target.Name);
            if (!string.IsNullOrWhiteSpace(name))
                hierarchy.RenameNode(target, name);
        };

        menu.Items.Add(rename);

        var delete = new MenuItem { Header = Localizer.Get("Context.Delete") };
        delete.Click += (_, _) => hierarchy.DeleteNode(target);
        menu.Items.Add(delete);

        return menu;
    }
}
