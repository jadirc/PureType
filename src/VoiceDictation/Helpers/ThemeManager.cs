using System.Windows;
using Microsoft.Win32;

namespace VoiceDictation.Helpers;

public static class ThemeManager
{
    private static readonly Uri DarkUri = new("Themes/Dark.xaml", UriKind.Relative);
    private static readonly Uri LightUri = new("Themes/Light.xaml", UriKind.Relative);

    private static string _currentSetting = "Auto";

    public static void Apply(string theme)
    {
        _currentSetting = theme;
        ApplyResolved();

        if (theme == "Auto")
            StartWatching();
        else
            StopWatching();
    }

    private static void ApplyResolved()
    {
        var resolved = _currentSetting == "Auto" ? DetectSystemTheme() : _currentSetting;
        var uri = resolved == "Light" ? LightUri : DarkUri;
        var dict = new ResourceDictionary { Source = uri };

        var merged = Application.Current.Resources.MergedDictionaries;
        if (merged.Count > 0)
            merged.RemoveAt(0);
        merged.Insert(0, dict);
    }

    private static bool _watching;

    private static void StartWatching()
    {
        if (_watching) return;
        _watching = true;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    private static void StopWatching()
    {
        if (!_watching) return;
        _watching = false;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General) return;
        if (_currentSetting != "Auto") return;

        Application.Current.Dispatcher.InvokeAsync(ApplyResolved);
    }

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
            // Registry access failed
        }
        return "Dark";
    }
}
