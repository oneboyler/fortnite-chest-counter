using System.Text.Json;
using Microsoft.Win32;

namespace ChestCounter;

/// <summary>Overlay and tray preferences, saved in data/settings.json.</summary>
sealed class Settings
{
    /// <summary>Overlay position (screen pixels, top-left corner).</summary>
    public int? Left { get; set; }
    public int? Top { get; set; }
    public bool OverlayVisible { get; set; } = true;
    /// <summary>Overlay page, Ctrl+F8 cycles: full numbers, compact, graph.</summary>
    public int Page { get; set; }
    public const int PageFull = 0, PageCompact = 1, PageGraph = 2, PageCount = 3;
    /// <summary>Windows pop-up after each game. The overlay already flashes "+N", so off by default.</summary>
    public bool NotifyAfterGame { get; set; }
    /// <summary>Set by "New session"; games before this don't count toward the session.</summary>
    public DateTime? SessionStart { get; set; }
    /// <summary>The setup window was closed once; it doesn't open by itself again (tray menu > Setup and help).</summary>
    public bool SetupDone { get; set; }

    static string FileIn(string dataDir) => Path.Combine(dataDir, "settings.json");

    public static Settings Load(string dataDir)
    {
        try
        {
            var f = FileIn(dataDir);
            if (File.Exists(f)) return JsonSerializer.Deserialize<Settings>(File.ReadAllText(f)) ?? new();
        }
        catch { /* broken file: start with defaults */ }
        return new();
    }

    public void Save(string dataDir)
    {
        try { File.WriteAllText(FileIn(dataDir), JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); }
        catch { /* not worth bothering the user about */ }
    }
}

/// <summary>"Start with Windows": a per-user entry in the registry Run key.</summary>
static class StartWithWindows
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "Chest Counter";
    static string Command => $"\"{Environment.ProcessPath}\" --hidden";

    public static bool IsOn()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue(ValueName) is string s && s.Equals(Command, StringComparison.OrdinalIgnoreCase);
    }

    public static void Set(bool on)
    {
        using var k = Registry.CurrentUser.CreateSubKey(RunKey);
        if (on) k.SetValue(ValueName, Command);
        else k.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
