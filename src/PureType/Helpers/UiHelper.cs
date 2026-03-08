using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PureType.Services;

namespace PureType.Helpers;

internal static class UiHelper
{
    /// <summary>
    /// Populates a ComboBox with available Whisper models, showing a checkmark for downloaded ones.
    /// Selects <paramref name="preferredModel"/> if found, otherwise the first downloaded model, otherwise "base".
    /// </summary>
    internal static void PopulateWhisperModelCombo(System.Windows.Controls.ComboBox combo, string? preferredModel = null)
    {
        combo.Items.Clear();
        foreach (var (name, displayName, _) in WhisperModelManager.AvailableModels)
        {
            var isDownloaded = WhisperModelManager.IsModelDownloaded(name);
            var suffix = isDownloaded ? " \u2713" : "";
            var item = new System.Windows.Controls.ComboBoxItem
            {
                Content = displayName + suffix,
                Tag = name,
                FontWeight = isDownloaded ? FontWeights.SemiBold : FontWeights.Normal
            };
            combo.Items.Add(item);
        }

        if (preferredModel != null && SelectComboByTag(combo, preferredModel))
            return;

        foreach (System.Windows.Controls.ComboBoxItem item in combo.Items)
        {
            if (WhisperModelManager.IsModelDownloaded((string)item.Tag))
            {
                combo.SelectedItem = item;
                return;
            }
        }

        if (!SelectComboByTag(combo, "base") && combo.Items.Count > 0)
            combo.SelectedIndex = 0;
    }

    internal static bool SelectComboByTag(System.Windows.Controls.ComboBox combo, string tag)
    {
        foreach (System.Windows.Controls.ComboBoxItem item in combo.Items)
        {
            if ((string)item.Tag == tag)
            {
                combo.SelectedItem = item;
                return true;
            }
        }
        return false;
    }

    internal static string FormatShortcut(ModifierKeys mod, Key key)
    {
        var parts = new List<string>();
        if (mod.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        if (mod.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mod.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (mod.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        var keyName = key switch
        {
            Key.LeftCtrl => "L-Ctrl",
            Key.RightCtrl => "R-Ctrl",
            Key.LeftAlt => "L-Alt",
            Key.RightAlt => "R-Alt",
            Key.LeftShift => "L-Shift",
            Key.RightShift => "R-Shift",
            _ => key.ToString()
        };
        parts.Add(keyName);
        return string.Join("+", parts);
    }

    internal static (ModifierKeys mods, Key key) ParseShortcut(string value, Key defaultKey)
    {
        var mods = ModifierKeys.None;
        var key = defaultKey;
        var parts = value.Split('+');
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (trimmed.Equals("Win", StringComparison.OrdinalIgnoreCase))
                mods |= ModifierKeys.Windows;
            else if (trimmed.Equals("Ctrl", StringComparison.OrdinalIgnoreCase))
                mods |= ModifierKeys.Control;
            else if (trimmed.Equals("Alt", StringComparison.OrdinalIgnoreCase))
                mods |= ModifierKeys.Alt;
            else if (trimmed.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                mods |= ModifierKeys.Shift;
            else
            {
                var mappedKey = trimmed switch
                {
                    "L-Ctrl" => Key.LeftCtrl,
                    "R-Ctrl" => Key.RightCtrl,
                    "L-Alt" => Key.LeftAlt,
                    "R-Alt" => Key.RightAlt,
                    "L-Shift" => Key.LeftShift,
                    "R-Shift" => Key.RightShift,
                    _ => Enum.TryParse<Key>(trimmed, out var k) ? k : (Key?)null
                };
                if (mappedKey.HasValue)
                    key = mappedKey.Value;
            }
        }
        return (mods, key);
    }
}
