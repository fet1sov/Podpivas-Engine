using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PodpivasEngine.Localization;
using PodpivasEngine.Models;
using PodpivasEngine.Services;

namespace PodpivasEngine.ViewModels;

public enum EditorTool
{
    Select,
    Move,
    Rotate,
    Scale,
}

public sealed partial class MainViewModel : ViewModelBase
{
    public const int GameTabIndex = 1;

    public const int ScriptTabIndex = 2;

    public const int PreviewTabIndex = 3;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SpaceLabel))]
    private EditorProject _project = ProjectService.CreateInMemory(
        "untitled",
        RenderBackend.WebGPU,
        ProjectSpace.ThreeD);

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(IsSelectTool),
        nameof(IsMoveTool),
        nameof(IsRotateTool),
        nameof(IsScaleTool),
        nameof(ToolLabel))]
    private EditorTool _tool = EditorTool.Select;

    [ObservableProperty]
    private bool _isNewProjectOpen;

    [ObservableProperty]
    private bool _isLeftPanelVisible = true;

    [ObservableProperty]
    private bool _isRightPanelVisible = true;

    [ObservableProperty]
    private bool _isConsoleVisible = true;

    [ObservableProperty]
    private bool _isStatsVisible = true;

    [ObservableProperty]
    private bool _isAssetBrowserVisible = true;

    [ObservableProperty]
    private bool _isPreviewTabVisible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRussian), nameof(IsEnglish), nameof(ToolLabel))]
    private string _language = Localizer.Russian;

    private bool _panelsCustomized;
    private bool _applyingDefaults;
    private bool _closingPreview;
    private EditorProject? _observedProject;

    public HierarchyViewModel Hierarchy { get; } = new();

    public InspectorViewModel Inspector { get; } = new();

    public ConsoleViewModel Console { get; } = new();

    public StatsViewModel Stats { get; } = new();

    public NewProjectViewModel NewProject { get; } = new();

    public RuntimeViewModel Runtime { get; } = new();

    public AssetBrowserViewModel Assets { get; } = new();

    public ScriptEditorViewModel ScriptEditor { get; } = new();

    public PreviewViewModel Preview { get; } = new();

    public MainViewModel()
    {
        Hierarchy.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(HierarchyViewModel.SelectedNode))
                Inspector.Target = Hierarchy.SelectedNode;
        };

        Runtime.Logger = (message, color) => Console.LogRaw(message, color);
        Assets.Activated += OnAssetActivated;
        Assets.ApplyRequested += ApplyAsset;
        Assets.PreviewRequested += ShowPreview;
        Assets.AddToSceneRequested += entry => AddModelToScene(entry, 0, 0);
        Assets.Log = message => Console.LogRaw(message);
        Assets.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AssetBrowserViewModel.Selected)
                && Assets.Selected is { Kind: AssetKind.Script } script)
                ScriptEditor.TryOpen(script.FullPath);
        };
        ScriptEditor.Log = message => Console.LogRaw(message);
        ScriptEditor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ScriptEditorViewModel.IsModified))
                OnPropertyChanged(nameof(HasUnsavedChanges));
        };

        LoadProject(Project);
    }

    public Func<string, Task<string?>>? PickFolder { get; set; }

    public bool IsRussian => Language == Localizer.Russian;

    public bool IsEnglish => Language == Localizer.English;

    public bool IsSelectTool => Tool == EditorTool.Select;

    public bool IsMoveTool => Tool == EditorTool.Move;

    public bool IsRotateTool => Tool == EditorTool.Rotate;

    public bool IsScaleTool => Tool == EditorTool.Scale;

    public string ToolLabel => Localizer.Get($"Tool.{Tool}");

    public string SpaceLabel => Project.Space == ProjectSpace.TwoD ? "2D" : "3D";

    public bool HasUnsavedChanges => Project.IsDirty || ScriptEditor.IsModified;

    public string ProjectLocation =>
        string.IsNullOrEmpty(Project.RootPath) ? Localizer.Get("Project.Unsaved") : Project.RootPath;

    partial void OnIsLeftPanelVisibleChanged(bool value) => MarkPanelsCustomized();

    partial void OnIsRightPanelVisibleChanged(bool value) => MarkPanelsCustomized();

    partial void OnIsConsoleVisibleChanged(bool value) => MarkPanelsCustomized();

    partial void OnIsStatsVisibleChanged(bool value) => MarkPanelsCustomized();

    partial void OnIsAssetBrowserVisibleChanged(bool value) => MarkPanelsCustomized();

    partial void OnSelectedTabIndexChanged(int value)
    {
        if (value != PreviewTabIndex && IsPreviewTabVisible)
            ClosePreview();
    }

    partial void OnProjectChanged(EditorProject value)
    {
        if (_observedProject is not null)
            _observedProject.PropertyChanged -= OnProjectPropertyChanged;

        _observedProject = value;
        _observedProject.PropertyChanged += OnProjectPropertyChanged;

        OnPropertyChanged(nameof(SpaceLabel));
        OnPropertyChanged(nameof(ProjectLocation));
        OnPropertyChanged(nameof(HasUnsavedChanges));
    }

    private void OnProjectPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditorProject.IsDirty))
            OnPropertyChanged(nameof(HasUnsavedChanges));
    }

    private void MarkPanelsCustomized()
    {
        if (!_applyingDefaults)
            _panelsCustomized = true;
    }

    partial void OnLanguageChanged(string value)
    {
        Localizer.Apply(value);
        Console.Relocalize();
        Inspector.Relocalize();
        OnPropertyChanged(nameof(ToolLabel));
        OnPropertyChanged(nameof(ProjectLocation));
    }

    public void ApplyWindowSize(double width, double height)
    {
        if (_panelsCustomized)
            return;

        _applyingDefaults = true;
        IsLeftPanelVisible = width >= 960;
        IsRightPanelVisible = width >= 1240;
        IsConsoleVisible = height >= 560;
        IsAssetBrowserVisible = height >= 680;
        _applyingDefaults = false;
    }

    public void LoadProject(EditorProject project)
    {
        Project = project;
        Hierarchy.Load(project);
        Inspector.Load(project);
        Stats.Update(project);
        Console.Load(project);
        Runtime.Load(project);
        Assets.Load(project);
        ScriptEditor.Load(project);
        ClosePreview();
        project.TrackChanges();
        OnPropertyChanged(nameof(ProjectLocation));
        OnPropertyChanged(nameof(HasUnsavedChanges));
    }

    public bool OpenScript(string path)
    {
        if (!ScriptEditor.TryOpen(path))
            return false;

        SelectedTabIndex = ScriptTabIndex;
        return true;
    }

    public void ShowPreview(AssetEntry entry)
    {
        Preview.Show(entry);
        IsPreviewTabVisible = true;
        SelectedTabIndex = PreviewTabIndex;
    }

    private void ClosePreview()
    {
        if (_closingPreview)
            return;

        _closingPreview = true;
        IsPreviewTabVisible = false;
        Preview.Clear();

        if (SelectedTabIndex == PreviewTabIndex)
            SelectedTabIndex = 0;

        _closingPreview = false;
    }

    private void OnAssetActivated(AssetEntry entry)
    {
        switch (entry.Kind)
        {
            case AssetKind.Script:
                OpenScript(entry.FullPath);
                break;
            case AssetKind.Image or AssetKind.Model3D:
                ShowPreview(entry);
                break;
            default:
                Console.Log("Console.AssetUnsupported", entry.Name);
                break;
        }
    }

    public void AddModelToScene(AssetEntry entry, double x, double z)
    {
        if (string.IsNullOrEmpty(Project.RootPath))
        {
            Console.Log("Assets.NoProject");
            return;
        }

        if (Project.IsTwoD || !entry.IsModel)
        {
            Console.Log("Console.AssetUnsupported", entry.Name);
            return;
        }

        var asset = Path.GetRelativePath(Project.AssetsPath, entry.FullPath).Replace('\\', '/');
        if (Hierarchy.AddEntity(asset, x, z) is not { } node)
            return;

        Console.Log("Console.ModelAdded", entry.Name, node.Name);
    }

    private void ApplyAsset(AssetEntry entry)
    {
        if (Inspector.Target is not { } target)
        {
            Console.Log("Console.SelectObject");
            return;
        }

        switch (entry.Kind)
        {
            case AssetKind.Script:
                var script = Path.GetFileName(entry.FullPath);
                if (target.Components.All(component => !string.Equals(component.Script, script, StringComparison.OrdinalIgnoreCase)))
                    target.Components.Add(new ScriptComponent(script));
                break;
            case AssetKind.Image or AssetKind.Model3D:
                target.Asset = Path.GetRelativePath(Project.AssetsPath, entry.FullPath).Replace('\\', '/');
                break;
            default:
                Console.Log("Console.AssetUnsupported", entry.Name);
                return;
        }

        Console.Log("Console.AssetApplied", entry.Name, target.Name);
    }

    [RelayCommand]
    private void SetLanguage(string? language)
    {
        if (language is Localizer.Russian or Localizer.English)
            Language = language;
    }

    [RelayCommand]
    private void SetTool(string? tool)
    {
        if (Enum.TryParse<EditorTool>(tool, out var parsed))
            Tool = parsed;
    }

    [RelayCommand]
    private void OpenNewProject()
    {
        NewProject.Reset(Project);
        IsNewProjectOpen = true;
    }

    [RelayCommand]
    private void CancelNewProject() => IsNewProjectOpen = false;

    [RelayCommand]
    private void CreateProject()
    {
        var name = NewProject.Name.Trim();
        if (string.IsNullOrEmpty(name))
            name = Localizer.Get("NewProject.DefaultName");

        try
        {
            var project = ProjectService.Create(name, NewProject.Backend, NewProject.Space);
            LoadProject(project);
            Console.Log("Console.ProjectCreated", project.Name, project.Backend, SpaceLabel);
            Console.Log("Console.ProjectLocation", project.RootPath);
        }
        catch (Exception exception)
        {
            Console.LogRaw($"create project: {exception.Message}");
        }

        IsNewProjectOpen = false;
    }

    [RelayCommand]
    private async Task OpenProject()
    {
        if (PickFolder is null)
            return;

        var directory = await PickFolder(Localizer.Get("Dialog.PickFolder"));
        if (string.IsNullOrWhiteSpace(directory))
            return;

        try
        {
            var project = ProjectService.Open(directory);
            LoadProject(project);
            Console.Log("Console.ProjectOpened", project.Name);
            Console.Log("Console.ProjectLocation", project.RootPath);
        }
        catch (Exception exception)
        {
            Console.LogRaw($"open project: {exception.Message}");
        }
    }

    [RelayCommand]
    private async Task SaveProject()
    {
        if (string.IsNullOrEmpty(Project.RootPath))
        {
            await SaveProjectAs();
            return;
        }

        try
        {
            if (ScriptEditor.IsModified)
                ScriptEditor.Save();

            ProjectService.Save(Project);
            Inspector.Load(Project);
            Assets.Reload();
            Project.IsDirty = false;
            Console.Log("Console.ProjectSaved", Project.RootPath);
        }
        catch (Exception exception)
        {
            Console.LogRaw($"save project: {exception.Message}");
        }
    }

    [RelayCommand]
    private async Task SaveProjectAs()
    {
        if (PickFolder is null)
            return;

        var directory = await PickFolder(Localizer.Get("Dialog.PickFolder"));
        if (string.IsNullOrWhiteSpace(directory))
            return;

        try
        {
            if (ScriptEditor.IsModified)
                ScriptEditor.Save();

            var saved = ProjectService.SaveAs(Project, directory);
            LoadProject(saved);
            Console.Log("Console.ProjectSaved", saved.RootPath);
        }
        catch (Exception exception)
        {
            Console.LogRaw($"save project: {exception.Message}");
        }
    }

    [RelayCommand]
    private async Task ExportProject()
    {
        if (PickFolder is null)
            return;

        if (string.IsNullOrEmpty(Project.RootPath))
        {
            await SaveProjectAs();
            if (string.IsNullOrEmpty(Project.RootPath))
                return;
        }

        var directory = await PickFolder(Localizer.Get("Dialog.PickExportFolder"));
        if (string.IsNullOrWhiteSpace(directory))
            return;

        try
        {
            var path = ExportService.Export(Project, directory);
            Console.Log("Console.ProjectExported", path);
        }
        catch (Exception exception)
        {
            Console.LogRaw($"export: {exception.Message}");
        }
    }

    [RelayCommand]
    private void Play()
    {
        Runtime.Play();
        SelectedTabIndex = GameTabIndex;
        Console.Log("Console.Play");
    }

    [RelayCommand]
    private void Pause()
    {
        Runtime.Pause();
        Console.Log("Console.Pause");
    }

    [RelayCommand]
    private void Stop()
    {
        Runtime.Stop();
        Console.Log("Console.Stop");
    }
}
