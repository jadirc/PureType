using System.Windows;

namespace VoiceDictation.Helpers;

public static class ThemeManager
{
    private static readonly Uri DarkUri = new("Themes/Dark.xaml", UriKind.Relative);
    private static readonly Uri LightUri = new("Themes/Light.xaml", UriKind.Relative);

    public static void Apply(string theme)
    {
        var uri = theme == "Light" ? LightUri : DarkUri;
        var dict = new ResourceDictionary { Source = uri };

        var merged = Application.Current.Resources.MergedDictionaries;
        // Theme dictionary is always at index 0
        if (merged.Count > 0)
            merged.RemoveAt(0);
        merged.Insert(0, dict);
    }
}
