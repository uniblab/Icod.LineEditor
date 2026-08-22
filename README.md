# Icod.LineEditor

`Icod.LineEditor` is a managed .NET implementation of the classic Unix line-editing family: `ed`, its restricted form `red`, and the `sed` stream editor.

The repository targets .NET 10 and C# 13 and is designed for Windows, Linux, and macOS. The editors use managed code for their editing, regular-expression, record-processing, and command orchestration behavior rather than invoking the host system's `ed`, `red`, or `sed` executable.

This repository is the permanent home of the LineEditor family extracted from the former multi-suite `Icod.CoreUtils` development repository during Completion Gate G7.

## Included commands

| Command | Purpose |
|---|---|
| [`ed`](ed/README.md) | Interactive and scriptable line-oriented text editor, following the GNU ed 1.22.5 compatibility profile. |
| [`red`](red/README.md) | Permanently restricted `ed` profile that disables shell execution and limits pathname syntax. |
| [`sed`](sed/README.md) | Non-interactive stream editor with GNU-style addressing, substitutions, branching, hold space, in-place editing, sandboxing, and NUL-delimited records. |

Each executable directory contains a dedicated man-page-style `README.md` describing its implemented command-line profile, behavior, exit status, platform notes, authorship, and licensing.

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
    Icod.CommandFramework 1.1.0
            ↓ PackageReference
    ┌──────────────────────────────┐
    │ Icod.LineEditor.Ed.Shared    │
    └──────────────────────────────┘
          ↓ ProjectReference
       ed             red

    Icod.CommandFramework 1.1.0
            ↓ PackageReference
           sed
```

`ed` and `red` deliberately share the Ed engine through repository-local `ProjectReference` relationships. `sed` remains a separate execution engine. No production project in this repository references `Icod.CoreUtils.Shared`.

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

Or build the solution directly:

```text
dotnet restore Icod.LineEditor.sln
dotnet build Icod.LineEditor.sln -c Staging --no-restore
dotnet test Icod.LineEditor.sln -c Staging --no-build
```

The solution defines `Debug`, `Staging`, and `Release` configurations. Release builds treat compiler warnings as errors except for documentation warning `CS1591`.

## Continuous integration

Pull requests and pushes to `main` are built and tested with .NET 10 on:

- `windows-latest`
- `ubuntu-latest`
- `macos-latest`

The `main` workflow builds the `Release` configuration with `ContinuousIntegrationBuild=true` before running the complete test suite.

## Project layout

```text
Icod.LineEditor/
├── Icod.LineEditor.Ed.Shared/    mutable Ed/Red engine
├── ed/                           standard line editor
├── red/                          restricted line editor
├── sed/                          stream editor
├── tests/                        command and engine tests
├── docs/history/                 retained architecture and migration history
├── Icod.LineEditor.sln
├── build.cmd
└── build.sh
```

## Documentation

The executable READMEs are intended to function much like concise manual pages:

- [`ed/README.md`](ed/README.md)
- [`red/README.md`](red/README.md)
- [`sed/README.md`](sed/README.md)

For the reusable Ed-family engine, see [`Icod.LineEditor.Ed.Shared/README.md`](Icod.LineEditor.Ed.Shared/README.md). The detailed design and migration record is preserved under [`docs/history`](docs/history/).

## Licensing

The executable tools `ed`, `red`, and `sed` are distributed under the GNU General Public License, version 3 or later. Each tool directory contains its own `LICENSE` file, and the repository root [`LICENSE`](LICENSE) contains the same GPL text.

`Icod.LineEditor.Ed.Shared` is distributed under the GNU Lesser General Public License, version 3 or later. See [`Icod.LineEditor.Ed.Shared/LICENSE`](Icod.LineEditor.Ed.Shared/LICENSE).

The build projects copy their local `README.md` and `LICENSE` into the output directory as `$(AssemblyName).README.md` and `$(AssemblyName).LICENSE.txt` respectively.

## Upstream inspiration and authorship

GNU `ed` identifies **Andrew L. Moore** as the original GNU ed author and **Antonio Diaz Diaz** as its current maintainer. `red` is the restricted form of GNU ed and shares that implementation lineage rather than having a separate upstream author list.

GNU sed 4.9 identifies **Jay Fenlason, Tom Lord, Ken Pizzini, Paolo Bonzini, Jim Meyering, and Assaf Gordon** as its authors. The historical Unix `sed` lineage originated with **Lee E. McMahon**.

These upstream credits identify the authors of the programs and implementations on which this managed work is modeled; they do not imply authorship of the C# migration.

Migrated to .Net by Timothy J. Bruce <uniblab@hotmail.com>.

## Copyright

Copyright (c) 2026 Timothy J. Bruce
