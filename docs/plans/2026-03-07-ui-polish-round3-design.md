# UI Polish Round 3 - Design

## 1. Tooltip Styling

Add an implicit `ToolTip` style in both `Dark.xaml` and `Light.xaml`:

- **Background:** `SurfaceBrush`, `CornerRadius="6"`
- **Foreground:** `TextBrush`
- **Border:** `BorderBrush`, 1px
- **Drop shadow:** `DropShadowEffect` (BlurRadius 8, Opacity 0.3)
- **Padding:** 8,5
- **FontSize:** 13px (matches app)
- Applied implicitly so all existing `ToolTip="..."` attributes work without changes

## 2. Tray Context Menu (WPF Popup)

Replace the WinForms `ContextMenuStrip` with a WPF borderless window:

- Intercept `NotifyIcon.MouseClick` (right-click), show a WPF `Window` (borderless, `Topmost`, `AllowsTransparency`) near the tray icon
- Theme brushes: `SurfaceBrush` background, `TextBrush` text, `AccentBrush` hover highlights
- Rounded corners, drop shadow matching tooltip style
- Close on click-outside or Escape
- Keep WinForms `NotifyIcon` for the icon itself, remove its `ContextMenuStrip`
- Same menu items as before: status label, connect/disconnect, mute, settings, export, history, about, open, exit

## 3. Animated Theme Transition (Overlay Dissolve)

Before swapping resource dictionaries in `ThemeManager`:

1. Capture `RenderTargetBitmap` of each visible window
2. Place as `Image` overlay (same size, topmost in visual tree)
3. Swap theme resources
4. Animate overlay `Opacity` 1 -> 0 over ~250ms
5. Remove overlay

Applies to all visible windows (MainWindow, SettingsWindow if open).

## 4. First-Run Wizard (Minimal)

A single `Window` shown on first launch (no `settings.json` exists):

- App logo/title at top
- Provider selection: Whisper (preselected) / Deepgram radio buttons or cards
- API key `TextBox` visible only when Deepgram is selected
- "Get Started" button saves settings, closes wizard, starts main app
- Detection: `SettingsService` exposes "is first run" flag (no settings file)
- Themed with Auto theme (default)
