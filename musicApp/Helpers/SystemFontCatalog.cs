using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;

namespace musicApp.Helpers;

public static class SystemFontCatalog
{
    /// <summary>Stored in preferences and on the first combo item for the OS message / UI font.</summary>
    public const string SystemDefaultTag = "";

    public static void PopulateFontFamilyComboBox(ComboBox combo)
    {
        ArgumentNullException.ThrowIfNull(combo);
        combo.Items.Clear();

        combo.Items.Add(new ComboBoxItem { Content = "System default", Tag = SystemDefaultTag });

        var ietf = CultureInfo.CurrentUICulture.IetfLanguageTag;
        if (string.IsNullOrEmpty(ietf))
            ietf = CultureInfo.CurrentUICulture.Name;
        if (string.IsNullOrEmpty(ietf))
            ietf = "en-US";
        var lang = XmlLanguage.GetLanguage(ietf);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<(string Display, string Source)>();

        foreach (var ff in Fonts.SystemFontFamilies)
        {
            var source = ff.Source;
            if (string.IsNullOrEmpty(source) || !seen.Add(source))
                continue;

            string display;
            if (ff.FamilyNames.TryGetValue(lang, out var localized))
                display = localized;
            else if (ff.FamilyNames.Count > 0)
                display = ff.FamilyNames.Values.First();
            else
                display = source;

            entries.Add((display, source));
        }

        foreach (var (display, source) in entries.OrderBy(e => e.Display, StringComparer.CurrentCultureIgnoreCase))
            combo.Items.Add(new ComboBoxItem { Content = display, Tag = source });
    }

    public static FontFamily ResolveFontFamily(string? tagOrSource)
    {
        if (string.IsNullOrEmpty(tagOrSource))
            return SystemFonts.MessageFontFamily;

        try
        {
            return new FontFamily(tagOrSource);
        }
        catch
        {
            return SystemFonts.MessageFontFamily;
        }
    }
}
