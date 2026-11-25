using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using PodpivasEngine.Models;

namespace PodpivasEngine.ViewModels;

public sealed partial class HierarchyViewModel : ViewModelBase
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Roots))]
    private EditorProject? _project;

    [ObservableProperty]
    private SceneNode? _selectedNode;

    private static readonly ObservableCollection<SceneNode> Empty = [];

    public ObservableCollection<SceneNode> Roots => Project?.Nodes ?? Empty;

    public ProjectSpace Space => Project?.Space ?? ProjectSpace.ThreeD;

    public void Load(EditorProject project)
    {
        Project = project;
        SelectedNode = project.Nodes.FirstOrDefault();
    }

    public void AddNode(SceneNodeKind kind, SceneNode? parent)
    {
        if (Project is null)
            return;

        var node = new SceneNode(NextName(kind), kind);

        if (parent is { Kind: SceneNodeKind.Group })
            parent.Children.Add(node);
        else
            Project.Nodes.Add(node);

        SelectedNode = node;
    }

    public SceneNode? AddEntity(string asset, double x = 0, double z = 0)
    {
        if (Project is null)
            return null;

        var node = new SceneNode(NextName(Path.GetFileNameWithoutExtension(asset)), SceneNodeKind.Entity)
        {
            Asset = asset,
            X = x,
            Z = z,
        };

        Project.Nodes.Add(node);
        SelectedNode = node;
        return node;
    }

    public void DeleteNode(SceneNode node)
    {
        if (Project is null)
            return;

        RemoveNode(Project.Nodes, node);

        if (ReferenceEquals(SelectedNode, node))
            SelectedNode = Project.Nodes.FirstOrDefault();
    }

    public void RenameNode(SceneNode node, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;

        var previous = node.Name;
        var next = name.Trim();

        if (Project is not null && string.Equals(Project.MainCamera, previous, StringComparison.OrdinalIgnoreCase))
            Project.MainCamera = next;

        node.Name = next;
    }

    private bool RemoveNode(ObservableCollection<SceneNode> nodes, SceneNode target)
    {
        if (nodes.Remove(target))
            return true;

        foreach (var node in nodes)
        {
            if (RemoveNode(node.Children, target))
                return true;
        }

        return false;
    }

    public IReadOnlyList<SceneNodeKind> AvailableKinds =>
        Project?.IsTwoD == true
            ? [SceneNodeKind.Entity, SceneNodeKind.Sprite, SceneNodeKind.Tileset, SceneNodeKind.Camera, SceneNodeKind.Light, SceneNodeKind.Audio]
            : [SceneNodeKind.Entity, SceneNodeKind.Sprite, SceneNodeKind.Model, SceneNodeKind.Tileset, SceneNodeKind.Camera, SceneNodeKind.Light, SceneNodeKind.Audio];

    public void GroupNodes(IEnumerable<SceneNode> nodes)
    {
        if (Project is null)
            return;

        var selected = new HashSet<SceneNode>(nodes);
        var ordered = new List<SceneNode>();

        void Collect(IEnumerable<SceneNode> branch, bool insideSelected)
        {
            foreach (var node in branch)
            {
                var picked = selected.Contains(node);

                if (picked && !insideSelected)
                    ordered.Add(node);

                Collect(node.Children, insideSelected || picked);
            }
        }

        Collect(Project.Nodes, false);

        if (ordered.Count < 2 || !TryFindOwner(Project.Nodes, ordered[0], out var owner, out var index))
            return;

        var group = new SceneNode(NextName(SceneNodeKind.Group), SceneNodeKind.Group);
        owner.Insert(index, group);

        foreach (var node in ordered)
        {
            if (TryFindOwner(Project.Nodes, node, out var source, out _))
            {
                source.Remove(node);
                group.Children.Add(node);
            }
        }

        SelectedNode = group;
    }

    private static bool TryFindOwner(
        ObservableCollection<SceneNode> nodes,
        SceneNode target,
        out ObservableCollection<SceneNode> owner,
        out int index)
    {
        index = nodes.IndexOf(target);

        if (index >= 0)
        {
            owner = nodes;
            return true;
        }

        foreach (var node in nodes)
        {
            if (TryFindOwner(node.Children, target, out owner, out index))
                return true;
        }

        owner = nodes;
        index = -1;
        return false;
    }

    private string NextName(SceneNodeKind kind) => NextName(kind.ToString().ToLowerInvariant());

    private string NextName(string baseName)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var node in Enumerate(Project!.Nodes))
            names.Add(node.Name);

        if (!names.Contains(baseName))
            return baseName;

        for (var index = 2; ; index++)
        {
            var candidate = $"{baseName} {index}";
            if (!names.Contains(candidate))
                return candidate;
        }
    }

    private static IEnumerable<SceneNode> Enumerate(IEnumerable<SceneNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;

            foreach (var child in Enumerate(node.Children))
                yield return child;
        }
    }
}
