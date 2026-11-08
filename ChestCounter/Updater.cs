using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ChestCounter;

/// <summary>
/// The app's only internet use: asks GitHub whether a newer release exists and, when the user clicks "Update",
/// downloads it and swaps the program files. The data folder (saved games, settings) is never touched.
/// Nothing about the user or their games is sent; it's a plain request for the public release list.
/// </summary>
static class Updater
{
    public const string Repo = "oneboyler/fortnite-chest-counter";
    public const string ReleasesPage = $"https://github.com/{Repo}/releases/latest";
    const string AssetName = "ChestCounter-win-x64.zip";

    public static Version Current => Normalize(Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0));

    public sealed record Release(Version Version, string Tag, string ZipUrl, string? Sha256);

    static HttpClient NewClient(TimeSpan timeout)
    {
        var h = new HttpClient { Timeout = timeout };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("ChestCounter-update-check");
        return h;
    }

    /// <summary>The latest release on GitHub, or null if there's none we can use.</summary>
    public static async Task<Release?> CheckAsync()
    {
        using var http = NewClient(TimeSpan.FromSeconds(20));
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        using var doc = JsonDocument.Parse(await http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest"));
        var root = doc.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var v)) return null;
        foreach (var a in root.GetProperty("assets").EnumerateArray())
        {
            if (a.GetProperty("name").GetString() != AssetName) continue;
            var url = a.GetProperty("browser_download_url").GetString();
            // only ever download from this repo's own releases
            if (url == null || !url.StartsWith($"https://github.com/{Repo}/releases/download/", StringComparison.Ordinal)) return null;
            var sha = a.TryGetProperty("digest", out var d) && d.GetString() is string ds && ds.StartsWith("sha256:", StringComparison.Ordinal) ? ds[7..] : null;
            return new Release(Normalize(v), tag, url, sha);
        }
        return null;
    }

    public static bool IsNewer(Release r) => r.Version > Current;

    static Version Normalize(Version v) => new(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build));

    static string WorkDir => Path.Combine(Path.GetTempPath(), "ChestCounterUpdate");

    /// <summary>Downloads and unpacks the release. Returns the folder holding the new program files.</summary>
    public static async Task<string> DownloadAsync(Release r)
    {
        if (Directory.Exists(WorkDir)) Directory.Delete(WorkDir, recursive: true);
        Directory.CreateDirectory(WorkDir);
        var zip = Path.Combine(WorkDir, AssetName);
        using (var http = NewClient(TimeSpan.FromMinutes(10)))
        using (var resp = await http.GetAsync(r.ZipUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            resp.EnsureSuccessStatusCode();
            await using var fs = File.Create(zip);
            await resp.Content.CopyToAsync(fs);
        }
        if (r.Sha256 != null)
        {
            await using var fs = File.OpenRead(zip);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(fs));
            if (!hash.Equals(r.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The download was damaged (checksum doesn't match). Try again later.");
        }
        var unpack = Path.Combine(WorkDir, "new");
        ZipFile.ExtractToDirectory(zip, unpack);
        var src = Path.Combine(unpack, "Chest Counter");
        if (!File.Exists(Path.Combine(src, "Chest Counter.exe")))
            throw new InvalidDataException("The download doesn't look like Chest Counter.");
        var data = Path.Combine(src, "data");
        if (Directory.Exists(data)) Directory.Delete(data, recursive: true);   // never replace saved games
        return src;
    }

    /// <summary>
    /// Starts a small hidden script that waits for this app to close, copies the new files over the old ones
    /// (leaving the data folder alone) and starts the app again. The caller must exit right after.
    /// </summary>
    public static void InstallAndRestart(string src)
    {
        var dest = AppContext.BaseDirectory.TrimEnd('\\', '/');
        var script = Path.Combine(WorkDir, "install.cmd");
        var pid = Environment.ProcessId;
        var cmd = new StringBuilder()
            .AppendLine("@echo off")
            .AppendLine("chcp 65001 >nul")
            .AppendLine(":wait")
            .AppendLine($"tasklist /FI \"PID eq {pid}\" /NH | find \"{pid}\" >nul && (timeout /t 1 /nobreak >nul & goto wait)")
            .AppendLine($"robocopy \"{src}\" \"{dest}\" /E /XD data /R:10 /W:1 /NFL /NDL /NJH /NJS >nul")
            .AppendLine($"start \"\" \"{Path.Combine(dest, "Chest Counter.exe")}\"")
            .ToString();
        File.WriteAllText(script, cmd, new UTF8Encoding(false));
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"\"{script}\"\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        });
    }
}
