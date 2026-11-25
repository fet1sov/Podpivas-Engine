using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PodpivasEngine.Models;

public sealed partial class ScriptComponent : ObservableObject
{
    public ScriptComponent(string script) => _script = script;

    private string _script;

    public string Script
    {
        get => _script;
        set
        {
            // ComboBox writes null/empty when its list is refreshed or the node loses focus.
            if (string.IsNullOrEmpty(value) || string.Equals(_script, value, StringComparison.Ordinal))
                return;

            _script = value;
            OnPropertyChanged();
        }
    }

    [JsonIgnore]
    public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);

    public void SetValue(string name, string value)
    {
        if (Values.TryGetValue(name, out var current) && string.Equals(current, value, StringComparison.Ordinal))
            return;

        Values[name] = value;
        OnPropertyChanged(nameof(Values));
    }

    public ScriptComponent Clone()
    {
        var clone = new ScriptComponent(Script);
        foreach (var (name, value) in Values)
            clone.Values[name] = value;

        return clone;
    }
}
