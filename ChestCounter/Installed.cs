using Microsoft.Win32;

namespace ChestCounter;

/// <summary>
/// Only matters for a copy put there by ChestCounter-Setup.exe (see installer\ChestCounter.iss).
/// The portable zip never has the registry entry this looks for, so nothing happens there.
/// </summary>
static class Installed
{
    /// <summary>Same id as AppId in installer\ChestCounter.iss. Never change it: Windows uses it to find the installed copy.</summary>
    public const string AppId = "{8F2B6C1E-4D7A-4B93-9E5F-2C1A7D3E6B40}";
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppId + "_is1";

    /// <summary>
    /// The in-app updater swaps the program files without running the installer, so the version in
    /// Windows' "Installed apps" list would stay at whatever was first installed. This puts it right.
    /// </summary>
    public static void SyncVersion()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(UninstallKey, writable: true);
            if (k?.GetValue("InstallLocation") is not string where) return;
            static string Norm(string p) => Path.GetFullPath(p).TrimEnd('\\', '/');
            if (!Norm(where).Equals(Norm(AppContext.BaseDirectory), StringComparison.OrdinalIgnoreCase)) return;
            var now = Updater.Current.ToString(3);
            if (k.GetValue("DisplayVersion") as string != now) k.SetValue("DisplayVersion", now);
        }
        catch { /* cosmetic only */ }
    }
}
