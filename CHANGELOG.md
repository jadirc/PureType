# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/), and this project adheres to [Semantic Versioning](https://semver.org/).

## [1.1.0](https://github.com/jadirc/VoiceDictation/compare/v1.0.1...v1.1.0) (2026-03-07)


### Features

* add About dialog with open source library credits ([5573119](https://github.com/jadirc/VoiceDictation/commit/5573119e82a50b166a0ff4f70375c379b762c612))
* add AI trigger key, API endpoint presets, and improved toasts ([f93593b](https://github.com/jadirc/VoiceDictation/commit/f93593b0494f7a5de5a7dac26c3f82008bf88690))
* add Auto theme option that follows Windows system theme ([7b2dd18](https://github.com/jadirc/VoiceDictation/commit/7b2dd18a07561afdd1bae59d3dd7c61a319cfe08))
* add auto-reconnect with exponential backoff to DeepgramService ([4f78b1b](https://github.com/jadirc/VoiceDictation/commit/4f78b1b09829302eb2b1abcc8a91e4f8220bef71))
* add auto-update check (startup + About dialog) ([9c661bb](https://github.com/jadirc/VoiceDictation/commit/9c661bbbeb9013ceeaa2f9f47fc7730f1d4249c3))
* add clipboard mode for text output ([9c22210](https://github.com/jadirc/VoiceDictation/commit/9c22210c92125096696443d5990f0fec54395de3))
* add configurable keyboard injector delay ([f7d329f](https://github.com/jadirc/VoiceDictation/commit/f7d329fcb823df87413f0a27f8ca3d82c1c0d183))
* add configurable log level with runtime switching ([c1705d8](https://github.com/jadirc/VoiceDictation/commit/c1705d8577e3d16f08e49e96ad67e6f37c0e3ba7))
* add configurable mute hotkey ([f16f3bf](https://github.com/jadirc/VoiceDictation/commit/f16f3bf148e43450d4289d5cc5af78d5461406fa))
* add connect/disconnect, mute, and status to tray menu ([37f4fea](https://github.com/jadirc/VoiceDictation/commit/37f4feaaad10a46caffb2d13c4476a891a31e5e9))
* add dark-themed scrollbar styles and Settings scrollbar spacing ([d6dbcb5](https://github.com/jadirc/VoiceDictation/commit/d6dbcb5a64340ff1b05c9c8ed6a510a0d05d14a4))
* add Dark/Light theme toggle with Catppuccin color palettes ([2dead43](https://github.com/jadirc/VoiceDictation/commit/2dead43829fbd46bc15e7964ddef3abb1eaf0c78))
* add descriptive tooltips to all Settings controls ([ef957c8](https://github.com/jadirc/VoiceDictation/commit/ef957c8cb560079b2d41c196b5113570913ce9d3))
* add device polling and error events to AudioCaptureService ([4a88ba4](https://github.com/jadirc/VoiceDictation/commit/4a88ba46826b5cf7d8fcaaed26e43b6449eaabd3))
* add first-run WelcomeWindow wizard for provider selection ([d584fcf](https://github.com/jadirc/VoiceDictation/commit/d584fcfc22abc9a6379c9dbf8da447db4aa9786d))
* add ILlmClient with OpenAI and Anthropic implementations ([a517d7d](https://github.com/jadirc/VoiceDictation/commit/a517d7d1bce2d5dd8c7c393e0cd8cca7f0d6ba2b))
* add LLM post-processing settings UI ([20732c1](https://github.com/jadirc/VoiceDictation/commit/20732c1a3dc7f1079e3e0a2992735eaa41545b8f))
* add LLM prompt presets (Correction, Summary, Translation, Email) ([19100f5](https://github.com/jadirc/VoiceDictation/commit/19100f5c415d1c4c51fd19ca717d93220a578062))
* add Open Settings File button to Settings dialog ([e9935f2](https://github.com/jadirc/VoiceDictation/commit/e9935f21752f0b7245c18eeb11fec103c54388cd))
* add overlay dissolve animation for theme transitions ([039612d](https://github.com/jadirc/VoiceDictation/commit/039612d5ea95ae3d488f3f989b2f008d12eb708b))
* add provider selection to Settings dialog ([47991a3](https://github.com/jadirc/VoiceDictation/commit/47991a391dec87dc4ba112277ef50e7387827797))
* add reconnect sound notification + update Whisper docs ([c2036c4](https://github.com/jadirc/VoiceDictation/commit/c2036c494c046a3beda7b1d8d6dfd4e1c09c4f0e))
* add replacements button to main window ([f38c1ca](https://github.com/jadirc/VoiceDictation/commit/f38c1ca7199cee98a5d2d75ea032600ba121aa5a))
* add ReplacementService for custom text replacements ([651acff](https://github.com/jadirc/VoiceDictation/commit/651acfff8329a4845d75aa7c440402553ce3968d))
* add ReplacementsWindow UI editor ([7e6187c](https://github.com/jadirc/VoiceDictation/commit/7e6187ca7ddedfe4c261daf0f0e34ea26a9afa58))
* add SettingsService with JSON load/save and txt migration ([b3d9338](https://github.com/jadirc/VoiceDictation/commit/b3d93381c77f9a5e64b53cfda68fa37d21e0a148))
* add SettingsWindow with grouped sections and Save/Cancel ([fdd5dc8](https://github.com/jadirc/VoiceDictation/commit/fdd5dc8478b842ede113f0652ad64936a7e35338))
* add themed ToolTip styles to Dark and Light themes ([5e29557](https://github.com/jadirc/VoiceDictation/commit/5e2955718cbc87205cbfd2451f954f56cec51ff1))
* add toast notifications for reconnect events ([87b9434](https://github.com/jadirc/VoiceDictation/commit/87b94349b45583098ea66dc990896e3ff3392401))
* add ToastWindow for recording state notifications ([cd040b2](https://github.com/jadirc/VoiceDictation/commit/cd040b28c133083e189cf8c51062dc7bb41aaafd))
* add transcript export with timestamps ([22c5809](https://github.com/jadirc/VoiceDictation/commit/22c5809c88de0b0e104d3105d51ebf1191dad6e0))
* add transcript history with auto-save and viewer ([424b138](https://github.com/jadirc/VoiceDictation/commit/424b138b44f1a9b65a9e23fa31cd0908b9d5cc81))
* add transcript search with debounce and highlighting ([fc39d7d](https://github.com/jadirc/VoiceDictation/commit/fc39d7d81ee2da108534b4a5356059622a58d730))
* auto-switch theme when Windows system theme changes ([288aa94](https://github.com/jadirc/VoiceDictation/commit/288aa94c9fb1013fd2da4a6cb33e6fa231886b81))
* change default shortcuts to Ctrl+Alt+X and Win+L-Ctrl ([1932f17](https://github.com/jadirc/VoiceDictation/commit/1932f1739ead3e1942f4170fbf6681e7ff48bcd5))
* create WPF TrayMenuWindow as themed tray context menu ([a44604d](https://github.com/jadirc/VoiceDictation/commit/a44604dcc0b9b3e92e0f110ab57ae115790f41f4))
* dynamic tray icon with colored status dot and contrast improvements ([bdbf702](https://github.com/jadirc/VoiceDictation/commit/bdbf702ef4dad72269e9a98301271f0ec7c1250d))
* improve settings dialog section headers and persist dialog size ([06af3f3](https://github.com/jadirc/VoiceDictation/commit/06af3f3004583a1fbe67e818eb995394e68f8d20))
* integrate LLM post-processing on recording stop ([367b3c4](https://github.com/jadirc/VoiceDictation/commit/367b3c42dfc5a8a476b199567ee5fda0c1abe965))
* integrate replacement service into transcript pipeline ([dffb788](https://github.com/jadirc/VoiceDictation/commit/dffb788d8f4a87fa3035ef6851e87c2a1c86724b))
* show API key and keywords fields only for Deepgram provider ([1107642](https://github.com/jadirc/VoiceDictation/commit/1107642e8e5463ef9e18f967ad07c3af7e6c4356))
* show reconnect status in UI during DeepgramService retry ([c979888](https://github.com/jadirc/VoiceDictation/commit/c979888f1bbf12c6b7425e7a76c4c552c467c746))
* show toast overlay on recording start/stop ([03e6aef](https://github.com/jadirc/VoiceDictation/commit/03e6aeffc3e0ca691e6526109518004e9e42fefc))
* wire audio device hot-swap detection in MainWindow ([d71cb80](https://github.com/jadirc/VoiceDictation/commit/d71cb807efa5cdc557f172dccd22e683e5d2509a))
* wire TrayIconManager to use WPF TrayMenuWindow instead of WinForms menu ([08d2dd7](https://github.com/jadirc/VoiceDictation/commit/08d2dd7157e6d7da68819aaf1cde4ba98b40c984))


### Bug Fixes

* limit VAD auto-stop to toggle mode and make transcript selectable ([b7ec647](https://github.com/jadirc/VoiceDictation/commit/b7ec647626ef3474a834ed6339bc09e93df58c4a))
* log warnings instead of silently swallowing settings load errors ([b75e704](https://github.com/jadirc/VoiceDictation/commit/b75e704f9d6fd16b14690d2545e11528c238751c))
* prevent app shutdown when About opened from tray + reduce Settings TextBox height ([4738722](https://github.com/jadirc/VoiceDictation/commit/47387225b11fdef8b29935588bedaf5095257521))
* prevent double-close crash in TrayMenuWindow ([5b3a72d](https://github.com/jadirc/VoiceDictation/commit/5b3a72d98cd43f8ec3f9fcb45096e7236fb2122b))
* prevent NullReferenceException when opening Settings dialog ([52a9eb0](https://github.com/jadirc/VoiceDictation/commit/52a9eb0e890cb9e8dbc283b183226fce149323f5))

## [1.0.1](https://github.com/jadirc/VoiceDictation/compare/v1.0.0...v1.0.1) (2026-03-06)


### Bug Fixes

* allow Win+modifier shortcut recording regardless of key press order ([ce8a842](https://github.com/jadirc/VoiceDictation/commit/ce8a84275eec2d1cb780b025e50574b1cb4750b9))

## 1.0.0 (2026-03-06)


### Bug Fixes

* correct GitHub username in README URLs ([df762f5](https://github.com/jadirc/VoiceDictation/commit/df762f5541c8297a2e2802d5c0901656c02cc1cb))
* integrate release build into Release Please workflow ([8417e8b](https://github.com/jadirc/VoiceDictation/commit/8417e8bed2b71372f0ec93084918fbd6c7dbc830))
* remove PublishTrimmed from self-contained build ([01345db](https://github.com/jadirc/VoiceDictation/commit/01345dbd13706f4faf2bf58f4f09d831065fe317))

## 1.0.0 (2026-03-06)


### Bug Fixes

* correct GitHub username in README URLs ([df762f5](https://github.com/jadirc/VoiceDictation/commit/df762f5541c8297a2e2802d5c0901656c02cc1cb))
* integrate release build into Release Please workflow ([8417e8b](https://github.com/jadirc/VoiceDictation/commit/8417e8bed2b71372f0ec93084918fbd6c7dbc830))

## 1.0.0 (2026-03-06)


### Bug Fixes

* correct GitHub username in README URLs ([df762f5](https://github.com/jadirc/VoiceDictation/commit/df762f5541c8297a2e2802d5c0901656c02cc1cb))

## [0.1.0] - 2026-03-05

### Added

- Real-time voice-to-text transcription using Deepgram Nova-2 via WebSocket
- Simulated keystroke injection into any focused window (Unicode SendInput)
- Terminal-aware mode: automatic clipboard paste for terminal windows (Windows Terminal, PowerShell, cmd, Warp, Alacritty)
- Toggle recording mode with configurable hotkey (default: F9)
- Push-to-Talk mode with configurable hotkey (default: Right Ctrl)
- Configurable keyboard shortcuts with support for modifier keys and Win+key chords
- Multi-language support: German, English, and automatic language detection
- Five signal tone presets for audio feedback on recording start/stop
- System tray integration with minimize-to-tray
- Auto-connect on startup when API key is saved
- Dark UI theme inspired by Catppuccin Mocha
- Built-in log viewer for debugging
- Settings persistence to %LOCALAPPDATA%\VoiceDictation\settings.txt
