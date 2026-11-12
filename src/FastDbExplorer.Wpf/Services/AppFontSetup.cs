using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace FastDbExplorer.Wpf.Services;

/// <summary>
/// Points the app's text font at the Vazirmatn file in Assets\Fonts (copied next to the exe).
/// The family name is read from the font file itself, so nothing is guessed. Without a file the
/// app uses Segoe UI and Tahoma, and the reason is written to the log.
/// </summary>
public static class AppFontSetup
{
    private const string Fallbacks = ", Segoe UI, Tahoma";

    public static string Apply()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts");
        var files = Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir, "*.ttf").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList()
            : [];
        var file = files.FirstOrDefault(f => Path.GetFileName(f).Contains("Regular", StringComparison.OrdinalIgnoreCase))
                   ?? files.FirstOrDefault();
        if (file is null)
        {
            Set("Segoe UI, Tahoma");
            return "Vazirmatn file not found in Assets\\Fonts; using Segoe UI.";
        }

        try
        {
            var glyph = new GlyphTypeface(new Uri(file));
            var family = glyph.FamilyNames.Values.FirstOrDefault() ?? "Vazirmatn";
            var uriFolder = new Uri(dir + Path.DirectorySeparatorChar).AbsoluteUri;
            Set(uriFolder + "#" + family + Fallbacks);
            return "Font loaded: " + family + " (" + Path.GetFileName(file) + ")";
        }
        catch (Exception ex)
        {
            Set("Segoe UI, Tahoma");
            return "Font file could not be read (" + Path.GetFileName(file) + "): " + ex.Message;
        }
    }

    private static void Set(string value) =>
        System.Windows.Application.Current.Resources["AppFont"] = new FontFamily(value);
}
