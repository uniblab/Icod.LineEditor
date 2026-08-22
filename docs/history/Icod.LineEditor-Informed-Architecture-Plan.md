# Icod.LineEditor Revised Repository-Informed Architecture and Refactoring Plan

## Status of this document

This document supersedes the earlier proposed `Icod.LineEditor` architecture plan.

It is based on an inspection of the `main` branch of the current repository and roadmap as they stood on July 30, 2026:

- [Icod.CoreUtils repository](https://github.com/uniblab/Icod.CoreUtils)
- [Icod.CoreUtils Audit and Refactor Roadmap](https://github.com/uniblab/Icod.CoreUtils/blob/main/Icod.CoreUtils-Audit-and-Refactor-Roadmap.md)
- [Current Shared project README](https://github.com/uniblab/Icod.CoreUtils/blob/main/Shared/README.md)
- [Current Sed command implementation](https://github.com/uniblab/Icod.CoreUtils/blob/main/sed/src/Command.cs)
- [Current Sed project](https://github.com/uniblab/Icod.CoreUtils/blob/main/sed/Icod.LineEditor.Sed.csproj)
- [Current Sed tests](https://github.com/uniblab/Icod.CoreUtils/blob/main/tests/Sed.Tests/src/SedCommandTests.cs)
- [Current Ed command implementation](https://github.com/uniblab/Icod.CoreUtils/blob/main/ed/src/Command.cs)
- [Current Ed project](https://github.com/uniblab/Icod.CoreUtils/blob/main/ed/Icod.LineEditor.Ed.csproj)
- [Current Red project](https://github.com/uniblab/Icod.CoreUtils/blob/main/red/Icod.LineEditor.Red.csproj)

The authoritative upstream baselines currently relevant to the family are:

- [GNU ed 1.22.5 manual](https://www.gnu.org/software/ed/manual/ed_manual.html);
- [GNU sed 4.10 release archive](https://ftp.gnu.org/gnu/sed/);
- the exact pinned versions recorded in the repository's upstream-version ledger and roadmap.

The public command classes remain exactly:

```text
Icod.LineEditor.Ed.Command
Icod.LineEditor.Red.Command
Icod.LineEditor.Sed.Command
```

No `EdCommand`, `RedCommand`, or `SedCommand` classes are proposed.

## LE0 through LE10 implementation status

Phase LE0 was completed on August 4, 2026. Project and solution identities now follow the architecture below, Ed and Red explicitly use C# 13, the Red project file follows the repository's UTF-8-without-BOM convention, and the pre-LE1 source and three-runner test state is recorded in [`Icod.LineEditor-LE0-Baseline.md`](Icod.LineEditor-LE0-Baseline.md). Because the inspected Red project contained only a placeholder entry point, LE0 also establishes the required public `Icod.LineEditor.Red.Command` facade while preserving that seed output; Phase LE8 remains responsible for actual restricted-editor behavior.

Phase LE1 is now complete. The monolithic Sed implementation has been decomposed into responsibility-focused partial-class modules while preserving the public command signatures and the pre-LE1 regex, record, script-source, sandbox, process, and replacement semantics. New characterization and module-boundary tests make those temporary behaviors explicit for the later semantic phases.

Phase LE2 is now complete. The Gate R1 BRE/ERE foundation has been revalidated as the LineEditor consumer boundary, with direct acceptance coverage for syntax profiles, leftmost-longest selection, captures, locale policy, string and exact-byte coordinates, malformed input, diagnostics, cancellation, and resource limits. The audit found no missing cross-suite production contract and leaves Sed-specific state and replacement policy for LE3.

Phase LE3 migrated Sed matching to the Shared managed GNU BRE/ERE providers while retaining command-owned empty-expression reuse, modifiers, replacement context, GNU escape preprocessing, and diagnostic presentation.

Phase LE4 established byte-preserving LF/NUL record framing, explicit termination, C/POSIX byte and UTF-8 locale profiles, invalid-byte preservation, and explicit data separators.

Phase LE5 is now complete. Sed's primary entry point consumes `CommandContext`; script expressions, files, and the implicit operand retain distinct identities and source-relative locations; shell, auxiliary-file, and in-place operations are injectable; Shared `ProcessRunner` remains the system shell implementation; sandbox restrictions have compile-time and runtime enforcement; and the provisional replacement boundary established for LE10 has now been migrated to Completion Gate E6.

Phase LE6 is now complete. `Icod.LineEditor.Ed.Shared` and its dedicated tests establish the mutable Ed/Red engine with bounded segmented line storage, stable identities, Ed-specific addresses, marks, cut buffers, substitutions, global commands, undo and remembered state, injected file/process capabilities, immutable standard/restricted profiles, Shared BRE and record/process/temporary/filesystem consumption, and textual GNU/Icod Diffutils ed-script fixtures.

Phase LE7 is now complete. The `ed` executable retains `Icod.LineEditor.Ed.Command` and the lowercase `ed` assembly while becoming a thin GNU ed 1.22.5 command/session host over the LE6 engine. It now owns declarative option parsing, byte-preserving `CommandContext` orchestration, initial file and `+line`/search selection, standard/restricted profile composition, prompting, diagnostics, script presentation, signal/cancellation mapping, and command-level scale/interoperability tests.

Phase LE8 is now complete. `red` retains `Icod.LineEditor.Red.Command` and the lowercase `red` assembly, uses the same Ed engine and immutable restricted profile as `ed --restricted`, denies shell operations before dispatch and again at the process-capability layer, applies host-independent restricted pathname classification, captures its working directory once, documents pathname restriction rather than physical confinement, and adds adversarial command and state-preservation tests.

Phase LE9 is now complete. The completed Sed and Ed implementations were compared rather than abstracted speculatively. Neutral regular-expression, record, diagnostic, process, temporary, filesystem, and text contracts remain in the current Shared incubation project; mutable Ed/Red behavior remains in `Icod.LineEditor.Ed.Shared`; Sed program and cycle behavior remains in `Icod.LineEditor.Sed`. No cohesive residual family library remains, so `Icod.LineEditor.Shared` is not created. The evidence and dependency decision are recorded in `Icod.LineEditor-LE9-Sharing-Audit.md` and locked by architecture-boundary tests.

Phase LE10 is now complete. Sed in-place editing and Ed complete-file writes consume Completion Gate E6 transactional replacement; command-owned backup, append, force, modified-buffer, and link policies remain above the shared mechanism. Atomic publication, rollback, metadata, cancellation, link, failure-injection, and cleanup coverage is recorded in `Icod.LineEditor-LE10-Transactional-Replacement.md`. Completion Gate F1 is now active.

---

## Executive decision

The previous plan proposed creating both:

```text
Icod.LineEditor.Shared
Icod.LineEditor.Ed.Shared
```

before implementing Ed and Red and before refactoring Sed.

After examining the present repository, that is too eager.

The revised recommendation is:

1. **Do create `Icod.LineEditor.Ed.Shared`.**
   Ed and Red are two security profiles over the same mutable line-editor engine. Their shared engine is already proven by the upstream relationship between the commands.

2. **Do not create `Icod.LineEditor.Shared` at the beginning.**
   Most of the plausible cross-editor infrastructure already exists in the current `Icod.CoreUtils.Shared` incubation project and is more accurately classified as future `Icod.CommandFramework` material.

3. **Keep `Icod.LineEditor.Sed` as its own command and engine project.**
   It already has the correct project name, root namespace, executable assembly name, public command class, asynchronous entry point, dedicated test project identity, and direct reference to the current Shared project.

4. **Refactor Sed internally before attempting to extract an Ed/Sed family library.**
   The pre-LE1 Sed implementation was a large monolithic `Command.cs`. Phase LE1 has now made its internal boundaries visible without changing behavior.

5. **Implement `Icod.LineEditor.Ed.Shared`, then Ed and Red.**
   After both the decomposed Sed engine and complete Ed engine exist, perform a consumer audit.

6. **Create `Icod.LineEditor.Shared` only if the audit finds meaningful line-editor-family behavior that:**
   - is used by both Sed and the Ed family;
   - is not already appropriate for `Icod.CommandFramework`;
   - is not merely similar-looking syntax with different semantics;
   - is substantial enough to justify another assembly and package boundary.

The revised architecture during incubation is therefore:

```text
Current Icod.CoreUtils.Shared incubation project
│
├── Icod.LineEditor.Sed
│
└── Icod.LineEditor.Ed.Shared
    ├── Icod.LineEditor.Ed
    └── Icod.LineEditor.Red
```

An optional future library may appear later:

```text
Icod.LineEditor.Shared
```

but it is an outcome of the implementation audit, not a prerequisite.

---

## Why the recommendation changed

### The present Shared project already owns the likely cross-editor foundations

The current `Icod.CoreUtils.Shared` project is no longer a small Coreutils helper assembly. It already contains focused areas for:

```text
CommandLine
Diagnostics
Delimiters
Escapes
FileSystem
IO
Platform
Processes
Ranges
Records
RegularExpressions
Temporary
Text
Time
```

Its README explicitly identifies the project as an incubation location and instructs commands to use:

- `CommandContext`;
- `OptionParser`;
- decoded or byte-preserving record readers as appropriate;
- `ProcessRunner`;
- `IRegularExpressionProvider`;
- secure temporary-object infrastructure;
- explicit text, locale, and display-width abstractions.

These facilities are useful not only to Coreutils and the line editors, but also to Grep, Diffutils, Patch, Tar, and ProcPs. They are therefore natural future `Icod.CommandFramework` candidates.

Creating `Icod.LineEditor.Shared` now and placing wrappers or copies of these facilities into it would produce the wrong dependency boundary:

```text
Icod.LineEditor.Shared
    ├── a second regular-expression abstraction
    ├── a second record abstraction
    ├── a second process abstraction
    └── a second diagnostic abstraction
```

That would increase duplication immediately before the final framework audit is intended to eliminate it.

### The current Sed project has already completed the structural namespace move

The previous plan treated Sed separation as future work. That is no longer accurate.

The current project already has:

```xml
<AssemblyName>sed</AssemblyName>
<RootNamespace>Icod.LineEditor.Sed</RootNamespace>
```

and references:

```xml
<ProjectReference Include="..\Shared\Icod.CoreUtils.Shared.csproj" />
```

The command source is already:

```csharp
namespace Icod.LineEditor.Sed;

public static class Command
{
}
```

The test assembly and root namespace are already:

```text
Icod.LineEditor.Sed.Tests
```

Therefore, the next Sed task is not “move Sed to its final namespace.” The remaining structural cleanup is narrower:

- rename the stale test project filename from `Icod.CoreUtils.Sed.Tests.csproj` to `Icod.LineEditor.Sed.Tests.csproj`;
- update any stale solution-project display names or roadmap language;
- optionally move physical directories under a `LineEditor` suite directory when the suite block is undertaken;
- decompose the implementation internally.

### The current Sed engine is one monolithic source file

The present `sed/src` directory contains one source file, `Command.cs`, and that file is more than four thousand lines long.

It includes, as private nested types and private static methods:

- command options;
- address types;
- address ranges;
- instruction types;
- script parsing;
- script diagnostics;
- source specifications;
- record reading;
- input sequencing;
- execution state;
- pattern-space and hold-space behavior;
- substitution;
- regular-expression translation;
- transliteration;
- shell execution;
- auxiliary file access;
- in-place editing;
- backup handling;
- symlink handling;
- command orchestration.

This is not evidence that these concerns belong together. It is evidence that the existing implementation has not yet exposed its real internal boundaries.

Creating a family Shared project before decomposing this file would encourage extracting code based on textual proximity rather than proven ownership.

### The current Shared project already has a GNU BRE engine, while Sed bypasses it

The current Shared regular-expression foundation is explicitly designed to avoid translating GNU basic regular expressions into `System.Text.RegularExpressions`.

It provides:

```text
IRegularExpressionProvider
ICompiledRegularExpression
RegularExpressionCompileResult
RegularExpressionMatchResult
RegularExpressionDiagnostic
RegularExpressionOptions
```

and implements GNU/POSIX basic regular-expression behavior with leftmost-longest matching and injectable locale/classification policy.

Before LE3, Sed contained private methods that translated common BRE syntax and selected POSIX character classes into `System.Text.RegularExpressions.Regex`. That was the clearest immediate example of code that should not move into `Icod.LineEditor.Shared`.

Completion Gate R1 supplied direct managed GNU Basic and Extended providers, and LE2 confirmed that their syntax, locale, capture, coordinate, diagnostic, cancellation, and resource contracts satisfy the LineEditor consumers. LE3 now consumes those providers through `SedRegularExpressionCompiler`; the private translator is removed.

### The Shared regular-expression API is sufficient for Sed selection mechanics

`IRegularExpressionProvider` now supports the GNU Basic and Extended profiles required by `-E`, `-r`, and `--regexp-extended`. Sed layers its own empty-expression reuse, address/substitution modifiers, GNU escape preprocessing, POSIX mode, occurrence selection, empty-match iteration, replacement expansion, and diagnostic presentation above the command-neutral Shared matcher. No line-editor-only regex library is required.

### The current Shared project has a better record model than Sed currently uses

The current Shared project has two relevant levels:

1. `Icod.CoreUtils.Shared.IO.DelimitedRecordReader`
   - decoded `TextReader` records;
   - returns `string`;
   - may trim a carriage return preceding an LF;
   - suitable only when exact source bytes and final-termination state are not part of the command contract.

2. `Icod.CoreUtils.Shared.Records`
   - byte-preserving LF or NUL records;
   - explicit termination state;
   - segmented bounded reading for enormous records;
   - separate record content and separator writing.

The current Sed implementation wraps the decoded `DelimitedRecordReader`, selecting LF or NUL and enabling carriage-return trimming for LF input.

That is convenient, but it conflicts with the repository's newer byte-preserving text policy:

- LF is a data separator;
- NUL is a data separator under `-z`;
- CR is ordinary data unless a command option explicitly strips it;
- an unterminated final record is semantically different from a terminated record;
- invalid encoded bytes cannot be silently normalized through a `TextReader`.

This does not mean the entire Sed engine must immediately become a byte-array interpreter. It does mean the authoritative input and output model should eventually preserve bytes and terminator state.

### The current Sed process reuse is directionally correct

Sed already uses the shared `ProcessRunner` for shell execution instead of duplicating child-process redirection and cancellation.

That is good reuse and should be preserved.

The weakness is that shell execution is still reached through a private static method. Sed sandbox mode is enforced primarily during parsing by rejecting file and execution commands.

The stronger design is:

```text
parser-level prohibition
        +
runtime capability denial
```

A denied shell executor should remain incapable of launching a process even if a future parser or nested command path accidentally reaches it.

This facade is Sed-specific policy over a shared process mechanism; it is not a replacement for the shared process mechanism.

### The current in-place replacement is not yet the final transaction model

The current Sed implementation already has several worthwhile properties:

- it creates the temporary output with `FileMode.CreateNew`;
- it attempts cleanup after failure;
- it retains Unix mode where available;
- it supports backup suffixes;
- it has tests for backups, modes, and symlink-following behavior.

However, the replacement sequence can:

1. delete or move the original;
2. then move the temporary file into place.

There is no use of `File.Replace`, and the operation is not yet expressed through the shared transactional replacement model planned for Completion Gate E6.

A failure between removal of the original and installation of the temporary output can therefore produce a data-loss or recovery problem.

The correct architectural response is not to create a Sed-only transaction engine. Sed should isolate in-place editing behind an internal boundary now and consume the shared E6 replacement contract when that gate is complete.

---

## Current repository assessment

## `Icod.CoreUtils.Shared`

### Strengths

The current Shared project is already a strong incubation foundation.

It provides:

- a declarative GNU/POSIX-style option parser;
- command contexts and standard diagnostics;
- byte-preserving text units and logical lines;
- explicit locale and display-width providers;
- LF/NUL record framing;
- exact final-record termination metadata;
- GNU range parsing;
- delimiter and escape scanning;
- a managed GNU BRE/ERE implementation;
- shell-free child-process execution;
- secure temporary workspaces;
- platform capability reporting.

This makes it the correct temporary home for code that will eventually become `Icod.CommandFramework`.

### Architectural risk

The project name still says `Icod.CoreUtils.Shared`, but its contents now span three categories:

```text
future Icod.CommandFramework
future Icod.CoreUtils.Shared
possibly future Icod.FileUtils.Shared or Icod.TextUtils.Shared
```

The immediate danger is not that LineEditor lacks a shared project. The danger is that another shared project might be created to duplicate APIs already incubating here.

Every LineEditor proposal should therefore be classified before implementation:

```text
Cross-suite framework candidate
LineEditor-family-specific
Ed-family-specific
Sed-specific
Command-local
```

### Shared enhancements completed for the LineEditor work

The most important Shared work was not a LineEditor package. Completion Gate R1 extended the existing cross-suite contract, and Phase LE2 has now verified that contract against the pinned LineEditor baselines without requiring another production API.

#### GNU ERE support

The regular-expression API now explicitly compiles:

```text
GNU/POSIX basic regular expressions
GNU/POSIX extended regular expressions
```

A compatible shape might be:

```csharp
public enum RegularExpressionSyntax
{
    Basic,
    Extended,
}
```

with syntax selected through `RegularExpressionOptions`:

```csharp
public sealed record RegularExpressionOptions
{
    public RegularExpressionSyntax Syntax { get; init; }
        = RegularExpressionSyntax.Basic;
}
```

The existing `Compile(pattern, options, token)` methods can remain source-compatible because Basic remains the default.

The implementation must not translate ERE to .NET regex syntax as its conformance mechanism. The managed parser and matcher should understand the selected GNU/POSIX syntax profile directly.

Consumers would include:

```text
expr       BRE
ed         BRE
sed        BRE and ERE
grep       BRE and ERE
csplit     BRE
```

#### Byte-preserving regex integration

Eventually, regex matching needs a deliberate relationship with the Shared text-unit and byte-record models.

At minimum, the design must state:

- whether matching occurs over raw bytes or decoded scalars;
- how C/POSIX locale differs from UTF-8 locale;
- how match offsets map back to authoritative source bytes;
- how replacement output is encoded;
- how invalid sequences are preserved or rejected;
- how NUL-delimited input interacts with matching;
- how line-sensitive anchors are interpreted.

This is cross-suite infrastructure because Grep, Sed, Ed, Expr, and Csplit all depend on it.

#### Injectable process and filesystem capabilities

The existing static helpers should gradually be surfaced through injectable providers or factories where security profiles require denial or deterministic tests.

LineEditor consumers need:

```text
process execution
file opening
auxiliary file writes
temporary files
transactional replacement
path and symlink inspection
```

The general mechanisms belong in the current Shared incubation project. Sed and Red apply their own policy profiles over those mechanisms.

---

## `Icod.LineEditor.Sed`

### What is already good

The current Sed seed is substantially more than a placeholder.

It already has:

- the correct namespace and project identity;
- a public `Icod.LineEditor.Sed.Command`;
- C# 13 and `net10.0`;
- an asynchronous `Main`;
- cancellation handling;
- a `CommandContext` at the executable boundary;
- Shared `OptionParser` use;
- Shared diagnostics;
- Shared `ProcessRunner` use;
- streaming one-record lookahead rather than whole-input buffering;
- support for LF and NUL record modes;
- broad command coverage;
- basic and extended regular-expression options;
- pattern and hold spaces;
- branching;
- grouped commands;
- substitutions and transliteration;
- file read and write commands;
- sandbox mode;
- POSIX mode;
- debug output;
- in-place editing;
- backup suffixes;
- symlink-following behavior;
- a useful 483-line functional test suite.

These tests are valuable characterization assets and should be retained before structural changes.

### What should change

#### `Command.cs` should become an orchestration boundary

The exact public class remains:

```text
Icod.LineEditor.Sed.Command
```

but it should no longer contain the entire interpreter.

The public class should be responsible for:

- compatibility `Run` overloads;
- cancellation-aware `RunAsync` overloads;
- option parsing orchestration;
- script-source assembly;
- service composition;
- invoking the compiler and executor;
- mapping controlled failures to diagnostics and exit statuses.

It should not itself define every address, instruction, parser, record reader, executor, regex translator, file transaction, and shell adapter.

#### `CommandContext` should flow into the command core

The current `Program` creates a `CommandContext`, then immediately breaks it back into four arguments:

```text
StandardInput
StandardOutput
StandardError
CancellationToken
```

Add a core overload such as:

```csharp
public static Task<int> RunAsync(
    string[] args,
    CommandContext context
)
```

Keep the existing stream-based overloads as compatibility and test conveniences.

Internally, the core execution path should retain the context rather than reconstruct program-name and diagnostic behavior manually.

#### Script sources should remain distinct

The current implementation joins all `-e` and `-f` script fragments using `Environment.NewLine`.

That loses source identity and makes parsing depend on a host-generated line separator even though script separators are part of Sed grammar.

Represent script sources explicitly:

```text
command-line expression
script file
implicit first operand
```

Each source should retain:

- source name;
- source kind;
- original text or byte content;
- starting line and column;
- whether a synthetic separator is required between sources.

The compiler may consume a composite source stream, but diagnostics should still identify the original source.

A synthetic separator inserted between script sources is Sed grammar data and should be an explicit LF or parser token, not `Environment.NewLine`.

#### Replace the private regex translator

This is the highest-value semantic refactor.

Introduce a Sed-specific adapter over the shared provider:

```text
SedRegularExpressionCompiler
    ↓
IRegularExpressionProvider
```

The adapter owns Sed policy:

- BRE versus ERE selection;
- empty-pattern reuse;
- address versus substitution context;
- GNU and POSIX mode selection;
- substitution match iteration;
- Sed-specific diagnostics.

It does not own a second regex engine.

Delete the private BRE-to-.NET and POSIX-class translation code only after equivalent tests pass through the shared provider.

#### Preserve bytes and record termination

Introduce an internal Sed input model such as:

```text
SedInputRecord
SedRecordSeparator
SedInputPosition
```

It should retain:

- authoritative record bytes;
- whether the source record was terminated;
- the separator used;
- source-file identity;
- per-file and aggregate record number;
- optional decoded text and byte-to-text mapping.

Do not claim that Sed has bounded memory merely because its input reader is segmented. Sed commands such as `N`, `G`, substitutions, and repeated branching can legitimately grow pattern or hold space.

The meaningful invariant is:

> Sed streams the input and does not retain unrelated completed input records, while the current pattern and hold spaces may grow according to Sed semantics.

#### Treat LF and NUL as data semantics

For Sed:

```text
LF in normal mode
NUL under -z
```

are command data, not host presentation line endings.

Therefore:

- do not use `Environment.NewLine` to serialize Sed output records;
- do not trim CR automatically merely because the host is Windows;
- preserve an unterminated final record;
- add explicit tests for CRLF input, lone CR, invalid UTF-8, NUL records, and incomplete final records.

The general repository rule allowing `Environment.NewLine` for host-generated messages does not apply to Sed's transformed data stream.

Diagnostics may use host line endings. Sed output data must follow Sed semantics.

#### Separate compiler state from execution state

A proposed internal layout is:

```text
sed/src/
├── README.md
├── Command.cs
│
├── Options/
│   ├── README.md
│   └── SedOptions.cs
│
├── Scripting/
│   ├── README.md
│   ├── ScriptSource.cs
│   ├── ScriptSourceMap.cs
│   ├── ScriptParser.cs
│   ├── SedProgram.cs
│   ├── Instruction.cs
│   ├── InstructionKind.cs
│   ├── ScriptDiagnostic.cs
│   └── ScriptParseException.cs
│
├── Addresses/
│   ├── README.md
│   ├── SedAddress.cs
│   ├── SedAddressRange.cs
│   ├── AddressSelectionState.cs
│   └── AddressContext.cs
│
├── Execution/
│   ├── README.md
│   ├── SedExecutor.cs
│   ├── SedExecutionState.cs
│   ├── PatternSpace.cs
│   ├── HoldSpace.cs
│   ├── DeferredOutputQueue.cs
│   └── SedInputSequence.cs
│
├── Records/
│   ├── README.md
│   ├── SedInputRecord.cs
│   ├── SedRecordReader.cs
│   └── SedRecordWriter.cs
│
├── RegularExpressions/
│   ├── README.md
│   └── SedRegularExpressionCompiler.cs
│
├── Substitution/
│   ├── README.md
│   ├── SubstitutionCommand.cs
│   ├── SubstitutionFlags.cs
│   ├── ReplacementTemplate.cs
│   └── SedSubstitutionEngine.cs
│
├── Files/
│   ├── README.md
│   ├── AuxiliaryFileManager.cs
│   ├── InPlaceEditor.cs
│   └── BackupNamePolicy.cs
│
└── Processes/
    ├── README.md
    ├── ISedShellExecutor.cs
    ├── ProcessRunnerShellExecutor.cs
    └── DeniedShellExecutor.cs
```

The exact file split can change, but the responsibilities should be separate.

Under the repository convention, every directory containing more than one source file receives a substantive `README.md`, and all internal types and members receive substantive XML documentation.

#### Keep most types internal

The public contract should remain deliberately small:

```text
Icod.LineEditor.Sed.Command
```

Supporting parser, program, address, execution, substitution, and transaction types should remain `internal` unless an external consumer is demonstrated.

The dedicated tests may use `InternalsVisibleTo` where focused engine tests are preferable to command-line-only tests.

#### Add defense in depth to sandbox mode

Retain parser-level rejection so invalid sandbox scripts fail before processing input.

Also compose execution with denied capabilities:

```text
ISedShellExecutor
ISedAuxiliaryFileAccess
ISedInPlaceEditAccess
```

In sandbox mode, denied implementations should reject access even if an instruction somehow reaches runtime execution.

Do not reuse Red's complete security policy as Sed's sandbox policy. They have different rules:

- Red permits files in the current directory but denies directories and shell commands;
- Sed sandbox mode denies input, output, and external-command operations defined by GNU Sed policy.

They may consume the same lower-level shared process and filesystem abstractions without sharing one policy object.

#### Isolate in-place editing now; replace its mechanics at E6

Create an internal `InPlaceEditor` boundary before changing behavior.

The first refactor can preserve current behavior behind that boundary, with characterization tests.

When Completion Gate E6 is implemented, replace the internals with shared:

- secure sibling temporary files;
- atomic replacement where supported;
- backup-name policy;
- rollback;
- metadata preservation;
- symlink and reparse-point policy;
- deterministic cleanup;
- explicit capability diagnostics.

Add failure-injection tests for every transition:

```text
temporary creation
input read
output write
flush
backup creation
metadata capture
original replacement
metadata restoration
cleanup
```

---

## Revised Ed and Red architecture

## `Icod.LineEditor.Ed.Shared` is still required

Unlike the speculative Ed/Sed family library, the Ed/Red shared engine is certain.

GNU Red is restricted Ed. Both commands share:

- command-line interpretation;
- mutable line buffer;
- Ed addresses and ranges;
- current address;
- marks;
- cut buffer;
- insert, append, change, delete, move, copy, join, yank, and put;
- printing and listing;
- substitutions;
- global and inverse-global commands;
- undo;
- modified state;
- remembered filename;
- file commands;
- shell and filter command plumbing;
- signals and cancellation;
- diagnostic and exit-status rules.

The only meaningful difference is the selected security profile and command identity.

The correct projects are:

```text
Icod.LineEditor.Ed.Shared
Icod.LineEditor.Ed
Icod.LineEditor.Red
```

with public command classes:

```text
Icod.LineEditor.Ed.Command
Icod.LineEditor.Red.Command
```

### The present Ed code is a seed, not an engine

The current Ed command is a short synchronous implementation using:

- `List<string>`;
- direct `File.ReadAllLines`;
- direct `File.WriteAllLines`;
- .NET regular expressions;
- a small subset of commands;
- no complete address model;
- no complete state machine;
- no Red security profile.

It should be treated as a historical seed and source of a few tests, not as the architecture to be extracted.

### The present Red project is only a shell

The current Red project has the correct assembly and namespace identity but no implemented shared editor engine.

This is useful: there is little compatibility burden preventing the correct architecture from being established.

### Ed/Red dependency structure

During incubation:

```text
Current Shared incubation project
        ↓
Icod.LineEditor.Ed.Shared
        ↓
Icod.LineEditor.Ed
Icod.LineEditor.Red
```

Projects should explicitly reference the narrowest assemblies whose APIs they directly use. Do not depend on accidental transitive references.

The command projects should be thin:

```text
parse process-level invocation
select command identity
select standard or restricted security profile
invoke shared Ed application
return exit status
```

### Red security remains an Ed-specific policy

`Icod.LineEditor.Ed.Shared` should contain:

```text
EditorSecurityPolicy
IEditorFileAccess
IEditorProcessAccess
StandardEditorFileAccess
RestrictedEditorFileAccess
StandardEditorProcessAccess
DeniedEditorProcessAccess
```

Both:

```text
red
ed --restricted
```

must select the same immutable policy and engine path.

Red restrictions require defense in depth:

- parser or dispatcher rejects shell-bearing command forms;
- denied process capability cannot execute a process;
- every filename-bearing operation uses the restricted file capability;
- the current directory is captured once;
- Unix and Windows path forms are considered;
- symlink, hard-link, reparse-point, and validation/open race behavior is documented and tested;
- Red is not described as a complete hostile-code sandbox unless its actual confinement guarantees support that claim.

---

## Why a general `Icod.LineEditor.Shared` project is now optional

After the repository audit, the likely Ed/Sed overlap falls into three categories.

### Category 1 — already cross-suite

These belong in the current Shared incubation project and later `Icod.CommandFramework`:

```text
command contexts
option processing
diagnostics
record framing
text decoding
locale providers
regular-expression engine
process execution
temporary workspaces
filesystem capabilities
transactional replacement
```

### Category 2 — similar spelling but different semantics

These must remain separate:

```text
Ed addresses versus Sed addresses
Ed global commands versus Sed branch programs
Ed mutable buffer versus Sed pattern space
Ed undo versus Sed cycle state
Ed file-modified state versus Sed in-place transactions
Red restrictions versus Sed sandbox mode
```

### Category 3 — audited family candidates with no cohesive residual

Possible examples include:

- delimiter-aware scanning of editing expressions;
- source spans for command scripts;
- replacement-template lexical tokens;
- shared command-script diagnostic formatting;
- common adapters from the shared regex engine into substitution commands.

Phase LE9 found that these remain private or internal implementations in Sed and Ed because their grammar, source-location, diagnostic, and mutation contracts do not align.

Phase LE9 completed that comparison:

1. the implementations were compared;
2. their grammar and error semantics were found to differ materially;
3. neutral contracts were confirmed in the current Shared incubation project;
4. Ed-family and Sed-specific implementations were retained with their consumers;
5. no cohesive residual justified `Icod.LineEditor.Shared`.

It is entirely acceptable for the final architecture to have no `Icod.LineEditor.Shared` project:

```text
Icod.CommandFramework
├── Icod.LineEditor.Ed.Shared
│   ├── Icod.LineEditor.Ed
│   └── Icod.LineEditor.Red
└── Icod.LineEditor.Sed
```

That may be cleaner than creating a package containing only thin wrappers over framework APIs.

---

## Revised implementation sequence

The current roadmap records Batches 0 through 20 as complete and places the LineEditor milestones after the consecutive Diffutils block and the Patch milestone. This plan does not require moving the LineEditor work earlier.

When the LineEditor milestone is undertaken, use the following sequence.

## Phase LE0 — Correct documentation and project-policy drift

- [x] Replace stale roadmap references to `Icod.Ed.Shared`, `Icod.Ed.Ed`, and `Icod.Ed.Red`.
- [x] Use `Icod.LineEditor.Ed.Shared`, `Icod.LineEditor.Ed`, and `Icod.LineEditor.Red`.
- [x] Replace stale roadmap references to project `Icod.Sed` with `Icod.LineEditor.Sed`.
- [x] Assign the Ed engine to `Icod.LineEditor.Ed.Shared`, not `Icod.LineEditor.Shared`.
- [x] Keep `Icod.LineEditor.Shared` explicitly optional and evidence-based rather than part of the required initial project list.
- [x] Rename `Icod.CoreUtils.Sed.Tests.csproj` to `Icod.LineEditor.Sed.Tests.csproj`.
- [x] Confirm matching solution-project names and retain all test projects under the centralized `tests` solution folder.
- [x] Add `<LangVersion>13.0</LangVersion>` to the current Ed and Red projects.
- [x] Remove the UTF-8 BOM from the Red project file to follow repository text conventions.
- [x] Record GNU sed 4.10 and GNU ed 1.22.5 in the authoritative ledger.
- [x] Capture the current full solution and Sed test baseline before refactoring in [`Icod.LineEditor-LE0-Baseline.md`](Icod.LineEditor-LE0-Baseline.md).

LE0 is complete. The historical “Change/Replace” examples later in this document remain as rationale showing what was corrected; they are not active project names or ownership policy.

## Phase LE1 — Characterize and decompose the current Sed implementation

- [x] Add missing characterization tests before moving private types.
- [x] Split options, parser, program model, addresses, execution, records, substitution, files, and processes into focused internal modules.
- [x] Keep public behavior and `Icod.LineEditor.Sed.Command` signatures stable.
- [x] Add directory `README.md` files and XML documentation as required.
- [x] Add focused internal tests without deleting the command-level tests.
- [x] Keep the current regex and record behavior temporarily so the structural refactor remains reviewable.

Phase LE1 is complete and behavior-preserving. `Command.cs` now contains only the public facade and orchestration path, while nine partial-class modules make the existing private responsibilities explicit. Characterization tests freeze the current option, script-source, diagnostic, record, sandbox, and in-place-edit behavior until their scheduled semantic phases.

## Phase LE2 — Extend the current Shared regex foundation

- [x] Add an explicit Basic-versus-Extended syntax profile.
- [x] Implement GNU/POSIX ERE in the managed parser and matcher.
- [x] Preserve existing BRE callers and default behavior.
- [x] Add leftmost-longest, capture, repetition, alternation, bracket, locale, cancellation, and diagnostic tests for ERE.
- [x] Update the Shared regular-expression README to identify Sed and Grep as consumers.
- [x] Define byte/text mapping requirements for future byte-preserving matches.

This phase belongs in Shared because it is cross-suite infrastructure. Completion Gate R1 delivered the production foundation before Batch 26; Phase LE2 has now revalidated it against the pinned GNU Sed and GNU Ed baselines. The LineEditor acceptance suite covers syntax, ERE composition, leftmost-longest selection, captures, locale policy, string and byte coordinates, invalid input, diagnostics, cancellation, and resource limits. No production Shared extension was required for the LF-oriented evidence available during LE2. LE4 later supplied concrete `-z` multiline evidence for a narrow command-neutral separator option; the follow-up is recorded in `Icod.LineEditor-LE2-Regex-Contract-Audit.md`.

## Phase LE3 — Migrate Sed to the shared regex provider

- [x] Introduce `SedRegularExpressionCompiler`.
- [x] Preserve Sed's empty-pattern reuse, GNU escape preprocessing, and command-context semantics.
- [x] Route both address and substitution regex compilation through the shared provider.
- [x] Remove the private .NET regex translator only after equivalence tests pass.
- [x] Add GNU Sed differential tests for BRE and ERE.
- [x] Add locale and leftmost-longest cases that .NET regex translation handled incorrectly.

Phase LE3 is complete. The Sed adapter now consumes the Shared managed GNU provider without moving Sed state into Shared. It retains the exact last compiled expression across address and substitution contexts, owns `I`/`M` modifiers, GNU escape preprocessing, POSIX-mode interpretation, controlled diagnostic presentation, and GNU zero-length global-substitution progression. The previous `System.Text.RegularExpressions` translation path is gone, and the migration suite includes GNU sed 4.10 cases for BRE, ERE, captures, locale classes, empty-expression reuse, multiline anchors, control and numeric escapes, strict-POSIX bracket behavior, repeated empty matches, and leftmost-longest selection. Phase LE4 has now completed the byte-preserving record and text migration.

## Phase LE4 — Correct Sed record and text semantics

- [x] Introduce byte-preserving `SedInputRecord`.
- [x] Use Shared record framing for LF and NUL modes.
- [x] Preserve CR as data.
- [x] Preserve explicit final-record termination.
- [x] Define C/POSIX byte-locale matching and UTF-8 decoding behavior.
- [x] Preserve invalid source bytes according to the selected profile.
- [x] Write output separators explicitly as Sed data.
- [x] Add CRLF, lone CR, invalid UTF-8, NUL, huge-record, and unterminated-record tests.
- [x] Document that current pattern and hold spaces may grow according to Sed semantics.

Phase LE4 is complete as a semantic change separate from the LE1 decomposition. The CLI now consumes raw byte streams, LF and NUL framing comes from Shared records, CR remains data, final termination is explicit, malformed UTF-8 is preserved deterministically, and output separators are never selected from the host newline. Internal pattern/hold-space line operations select LF or NUL consistently, and Shared line-sensitive regex matching now accepts a caller-selected logical separator plus explicit NUL-dot policy for `-z`. The detailed contract and test matrix are recorded in `Icod.LineEditor-LE4-Record-and-Text-Semantics.md`; that contract is retained by the now-complete Phase LE5 orchestration boundary.

## Phase LE5 — Harden Sed capabilities

- [x] Add a `CommandContext` core overload.
- [x] Introduce injectable shell and external-file capabilities.
- [x] Enforce sandbox restrictions at compile and runtime layers.
- [x] Preserve Shared `ProcessRunner` rather than replacing it.
- [x] Isolate in-place editing behind `InPlaceEditor`.
- [x] Add failure-injection and cleanup tests.
- [x] Defer final atomic replacement internals until Completion Gate E6.
- [x] Remove `Environment.NewLine` from Sed data serialization and script-source joining.

Phase LE5 is complete. The detailed orchestration, script-source, sandbox, capability, and provisional in-place-edit contracts are recorded in `Icod.LineEditor-LE5-Orchestration-and-Capabilities.md`. Phases LE6 through LE10 are complete and Completion Gate F1 is active.

## Phase LE6 — Create `Icod.LineEditor.Ed.Shared`

- [x] Create the library and dedicated test project.
- [x] Design the mutable buffer, line identity, marks, undo, and editor state.
- [x] Implement Ed addresses and command parsing independently from Sed addresses.
- [x] Consume the shared BRE provider.
- [x] Consume Shared records, process, temporary, and filesystem contracts.
- [x] Define file and process security capabilities.
- [x] Add textual compatibility fixtures for Ed scripts emitted by GNU Diffutils and `Icod.DiffUtils`.

Phase LE6 is complete. The reusable engine, security/capability boundary, Shared-contract consumption, compatibility fixtures, and LE7/LE8 migration boundary are documented in `Icod.LineEditor-LE6-Ed-Shared-Engine.md`.

## Phase LE7 — Rebuild `Icod.LineEditor.Ed`

- [x] Retain `Icod.LineEditor.Ed.Command`.
- [x] Replace the current seed internals with the shared Ed engine.
- [x] Implement the standard security profile.
- [x] Add GNU Ed conformance tests.
- [x] Keep the executable assembly name `ed`.

Phase LE7 is complete. The command boundary, option and session policy, standard/restricted composition, byte-stream contract, exit statuses, and command-level validation matrix are documented in `Icod.LineEditor-LE7-Ed-Command.md`.

## Phase LE8 — Implement `Icod.LineEditor.Red`

- [x] Retain `Icod.LineEditor.Red.Command`.
- [x] Use the same Ed engine.
- [x] Select the restricted security profile.
- [x] Make `red` and `ed --restricted` equivalent.
- [x] Add shell, path, filename-state, link, race, and platform-path adversarial tests.
- [x] Keep the executable assembly name `red`.

Phase LE8 is complete. The permanent restricted command, shared immutable profile, parser/dispatcher and process-capability defenses, host-independent pathname policy, captured-directory and pathname-only confinement contract, and adversarial validation matrix are recorded in `Icod.LineEditor-LE8-Red-Restricted-Profile.md`.

## Phase LE9 — Perform the actual LineEditor sharing audit

- [x] Compare Sed and Ed parser primitives.
- [x] Compare replacement-template grammars and diagnostics.
- [x] Separate cross-suite candidates from editor-family candidates.
- [x] Confirm cross-suite code in the current Shared incubation project; no additional production move was required.
- [x] Keep Ed-only code in `Icod.LineEditor.Ed.Shared`.
- [x] Keep Sed-only code in `Icod.LineEditor.Sed`.
- [x] Create `Icod.LineEditor.Shared` only if a cohesive residual library remains; the audit found none and does not create the project.
- [x] Record consumer evidence and dependency direction for every retained or previously moved API.

Phase LE9 is complete. Ed and Sed delimiter parsing, address state, replacement templates, diagnostics, mutation models, and security policies were compared in detail. Similar-looking syntax did not form a stable family contract. Existing neutral contracts remain in the Shared incubation project, Ed/Red state remains in `Icod.LineEditor.Ed.Shared`, and Sed state remains in `Icod.LineEditor.Sed`. `Icod.LineEditor.Shared` is therefore not created. The classification matrix, consumer evidence, dependency graph, and reopening criteria are recorded in `Icod.LineEditor-LE9-Sharing-Audit.md`; architecture-boundary tests enforce the result.

## Phase LE10 — Integrate the later filesystem transaction gate

After Completion Gate E6:

- [x] migrate Sed in-place editing to the shared transaction model;
- [x] migrate Ed write/replacement operations where applicable;
- [x] preserve command-specific backup and write policies;
- [x] add atomicity, rollback, metadata, symlink, and cleanup tests;
- [x] remove temporary command-local replacement mechanisms.

Phase LE10 is complete. Sed maps each in-place input to one E6 recovery unit, retains requested explicit backups, restores pre-existing backups during rollback, resolves `--follow-symlinks` before no-follow planning, and rejects unsupported terminal indirection without a nontransactional fallback. Ed stages whole-file writes and creations through E6, resolves terminal symbolic-link targets, preserves representable metadata, and retains direct append semantics. Both integrations use authoritative observations, stable-identity preconditions, durable secure sibling staging, structured transaction diagnostics, cancellation rollback, and deterministic cleanup. The implementation and validation matrix are recorded in `Icod.LineEditor-LE10-Transactional-Replacement.md`; Completion Gate F1 is now active.

---

## Recommended final namespace and project structure

## Required projects

```text
Icod.LineEditor.Ed.Shared
Icod.LineEditor.Ed
Icod.LineEditor.Red
Icod.LineEditor.Sed
```

## Required public command classes

```text
Icod.LineEditor.Ed.Command
Icod.LineEditor.Red.Command
Icod.LineEditor.Sed.Command
```

## Optional project

```text
Icod.LineEditor.Shared
```

Phase LE9 found no cohesive residual library and therefore did not create it. Add it only if later completed consumers provide new evidence that reopens the audit.

## Likely namespaces

### Ed shared engine

```text
Icod.LineEditor.Ed
Icod.LineEditor.Ed.Addresses
Icod.LineEditor.Ed.Buffering
Icod.LineEditor.Ed.Commands
Icod.LineEditor.Ed.Files
Icod.LineEditor.Ed.Parsing
Icod.LineEditor.Ed.Processes
Icod.LineEditor.Ed.Security
Icod.LineEditor.Ed.State
Icod.LineEditor.Ed.Undo
```

### Sed engine

```text
Icod.LineEditor.Sed
Icod.LineEditor.Sed.Addresses
Icod.LineEditor.Sed.Execution
Icod.LineEditor.Sed.Files
Icod.LineEditor.Sed.Options
Icod.LineEditor.Sed.Processes
Icod.LineEditor.Sed.Records
Icod.LineEditor.Sed.RegularExpressions
Icod.LineEditor.Sed.Scripting
Icod.LineEditor.Sed.Substitution
```

A project name ending in `.Shared` does not require namespaces ending in `.Shared`.

---

## Recommended roadmap corrections

The current roadmap has already adopted the `Icod.LineEditor` namespace family in several places, but older names remain.

The following corrections are recommended.

### Development architecture

Change:

```text
Icod.Ed.Shared
```

to:

```text
Icod.LineEditor.Ed.Shared
```

in the suite-specific Shared library lists and ultimate architecture examples.

### Temporary project inventory

Change the required LineEditor inventory from:

```text
Icod.LineEditor.Shared
Icod.LineEditor.Ed
Icod.LineEditor.Red
Icod.LineEditor.Sed
```

to:

```text
Icod.LineEditor.Ed.Shared
Icod.LineEditor.Ed
Icod.LineEditor.Red
Icod.LineEditor.Sed
optional Icod.LineEditor.Shared after a consumer audit
```

### Suite-specific ownership

Replace the current statement that `Icod.LineEditor.Shared` owns the Ed engine with:

```text
Icod.LineEditor.Ed.Shared owns Ed/Red address parsing, command parsing,
mutable line buffers, marks, substitutions, global commands, undo,
file operations, shell integration, and restricted-mode enforcement.
```

Retain:

```text
Icod.LineEditor.Sed owns Sed-specific script parsing, address and range state,
pattern and hold spaces, branching, command-cycle behavior, substitutions,
sandbox policy, and in-place-editing semantics.
```

Add:

```text
A general Icod.LineEditor.Shared project is created only if completed Ed and
Sed implementations demonstrate cohesive editor-family reuse that is neither
cross-suite Icod.CommandFramework material nor specific to one engine.
```

### Ed milestone

Replace the stale milestone with:

```markdown
### In-solution suite incubation milestone — `Icod.LineEditor.Ed` and `Icod.LineEditor.Red`

- [ ] Create `Icod.LineEditor.Ed.Shared` and its test project.
- [ ] Retain or rebuild `Icod.LineEditor.Ed` with public
      `Icod.LineEditor.Ed.Command`.
- [ ] Retain or complete `Icod.LineEditor.Red` with public
      `Icod.LineEditor.Red.Command`.
- [ ] Record GNU ed 1.22.5 as the authoritative baseline.
- [ ] Put the complete mutable Ed engine and Red restricted-mode policy in
      `Icod.LineEditor.Ed.Shared`.
- [ ] Make `red` and `ed --restricted` select the same engine profile.
- [ ] Consume common regex, record, process, temporary, filesystem, and
      transactional-replacement contracts from the current Shared incubation
      project.
- [ ] Establish textual compatibility fixtures for Ed scripts emitted by GNU
      Diffutils and `Icod.DiffUtils`.
- [ ] Preserve lowercase assembly names `ed` and `red`.
```

### Sed milestone

Replace the stale project names and acknowledge existing progress:

```markdown
### In-solution suite incubation milestone — `Icod.LineEditor.Sed`

This milestone preserves completed historical Batch 2 and the already-completed
project and namespace rename.

- [ ] Retain `Icod.LineEditor.Sed` with lowercase assembly name `sed` and
      public `Icod.LineEditor.Sed.Command`.
- [ ] Rename the stale Sed test project filename and normalize solution display
      names.
- [ ] Record GNU sed 4.10 as the authoritative baseline.
- [ ] Decompose the current monolithic command into internal parser, program,
      address, execution, record, substitution, process, and file modules.
- [ ] Extend and consume the Shared GNU regex provider for BRE and ERE rather
      than translating patterns to .NET Regex.
- [ ] Consume byte-preserving Shared record and text contracts for LF, NUL,
      invalid-input, and incomplete-final-record behavior.
- [ ] Keep Sed-specific pattern space, hold space, address state, branching,
      command cycle, sandbox policy, and in-place-editing policy in
      `Icod.LineEditor.Sed`.
- [ ] Isolate in-place editing now and consume Completion Gate E6 transaction
      contracts when available.
- [ ] Do not create `Icod.LineEditor.Shared` merely to wrap existing
      cross-suite Shared APIs.
```

### Completion Gate G

Inventory:

```text
Icod.LineEditor.Ed.Shared
```

as a definite suite engine.

Inventory:

```text
Icod.LineEditor.Shared
```

only if it was created after the evidence-based sharing audit.

---

## Testing additions required before claiming conformance

The current Sed test suite is a useful functional baseline, but the revised architecture needs additional categories.

### Structural characterization

- every existing test must pass before and after file decomposition;
- debug output and diagnostic wording should be captured where contractual;
- option ordering and multiple script-source ordering should be tested;
- public compatibility overloads should remain functional.

### Regular expressions

- GNU BRE grouping, intervals, back-references, alternation extensions, and empty expressions;
- GNU/POSIX ERE grouping, alternation, intervals, and repetition;
- leftmost-longest cases that differ from .NET's default behavior;
- bracket classes and locale providers;
- invalid-pattern diagnostics with stable source locations;
- cancellation and resource limits;
- both address and substitution contexts.

### Records and encoding

- LF records;
- NUL records;
- CRLF input with CR preserved as data;
- lone CR;
- empty records;
- an unterminated final record;
- invalid UTF-8 in C and UTF-8 profiles;
- byte-for-byte unchanged pass-through;
- huge individual records;
- pattern space formed from multiple records;
- hold-space growth;
- output after `q`, `Q`, `n`, `N`, `D`, and `P`.

### Script sources

- multiple `-e` fragments;
- multiple `-f` files;
- mixed `-e` and `-f` order;
- a fragment ending in backslash;
- comments and labels crossing source boundaries;
- source-specific diagnostics;
- no host-line-ending dependence.

### Sandbox and shell execution

- compile-time denial;
- runtime capability denial;
- `e` commands;
- substitution `e` flags;
- file read and write commands;
- nested or branched paths;
- child exit status;
- child stderr;
- cancellation;
- Windows and Unix shell invocation profiles.

### In-place editing

- exclusive temporary creation;
- backup suffixes;
- wildcard backup suffixes;
- existing backup collision;
- symlink following and no-follow behavior;
- mode preservation;
- timestamps and ownership where supported;
- write failure;
- flush failure;
- backup failure;
- replacement failure;
- metadata restoration failure;
- cancellation;
- cleanup and rollback;
- no data loss between original removal and final installation.

### Ed and Red

- complete address grammar;
- current-address transitions;
- marks;
- global commands;
- substitutions;
- undo;
- modified-state protection;
- read/write/file commands;
- shell filters;
- signal and cancellation behavior;
- `red` and `ed --restricted` equivalence;
- shell denial;
- parent, absolute, subdirectory, drive-relative, UNC, device, and alternate-stream paths;
- symlinks, hard links, reparse points, and races;
- textual Ed scripts emitted by Diffutils.

---

## Final conclusion

The repository already contains most of the infrastructure that the previous plan proposed placing in `Icod.LineEditor.Shared`.

Therefore, the proper revised design is:

```text
Current Shared incubation project
    future Icod.CommandFramework candidates
            │
            ├── Icod.LineEditor.Sed
            │       └── Icod.LineEditor.Sed.Command
            │
            └── Icod.LineEditor.Ed.Shared
                    ├── Icod.LineEditor.Ed
                    │       └── Icod.LineEditor.Ed.Command
                    └── Icod.LineEditor.Red
                            └── Icod.LineEditor.Red.Command
```

The principal decisions are:

- keep the current Sed project and namespace;
- decompose Sed before extracting shared editor code;
- extend the existing Shared regex engine for ERE;
- migrate Sed away from .NET-regex translation;
- migrate Sed toward byte-preserving records and exact terminator semantics;
- preserve the existing Shared process and temporary infrastructure;
- isolate and later replace Sed's in-place-editing transaction;
- create `Icod.LineEditor.Ed.Shared` because Ed and Red unquestionably share one engine;
- implement Red as a restricted profile of that engine;
- make `Icod.LineEditor.Shared` optional and evidence-based;
- move true cross-suite abstractions toward `Icod.CommandFramework`, not into another suite wrapper.

This approach follows the current roadmap's incubation philosophy more closely than the earlier plan. It lets actual consumers determine the final package boundaries and avoids manufacturing a family-level library before the repository has demonstrated what that library would uniquely own.
