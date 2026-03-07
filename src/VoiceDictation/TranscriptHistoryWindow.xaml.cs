using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace VoiceDictation;

public partial class TranscriptHistoryWindow : Window
{
    private static readonly string TranscriptDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VoiceDictation", "transcripts");

    public static string TranscriptDirectory => TranscriptDir;

    private readonly List<string> _filePaths = new();
    private CancellationTokenSource? _searchDebounce;
    private string _currentSearch = string.Empty;

    public TranscriptHistoryWindow()
    {
        InitializeComponent();
        SetPreviewPlaceholder();
        LoadFiles();
    }

    private void SetPreviewPlaceholder()
    {
        var doc = new FlowDocument();
        var para = new Paragraph(new Run("Select a session to preview \u2026"));
        doc.Blocks.Add(para);
        PreviewText.Document = doc;
    }

    private void SetPreviewText(string text)
    {
        var doc = new FlowDocument();
        var para = new Paragraph(new Run(text));
        doc.Blocks.Add(para);
        PreviewText.Document = doc;
    }

    private void LoadFiles(string? filter = null)
    {
        FileList.Items.Clear();
        _filePaths.Clear();

        if (!Directory.Exists(TranscriptDir)) return;

        var files = Directory.GetFiles(TranscriptDir, "*.txt")
            .OrderByDescending(f => f)
            .ToArray();

        foreach (var file in files)
        {
            if (!string.IsNullOrEmpty(filter))
            {
                try
                {
                    var content = File.ReadAllText(file);
                    if (!content.Contains(filter, StringComparison.OrdinalIgnoreCase))
                        continue;
                }
                catch
                {
                    continue;
                }
            }

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

    private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchDebounce?.Cancel();
        _searchDebounce = new CancellationTokenSource();
        var token = _searchDebounce.Token;

        try
        {
            await System.Threading.Tasks.Task.Delay(300, token);
        }
        catch (System.Threading.Tasks.TaskCanceledException)
        {
            return;
        }

        _currentSearch = SearchBox.Text.Trim();
        LoadFiles(string.IsNullOrEmpty(_currentSearch) ? null : _currentSearch);
    }

    private void FileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FileList.SelectedItem is not ListBoxItem item || item.Tag is not string path) return;

        try
        {
            var content = File.ReadAllText(path);
            PreviewHeader.Text = $"PREVIEW \u2014 {Path.GetFileNameWithoutExtension(path)}";

            if (!string.IsNullOrEmpty(_currentSearch))
            {
                SetPreviewWithHighlights(content, _currentSearch);
            }
            else
            {
                SetPreviewText(content);
            }
        }
        catch (Exception ex)
        {
            SetPreviewText($"Error reading file: {ex.Message}");
        }
    }

    private void SetPreviewWithHighlights(string content, string search)
    {
        var doc = new FlowDocument();
        var para = new Paragraph();
        var highlightBrush = new SolidColorBrush(Color.FromArgb(100, 255, 200, 0));
        bool firstHighlight = true;
        Run? firstRun = null;

        int pos = 0;
        while (pos < content.Length)
        {
            int idx = content.IndexOf(search, pos, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                para.Inlines.Add(new Run(content[pos..]));
                break;
            }

            if (idx > pos)
                para.Inlines.Add(new Run(content[pos..idx]));

            var highlightRun = new Run(content[idx..(idx + search.Length)])
            {
                Background = highlightBrush,
                FontWeight = FontWeights.Bold
            };
            para.Inlines.Add(highlightRun);

            if (firstHighlight)
            {
                firstRun = highlightRun;
                firstHighlight = false;
            }

            pos = idx + search.Length;
        }

        doc.Blocks.Add(para);
        PreviewText.Document = doc;

        // Scroll to first match
        if (firstRun != null)
        {
            PreviewText.UpdateLayout();
            var pointer = firstRun.ContentStart;
            PreviewText.Selection.Select(pointer, firstRun.ContentEnd);
            var rect = pointer.GetCharacterRect(LogicalDirection.Forward);
            PreviewText.ScrollToVerticalOffset(rect.Top);
            // Clear the selection after scrolling so the highlight is visible
            PreviewText.Selection.Select(pointer, pointer);
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
