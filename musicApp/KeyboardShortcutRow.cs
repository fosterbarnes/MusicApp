using System;
using System.Collections.Generic;

namespace musicApp;

public sealed class KeyboardShortcutRow
{
    public string Shortcut { get; init; } = "";
    public string Action { get; init; } = "";
}

public static class KeyboardShortcutCatalog
{
    public static IReadOnlyList<KeyboardShortcutRow> InApp { get; } = new[]
    {
        new KeyboardShortcutRow { Shortcut = "⎵", Action = "Play/Pause" },
        new KeyboardShortcutRow { Shortcut = "←", Action = "Previous Track" },
        new KeyboardShortcutRow { Shortcut = "→", Action = "Next Track" },
        new KeyboardShortcutRow { Shortcut = "↑", Action = "Navigate Up" },
        new KeyboardShortcutRow { Shortcut = "↓", Action = "Navigate Down" },
        new KeyboardShortcutRow { Shortcut = "↵", Action = "Play Selected Track" }
    };

    public static IReadOnlyList<KeyboardShortcutRow> Global { get; } =
        Array.Empty<KeyboardShortcutRow>();
}
