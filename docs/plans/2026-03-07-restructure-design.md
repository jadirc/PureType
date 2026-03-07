# Project Restructuring Design

## Goal

Restructure the VoiceDictation repository to follow the standard `src/` + `tests/` layout common in .NET open-source projects, and add an xUnit test project for service-layer unit tests.

## Directory Layout

```
VoiceDictation/
├── VoiceDictation.sln
├── global.json
├── .editorconfig
├── README.md, LICENSE, CHANGELOG.md, CLAUDE.md
├── .github/
├── docs/
├── release-please-config.json
├── .release-please-manifest.json
├── src/
│   └── VoiceDictation/
│       ├── VoiceDictation.csproj
│       ├── App.xaml(.cs)
│       ├── MainWindow.xaml(.cs)
│       ├── LogWindow.xaml(.cs)
│       ├── ReplacementsWindow.xaml(.cs)
│       ├── ToastWindow.xaml(.cs)
│       ├── GlobalUsings.cs
│       ├── Helpers/
│       ├── Services/
│       └── Resources/
└── tests/
    └── VoiceDictation.Tests/
        ├── VoiceDictation.Tests.csproj (xUnit, net8.0-windows)
        └── Services/
```

## What Moves

All source files (*.xaml, *.xaml.cs, *.cs, Services/, Helpers/, Resources/, VoiceDictation.csproj) move to `src/VoiceDictation/`.

## What Stays at Root

README.md, LICENSE, CHANGELOG.md, CLAUDE.md, .github/, docs/, .editorconfig, global.json, release-please configs.

## New Files

- `VoiceDictation.sln` at root (references both projects)
- `tests/VoiceDictation.Tests/VoiceDictation.Tests.csproj` (xUnit, project reference)

## Config Updates

- `release-please-config.json`: update component path to `src/VoiceDictation`
- `.github/workflows/ci.yml`: verify build commands work with .sln at root
- `CLAUDE.md`: add `dotnet test` to build commands

## Test Scope

Service-layer tests only (ReplacementService, VadService, LLM clients, DeepgramService JSON parsing). No ViewModel extraction or UI tests.

## Test Framework

xUnit with `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`.
