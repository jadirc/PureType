using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using PureType.Services;

namespace PureType;

public partial class ReplacementsWindow : Window
{
    private readonly ReplacementService _service;
    private readonly ObservableCollection<ReplacementRule> _rules = new();

    public ReplacementsWindow(ReplacementService service)
    {
        InitializeComponent();
        _service = service;
        RulesGrid.ItemsSource = _rules;
        LoadRules();
    }

    private void LoadRules()
    {
        _rules.Clear();
        foreach (var (trigger, replacement) in _service.Rules)
        {
            var display = replacement.Replace("\n", "\\n").Replace("\t", "\\t");
            _rules.Add(new ReplacementRule { Trigger = trigger, Replacement = display });
        }
    }

    private void SaveRules()
    {
        var rules = _rules
            .Where(r => !string.IsNullOrWhiteSpace(r.Trigger))
            .Select(r => (r.Trigger, r.Replacement.Replace("\\n", "\n").Replace("\\t", "\t")))
            .ToList();
        _service.Save(rules);
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        _rules.Add(new ReplacementRule { Trigger = "", Replacement = "" });
        RulesGrid.SelectedIndex = _rules.Count - 1;
        RulesGrid.Focus();
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (RulesGrid.SelectedItem is ReplacementRule rule)
        {
            _rules.Remove(rule);
            SaveRules();
        }
    }

    private void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        var path = _service.FilePath;
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "# Replacement rules: trigger -> replacement\n");
        }
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        SaveRules();
        base.OnClosing(e);
    }
}

public class ReplacementRule
{
    public string Trigger { get; set; } = "";
    public string Replacement { get; set; } = "";
}
