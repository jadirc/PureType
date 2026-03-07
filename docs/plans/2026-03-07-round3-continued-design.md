# Round 3 Continued — Design

## 7. Audio Device Hot-Swap

- Listen for Windows device change events via MMDeviceEnumerator notification callback
- When a device is removed: stop recording, show toast "Microphone disconnected", refresh combo box
- When a device is added: refresh combo box, show toast "New microphone detected"
- No automatic switching — user picks manually
- Re-enumerate devices in AudioCaptureService, fire a DevicesChanged event consumed by MainWindow

## 8. Transcript Search (History Window)

- Add a TextBox search field above the session list in TranscriptHistoryWindow.xaml
- On text input (with ~300ms debounce): search all .txt files in transcripts folder for matching lines
- Filter the session list to only show files with matches
- In the preview panel: highlight matching text (bold or accent color)
- Empty search = show all sessions (current behavior)

## 10. Unit Tests

New test coverage for Round 2+3 code:
- ThemeManagerTests — verify resource dictionary swap, DetectSystemTheme logic
- SettingsServiceTests — add IsFirstRun tests (no file, with file, with legacy txt)
- SoundFeedbackTests — verify tone generation produces valid WAV bytes, MigrateName mapping
- TrayMenuWindow — UpdateState returns correct status text/color for all 4 states

## 11. Logging — Minimal Cleanup

- Add a LogLevel setting to AppSettings.Window (default: "Information")
- Add a combo in SettingsWindow: Debug / Information / Warning
- Apply via LoggingLevelSwitch in Serilog (runtime-changeable)
- Add missing structured properties where string interpolation is used

## 12. Auto-Update Check

- New static class UpdateChecker with CheckAsync() method
- Calls GitHub Releases API (repos/jadirc/VoiceDictation/releases/latest)
- Compares tag_name against current assembly version
- On startup: silent check, show toast if newer version found
- In AboutWindow: add "Check for Updates" button, shows result inline
- No auto-download — just notification + link to GitHub releases page

## 13. README Update

Add documentation for all Round 2+3 features:
- Dark/Light/Auto Theme (Catppuccin palettes)
- Mute function + shortcut
- Clipboard mode
- Themed tooltips, tray context menu
- Theme transition animation
- First-run wizard
- Audio device hot-swap
- Transcript search
- Auto-update check
