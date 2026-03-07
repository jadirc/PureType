using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Image = System.Windows.Controls.Image;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
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

        // Capture overlays for visible windows before swapping theme
        var overlays = new System.Collections.Generic.List<(Window window, Image overlay)>();
        foreach (var window in Application.Current.Windows.OfType<Window>()
                     .Where(w => w.IsVisible && w.Content is Grid))
        {
            try
            {
                var overlay = CaptureOverlay(window);
                if (overlay != null)
                    overlays.Add((window, overlay));
            }
            catch
            {
                // Capture failed for this window; skip silently
            }
        }

        // Swap theme resources
        var merged = Application.Current.Resources.MergedDictionaries;
        if (merged.Count > 0)
            merged.RemoveAt(0);
        merged.Insert(0, dict);

        // Fade out overlays
        foreach (var (window, overlay) in overlays)
        {
            try
            {
                FadeOutOverlay(window, overlay);
            }
            catch
            {
                // Fade failed; try to remove overlay
                if (window.Content is Grid grid && grid.Children.Contains(overlay))
                    grid.Children.Remove(overlay);
            }
        }
    }

    private static Image? CaptureOverlay(Window window)
    {
        var grid = (Grid)window.Content;

        var dpi = VisualTreeHelper.GetDpi(window);
        int width = (int)(grid.ActualWidth * dpi.DpiScaleX);
        int height = (int)(grid.ActualHeight * dpi.DpiScaleY);

        if (width <= 0 || height <= 0)
            return null;

        var rtb = new RenderTargetBitmap(width, height, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        rtb.Render(grid);
        rtb.Freeze();

        var overlay = new Image
        {
            Source = rtb,
            Stretch = Stretch.Fill,
            IsHitTestVisible = false
        };

        // Span all rows and columns so it covers the entire grid
        if (grid.RowDefinitions.Count > 1)
            Grid.SetRowSpan(overlay, grid.RowDefinitions.Count);
        if (grid.ColumnDefinitions.Count > 1)
            Grid.SetColumnSpan(overlay, grid.ColumnDefinitions.Count);

        System.Windows.Controls.Panel.SetZIndex(overlay, 9999);
        grid.Children.Add(overlay);

        return overlay;
    }

    private static void FadeOutOverlay(Window window, Image overlay)
    {
        var animation = new DoubleAnimation
        {
            From = 1.0,
            To = 0.0,
            Duration = TimeSpan.FromMilliseconds(250),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        animation.Completed += (_, _) =>
        {
            if (window.Content is Grid grid && grid.Children.Contains(overlay))
                grid.Children.Remove(overlay);
        };

        overlay.BeginAnimation(UIElement.OpacityProperty, animation);
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
