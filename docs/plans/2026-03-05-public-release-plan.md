# Public Release Setup — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Prepare VoiceDictation for public GitHub release with CI/CD, versioning, release automation, contributor templates, and changelog.

**Architecture:** Tag-driven versioning with two GitHub Actions workflows (CI on push/PR, Release on tag push). Release builds both framework-dependent and self-contained ZIPs. Standard issue/PR templates and Keep a Changelog format.

**Tech Stack:** GitHub Actions, .NET 8, dotnet publish

---

### Task 1: Add .editorconfig

**Files:**
- Create: `.editorconfig`

**Step 1: Create .editorconfig**

```ini
root = true

[*]
indent_style = space
indent_size = 4
end_of_line = crlf
charset = utf-8
trim_trailing_whitespace = true
insert_final_newline = true

[*.{yml,yaml}]
indent_size = 2

[*.md]
trim_trailing_whitespace = false
```

**Step 2: Verify**

Run: `dotnet build`
Expected: Build succeeds, no new warnings.

**Step 3: Commit**

```bash
git add .editorconfig
git commit -m "chore: add .editorconfig for consistent code style"
```

---

### Task 2: CI Workflow

**Files:**
- Create: `.github/workflows/ci.yml`

**Step 1: Create CI workflow**

```yaml
name: CI

on:
  push:
    branches: [master]
  pull_request:
    branches: [master]

jobs:
  build:
    runs-on: windows-latest

    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 8.0.x

      - name: Restore
        run: dotnet restore

      - name: Build
        run: dotnet build --no-restore --configuration Release /p:TreatWarningsAsErrors=true
```

**Step 2: Validate YAML syntax**

Run: `python -c "import yaml; yaml.safe_load(open('.github/workflows/ci.yml'))"`
If python/yaml not available, visually inspect indentation.

**Step 3: Commit**

```bash
git add .github/workflows/ci.yml
git commit -m "ci: add build workflow for push and PR"
```

---

### Task 3: Release Workflow

**Files:**
- Create: `.github/workflows/release.yml`

**Step 1: Create Release workflow**

```yaml
name: Release

on:
  push:
    tags: ["v*"]

permissions:
  contents: write

jobs:
  release:
    runs-on: windows-latest

    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 8.0.x

      - name: Extract version from tag
        id: version
        shell: bash
        run: echo "VERSION=${GITHUB_REF_NAME#v}" >> "$GITHUB_OUTPUT"

      - name: Publish framework-dependent
        run: >
          dotnet publish
          -c Release
          -r win-x64
          --self-contained false
          -p:Version=${{ steps.version.outputs.VERSION }}
          -p:PublishSingleFile=true
          -o publish/framework-dependent

      - name: Publish self-contained portable
        run: >
          dotnet publish
          -c Release
          -r win-x64
          --self-contained true
          -p:Version=${{ steps.version.outputs.VERSION }}
          -p:PublishSingleFile=true
          -p:EnableCompressionInSingleFile=true
          -p:IncludeNativeLibrariesForSelfExtract=true
          -p:PublishTrimmed=true
          -p:TrimMode=partial
          -o publish/self-contained

      - name: Create ZIPs
        shell: pwsh
        run: |
          $tag = "${{ github.ref_name }}"
          Compress-Archive -Path publish/framework-dependent/* -DestinationPath "VoiceDictation-${tag}-win-x64.zip"
          Compress-Archive -Path publish/self-contained/* -DestinationPath "VoiceDictation-${tag}-win-x64-portable.zip"

      - name: Create GitHub Release
        uses: softprops/action-gh-release@v2
        with:
          generate_release_notes: true
          files: |
            VoiceDictation-*.zip
```

**Step 2: Validate YAML syntax**

Run: `python -c "import yaml; yaml.safe_load(open('.github/workflows/release.yml'))"`
If python/yaml not available, visually inspect indentation.

**Step 3: Commit**

```bash
git add .github/workflows/release.yml
git commit -m "ci: add release workflow with framework-dependent and portable builds"
```

---

### Task 4: Issue Templates

**Files:**
- Create: `.github/ISSUE_TEMPLATE/bug_report.md`
- Create: `.github/ISSUE_TEMPLATE/feature_request.md`

**Step 1: Create bug report template**

```markdown
---
name: Bug Report
about: Report a bug to help improve VoiceDictation
labels: bug
---

## Description

A clear description of the bug.

## Steps to Reproduce

1. ...
2. ...
3. ...

## Expected Behavior

What you expected to happen.

## Actual Behavior

What actually happened.

## Environment

- Windows version:
- .NET version:
- VoiceDictation version:
- Microphone:
```

**Step 2: Create feature request template**

```markdown
---
name: Feature Request
about: Suggest a new feature or improvement
labels: enhancement
---

## Use Case

Describe the problem or workflow this feature would address.

## Proposed Solution

Describe what you'd like to happen.

## Alternatives Considered

Any alternative solutions or workarounds you've considered.
```

**Step 3: Commit**

```bash
git add .github/ISSUE_TEMPLATE/
git commit -m "docs: add bug report and feature request issue templates"
```

---

### Task 5: Pull Request Template

**Files:**
- Create: `.github/pull_request_template.md`

**Step 1: Create PR template**

```markdown
## What

Brief description of the changes.

## Why

Why are these changes needed?

## Checklist

- [ ] Code builds without warnings (`dotnet build`)
- [ ] Tested manually
- [ ] Updated CHANGELOG.md (if user-facing change)
```

**Step 2: Commit**

```bash
git add .github/pull_request_template.md
git commit -m "docs: add pull request template"
```

---

### Task 6: CHANGELOG.md

**Files:**
- Create: `CHANGELOG.md`

**Step 1: Create CHANGELOG with initial release**

Review git log to summarize all features. The changelog should cover everything built so far as v1.0.0.

```markdown
# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/), and this project adheres to [Semantic Versioning](https://semver.org/).

## [1.0.0] - 2026-03-05

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
```

**Step 2: Commit**

```bash
git add CHANGELOG.md
git commit -m "docs: add CHANGELOG.md with v1.0.0 initial release"
```

---

### Task 7: Update README for release

**Files:**
- Modify: `README.md`

**Step 1: Update clone URL and add download section**

In README.md, replace `<your-username>` in the clone URL with the actual GitHub username. Add a "Download" section after "Prerequisites" pointing to the GitHub Releases page:

```markdown
## Download

Grab the latest release from the [Releases page](https://github.com/<username>/VoiceDictation/releases):

| Asset | Description |
|---|---|
| `VoiceDictation-vX.Y.Z-win-x64.zip` | Requires [.NET 8 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) installed |
| `VoiceDictation-vX.Y.Z-win-x64-portable.zip` | Standalone, no runtime needed (~70 MB) |
```

**Step 2: Commit**

```bash
git add README.md
git commit -m "docs: add download section and fix clone URL in README"
```

---

### Task 8: Final verification and tag

**Step 1: Full build check**

Run: `dotnet build --configuration Release /p:TreatWarningsAsErrors=true`
Expected: Build succeeds.

**Step 2: Review all new files**

Run: `git status && git log --oneline -10`
Verify all commits are clean and in order.

**Step 3: Tag for first release**

```bash
git tag v1.0.0
```

Note: Do NOT push the tag yet. The user should push when ready (`git push origin master --tags`).
