# Release Please Automation — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Automate versioning, changelog generation, and GitHub Releases using Release Please.

**Architecture:** Release Please GitHub Action runs on every push to `master`, creates/updates a Release PR with changelog and version bump. Merging the PR creates a `v*` tag, which triggers the existing `release.yml` workflow to build and publish binaries.

**Tech Stack:** GitHub Actions, googleapis/release-please-action@v4

---

### Task 1: Add Version property to .csproj

**Files:**
- Modify: `VoiceDictation.csproj:4` (inside first `<PropertyGroup>`)

**Step 1: Add `<Version>` element**

Add `<Version>0.0.0</Version>` as the first line inside the existing `<PropertyGroup>`:

```xml
  <PropertyGroup>
    <Version>0.0.0</Version>
    <OutputType>WinExe</OutputType>
```

Release Please will bump this value in the Release PR. The `release.yml` workflow already overrides it via `-p:Version=` at publish time, so both paths work.

**Step 2: Verify build still works**

Run: `dotnet build`
Expected: Build succeeds with 0 errors.

**Step 3: Commit**

```bash
git add VoiceDictation.csproj
git commit -m "chore: add Version property for Release Please"
```

---

### Task 2: Create Release Please config files

**Files:**
- Create: `release-please-config.json`
- Create: `.release-please-manifest.json`

**Step 1: Create `release-please-config.json`**

```json
{
  "$schema": "https://raw.githubusercontent.com/googleapis/release-please/main/schemas/config.json",
  "release-type": "simple",
  "packages": {
    ".": {
      "package-name": "VoiceDictation",
      "changelog-path": "CHANGELOG.md",
      "extra-files": [
        {
          "type": "xml",
          "path": "VoiceDictation.csproj",
          "xpath": "//Project/PropertyGroup/Version"
        }
      ]
    }
  }
}
```

Key details:
- `release-type: simple` — generic release type, creates tags and changelogs
- `extra-files` with `type: xml` — tells Release Please to update the `<Version>` element in the .csproj using XPath

**Step 2: Create `.release-please-manifest.json`**

```json
{
  ".": "0.0.0"
}
```

This tells Release Please the current version is `0.0.0`. The first releasable commit will bump to `0.1.0`.

**Step 3: Commit**

```bash
git add release-please-config.json .release-please-manifest.json
git commit -m "chore: add Release Please configuration"
```

---

### Task 3: Create Release Please workflow

**Files:**
- Create: `.github/workflows/release-please.yml`

**Step 1: Create the workflow file**

```yaml
name: Release Please

on:
  push:
    branches: [master]

permissions:
  contents: write
  pull-requests: write

jobs:
  release-please:
    runs-on: ubuntu-latest
    steps:
      - uses: googleapis/release-please-action@v4
        with:
          token: ${{ secrets.GITHUB_TOKEN }}
```

Key details:
- Runs on `ubuntu-latest` (no build needed, just PR management)
- Needs `contents: write` (to create tags) and `pull-requests: write` (to create/update the Release PR)
- No extra config needed — the action reads `release-please-config.json` and `.release-please-manifest.json` automatically

**Step 2: Commit**

```bash
git add .github/workflows/release-please.yml
git commit -m "ci: add Release Please workflow for automated releases"
```

---

### Task 4: Update release.yml to use Release Please changelog

**Files:**
- Modify: `.github/workflows/release.yml:58-63`

**Step 1: Replace `generate_release_notes` with changelog body**

The existing `release.yml` uses `generate_release_notes: true` which creates GitHub's auto-generated notes. Since Release Please now maintains a `CHANGELOG.md`, we should use that instead for consistency. But since `CHANGELOG.md` contains the full history, it's simpler to keep `generate_release_notes: true` and let GitHub generate the notes from commits between tags.

**Decision: No change needed.** The existing `generate_release_notes: true` works well alongside Release Please. The `CHANGELOG.md` serves as the persistent record, while GitHub Release notes show the diff for each release.

**Step 2: Commit (skip — no changes)**

No commit needed for this task.

---

### Task 5: Final verification and commit

**Step 1: Review all new/modified files**

Verify these files exist and look correct:
- `release-please-config.json` — config with `simple` type and `.csproj` XPath
- `.release-please-manifest.json` — `{".":" 0.0.0"}`
- `.github/workflows/release-please.yml` — workflow on push to master
- `VoiceDictation.csproj` — has `<Version>0.0.0</Version>`

**Step 2: Verify build**

Run: `dotnet build`
Expected: Build succeeds with 0 errors.

**Step 3: Verify workflow syntax**

Run: `cat .github/workflows/release-please.yml` and confirm valid YAML.

---

## After merging to master

Once this branch is merged to `master`, the Release Please workflow will run for the first time. Because there are `feat:` commits in the history, it will immediately create a Release PR proposing version `0.1.0` with a generated `CHANGELOG.md`.
