using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using PodpivasEngine.Models;

namespace PodpivasEngine.ViewModels;

public sealed partial class ConsoleViewModel : ViewModelBase
{
    public ObservableCollection<ConsoleMessage> Messages { get; } = [];

    public void Load(EditorProject project)
    {
        Messages.Clear();
        Log("Console.Init");
        Log("Console.SceneLoaded", project.Name);
        Log("Console.BackendReady", project.Backend);
    }

    public void Log(string key, params object?[] arguments) =>
        Messages.Add(new ConsoleMessage(key, arguments));

    public void LogRaw(string message, string? color = null) => Messages.Add(ConsoleMessage.Raw(message, color));

    public void Relocalize()
    {
        foreach (var message in Messages)
            message.Relocalize();
    }

    [RelayCommand]
    private void Clear() => Messages.Clear();
}
