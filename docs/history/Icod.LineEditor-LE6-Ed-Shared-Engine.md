# Icod.LineEditor Phase LE6 — Ed/Red Shared Engine

## Status

Phase LE6 creates `Icod.LineEditor.Ed.Shared` and its dedicated `Icod.LineEditor.Ed.Shared.Tests` project in the current solution. The phase establishes the reusable mutable editor engine required by both standard Ed and restricted Red without migrating either executable prematurely.

## Project boundary

The public engine namespace is `Icod.LineEditor.Ed`; the assembly is `Icod.LineEditor.Ed.Shared`.

The shared engine owns:

- mutable line-buffer mechanics;
- Ed addresses and ranges;
- marks and cut-buffer state;
- substitutions and global commands;
- undo and remembered session state;
- file and shell capability orchestration;
- diagnostics, cooperative signals, cancellation, and controlled exit statuses;
- immutable standard and restricted security profiles.

The engine does not own:

- the `ed` or `red` process-level command line;
- Sed's pattern-space, hold-space, range-state, or streaming-cycle semantics;
- cross-suite regular-expression, record, process, temporary, or filesystem foundations;
- Diffutils runtime implementation types.

## Buffer and state model

`EditorBuffer` stores lines in bounded segments rather than one monolithic `List<string>`. Each inserted line receives a stable nonzero identity. Moves preserve identity; copies allocate new identities; substitutions and joins retain the identity of the surviving line. Marks and global-command selections therefore survive address movement and can detect deleted lines deterministically.

The engine records:

- current and last addresses;
- marks `a` through `z`;
- the cut buffer;
- the most recent regular expression, replacement, and shell command;
- the remembered filename where permitted;
- final-record termination;
- modified state;
- one reversible undo snapshot.

## Shared contract consumption

The implementation directly consumes the current Shared incubation APIs:

- `GnuBasicRegularExpressionProvider` and `ICompiledRegularExpression` for Ed searches and substitutions;
- `ByteRecordReader` for LF-delimited script and file records;
- `ProcessRunner` for host shell execution;
- `SecureTemporaryObjectCreator` and `TemporaryNameTemplate` for sibling staging files;
- `IFileSystemOperations.FlushFileAsync` for durability requests.

No parallel regular-expression, process-runner, temporary-name, or filesystem-durability abstraction is introduced.

## Capability and security model

`IEditorFileAccess` and `IEditorProcessAccess` contain all external effects. Standard implementations use Shared infrastructure. Denied implementations fail without touching the host. `RestrictedEditorFileAccess` resolves validated simple logical filenames beneath one captured working directory and rejects rooted, directory-bearing, alternate-stream, symbolic-link, and reparse-point leaves before delegation.

`EditorSecurityPolicy` is immutable. The restricted profile rejects shell commands in the dispatcher and uses a denied process capability, providing defense in depth. The engine preserves logical simple filenames rather than converting them to absolute paths before capability validation.

LE8 remains responsible for complete GNU Red pathname and race conformance, including hard-link and validation/open race analysis. Phase LE6 supplies the mandatory injection and policy boundaries so that work does not require another engine.

## Compatibility fixtures

The dedicated test project contains textual fixtures for:

- a GNU Diffutils-style ed script using change and append commands;
- an `Icod.DiffUtils`-style ed script using append, delete, and substitution commands.

The tests load the original text, execute the script through `EditorEngine`, and compare the resulting buffer to the expected text. The test project has no runtime dependency on `Icod.DiffUtils.Shared`.

## Phase boundary

Phase LE7 replaces the current `Icod.LineEditor.Ed.Command` seed internals with this engine under the standard profile and adds GNU ed 1.22.5 command-line and conformance coverage.

Phase LE8 makes `Icod.LineEditor.Red.Command` and `ed --restricted` select the same restricted engine profile and completes adversarial platform-path and confinement testing.
