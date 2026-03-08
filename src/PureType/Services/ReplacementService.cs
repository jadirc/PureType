using System.IO;
using System.Text.RegularExpressions;
using Serilog;

namespace PureType.Services;

public class ReplacementService : IDisposable
{
    private readonly string _filePath;
    private FileSystemWatcher? _watcher;
    private List<(string trigger, string replacement)> _rules = new();

    public string FilePath => _filePath;
    public IReadOnlyList<(string trigger, string replacement)> Rules => _rules;

    public ReplacementService(string filePath)
    {
        _filePath = filePath;
        Reload();
        WatchFile();
    }

    public void Reload()
    {
        if (!File.Exists(_filePath))
        {
            _rules = new List<(string, string)>();
            return;
        }

        var rules = new List<(string, string)>();
        foreach (var line in File.ReadAllLines(_filePath))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                continue;

            string? trigger = null, replacement = null;

            var idx = line.IndexOf(" -> ", StringComparison.Ordinal);
            if (idx >= 0)
            {
                trigger = line[..idx].Trim();
                replacement = line[(idx + 4)..].Trim();
            }
            else
            {
                idx = line.IndexOf(" → ", StringComparison.Ordinal);
                if (idx >= 0)
                {
                    trigger = line[..idx].Trim();
                    replacement = line[(idx + 3)..].Trim();
                }
            }

            if (trigger != null && replacement != null && trigger.Length > 0)
            {
                replacement = replacement
                    .Replace("\\n", "\n")
                    .Replace("\\t", "\t");
                rules.Add((trigger, replacement));
            }
        }

        _rules = rules;
        Log.Information("Loaded {Count} replacement rules from {Path}", rules.Count, _filePath);
    }

    public string Apply(string text)
    {
        if (string.IsNullOrEmpty(text) || _rules.Count == 0)
            return text;

        foreach (var (trigger, replacement) in _rules)
        {
            var r = replacement;
            text = Regex.Replace(text, @"(\s?)\b" + Regex.Escape(trigger) + @"\b[.,;:!?]?(\s?)",
                m =>
                {
                    bool keepLead = char.IsLetterOrDigit(r.FirstOrDefault());
                    bool keepTrail = !r.Contains('\n');
                    return (keepLead ? m.Groups[1].Value : "")
                         + r
                         + (keepTrail ? m.Groups[2].Value : "");
                },
                RegexOptions.IgnoreCase);
        }

        return text;
    }

    public void Save(IEnumerable<(string trigger, string replacement)> rules)
    {
        var lines = new List<string>();
        foreach (var (trigger, replacement) in rules)
        {
            var stored = replacement
                .Replace("\n", "\\n")
                .Replace("\t", "\\t");
            lines.Add($"{trigger} -> {stored}");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        File.WriteAllLines(_filePath, lines);
        Reload();
    }

    private void WatchFile()
    {
        var dir = Path.GetDirectoryName(_filePath);
        var name = Path.GetFileName(_filePath);
        if (dir == null) return;
        Directory.CreateDirectory(dir);

        _watcher = new FileSystemWatcher(dir, name)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime
        };
        _watcher.Changed += (_, _) =>
        {
            Thread.Sleep(100);
            try { Reload(); } catch (Exception ex) { Log.Warning(ex, "Failed to reload replacements"); }
        };
        _watcher.EnableRaisingEvents = true;
    }

    public void Dispose()
    {
        _watcher?.Dispose();
    }
}
