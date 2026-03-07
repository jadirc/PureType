using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace VoiceDictation;

public partial class TranscriptHistoryWindow : Window
{
    private static readonly string TranscriptDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VoiceDictation", "transcripts");

    public static string TranscriptDirectory => TranscriptDir;

    private readonly List<string> _filePaths = new();

    public TranscriptHistoryWindow()
    {
        InitializeComponent();
        LoadFiles();
    }

    private void LoadFiles()
    {
        FileList.Items.Clear();
        _filePaths.Clear();

        if (!Directory.Exists(TranscriptDir)) return;

        var files = Directory.GetFiles(TranscriptDir, "*.txt")
            .OrderByDescending(f => f)
            .ToArray();

        foreach (var file in files)
        {
            var name = Path.GetFileNameWithoutExtension(file);
            // Format: transcript_2026-03-07_143022 → "2026-03-07  14:30:22"
            var display = name.Replace("transcript_", "")
                .Replace("_", "  ");
            if (display.Length >= 12)
                display = display[..10] + "  " + display[12..14] + ":" + display[14..16] + ":" + display[16..];

            FileList.Items.Add(new ListBoxItem { Content = display, Tag = file });
            _filePaths.Add(file);
        }
    }

    private void FileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FileList.SelectedItem is not ListBoxItem item || item.Tag is not string path) return;

        try
        {
            PreviewText.Text = File.ReadAllText(path);
            PreviewHeader.Text = $"PREVIEW — {Path.GetFileNameWithoutExtension(path)}";
        }
        catch (Exception ex)
        {
            PreviewText.Text = $"Error reading file: {ex.Message}";
        }
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(TranscriptDir);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(TranscriptDir)
        {
            UseShellExecute = true
        });
    }

    /// <summary>
    /// Saves a transcript log to the transcripts directory.
    /// Called automatically on app shutdown.
    /// </summary>
    public static void SaveSession(IReadOnlyList<(DateTime Timestamp, string Text)> log)
    {
        if (log.Count == 0) return;

        Directory.CreateDirectory(TranscriptDir);
        var fileName = $"transcript_{log[0].Timestamp:yyyy-MM-dd_HHmmss}.txt";
        var path = Path.Combine(TranscriptDir, fileName);

        var sb = new System.Text.StringBuilder();
        foreach (var (timestamp, text) in log)
            sb.AppendLine($"[{timestamp:HH:mm:ss}] {text}");

        File.WriteAllText(path, sb.ToString());
    }
}
