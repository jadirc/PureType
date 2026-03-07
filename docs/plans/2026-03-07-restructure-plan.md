# Project Restructuring Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Move all source files into `src/VoiceDictation/`, add an xUnit test project in `tests/VoiceDictation.Tests/`, create a solution file, and update all configs.

**Architecture:** Standard .NET `src/` + `tests/` layout with a root `.sln` file. The main WPF app project stays unchanged except for its location. A new xUnit test project references the app project for service-layer testing.

**Tech Stack:** .NET 8, xUnit 2.x, WPF, NAudio, Whisper.net

---

### Task 1: Create directory structure and move source files

**Files:**
- Create: `src/VoiceDictation/` (directory)
- Move: all source files into it

**Step 1: Create the src directory**

```bash
mkdir -p src/VoiceDictation
```

**Step 2: Move all source files using git mv**

```bash
git mv VoiceDictation.csproj src/VoiceDictation/
git mv App.xaml src/VoiceDictation/
git mv App.xaml.cs src/VoiceDictation/
git mv GlobalUsings.cs src/VoiceDictation/
git mv MainWindow.xaml src/VoiceDictation/
git mv MainWindow.xaml.cs src/VoiceDictation/
git mv LogWindow.xaml src/VoiceDictation/
git mv LogWindow.xaml.cs src/VoiceDictation/
git mv ReplacementsWindow.xaml src/VoiceDictation/
git mv ReplacementsWindow.xaml.cs src/VoiceDictation/
git mv ToastWindow.xaml src/VoiceDictation/
git mv ToastWindow.xaml.cs src/VoiceDictation/
git mv Services src/VoiceDictation/
git mv Helpers src/VoiceDictation/
git mv Resources src/VoiceDictation/
```

**Step 3: Verify the move**

```bash
ls src/VoiceDictation/
```

Expected: all `.xaml`, `.cs` files plus `Services/`, `Helpers/`, `Resources/` directories and `VoiceDictation.csproj`.

**Step 4: Commit**

```bash
git add -A
git commit -m "refactor: move source files to src/VoiceDictation"
```

---

### Task 2: Create the solution file

**Files:**
- Create: `VoiceDictation.sln` (at repo root)

**Step 1: Create the .sln and add the main project**

```bash
dotnet new sln --name VoiceDictation
dotnet sln add src/VoiceDictation/VoiceDictation.csproj
```

**Step 2: Verify build works**

```bash
dotnet build
```

Expected: successful build with 0 errors.

**Step 3: Commit**

```bash
git add VoiceDictation.sln
git commit -m "build: add solution file"
```

---

### Task 3: Create the xUnit test project

**Files:**
- Create: `tests/VoiceDictation.Tests/VoiceDictation.Tests.csproj`

**Step 1: Create test project**

```bash
mkdir -p tests/VoiceDictation.Tests
dotnet new xunit -o tests/VoiceDictation.Tests --framework net8.0-windows
```

**Step 2: Add project reference to the main app**

```bash
dotnet add tests/VoiceDictation.Tests/VoiceDictation.Tests.csproj reference src/VoiceDictation/VoiceDictation.csproj
```

**Step 3: Add test project to the solution**

```bash
dotnet sln add tests/VoiceDictation.Tests/VoiceDictation.Tests.csproj
```

**Step 4: Clean up the generated test file**

The `dotnet new xunit` template creates a `UnitTest1.cs` — delete it and create a proper placeholder:

Delete `tests/VoiceDictation.Tests/UnitTest1.cs`.

Create `tests/VoiceDictation.Tests/Services/ReplacementServiceTests.cs`:

```csharp
using VoiceDictation.Services;

namespace VoiceDictation.Tests.Services;

public class ReplacementServiceTests
{
    [Fact]
    public void Placeholder_test_to_verify_project_builds()
    {
        Assert.True(true);
    }
}
```

**Step 5: Verify tests run**

