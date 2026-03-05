# Public Release Setup — Design

## Goal

Prepare VoiceDictation for public release on GitHub as a community project with CI/CD, versioning, releases, and contributor-friendly templates.

## Decisions

### Versionierung

- **Tag-driven**: No version in csproj. GitHub Actions extracts version from git tag (`v1.0.0` -> `1.0.0`).
- Local builds without tag fall back to `0.0.0-dev`.
- Semantic Versioning (MAJOR.MINOR.PATCH).

### CI Workflow (`.github/workflows/ci.yml`)

- **Trigger:** Push to `master`, PRs against `master`.
- **Steps:** Checkout, Setup .NET 8, `dotnet restore`, `dotnet build --warnaserror`.
- Purpose: Fast feedback that the build is not broken.

### Release Workflow (`.github/workflows/release.yml`)

- **Trigger:** Push of tag matching `v*`.
- **Steps:**
  1. Extract version from tag.
  2. Build framework-dependent -> ZIP (`VoiceDictation-v1.2.0-win-x64.zip`).
  3. Build self-contained single-file + trimmed -> ZIP (`VoiceDictation-v1.2.0-win-x64-portable.zip`).
  4. Create GitHub Release with both ZIPs as assets.
  5. Auto-generated release notes from commits since last tag.

### Issue & PR Templates

- `.github/ISSUE_TEMPLATE/bug_report.md` — Bug with Steps to Reproduce, Expected/Actual.
- `.github/ISSUE_TEMPLATE/feature_request.md` — Feature with Use Case, Proposed Solution.
- `.github/pull_request_template.md` — Short checklist: What, Why, Tested?

### CHANGELOG

- `CHANGELOG.md` in Keep a Changelog format.
- Categories: Added, Changed, Fixed, Removed.
- Initial entry summarizes all existing features for v1.0.0.

### Additional Files

- `.editorconfig` — Consistent code style for contributors.

### Out of Scope (YAGNI)

- No Dependabot / Stale Bot.
- No Code of Conduct (can be added later).
- No automatic changelog generation.
- No installer / MSI (ZIP is sufficient).
- No FUNDING.yml (can be added later).

## Release Artifacts

| Asset | Description |
|---|---|
| `VoiceDictation-vX.Y.Z-win-x64.zip` | Framework-dependent, requires .NET 8 Runtime |
| `VoiceDictation-vX.Y.Z-win-x64-portable.zip` | Self-contained single-file, no runtime needed |
