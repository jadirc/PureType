# Changelog

## [1.4.0](https://github.com/jadirc/PureType/compare/v1.3.0...v1.4.0) (2026-04-29)


### Features

* add auto-correction settings UI with toggle and provider fields ([92cc9d5](https://github.com/jadirc/PureType/commit/92cc9d5d2b621f653b0364c775313ac32f913a19))
* add auto-correction suppression and event in RecordingController ([0411ac1](https://github.com/jadirc/PureType/commit/0411ac126b800a0ac066f813898cc97fb67f87f1))
* add AutoCorrectionSettings data model ([0f8729e](https://github.com/jadirc/PureType/commit/0f8729e5979354997604f46eef955942f387de25))
* add diagnostic logging to RecordingController transcript handling ([84f7a9b](https://github.com/jadirc/PureType/commit/84f7a9bcf5fccfa565506ea5e32914e76fd2dcd2))
* add Mistral AI endpoint and fix reasoning model response parsing ([b06f105](https://github.com/jadirc/PureType/commit/b06f105aa55774632fca5b748935cf245a3421e2))
* add STT and AI timing fields to StatsService ([12fd7c6](https://github.com/jadirc/PureType/commit/12fd7c68db18a59180c3ce0c3e55b6f4cffaf92e))
* add TranscriptionTimed event to WhisperService with Stopwatch ([d337340](https://github.com/jadirc/PureType/commit/d3373409ba360e36978215938df506dc2c1cb67b))
* add Voxtral provider to MainWindow connect flow ([457b6cb](https://github.com/jadirc/PureType/commit/457b6cb246581ef8ed4f1b8f6e0412abf2aeed1e))
* add Voxtral provider UI to SettingsWindow ([e27f6fa](https://github.com/jadirc/PureType/commit/e27f6fab0cf838429f99bc8138362a4bff93e82e))
* add VoxtralModel to TranscriptionSettings ([01b184f](https://github.com/jadirc/PureType/commit/01b184fee1f32d0bf906cc7639fd49cae6a30602))
* add VoxtralService with WAV packaging and silence detection ([773c339](https://github.com/jadirc/PureType/commit/773c339fe282c5c8d5af0f22ba3291c207e9b63e))
* capture STT timing in RecordingController and show in toast ([3eeb08f](https://github.com/jadirc/PureType/commit/3eeb08f141a6a4eb867fafcb80a358cda9c3b482))
* handle AutoCorrectionRequested with provider fallback and error handling ([2fa4898](https://github.com/jadirc/PureType/commit/2fa4898a3f50d75cea92c5081570a5828f023cc7))
* log SendInput failures across all KeyboardInjector paths ([6296404](https://github.com/jadirc/PureType/commit/6296404da84160da25ebab6a507846f574e2d73d))
* show average STT and AI timing in StatsWindow ([d3630f1](https://github.com/jadirc/PureType/commit/d3630f1b4558120f9e742c1200f168c172d6aadf))
* show STT+AI timing in toasts and record AI time in stats ([279aa3c](https://github.com/jadirc/PureType/commit/279aa3cf82d8409ae640659ffdd8b7ebf70d92a8))


### Bug Fixes

* add 30s timeout to Whisper transcription to prevent CUDA hangs ([3ed64d1](https://github.com/jadirc/PureType/commit/3ed64d15440412a42dd908899ad30ffa39f278bc))
* avoid losing AI time when first AI call of the day arrives before any session ([1b5b994](https://github.com/jadirc/PureType/commit/1b5b99479454801bbf67e6058ebda75224d7d5d0))
* compress long silences in Voxtral audio to prevent transcript truncation ([a537883](https://github.com/jadirc/PureType/commit/a537883f08eac6871e1ab1ed0e9e99b365ecabc9))
* correct stale row number in MainWindow grid layout comment ([3f612b3](https://github.com/jadirc/PureType/commit/3f612b33262895539d0a9d4cacc30620ce690922))
* correct Voxtral model name, add auto-correction toggle to MainWindow ([e406343](https://github.com/jadirc/PureType/commit/e406343ce2b5850964457ab1c869bfd2848d0b58))
* fall back to SendInput when clipboard paste fails ([dd8bbdb](https://github.com/jadirc/PureType/commit/dd8bbdbe80b84ce455c984446f83abb1b91ac47a))
* grant write permissions to Claude Code Action workflows ([e935ec9](https://github.com/jadirc/PureType/commit/e935ec99b60bc4c0b277a15c423b8474808ca2c1))
* guard against race condition during audio device reinitialization ([69b4b0e](https://github.com/jadirc/PureType/commit/69b4b0ea6574bf2b1de343ec1115ee569e502985))
* prevent phantom day entries in RecordAiTime, add timing test coverage ([fc9967c](https://github.com/jadirc/PureType/commit/fc9967c6a6b3c2f44d9d30c3bf54f2676cfe807c))

## [1.4.0](https://github.com/jadirc/PureType/compare/v1.3.0...v1.4.0) (2026-04-10)


### Features

* add auto-correction settings UI with toggle and provider fields ([92cc9d5](https://github.com/jadirc/PureType/commit/92cc9d5))
* handle AutoCorrectionRequested with provider fallback and error handling ([2fa4898](https://github.com/jadirc/PureType/commit/2fa4898))
* add auto-correction suppression and event in RecordingController ([0411ac1](https://github.com/jadirc/PureType/commit/0411ac1))
* add AutoCorrectionSettings data model ([0f8729e](https://github.com/jadirc/PureType/commit/0f8729e))
* detect silent microphone and auto-reinitialize audio device ([36ab0f1](https://github.com/jadirc/PureType/commit/36ab0f1))


### Bug Fixes

* add 30s timeout to Whisper transcription to prevent CUDA hangs ([3ed64d1](https://github.com/jadirc/PureType/commit/3ed64d1))

## [1.3.0](https://github.com/jadirc/PureType/compare/v1.2.0...v1.3.0) (2026-04-03)


### Features

* add Clipboard AI shortcut to settings UI ([124ca66](https://github.com/jadirc/PureType/commit/124ca667fab85f49f108c204511e64440f9a09ac))
* add ClipboardAi channel to KeyboardHookService ([2d3289b](https://github.com/jadirc/PureType/commit/2d3289ba52fa3816782d9bba114a651a7fd643b3))
* add ClipboardAi shortcut setting ([c89caa8](https://github.com/jadirc/PureType/commit/c89caa832d22d0e0d476c308d1befc9715513fbb))
* add PromptPickerWindow for clipboard AI prompt selection ([0560df5](https://github.com/jadirc/PureType/commit/0560df52728fe00d21270df4762bcf7bed6221bc))
* add Whisper tuning settings and improve keyword boosting ([c0c185c](https://github.com/jadirc/PureType/commit/c0c185c4da816df2978e7abe302d85bf115663b8))
* integrate clipboard AI processing in MainWindow ([dcb505f](https://github.com/jadirc/PureType/commit/dcb505f60bc7f1c46fda12a6f562433598cdb880))


### Bug Fixes

* prevent LLM from treating dictated text as instructions, fix prompt picker focus ([49fb2f0](https://github.com/jadirc/PureType/commit/49fb2f01d3fa5274d6e55b89eeaebdb93d49f02e))
* prevent phantom recording after sleep by checking real Win key state ([65da7fb](https://github.com/jadirc/PureType/commit/65da7fb93014823fdf93fe3358ceb12760784868))
* prevent start tone capture and Whisper silence hallucinations ([1f09e50](https://github.com/jadirc/PureType/commit/1f09e505eb44464aaf4293b59c5c6fdb95e8ba38))
* raise silence detection threshold and require 2 speech chunks ([dc931e6](https://github.com/jadirc/PureType/commit/dc931e6cd4ce4845fa7ef94e4218375ac47736f4))
* reset hook when ClipboardAi shortcut cleared, remove duplicate toast ([a479c71](https://github.com/jadirc/PureType/commit/a479c7141d9d7c35c150ce33aaa39a67b7f21349))
* resolve Whisper native library loading in single-file publish ([715aa44](https://github.com/jadirc/PureType/commit/715aa44382cea0cba673d70b7b4a02e6e1a24357))
* use per-chunk RMS for silence detection to avoid dropping short recordings ([2e43e7c](https://github.com/jadirc/PureType/commit/2e43e7c988f4ed567ad60c055b167c6fc64fde68))

## [1.2.0](https://github.com/jadirc/PureType/compare/v1.1.0...v1.2.0) (2026-03-11)


### Features

* add real-time search box to settings dialog ([6f9b405](https://github.com/jadirc/PureType/commit/6f9b4058c9e650d854ba391f492eac61f48624c8))
* add search tests, escape-to-clear, LLM panel searchable ([c2d518b](https://github.com/jadirc/PureType/commit/c2d518ba2220ba8e9701a8a8fa529b384ede64b1))
* add Whisper keyword prompting, LLM profile switcher, fix INPUT MODE styling ([9cc9029](https://github.com/jadirc/PureType/commit/9cc9029d442e6986345af90b31f09b9487d628c3))
* smaller search box with red clear button ([95f49d3](https://github.com/jadirc/PureType/commit/95f49d30c25cce805f07b284acb2482afdd9bb29))

## [1.1.0](https://github.com/jadirc/PureType/compare/v1.0.0...v1.1.0) (2026-03-09)


### Features

* add 'Show floating status overlay' checkbox to settings ([36ace72](https://github.com/jadirc/PureType/commit/36ace72bfa24f52e26840ef0977797f45f8a0006))
* add ApplyAutoCapitalize static helper with tests ([d1b6704](https://github.com/jadirc/PureType/commit/d1b670423775cef875499d651345cf80039c903a))
* add AutoCapitalize setting to AudioSettings (default: true) ([ad9d9c9](https://github.com/jadirc/PureType/commit/ad9d9c9c633810c918306f243ed437530b61f208))
* add CodeFormatter with camelCase support and tests ([7dcefd9](https://github.com/jadirc/PureType/commit/7dcefd9829be67def844f916baec3023ec8e1055))
* add Language combo to MainWindow, document all new features in README ([08f2810](https://github.com/jadirc/PureType/commit/08f281070f8a9649e78197bf4fee4b6be2b487bb))
* add Language Switch shortcut recorder to Settings UI ([08aa525](https://github.com/jadirc/PureType/commit/08aa5253cbbb125f285e61586fce85cc9c84ce81))
* add LanguageSwitch shortcut setting (default: empty/disabled) ([d5f0a47](https://github.com/jadirc/PureType/commit/d5f0a474657fdb7fe375520dfd8de433b66f4d47))
* add LanguageSwitchPressed event to KeyboardHookService ([bec760b](https://github.com/jadirc/PureType/commit/bec760bff237000beff6b2255b32b60c51b1d1bb))
* add OverlayLeft/OverlayTop settings to WindowSettings ([7fc4199](https://github.com/jadirc/PureType/commit/7fc41997a872f37cae381b572374fe58f3c57c12))
* add SetLanguageAsync default method to ITranscriptionProvider ([18baf12](https://github.com/jadirc/PureType/commit/18baf12566da8c2674de4a3b53ac6f54e8170a15))
* add SetLanguageAsync to WhisperService for instant language switching ([f1d0ff4](https://github.com/jadirc/PureType/commit/f1d0ff4f063b040bbc98f528b2f4edb64ba8f0fb))
* add ShowOverlay setting to WindowSettings (default: true) ([1369398](https://github.com/jadirc/PureType/commit/1369398c75d0a4ebfe7d4151fd8f87345e20f4ec))
* add Statistics button to MainWindow and tray menu ([7679240](https://github.com/jadirc/PureType/commit/76792409c5f9a1ab5d038d42cebc2e974203d3e4))
* add stats summary line to MainWindow ([500729c](https://github.com/jadirc/PureType/commit/500729c992e83956a3cd987ca9be96f17ab61a15))
* add StatsService with daily tracking and persistence ([cf07239](https://github.com/jadirc/PureType/commit/cf072390d00c56d551c23bf6af971ef565591249))
* add StatsWindow with today/all-time summary and 30-day history ([ecfb7ca](https://github.com/jadirc/PureType/commit/ecfb7caade9acc8b5835cdb4d9e8d7772dd28a6e))
* add StatusOverlayWindow with non-activating top-center pill ([cb3f109](https://github.com/jadirc/PureType/commit/cb3f1090ce45c2cbd9f37badfa2432071815a792))
* integrate StatusOverlayWindow into MainWindow lifecycle ([f9ac0ab](https://github.com/jadirc/PureType/commit/f9ac0ab91e42166ee549903964718534bbd5ea64))
* make overlay draggable with double-click reset and middle-click hide ([3b840d9](https://github.com/jadirc/PureType/commit/3b840d90f259a8a0e8e8d5c8e82e4ff76955c701))
* toggle overlay at runtime when setting changes ([b6f04c5](https://github.com/jadirc/PureType/commit/b6f04c57c11821fb03e37ed7455d8317bb7b0a21))
* wire auto-capitalize into transcript pipeline ([6c7ddc3](https://github.com/jadirc/PureType/commit/6c7ddc39bd27eeb2858fdad3a45c04eb17e67667))
* wire CodeFormatter into transcript pipeline ([72eb58a](https://github.com/jadirc/PureType/commit/72eb58ae9b270c22db87cea0a15b2bace2aa25f6))
* wire language quick-switch hotkey into MainWindow ([3108242](https://github.com/jadirc/PureType/commit/3108242c5bb905286201b42e2597b216ae47f093))
* wire overlay drag/hide events into MainWindow with position persistence ([536a784](https://github.com/jadirc/PureType/commit/536a7847e21efae06dc093afe711397cfd0be80d))
* wire StatsService into RecordingController with StatsUpdated event ([d45284f](https://github.com/jadirc/PureType/commit/d45284fa9be8051931c8089f92402538a9092507))


### Bug Fixes

* apply code review improvements across features 1-5 ([7f2a62a](https://github.com/jadirc/PureType/commit/7f2a62a3a785cd9392e9c7f9d9e9282be6e2bdcf))

## 1.0.0 (2026-03-08)


### Features

* add About dialog with open source library credits ([5573119](https://github.com/jadirc/PureType/commit/5573119e82a50b166a0ff4f70375c379b762c612))
* add AI trigger key, API endpoint presets, and improved toasts ([f93593b](https://github.com/jadirc/PureType/commit/f93593b0494f7a5de5a7dac26c3f82008bf88690))
* add Auto theme option that follows Windows system theme ([7b2dd18](https://github.com/jadirc/PureType/commit/7b2dd18a07561afdd1bae59d3dd7c61a319cfe08))
* add auto-reconnect with exponential backoff to DeepgramService ([4f78b1b](https://github.com/jadirc/PureType/commit/4f78b1b09829302eb2b1abcc8a91e4f8220bef71))
* add auto-update check (startup + About dialog) ([9c661bb](https://github.com/jadirc/PureType/commit/9c661bbbeb9013ceeaa2f9f47fc7730f1d4249c3))
* add clipboard mode for text output ([9c22210](https://github.com/jadirc/PureType/commit/9c22210c92125096696443d5990f0fec54395de3))
* add configurable keyboard injector delay ([f7d329f](https://github.com/jadirc/PureType/commit/f7d329fcb823df87413f0a27f8ca3d82c1c0d183))
* add configurable log level with runtime switching ([c1705d8](https://github.com/jadirc/PureType/commit/c1705d8577e3d16f08e49e96ad67e6f37c0e3ba7))
* add configurable mute hotkey ([f16f3bf](https://github.com/jadirc/PureType/commit/f16f3bf148e43450d4289d5cc5af78d5461406fa))
* add connect/disconnect, mute, and status to tray menu ([37f4fea](https://github.com/jadirc/PureType/commit/37f4feaaad10a46caffb2d13c4476a891a31e5e9))
* add dark-themed scrollbar styles and Settings scrollbar spacing ([d6dbcb5](https://github.com/jadirc/PureType/commit/d6dbcb5a64340ff1b05c9c8ed6a510a0d05d14a4))
* add Dark/Light theme toggle with Catppuccin color palettes ([2dead43](https://github.com/jadirc/PureType/commit/2dead43829fbd46bc15e7964ddef3abb1eaf0c78))
* add descriptive tooltips to all Settings controls ([ef957c8](https://github.com/jadirc/PureType/commit/ef957c8cb560079b2d41c196b5113570913ce9d3))
* add device polling and error events to AudioCaptureService ([4a88ba4](https://github.com/jadirc/PureType/commit/4a88ba46826b5cf7d8fcaaed26e43b6449eaabd3))
* add first-run WelcomeWindow wizard for provider selection ([d584fcf](https://github.com/jadirc/PureType/commit/d584fcfc22abc9a6379c9dbf8da447db4aa9786d))
* add ILlmClient with OpenAI and Anthropic implementations ([a517d7d](https://github.com/jadirc/PureType/commit/a517d7d1bce2d5dd8c7c393e0cd8cca7f0d6ba2b))
* add LLM post-processing settings UI ([20732c1](https://github.com/jadirc/PureType/commit/20732c1a3dc7f1079e3e0a2992735eaa41545b8f))
* add LLM prompt presets (Correction, Summary, Translation, Email) ([19100f5](https://github.com/jadirc/PureType/commit/19100f5c415d1c4c51fd19ca717d93220a578062))
* add Open Settings File button to Settings dialog ([e9935f2](https://github.com/jadirc/PureType/commit/e9935f21752f0b7245c18eeb11fec103c54388cd))
* add overlay dissolve animation for theme transitions ([039612d](https://github.com/jadirc/PureType/commit/039612d5ea95ae3d488f3f989b2f008d12eb708b))
* add provider selection to Settings dialog ([47991a3](https://github.com/jadirc/PureType/commit/47991a391dec87dc4ba112277ef50e7387827797))
* add reconnect sound notification + update Whisper docs ([c2036c4](https://github.com/jadirc/PureType/commit/c2036c494c046a3beda7b1d8d6dfd4e1c09c4f0e))
* add replacements button to main window ([f38c1ca](https://github.com/jadirc/PureType/commit/f38c1ca7199cee98a5d2d75ea032600ba121aa5a))
* add ReplacementService for custom text replacements ([651acff](https://github.com/jadirc/PureType/commit/651acfff8329a4845d75aa7c440402553ce3968d))
* add ReplacementsWindow UI editor ([7e6187c](https://github.com/jadirc/PureType/commit/7e6187ca7ddedfe4c261daf0f0e34ea26a9afa58))
* add SettingsService with JSON load/save and txt migration ([b3d9338](https://github.com/jadirc/PureType/commit/b3d93381c77f9a5e64b53cfda68fa37d21e0a148))
* add SettingsWindow with grouped sections and Save/Cancel ([fdd5dc8](https://github.com/jadirc/PureType/commit/fdd5dc8478b842ede113f0652ad64936a7e35338))
* add themed ToolTip styles to Dark and Light themes ([5e29557](https://github.com/jadirc/PureType/commit/5e2955718cbc87205cbfd2451f954f56cec51ff1))
* add three input modes (Type, Paste, Copy) with main window selector ([44ffbeb](https://github.com/jadirc/PureType/commit/44ffbeb25aa99dbec2dced09ba4131e9b39bf6b2))
* add toast notifications for reconnect events ([87b9434](https://github.com/jadirc/PureType/commit/87b94349b45583098ea66dc990896e3ff3392401))
* add ToastWindow for recording state notifications ([cd040b2](https://github.com/jadirc/PureType/commit/cd040b28c133083e189cf8c51062dc7bb41aaafd))
* add transcript export with timestamps ([22c5809](https://github.com/jadirc/PureType/commit/22c5809c88de0b0e104d3105d51ebf1191dad6e0))
* add transcript history with auto-save and viewer ([424b138](https://github.com/jadirc/PureType/commit/424b138b44f1a9b65a9e23fa31cd0908b9d5cc81))
* add transcript search with debounce and highlighting ([fc39d7d](https://github.com/jadirc/PureType/commit/fc39d7d81ee2da108534b4a5356059622a58d730))
* add Whisper model download step to setup wizard ([b43f8ab](https://github.com/jadirc/PureType/commit/b43f8ab3f06bba7cbaaa2d9a726c0ece3bb9fa81))
* auto-switch theme when Windows system theme changes ([288aa94](https://github.com/jadirc/PureType/commit/288aa94c9fb1013fd2da4a6cb33e6fa231886b81))
* change default shortcuts to Ctrl+Alt+X and Win+L-Ctrl ([1932f17](https://github.com/jadirc/PureType/commit/1932f1739ead3e1942f4170fbf6681e7ff48bcd5))
* create WPF TrayMenuWindow as themed tray context menu ([a44604d](https://github.com/jadirc/PureType/commit/a44604dcc0b9b3e92e0f110ab57ae115790f41f4))
* dynamic tray icon with colored status dot and contrast improvements ([bdbf702](https://github.com/jadirc/PureType/commit/bdbf702ef4dad72269e9a98301271f0ec7c1250d))
* emit VK_RETURN for newline characters in keyboard injection ([249ef5c](https://github.com/jadirc/PureType/commit/249ef5cf826b23d3b359dcc6aeb5655862354150))
* improve settings dialog section headers and persist dialog size ([06af3f3](https://github.com/jadirc/PureType/commit/06af3f3004583a1fbe67e818eb995394e68f8d20))
* integrate LLM post-processing on recording stop ([367b3c4](https://github.com/jadirc/PureType/commit/367b3c42dfc5a8a476b199567ee5fda0c1abe965))
* integrate replacement service into transcript pipeline ([dffb788](https://github.com/jadirc/PureType/commit/dffb788d8f4a87fa3035ef6851e87c2a1c86724b))
* prevent multiple app instances via named mutex ([6b7d558](https://github.com/jadirc/PureType/commit/6b7d55809ed2aed686f845a5a546e9871639feca))
* replace single AI trigger key with named prompt library ([1fc4a98](https://github.com/jadirc/PureType/commit/1fc4a981b768b40b4bfb8f59b1df8f684c15708f))
* show API key and keywords fields only for Deepgram provider ([1107642](https://github.com/jadirc/PureType/commit/1107642e8e5463ef9e18f967ad07c3af7e6c4356))
* show reconnect status in UI during DeepgramService retry ([c979888](https://github.com/jadirc/PureType/commit/c979888f1bbf12c6b7425e7a76c4c552c467c746))
* show toast overlay on recording start/stop ([03e6aef](https://github.com/jadirc/PureType/commit/03e6aeffc3e0ca691e6526109518004e9e42fefc))
* smart space handling for replacement keywords ([ab7476d](https://github.com/jadirc/PureType/commit/ab7476d68777abc07256914964fe37e43134273a))
* switch replacement rules from custom txt to JSON format ([fdc2e26](https://github.com/jadirc/PureType/commit/fdc2e26e7408f7c927445f9454dc2b5c258bf004))
* use word-boundary matching for text replacements ([b2358da](https://github.com/jadirc/PureType/commit/b2358dab20f4d3eb2485d3ec70e98518d7916004))
* wire audio device hot-swap detection in MainWindow ([d71cb80](https://github.com/jadirc/PureType/commit/d71cb807efa5cdc557f172dccd22e683e5d2509a))
* wire TrayIconManager to use WPF TrayMenuWindow instead of WinForms menu ([08d2dd7](https://github.com/jadirc/PureType/commit/08d2dd7157e6d7da68819aaf1cde4ba98b40c984))


### Bug Fixes

* allow Win+modifier shortcut recording regardless of key press order ([ce8a842](https://github.com/jadirc/PureType/commit/ce8a84275eec2d1cb780b025e50574b1cb4750b9))
* correct GitHub username in README URLs ([df762f5](https://github.com/jadirc/PureType/commit/df762f5541c8297a2e2802d5c0901656c02cc1cb))
* ensure AppData directory exists before FileSystemWatcher + update screenshot ([523ef52](https://github.com/jadirc/PureType/commit/523ef52d780ad8f422032fc614bc59583639082e))
* integrate release build into Release Please workflow ([8417e8b](https://github.com/jadirc/PureType/commit/8417e8bed2b71372f0ec93084918fbd6c7dbc830))
* limit VAD auto-stop to toggle mode and make transcript selectable ([b7ec647](https://github.com/jadirc/PureType/commit/b7ec647626ef3474a834ed6339bc09e93df58c4a))
* log warnings instead of silently swallowing settings load errors ([b75e704](https://github.com/jadirc/PureType/commit/b75e704f9d6fd16b14690d2545e11528c238751c))
* prevent app shutdown when About opened from tray + reduce Settings TextBox height ([4738722](https://github.com/jadirc/PureType/commit/47387225b11fdef8b29935588bedaf5095257521))
* prevent double-close crash in TrayMenuWindow ([5b3a72d](https://github.com/jadirc/PureType/commit/5b3a72d98cd43f8ec3f9fcb45096e7236fb2122b))
* prevent NullReferenceException when opening Settings dialog ([52a9eb0](https://github.com/jadirc/PureType/commit/52a9eb0e890cb9e8dbc283b183226fce149323f5))
* remove framework-dependent build from release workflow ([f1fe53b](https://github.com/jadirc/PureType/commit/f1fe53b4e380a198b0c8f0c730476a0b79165f4c))
* remove PublishTrimmed from self-contained build ([01345db](https://github.com/jadirc/PureType/commit/01345dbd13706f4faf2bf58f4f09d831065fe317))
* reset version to 0.0.0 for clean v1.0.0 release ([f1812e3](https://github.com/jadirc/PureType/commit/f1812e35e80171270594c7c07b8ffe547fd1e0ad))
* reset version to 0.0.0 for clean v1.0.0 release ([287475f](https://github.com/jadirc/PureType/commit/287475f4165f912c16a6b6b9b8f538d63c8e4521))
* reset version to 0.0.0 for clean v1.0.0 release ([198a9c0](https://github.com/jadirc/PureType/commit/198a9c0a5347a0095170f85e5ac30992e7ac969f))
* simplify README download section to prevent staleness ([9292a2b](https://github.com/jadirc/PureType/commit/9292a2ba6600a83a3ae3e09405b944e3d4a36484))

## Changelog
