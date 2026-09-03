# Icod.LineEditor

![Icod.LineEditor](https://raw.githubusercontent.com/uniblab/Icod.LineEditor/v1.1.1/LineEditor.banner.png)

[![PR Staging build](https://github.com/uniblab/Icod.LineEditor/actions/workflows/pull-request.yaml/badge.svg?event=pull_request)](https://github.com/uniblab/Icod.LineEditor/actions/workflows/pull-request.yaml)
[![Main Release validation](https://github.com/uniblab/Icod.LineEditor/actions/workflows/main.yaml/badge.svg?branch=main)](https://github.com/uniblab/Icod.LineEditor/actions/workflows/main.yaml)

`Icod.LineEditor` is a managed .NET implementation of the classic Unix line-editing family: `ed`, its restricted form `red`, and the `sed` stream editor.

The repository targets .NET 10 and C# 13 and is designed for Windows, Linux, and macOS. The editors use managed code for their editing, regular-expression, record-processing, and command orchestration behavior rather than invoking the host system's `ed`, `red`, or `sed` executable.

This repository is the permanent home of the LineEditor family extracted from the former multi-suite `Icod.CoreUtils` development repository during Completion Gate G7.

## Included commands

| Command | Purpose |
|---|---|
| [`lineeditor`](lineeditor/README.md) | Distribution router that directly multiplexes the managed `ed`, `red`, and `sed` commands. |
| [`ed`](ed/README.md) | Interactive and scriptable line-oriented text editor, following the GNU ed 1.22.5 compatibility profile. |
| [`red`](red/README.md) | Permanently restricted `ed` profile that disables shell execution and limits pathname syntax. |
| [`sed`](sed/README.md) | Non-interactive stream editor with GNU-style addressing, substitutions, branching, hold space, in-place editing, sandboxing, and NUL-delimited records. |

The router supports:

```text
lineeditor ed [OPTION]...
lineeditor red [OPTION]...
lineeditor sed [OPTION]...
```

It calls the managed command implementations directly and does not spawn the standalone executables. The standalone `ed`, `red`, and `sed` programs remain first-class build and release outputs.

## Installation and distribution

The distribution router is published as the .NET tool package `Icod.LineEditor.Tools`. Install version `1.1.0` with:

```text
dotnet tool install --global Icod.LineEditor.Tools --version 1.1.0
```

The installed command is:

```text
lineeditor
```

Use the router to select an editor:

```text
lineeditor ed --help
lineeditor red --help
lineeditor sed --help
```

A missing or unknown router command is a usage error. `lineeditor --help` lists the supported commands and `lineeditor --version` reports the router version. Once a command is selected, arguments and standard streams are passed directly to the managed command implementation and its exit status is returned.

Tagged releases also provide framework-dependent ZIP archives for Windows, Linux, and macOS on x64 and ARM64. Each archive contains all four executable entry points:

```text
lineeditor
ed
red
sed
```

Windows archive entries use the `.exe` suffix. Archives also contain the repository `README.md` and `LICENSE` and require the .NET 10 runtime.

**This repository root `README.md` is also the NuGet package README for `Icod.LineEditor.Tools`.** The router project packs this file at the package root as `README.md`, so the NuGet landing page and repository overview share the same installation, architecture, compatibility, and licensing documentation.

The narrower [`lineeditor/README.md`](lineeditor/README.md) documents the router itself; it is not the NuGet package README.

## `Icod.LineEditor.Ed.Shared`

[`Icod.LineEditor.Ed.Shared`](Icod.LineEditor.Ed.Shared/README.md) is the repository-local mutable editor engine shared by `ed` and `red`.

It owns the Ed-family behavior that is genuinely shared by those two commands, including:

- mutable line storage and stable line identities;
- Ed address and range parsing;
- marks, cut/yank state, and reversible undo;
- append, insert, change, delete, move, copy, join, print, list, and number operations;
- searches and substitutions using the managed GNU BRE/ERE providers;
- global command execution;
- file reads, complete-file transactional writes, and append operations;
- shell/filter capability boundaries; and
- the immutable standard and restricted security profiles used by `ed` and `red`.

There is intentionally **no** general `Icod.LineEditor.Shared` project. The completed sharing audit found no cohesive common engine that should couple Sed's streaming execution model to Ed's mutable-buffer model.

`Icod.LineEditor.Ed.Shared` is a repository-local implementation library, not a separately published cross-suite dependency.

## Architecture

The permanent dependency direction is:

```text
Published neutral foundation
    Icod.CommandFramework 2.1.0
            ↓ PackageReference
    ┌──────────────────────────────┐
    │ Icod.LineEditor.Ed.Shared    │
    └──────────────────────────────┘
          ↓ ProjectReference
       ed             red

    Icod.CommandFramework 2.1.0
            ↓ PackageReference
           sed

        ed    red    sed
          \    |    /
           lineeditor
       distribution router
```

`ed` and `red` deliberately share the Ed engine through repository-local `ProjectReference` relationships. `sed` remains a separate execution engine. The `lineeditor` router references all three command projects only to provide in-process dispatch. No production project in this repository references `Icod.CoreUtils.Shared`.

## Compatibility philosophy

The implementation aims for practical GNU-compatible behavior while retaining a managed, cross-platform architecture.

`ed` and `red` follow the GNU ed 1.22.5 command-line and editing profile implemented by the project. `red` is not a separate editing engine: it invokes the same Ed-family engine through a permanently restricted capability profile.

`sed` follows GNU sed command and option conventions where implemented, including addressed commands, BRE/ERE operation, substitutions, branching, pattern and hold spaces, GNU address extensions, in-place editing, sandbox mode, and NUL-delimited input. Its core work is performed in process; it does not shell out to another `sed` implementation.

Platform-sensitive command mechanics are supplied by .NET and the published `Icod.CommandFramework` package. Unsupported host capabilities are surfaced explicitly rather than emulated by invoking neighboring Unix tools.

## Highlights

### `ed`

`ed` supports standard and extended regular expressions, traditional compatibility mode, prompting, quiet/script/verbose modes, initial addresses, controlled filename policy, optional CR stripping, shell commands and filters in the standard profile, and `--restricted` for the same capability boundary used by `red`.

### `red`

`red` is always restricted. It disables shell execution before mutable editor dispatch and permits only simple leaf filenames under a captured working-directory policy. The restriction is a pathname-policy boundary; it does not claim physical confinement across symbolic links, mount points, hard links, reparse points, or validation/open races.

### `sed`

`sed` supports script expressions and files, automatic-print suppression, GNU BRE/ERE selection, in-place editing with optional backup suffixes, separate-file processing, unbuffered output, NUL-delimited records, list-width control, GNU address forms, and sandbox mode. Record framing is byte preserving and independent of host newline conventions.

## Building

The repository requires a .NET 10 SDK.

On Windows:

```text
build.cmd
```

On Unix-like hosts:

```text
./build.sh
```

With no section argument, the wrappers use `Debug` and run:

```text
clean → restore → build → test → pack → validate
```

The individual `clean`, `restore`, `build`, `test`, `pack`, and `validate` stages may also be requested.

Or build the solution directly:

```text
dotnet restore Icod.LineEditor.sln
dotnet build Icod.LineEditor.sln -c Staging --no-restore
dotnet test Icod.LineEditor.sln -c Staging --no-build --no-restore
```

The solution defines `Debug`, `Staging`, and `Release` configurations. Release builds treat compiler warnings as errors except for documentation warning `CS1591`.

## Versioning

Repository versioning is centralized in the root [`Directory.Build.props`](Directory.Build.props). `VersionPrefix` is the single authoritative release-version literal and is currently `1.1.0`. `Version`, `PackageVersion`, `AssemblyVersion`, and `FileVersion` are derived from it for projects in the repository.

For a tagged release, the `v<semver>` tag must agree with the generated NuGet package version. The release workflow selects packages by their actual nuspec version, so a mismatched tag cannot silently publish a differently versioned package.

## Continuous integration and release

The repository follows the canonical `uniblab/.github` lifecycle:

- pull requests build and test `Staging` on Windows, Linux, and macOS; Linux additionally packs and verifies generated NuGet artifacts;
- pushes to `main` run `Release` distribution validation on Windows/Linux/macOS for x64 and ARM64; and
- `v<semver>` tags contained in the default branch run the `Release` package/archive publication graph.

Executable release archives are produced for `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64`, and `osx-arm64`. Each archive contains `lineeditor`, `ed`, `red`, and `sed` (with `.exe` suffixes on Windows), plus the repository `README.md` and `LICENSE`.

The `Icod.LineEditor.Tools` router is a .NET tool package whose installed command is `lineeditor`. Package publication is version-gated by the actual generated nuspec version. NuGet.org Trusted Publishing must authorize the `Icod.LineEditor.Tools` package for repository `uniblab/Icod.LineEditor`, workflow `release.yaml`, and environment `Release`.

See [`packaging/README.md`](packaging/README.md) for the complete build, validation, archive, package-publication, and release contract.

## Project layout

```text
Icod.LineEditor/
├── Directory.Build.props          centralized repository version
├── Icod.LineEditor.Ed.Shared/    mutable Ed/Red engine
├── ed/                           standard line editor
├── red/                          restricted line editor
├── sed/                          stream editor
├── lineeditor/                   ed/red/sed command router
├── tests/                        command, engine, and router tests
├── packaging/                    normalized build/distribution helpers
├── docs/history/                 retained architecture and migration history
├── Icod.LineEditor.sln
├── build.cmd
└── build.sh
```

## Documentation

The executable READMEs are intended to function much like concise manual pages:

- [`lineeditor/README.md`](lineeditor/README.md)
- [`ed/README.md`](ed/README.md)
- [`red/README.md`](red/README.md)
- [`sed/README.md`](sed/README.md)

For the reusable Ed-family engine, see [`Icod.LineEditor.Ed.Shared/README.md`](Icod.LineEditor.Ed.Shared/README.md). The detailed design and migration record is preserved under [`docs/history`](docs/history/).

## Licensing

The executable tools `lineeditor`, `ed`, `red`, and `sed` are distributed under the GNU General Public License, version 3 or later. Each standalone tool directory contains its own `LICENSE` file, and the repository root [`LICENSE`](LICENSE) contains the same GPL text used by the router distribution.

`Icod.LineEditor.Ed.Shared` is distributed under the GNU Lesser General Public License, version 3 or later. See [`Icod.LineEditor.Ed.Shared/LICENSE`](Icod.LineEditor.Ed.Shared/LICENSE).

The `Icod.LineEditor.Tools` NuGet package includes the repository root `README.md` and `LICENSE`. Runtime-specific executable archives likewise include the root `README.md` and `LICENSE` alongside `lineeditor`, `ed`, `red`, and `sed`.

## Upstream inspiration and authorship

GNU `ed` identifies **Andrew L. Moore** as the original GNU ed author and **Antonio Diaz Diaz** as its current maintainer. `red` is the restricted form of GNU ed and shares that implementation lineage rather than having a separate upstream author list.

GNU sed 4.9 identifies **Jay Fenlason, Tom Lord, Ken Pizzini, Paolo Bonzini, Jim Meyering, and Assaf Gordon** as its authors. The historical Unix `sed` lineage originated with **Lee E. McMahon**.

These upstream credits identify the authors of the programs and implementations on which this managed work is modeled; they do not imply authorship of the C# migration.

Migrated to .Net by Timothy J. Bruce <uniblab@hotmail.com>.

## Copyright

Copyright (c) 2026 Timothy J. Bruce