```bash
dotnet test
```

Expected: 1 test passed.

**Step 6: Commit**

```bash
git add -A
git commit -m "test: add xUnit test project with project reference"
```

---

### Task 4: Update CI workflow

**Files:**
- Modify: `.github/workflows/ci.yml`

**Step 1: Add test step to ci.yml**

After the existing `Build` step, add:

```yaml
      - name: Test
        run: dotnet test --no-restore --configuration Release
```

The `dotnet restore`, `dotnet build` commands already work from root because the `.sln` is there. No path changes needed for those.

**Step 2: Commit**

```bash
git add .github/workflows/ci.yml
git commit -m "ci: add test step to CI workflow"
```

---

### Task 5: Update release-please config

**Files:**
- Modify: `release-please-config.json`

**Step 1: Update the csproj path in release-please-config.json**

Change the `extra-files` path from `VoiceDictation.csproj` to `src/VoiceDictation/VoiceDictation.csproj`:

```json
{
  "$schema": "https://raw.githubusercontent.com/googleapis/release-please/main/schemas/config.json",
  "release-type": "simple",
  "packages": {
    ".": {
      "package-name": "VoiceDictation",
      "include-component-in-tag": false,
      "changelog-path": "CHANGELOG.md",
      "extra-files": [
        {
          "type": "xml",
          "path": "src/VoiceDictation/VoiceDictation.csproj",
          "xpath": "//Project/PropertyGroup/Version"
        }
      ]
    }
  }
}
```

**Step 2: Commit**

```bash
git add release-please-config.json
git commit -m "build: update release-please csproj path"
```

---

### Task 6: Update release workflow publish commands

**Files:**
- Modify: `.github/workflows/release-please.yml`

**Step 1: Add explicit project path to publish commands**

Both `dotnet publish` commands need the project path since the csproj is no longer at root. Add `src/VoiceDictation/VoiceDictation.csproj` as the first argument to both publish steps:

For "Publish framework-dependent":
```yaml
      - name: Publish framework-dependent
        run: >
          dotnet publish src/VoiceDictation/VoiceDictation.csproj
          -c Release
          -r win-x64
          --self-contained false
          -p:Version=${{ needs.release-please.outputs.version }}
          -p:PublishSingleFile=true
          -p:DebugType=none
          -o publish/framework-dependent
```

For "Publish self-contained portable":
```yaml
      - name: Publish self-contained portable
        run: >
          dotnet publish src/VoiceDictation/VoiceDictation.csproj
          -c Release
          -r win-x64
          --self-contained true
          -p:Version=${{ needs.release-please.outputs.version }}
          -p:PublishSingleFile=true
          -p:EnableCompressionInSingleFile=true
          -p:IncludeNativeLibrariesForSelfExtract=true
          -p:DebugType=none
          -o publish/self-contained
```

**Step 2: Commit**

```bash
git add .github/workflows/release-please.yml
git commit -m "build: update publish paths in release workflow"
```

---

### Task 7: Update CLAUDE.md

**Files:**
- Modify: `CLAUDE.md`

**Step 1: Update build commands section**

Replace the Build & Run section with:

```markdown
## Build & Run

```bash
dotnet build
dotnet test
dotnet run --project src/VoiceDictation
```

A `.sln` file exists at the repo root. `dotnet build` and `dotnet test` operate on it automatically.
```

**Step 2: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: update CLAUDE.md build commands for new structure"
```

---

### Task 8: Final verification

**Step 1: Verify full build**

```bash
dotnet build
```

Expected: both projects build successfully.

**Step 2: Verify tests pass**

```bash
dotnet test
```

Expected: 1 test passed.

**Step 3: Verify project runs**

```bash
dotnet run --project src/VoiceDictation
```

Expected: application starts normally.

**Step 4: Verify git status is clean**

```bash
git status
```

Expected: clean working tree, no untracked files.
