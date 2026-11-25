using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PodpivasEngine.Models;
using PodpivasEngine.Scripting;

namespace PodpivasEngine.ViewModels;

public sealed partial class ScriptComponentViewModel : ViewModelBase
{
    private readonly IReadOnlyList<string> _scripts;
    private readonly Func<string, IReadOnlyList<LuaExportField>> _readExports;
    private readonly IReadOnlyList<string> _sceneObjects;

    public ScriptComponentViewModel(
        ScriptComponent model,
        IReadOnlyList<string> scripts,
        Func<string, IReadOnlyList<LuaExportField>> readExports,
        IReadOnlyList<string> sceneObjects)
    {
        Model = model;
        _scripts = scripts;
        _readExports = readExports;
        _sceneObjects = sceneObjects;
        _script = model.Script;
        ReloadParameters();
    }

    public ScriptComponent Model { get; }

    public IReadOnlyList<string> Scripts => _scripts;

    public ObservableCollection<ScriptParameterViewModel> Parameters { get; } = [];

    private string _script;

    public string Script
    {
        get => _script;
        set
        {
            // ComboBox writes null/empty when its list is rebuilt or the selection is removed.
            if (string.IsNullOrEmpty(value) || string.Equals(_script, value, StringComparison.Ordinal))
                return;

            _script = value;
            OnPropertyChanged();
            Model.Script = value;
            ReloadParameters();
        }
    }

    public bool HasParameters => Parameters.Count > 0;

    public event Action<ScriptComponentViewModel>? Removed;

    private void ReloadParameters()
    {
        Parameters.Clear();

        if (!string.IsNullOrWhiteSpace(Script))
        {
            foreach (var field in _readExports(Script))
                Parameters.Add(new ScriptParameterViewModel(field, Model, _sceneObjects));
        }

        OnPropertyChanged(nameof(HasParameters));
    }

    [RelayCommand]
    private void Remove() => Removed?.Invoke(this);
}

public sealed partial class ScriptParameterViewModel : ViewModelBase
{
    private readonly ScriptComponent _component;

    public ScriptParameterViewModel(LuaExportField field, ScriptComponent component, IReadOnlyList<string> sceneObjects)
    {
        Name = field.Name;
        Label = field.Label;
        Kind = field.Kind;
        SceneObjects = sceneObjects;
        _component = component;

        _value = component.Values.TryGetValue(field.Name, out var stored) ? stored : field.DefaultValue;
        component.Values[field.Name] = _value;
    }

    public string Name { get; }

    public string Label { get; }

    public LuaExportKind Kind { get; }

    public IReadOnlyList<string> SceneObjects { get; }

    public bool IsBoolean => Kind == LuaExportKind.Boolean;

    public bool IsScalar => Kind is LuaExportKind.Number or LuaExportKind.Text;

    public bool IsObject => Kind == LuaExportKind.Object;

    [ObservableProperty]
    private string _value;

    public bool BooleanValue
    {
        get => string.Equals(Value, "true", StringComparison.OrdinalIgnoreCase);
        set
        {
            Value = value ? "true" : "false";
            OnPropertyChanged();
        }
    }

    partial void OnValueChanged(string value) => _component.SetValue(Name, value);
}
