# Dual-Mode: Toggle + PTT simultaneously active

## Context

Currently the app requires switching between Toggle and Push-to-Talk mode via a dropdown. Only one mode is active at a time. Users want both modes active simultaneously so either shortcut works at any time.

## Decision

Remove the mode dropdown. Both Toggle and PTT shortcuts are always active. A `_recordingSource` field tracks which shortcut started the recording so the other is ignored until recording stops.

## Behavior

- **No recording active**: both Toggle and PTT shortcuts work
- **Recording started via Toggle**: PTT-Down is ignored, PTT-Up is ignored, Toggle stops recording
- **Recording started via PTT**: Toggle is ignored, PTT-Up stops recording

## Changes

### MainWindow.xaml.cs

1. Replace `_isPttMode` (bool) with `_recordingSource` enum (`None`, `Toggle`, `Ptt`)
2. `OnToggleHotkey()`: skip if `_recordingSource == Ptt`, set `_recordingSource = Toggle` on start, `None` on stop
3. `OnPttKeyDown()`: skip if `_recordingSource == Toggle`, set `_recordingSource = Ptt`
4. `OnPttKeyUp()`: skip if `_recordingSource != Ptt`, set `_recordingSource = None`
5. Remove `ModeCombo_SelectionChanged` handler

### MainWindow.xaml

1. Remove ModeCombo dropdown and its label
2. Update info text to show both shortcuts: "F9 = Toggle | Win+L-Strg = Push-to-Talk"

### Settings

1. Remove `mode=` from SaveSettings
2. Ignore `mode=` in LoadSettings (backwards compatible)

### No changes needed

- `KeyboardHookService` — already fires both events independently
- `StartRecording()` / `StopRecording()` — unchanged
- Shortcut configuration fields — unchanged
