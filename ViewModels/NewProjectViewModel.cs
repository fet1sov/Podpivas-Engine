using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PodpivasEngine.Models;
using PodpivasEngine.Services;

namespace PodpivasEngine.ViewModels;

public sealed partial class NewProjectViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private RenderBackend _backend = RenderBackend.WebGPU;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTwoD), nameof(IsThreeD), nameof(TargetFolder))]
    private ProjectSpace _space = ProjectSpace.ThreeD;

    public IReadOnlyList<RenderBackend> Backends { get; } = Enum.GetValues<RenderBackend>();

    public bool IsTwoD => Space == ProjectSpace.TwoD;

    public bool IsThreeD => Space == ProjectSpace.ThreeD;

    public string TargetFolder =>
        System.IO.Path.Combine(ProjectService.ProjectsRoot, ProjectService.SanitizeName(Name));

    public void Reset(EditorProject project)
    {
        Name = string.Empty;
        Backend = project.Backend;
        Space = project.Space;
        OnPropertyChanged(nameof(TargetFolder));
    }

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(TargetFolder));

    [RelayCommand]
    private void SetSpace(string? space) =>
        Space = space == "2D" ? ProjectSpace.TwoD : ProjectSpace.ThreeD;
}
