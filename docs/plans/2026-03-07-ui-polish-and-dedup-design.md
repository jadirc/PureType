# UI Polish & Code Deduplication Design

## Goal

Combine two small improvements into a single focused change:
1. Extract duplicated helper methods into a shared utility class
2. Reduce the main window default height (settings moved out)
3. Add a "Settings" entry to the system tray context menu

## 1. Extract Shared Helpers to `Helpers/UiHelper.cs`

Create a new static class `UiHelper` with three methods currently in `SettingsWindow`:

- `SelectComboByTag(ComboBox combo, string tag) -> bool` — duplicated identically in `MainWindow` and `SettingsWindow`
- `FormatShortcut(ModifierKeys mod, Key key) -> string` — `internal static` in `SettingsWindow`, called cross-class from `MainWindow`
- `ParseShortcut(string value, Key defaultKey) -> (ModifierKeys, Key)` — same situation

Both windows change their calls to `UiHelper.*`. The originals are deleted from both classes.

## 2. Reduce MainWindow Default Height

- `MainWindow.xaml`: `Height="600"` -> `Height="450"`
- `MinHeight="400"` unchanged
- Users with persisted window size in `settings.json` are unaffected

## 3. Add "Settings" to Tray Context Menu

Insert a "Settings" menu item in `SetupTrayIcon()` before "Open":

```
Status label
-----------
Connect
Mute
-----------
Settings    <- new
Open
Exit
```

The click handler reuses the existing `SettingsButton_Click` logic. The `SettingsWindow` dialog is modal via `ShowDialog()` with `Owner = this`.

## Approach

Single commit, Approach A — total change is small enough that splitting adds ceremony without value.
