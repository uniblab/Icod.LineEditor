# Icod.LineEditor.Ed.Shared

`Icod.LineEditor.Ed.Shared` is the repository-local mutable editor engine shared by the `ed` and `red` command projects.

The project owns Ed-family editing behavior rather than process-level command-line policy. It is intentionally specific to the Ed/Red model and is not a general LineEditor abstraction.

## What the library provides

- segmented mutable line storage with stable line identities;
- current and last address state;
- marks, a cut/yank buffer, and one-level reversible undo;
- Ed address and range parsing independent of Sed's streaming address model;
- append, insert, change, delete, print, list, number, mark, move, copy, join, yank, put, substitution, global, file, shell/filter, undo, and quit operations;
- managed GNU Basic and Extended Regular Expression consumption for searches and substitutions;
- injected file and process capabilities;
- immutable standard and restricted security profiles;
- controlled diagnostics, cancellation, signal, and exit-status results; and
- complete-file writes through the shared transactional-replacement foundation.

The project deliberately has no runtime reference to `Icod.DiffUtils.Shared`. Compatibility with ed scripts emitted by GNU Diffutils and `Icod.DiffUtils` is verified through textual fixtures rather than a runtime suite dependency.

## Dependency direction

The permanent repository boundary is:

```text
Published neutral foundation
    Icod.CommandFramework 1.1.0
            ↓ PackageReference
    Icod.LineEditor.Ed.Shared
          ↓             ↓
        ed               red
          ProjectReference
```

The shared engine consumes neutral command-line, record, regular-expression, process, temporary-file, filesystem, metadata, traversal, and transactional-replacement contracts from the published `Icod.CommandFramework` package.

`ed` and `red` consume this engine through repository-local `ProjectReference` relationships. The library has no source-tree dependency on `Icod.CoreUtils.Shared`.

## Standard and restricted profiles

The engine is constructed with an immutable capability profile.

The standard profile permits the file and process capabilities supplied by the `ed` host. The restricted profile used by both `red` and `ed --restricted` disables shell commands, narrows accepted pathname syntax, captures the working directory used by restricted pathname policy, and provides a denied process capability as defense in depth.

The restricted profile is a command/pathname capability boundary; it does not claim OS-level physical confinement across links, mount points, reparse points, or races.

## Transactional writes

Complete-file writes and creations use the command framework's transactional-replacement capability. The file-access layer resolves the applicable terminal-link target, freezes the available identity/absence precondition, stages and flushes the complete editor buffer in a secure sibling file, preserves representable metadata, and delegates publication, rollback, and cleanup to the shared transaction mechanism.

Append remains a direct append-and-flush operation because it is not a whole-file replacement.

## Why there is no `Icod.LineEditor.Shared`

The completed LE9 sharing audit compared the mature Ed and Sed implementations and found no cohesive residual contract that justified a general LineEditor-family Shared assembly.

Mutable buffer, address, undo, and Red security behavior remain here. Sed's program model, address/range state, command cycle, sandbox policy, and in-place orchestration remain in `Icod.LineEditor.Sed`. Truly neutral mechanisms live in `Icod.CommandFramework` instead of being duplicated under a LineEditor namespace.

The architecture decision is retained in `docs/history/Icod.LineEditor-LE9-Sharing-Audit.md` and enforced by architecture-boundary tests.

## Build and test

The project targets .NET 10 and C# 13 and is built as part of `Icod.LineEditor.sln`.

```text
dotnet restore Icod.LineEditor.sln
dotnet build Icod.LineEditor.sln -c Release --no-restore
dotnet test Icod.LineEditor.sln -c Release --no-build
```

Its dedicated tests live in `tests/Ed.Shared.Tests`.

## License

`Icod.LineEditor.Ed.Shared` is licensed under the GNU Lesser General Public License, version 3 or later. See [`LICENSE`](LICENSE).

The executable `ed` and `red` projects have their own GPL-licensed distribution files in their respective directories.
