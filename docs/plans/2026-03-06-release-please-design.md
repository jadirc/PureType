# Release Automation with Release Please

## Context

VoiceDictation has no versioning or automated release process. A `release.yml` workflow exists that builds and publishes GitHub Releases when a `v*` tag is pushed, but tags are created manually. We want Conventional Commits to drive automatic version bumps, changelogs, and releases.

## Decision

Use Google's [Release Please](https://github.com/googleapis/release-please) GitHub Action to automate versioning and changelog generation. Release Please creates a "Release PR" that accumulates changes; merging the PR triggers tagging and the existing release workflow.

## Flow

```
Commits on master ──> Release Please Action runs
                           │
                           ▼
                      Release PR created/updated
                      (CHANGELOG.md + Version in .csproj)
                           │
                      Developer merges PR
                           │
                           ▼
                      Git tag v0.x.y created
                           │
                           ▼
                      Existing release.yml triggers
                      (build > ZIP > GitHub Release)
```

## Configuration

- **Release type:** `simple` (updates `<Version>` in `.csproj`)
- **Starting version:** `0.1.0` (pre-stable phase)
- **Versioning:** Standard SemVer — `feat:` bumps minor, `fix:` bumps patch, `BREAKING CHANGE` bumps major
- **Changelog language:** English
- **Branch:** `master` only, no pre-releases

## Components

### 1. `.github/workflows/release-please.yml`

New workflow that runs on every push to `master`. Calls the `googleapis/release-please-action@v4` action.

### 2. `release-please-config.json`

Root-level config file:
- Package name: `VoiceDictation`
- Release type: `simple`
- Extra files to update: `VoiceDictation.csproj` (the `<Version>` property)
- Changelog sections: Features, Bug Fixes

### 3. `.release-please-manifest.json`

Tracks current version. Starts at `0.0.0` so the first release becomes `0.1.0`.

### 4. `VoiceDictation.csproj`

Add `<Version>0.0.0</Version>` property. Release Please updates this on each release.

### 5. Existing workflows

- `release.yml` — unchanged, triggers on `v*` tags as before
- `ci.yml` — unchanged

## Commit-type mapping

| Prefix | Version bump | In changelog |
|---|---|---|
| `feat:` | Minor | Yes (Features) |
| `fix:` | Patch | Yes (Bug Fixes) |
| `perf:` | Patch | Yes (Performance) |
| `docs:` | None alone | No |
| `chore:` | None alone | No |
| `refactor:` | None alone | No |
| `BREAKING CHANGE` | Major | Yes |

Only `feat` and `fix` trigger a Release PR. Other types are bundled when a releasable commit is present.

## Files to create/modify

| File | Action |
|---|---|
| `.github/workflows/release-please.yml` | Create |
| `release-please-config.json` | Create |
| `.release-please-manifest.json` | Create |
| `VoiceDictation.csproj` | Add `<Version>0.0.0</Version>` |
