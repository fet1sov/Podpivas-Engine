using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace PodpivasEngine.Services;

public sealed class PanelPlacement
{
    public string Panel { get; set; } = "";

    public string Slot { get; set; } = "";
}

public sealed class DockLayoutState
{
    public List<PanelPlacement> Panels { get; set; } = [];

    public double LeftWidth { get; set; } = 250;

    public double RightWidth { get; set; } = 300;

    public double BottomHeight { get; set; } = 170;
}

public static class LayoutStore
{
    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PodpivasEngine",
        "layout.json");

    public static DockLayoutState Load()
    {
        try
        {
            var path = FilePath;
            if (!File.Exists(path))
                return new DockLayoutState();

            return JsonSerializer.Deserialize<DockLayoutState>(File.ReadAllText(path))
                   ?? new DockLayoutState();
        }
        catch (Exception)
        {
            return new DockLayoutState();
        }
    }

    public static void Save(DockLayoutState state)
    {
        try
        {
            var path = FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(state));
        }
        catch (Exception)
        {
        }
    }
}
