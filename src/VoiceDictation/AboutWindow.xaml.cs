using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Navigation;

namespace VoiceDictation;

public partial class AboutWindow : Window
{
    private static readonly (string Name, string Version, string License, string Url)[] Libraries =
    {
        ("NAudio", "2.2.1", "MIT", "https://github.com/naudio/NAudio"),
        ("Serilog", "4.2.0", "Apache-2.0", "https://github.com/serilog/serilog"),
        ("Serilog.Sinks.File", "6.0.0", "Apache-2.0", "https://github.com/serilog/serilog-sinks-file"),
        ("Whisper.net", "1.9.0", "MIT", "https://github.com/sandrohanea/whisper.net"),
    };

    public AboutWindow()
    {
        InitializeComponent();

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"Version {version?.ToString(3) ?? "?"}";

        PopulateLibraries();
    }

    private void PopulateLibraries()
    {
        var labelColor = (SolidColorBrush)FindResource("TextBrush");
        var dimColor = (SolidColorBrush)FindResource("TextDimBrush");
        var linkColor = (SolidColorBrush)FindResource("AccentBrush");

        foreach (var (name, ver, license, url) in Libraries)
        {
            var nameBlock = new TextBlock
            {
                Foreground = labelColor,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 2),
            };
            nameBlock.Inlines.Add(name);
            nameBlock.Inlines.Add(new Run($"  {ver}") { Foreground = dimColor, FontWeight = FontWeights.Normal });

            var detailBlock = new TextBlock
            {
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 10),
            };
            detailBlock.Inlines.Add(new Run($"License: {license}  \u2022  ") { Foreground = dimColor });
            var link = new Hyperlink(new Run("Source")) { NavigateUri = new Uri(url), Foreground = linkColor };
            link.RequestNavigate += Hyperlink_RequestNavigate;
            detailBlock.Inlines.Add(link);

            LibrariesPanel.Children.Add(nameBlock);
            LibrariesPanel.Children.Add(detailBlock);
        }
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri)
        {
            UseShellExecute = true
        });
        e.Handled = true;
    }
}
