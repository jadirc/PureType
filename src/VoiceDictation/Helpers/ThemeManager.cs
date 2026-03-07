using System.Windows;
using Microsoft.Win32;

namespace VoiceDictation.Helpers;

public static class ThemeManager
{
    private static readonly Uri DarkUri = new("Themes/Dark.xaml", UriKind.Relative);
    private static readonly Uri LightUri = new("Themes/Light.xaml", UriKind.Relative);

    public static void Apply(string theme)
    {
        var resolved = theme == "Auto" ? DetectSystemTheme() : theme;
        var uri = resolved == "Light" ? LightUri : DarkUri;
        var dict = new ResourceDictionary { Source = uri };

        var merged = Application.Current.Resources.MergedDictionaries;
        // Theme dictionary is always at index 0
        if (merged.Count > 0)
            merged.RemoveAt(0);
        merged.Insert(0, dict);
    }

    /// <summary>
    /// Reads the Windows app theme from the registry.
    /// AppsUseLightTheme: 0 = Dark, 1 = Light.
    /// </summary>
    private static string DetectSystemTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            if (value is int i)
                return i == 1 ? "Light" : "Dark";
        }
        catch
        {
            // Registry access failed — fall back to Dark
        }
        return "Dark";
    }
}
