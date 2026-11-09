namespace ChestCounter;

/// <summary>
/// A key the app reacts to while you play (it only reads whether the key is held, so the game still gets it):
/// a keyboard key or mouse button, plus the Ctrl/Shift/Alt that must be held with it.
/// </summary>
readonly record struct HotKey(Keys Key, bool Ctrl, bool Shift, bool Alt)
{
    // What each key does, in the order they're saved and shown.
    public const int Hide = 0, Move = 1, Page = 2, Count = 3;

    public static readonly HotKey[] Defaults =
    [
        new(Keys.F8, false, false, false),
        new(Keys.F8, false, true, false),
        new(Keys.F8, true, false, false),
    ];

    public int ModCount => (Ctrl ? 1 : 0) + (Shift ? 1 : 0) + (Alt ? 1 : 0);

    /// <summary>Its Ctrl/Shift/Alt are held (extra ones are fine; the key with the most matching ones wins).</summary>
    public bool ModsHeld(bool ctrl, bool shift, bool alt) => (!Ctrl || ctrl) && (!Shift || shift) && (!Alt || alt);

    /// <summary>For showing: "Ctrl", "Shift", "F8".</summary>
    public string[] Parts()
    {
        var p = new List<string>();
        if (Ctrl) p.Add("Ctrl");
        if (Shift) p.Add("Shift");
        if (Alt) p.Add("Alt");
        p.Add(KeyName(Key));
        return [.. p];
    }

    public override string ToString() => string.Join("+", Parts());

    /// <summary>For settings.json: "Ctrl+Shift+F8", with the key's Windows name.</summary>
    public string Save() => (Ctrl ? "Ctrl+" : "") + (Shift ? "Shift+" : "") + (Alt ? "Alt+" : "") + Key;

    public static HotKey Parse(string? s, HotKey fallback)
    {
        if (string.IsNullOrWhiteSpace(s)) return fallback;
        var parts = s.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || !Enum.TryParse<Keys>(parts[^1], true, out var key) || !Usable(key)) return fallback;
        var mods = parts[..^1];
        bool Has(string m) => mods.Contains(m, StringComparer.OrdinalIgnoreCase);
        return new(key, Has("Ctrl"), Has("Shift"), Has("Alt"));
    }

    /// <summary>Keys that can't be used: Esc (cancels picking), left/right mouse, and the modifiers on their own.</summary>
    public static bool Usable(Keys k) => k is not (Keys.None or Keys.Escape or Keys.LButton or Keys.RButton
        or Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey or Keys.ControlKey or Keys.LControlKey or Keys.RControlKey
        or Keys.Menu or Keys.LMenu or Keys.RMenu or Keys.LWin or Keys.RWin);

    public static string KeyName(Keys k)
    {
        switch (k)
        {
            case Keys.XButton1: return "Mouse 4";
            case Keys.XButton2: return "Mouse 5";
            case Keys.MButton: return "Middle mouse";
            case >= Keys.NumPad0 and <= Keys.NumPad9: return "Num " + (k - Keys.NumPad0);
            case Keys.Add: return "Num +";
            case Keys.Subtract: return "Num -";
            case Keys.Multiply: return "Num *";
            case Keys.Divide: return "Num /";
            case Keys.Decimal: return "Num .";
            case Keys.Space: return "Space";
            case Keys.Return: return "Enter";
            case Keys.Back: return "Backspace";
            case Keys.Tab: return "Tab";
            case Keys.Capital: return "Caps Lock";
            case Keys.Prior: return "Page Up";
            case Keys.Next: return "Page Down";
            case Keys.Scroll: return "Scroll Lock";
            case Keys.Snapshot: return "Print Screen";
            case Keys.Delete: return "Delete";
            case Keys.Insert: return "Insert";
        }
        // letters, digits and symbol keys: the character it types on this keyboard layout
        var ch = (char)(Native.MapVirtualKey((uint)k, Native.MAPVK_VK_TO_CHAR) & 0x7FFF);
        return ch > ' ' ? char.ToUpperInvariant(ch).ToString() : k.ToString();
    }

    public static HotKey[] Load(Settings s) =>
    [
        Parse(s.KeyHide, Defaults[Hide]),
        Parse(s.KeyMove, Defaults[Move]),
        Parse(s.KeyPage, Defaults[Page]),
    ];

    public static void Store(Settings s, HotKey[] keys)
    {
        s.KeyHide = keys[Hide].Save();
        s.KeyMove = keys[Move].Save();
        s.KeyPage = keys[Page].Save();
    }

    /// <summary>The line shown on the overlay for the first few seconds.</summary>
    public static string Hint(HotKey[] k) => $"{k[Hide]} hide/show   {k[Move]} move   {k[Page]} page\ncounts update right after each game";
}
