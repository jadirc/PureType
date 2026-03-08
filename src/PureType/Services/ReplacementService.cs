using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Serilog;

namespace PureType.Services;

public class ReplacementService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private static readonly Dictionary<string, string> DefaultRules = new()
    {
        ["period"] = ".",
        ["comma"] = ",",
        ["question mark"] = "?",
        ["exclamation mark"] = "!",
        ["colon"] = ":",
        ["semicolon"] = ";",
        ["new line"] = "\n",
        ["new paragraph"] = "\n\n",
        ["dash"] = "—",
        ["open paren"] = "(",
        ["close paren"] = ")",
    };

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
            SeedDefaults();

        try
        {
            var json = File.ReadAllText(_filePath);
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions);

            if (dict == null)
            {
                _rules = new List<(string, string)>();
                return;
            }

            _rules = dict
                .Where(kv => !string.IsNullOrEmpty(kv.Key))
                .Select(kv => (kv.Key, kv.Value))
                .ToList();

            Log.Information("Loaded {Count} replacement rules from {Path}", _rules.Count, _filePath);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to parse replacements from {Path}", _filePath);
            _rules = new List<(string, string)>();
        }
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
        var dict = new Dictionary<string, string>();
        foreach (var (trigger, replacement) in rules)
        {
            if (!string.IsNullOrWhiteSpace(trigger))
                dict[trigger] = replacement;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        File.WriteAllText(_filePath, JsonSerializer.Serialize(dict, JsonOptions));
        Reload();
    }

    private void SeedDefaults()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        File.WriteAllText(_filePath, JsonSerializer.Serialize(DefaultRules, JsonOptions));
        Log.Information("Created default replacements at {Path}", _filePath);
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
