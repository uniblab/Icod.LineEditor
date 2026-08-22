# Icod.LineEditor LE0 Baseline

## Purpose

This document freezes the repository identity, policy, source shape, and verified CI state immediately before Phase LE1 begins the behavior-preserving decomposition of `Icod.LineEditor.Sed`.

Phase LE0 does not change command behavior. It normalizes project policy and records the state that later LineEditor phases must preserve or change deliberately.

## Authoritative upstream baselines

| Command family | Repository project | Authority |
|---|---|---|
| `sed` | `Icod.LineEditor.Sed` | GNU sed 4.10 |
| `ed`, `red` | `Icod.LineEditor.Ed` and the future `Icod.LineEditor.Ed.Shared` engine | GNU ed 1.22.5 |

## Public and executable identities

| Command | Project | Assembly | Root namespace | Public command facade |
|---|---|---|---|---|
| `sed` | `Icod.LineEditor.Sed` | `sed` | `Icod.LineEditor.Sed` | `Icod.LineEditor.Sed.Command` |
| `ed` | `Icod.LineEditor.Ed` | `ed` | `Icod.LineEditor.Ed` | `Icod.LineEditor.Ed.Command` |
| `red` | `Icod.LineEditor.Red` | `red` | `Icod.LineEditor.Red` | `Icod.LineEditor.Red.Command` |

The Sed test project is `Icod.LineEditor.Sed.Tests`, stored at `tests/Sed.Tests/Icod.LineEditor.Sed.Tests.csproj`. Ed and Red dedicated test projects are scheduled with their implementation phases.

The LE0 Red facade preserves the pre-existing seed output and establishes the final public type name only. It is not a GNU Red implementation; Phase LE8 replaces the seed with the `Icod.LineEditor.Ed.Shared` engine under restricted capabilities.

## Architecture boundary

- `Icod.LineEditor.Ed.Shared` is the definite owner of the common Ed/Red mutable editor engine.
- `Icod.LineEditor.Sed` retains Sed-specific parsing, addresses and range state, pattern and hold spaces, branching, command-cycle behavior, substitutions, sandbox policy, and in-place-editing policy.
- A general `Icod.LineEditor.Shared` is optional. Phase LE9 may create it only after completed Ed and Sed engines demonstrate cohesive family-specific reuse that is neither cross-suite framework material nor specific to one engine.

## Pre-LE1 source snapshot

The source snapshot was taken from `main` on August 4, 2026, before LE0 project-file edits.

| File | Lines | SHA-256 |
|---|---:|---|
| `sed/src/Command.cs` | 4,604 | `a9532865c4759c7d8ca9b146b8f2a6a27907512ba6c26ffdb897d3a792b8c40b` |
| `tests/Sed.Tests/src/SedCommandTests.cs` | 514 | `12fe322ee0dcb55b828f2cf5aeea4ed93644c1a4485fc6e9c9eaa6a75182ba05` |

The Sed test source declares 30 `[Fact]` cases and one `[Theory]` with three `[InlineData]` rows, for 33 discovered cases under the current xUnit model.

## Full-solution execution baseline

The pre-LE0 `main` baseline is GitHub Actions run **build and publish #111**, commit `bb8a087`, triggered August 4, 2026 at 17:42 UTC. The run completed successfully in 3 minutes 25 seconds with all three matrix jobs complete:

- `windows-latest`;
- `ubuntu-latest`;
- `macos-latest`.

The workflow's successful solution-wide build-and-test result is the behavioral baseline for LE1. The run reported documentation warnings outside the LineEditor scope; those warnings do not change the pass result.

## Reproduction commands

Run from the repository root:

```sh
dotnet clean Icod.CoreUtils.sln -c Debug
dotnet restore Icod.CoreUtils.sln
dotnet build Icod.CoreUtils.sln -c Debug --no-restore
dotnet test Icod.CoreUtils.sln -c Debug --no-build --logger trx
```

Run the focused Sed baseline with:

```sh
dotnet test tests/Sed.Tests/Icod.LineEditor.Sed.Tests.csproj -c Debug --logger trx
```

The required acceptance matrix remains `windows-latest`, `ubuntu-latest`, and `macos-latest`. Local `windows-10` testing is useful additional coverage but does not replace the required CI matrix.

## LE1 preservation rule

LE1 may move private implementation types and add characterization coverage, but it must keep the public `Icod.LineEditor.Sed.Command` boundary and existing command behavior stable. Regex, record, encoding, process, sandbox, and transactional semantic changes belong to their later dedicated phases.
