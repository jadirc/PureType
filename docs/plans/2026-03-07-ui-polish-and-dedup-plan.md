# UI Polish & Code Deduplication Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Extract duplicated helpers into `UiHelper`, shrink main window height, and add "Settings" to the tray menu.

**Architecture:** Move three static methods (`SelectComboByTag`, `FormatShortcut`, `ParseShortcut`) from `SettingsWindow` and `MainWindow` into a new `Helpers/UiHelper.cs`. Update all call sites. Adjust XAML default height. Add one tray menu item.

**Tech Stack:** C# / WPF / .NET 8

---

### Task 1: Create `Helpers/UiHelper.cs` with shared methods

**Files:**
- Create: `src/VoiceDictation/Helpers/UiHelper.cs`

**Step 1: Create `UiHelper.cs`**

```csharp
using System.Windows.Controls;
using System.Windows.Input;

namespace VoiceDictation.Helpers;

internal static class UiHelper
{
    internal static bool SelectComboByTag(ComboBox combo, string tag)
    {
        foreach (ComboBoxItem item in combo.Items)
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
```

**Step 2: Build to verify it compiles**

Run: `dotnet build`
Expected: Build succeeded (new file compiles, no callers yet)

---

### Task 2: Update `MainWindow.xaml.cs` to use `UiHelper`

**Files:**
- Modify: `src/VoiceDictation/MainWindow.xaml.cs`

**Step 1: Replace call sites**

In `MainWindow.xaml.cs`, make these changes:

- Line 114: `SettingsWindow.ParseShortcut(...)` → `UiHelper.ParseShortcut(...)`
- Line 115: `SettingsWindow.ParseShortcut(...)` → `UiHelper.ParseShortcut(...)`
- Line 118: `SelectComboByTag(...)` → `UiHelper.SelectComboByTag(...)`
- Line 141: `SettingsWindow.ParseShortcut(...)` → `UiHelper.ParseShortcut(...)`
- Line 142: `SettingsWindow.ParseShortcut(...)` → `UiHelper.ParseShortcut(...)`

Add `using VoiceDictation.Helpers;` if not already present.

**Step 2: Delete `SelectComboByTag` from `MainWindow`**

Delete lines 199-210 (the `SelectComboByTag` method) from `MainWindow.xaml.cs`.

**Step 3: Build to verify**

Run: `dotnet build`
Expected: Build succeeded

---

### Task 3: Update `SettingsWindow.xaml.cs` to use `UiHelper`

**Files:**
- Modify: `src/VoiceDictation/SettingsWindow.xaml.cs`

**Step 1: Replace call sites**

In `SettingsWindow.xaml.cs`, change all internal calls:

- Line 53: `SelectComboByTag(...)` → `UiHelper.SelectComboByTag(...)`
- Line 56: `ParseShortcut(...)` → `UiHelper.ParseShortcut(...)`
- Line 57: `ParseShortcut(...)` → `UiHelper.ParseShortcut(...)`
- Line 58: `FormatShortcut(...)` → `UiHelper.FormatShortcut(...)`
- Line 59: `FormatShortcut(...)` → `UiHelper.FormatShortcut(...)`
- Line 60: `SelectComboByTag(...)` → `UiHelper.SelectComboByTag(...)`
- Line 63: `SelectComboByTag(...)` → `UiHelper.SelectComboByTag(...)`
- Line 103: `SelectComboByTag(...)` → `UiHelper.SelectComboByTag(...)`
- Line 139: `FormatShortcut(...)` → `UiHelper.FormatShortcut(...)`
- Line 140: `FormatShortcut(...)` → `UiHelper.FormatShortcut(...)`
- Line 341: `FormatShortcut(...)` → `UiHelper.FormatShortcut(...)`
- Line 384: `FormatShortcut(...)` → `UiHelper.FormatShortcut(...)`

Add `using VoiceDictation.Helpers;` if not already present.

**Step 2: Delete the three methods from `SettingsWindow`**

Delete these methods:
- `SelectComboByTag` (lines 470-481)
- `FormatShortcut` (lines 483-502)
- `ParseShortcut` (lines 504-537)

**Step 3: Build to verify**

Run: `dotnet build`
Expected: Build succeeded

---

### Task 4: Reduce MainWindow default height

**Files:**
- Modify: `src/VoiceDictation/MainWindow.xaml`

**Step 1: Change Height attribute**

Line 5: Change `Height="600"` to `Height="450"`.

**Step 2: Build to verify**

Run: `dotnet build`
Expected: Build succeeded

---

### Task 5: Add "Settings" to tray context menu

**Files:**
- Modify: `src/VoiceDictation/MainWindow.xaml.cs`

**Step 1: Add "Settings" menu item in `SetupTrayIcon()`**

In the `SetupTrayIcon()` method, after the second separator (`menu.Items.Add(new ... ToolStripSeparator());` at line 679), insert before the "Open" line (line 681):

```csharp
menu.Items.Add("Settings", null, (_, _) =>
{
    Dispatcher.Invoke(() => SettingsButton_Click(this, new RoutedEventArgs()));
});
```

**Step 2: Build to verify**

Run: `dotnet build`
Expected: Build succeeded

---

### Task 6: Run tests and commit

**Step 1: Run all tests**

Run: `dotnet test`
Expected: All tests pass

**Step 2: Commit all changes**

```bash
git add src/VoiceDictation/Helpers/UiHelper.cs src/VoiceDictation/MainWindow.xaml src/VoiceDictation/MainWindow.xaml.cs src/VoiceDictation/SettingsWindow.xaml.cs
git commit -m "refactor: extract shared helpers to UiHelper, shrink window, add tray Settings"
```
