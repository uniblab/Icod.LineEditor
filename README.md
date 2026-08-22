# Icod.LineEditor

Managed C#/.NET implementations of the classic line-oriented Unix editors `ed`, `red`, and `sed`.

This repository is the permanent home of the LineEditor family extracted from `Icod.CoreUtils` during Completion Gate G7.

## Projects

- `Icod.LineEditor.Ed.Shared` â€” the shared editor engine and capability boundary used by `ed` and `red`.
- `ed` / `Icod.LineEditor.Ed` â€” the standard line editor.
- `red` / `Icod.LineEditor.Red` â€” the restricted `ed` front end, retaining the same shared engine with restricted capabilities.
- `sed` / `Icod.LineEditor.Sed` â€” the stream editor. `sed` remains a separate engine and does not depend on Ed.Shared.

There is intentionally no general `Icod.LineEditor.Shared` project.

## Platform and toolchain

- .NET 10 (`net10.0`)
- C# 13
- Windows, Linux, and macOS
- Debug, Staging, and Release configurations
- Release builds treat warnings as errors except `CS1591`

Cross-suite infrastructure is consumed from the published `Icod.CommandFramework` package. The LineEditor repository has no source-tree dependency on `Icod.CoreUtils.Shared`.

## Build and test

```text
dotnet restore Icod.LineEditor.sln
dotnet build Icod.LineEditor.sln -c Staging --no-restore
dotnet test Icod.LineEditor.sln -c Staging --no-build
```

`build.cmd` and `build.sh` perform the same clean/restore/build/test sequence; pass a configuration name as the first argument to override the default `Staging` configuration.

GitHub Actions validates pull requests and pushes to `main` on `windows-latest`, `ubuntu-latest`, and `macos-latest`.

## Extraction provenance

The initial G7 import was reviewed against `Icod.CoreUtils` commit `4ee41aa1dc1c549f85efab6e5fa156a3dfc7271b`. Historical LineEditor architecture, audit, migration, and Batch 34 design notes are retained under `docs/history/`.

## License

GPL-3.0. See `LICENSE`.