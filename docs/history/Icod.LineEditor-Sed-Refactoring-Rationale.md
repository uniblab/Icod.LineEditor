# Icod.LineEditor and Sed Refactoring Rationale

## Purpose

The creation of the `Icod.LineEditor` namespace is a substantial architectural change, but the namespace rename itself is not the dangerous part. The real risk is that it exposes older design choices in `sed` that should not simply be carried forward under a new name.

The present plan deliberately separates:

```text
identity and project cleanup
        ↓
behavior-preserving internal decomposition
        ↓
targeted semantic corrections and shared-infrastructure adoption
```

That sequence preserves working behavior while establishing a maintainable architecture for GNU Sed, Ed, and Red.

## LE0 through LE2 completion notes

Phase LE0 completed the identity and project-policy cleanup without changing command behavior. The repository now uses the final LineEditor project identities, treats `Icod.LineEditor.Ed.Shared` as the definite Ed/Red engine, keeps a general `Icod.LineEditor.Shared` optional, and records the exact pre-decomposition source and CI baseline in [`Icod.LineEditor-LE0-Baseline.md`](Icod.LineEditor-LE0-Baseline.md).

Phase LE1 completed the behavior-preserving decomposition described by this rationale. The public `Icod.LineEditor.Sed.Command` boundary and both established entry-point signatures remain unchanged. Its former single-file implementation is now divided into focused partial-class modules for options, scripting, addresses, execution, records, regular expressions, substitution, processes, and files. The decomposition deliberately retains the temporary behaviors assigned to LE3, LE4, LE5, and LE10; characterization and structural tests make those boundaries explicit.

Phase LE2 completed the Shared regular-expression contract audit. The managed Gate R1 foundation already satisfies the cross-suite GNU Sed and GNU Ed needs for BRE/ERE syntax, leftmost-longest matching, captures, locale injection, authoritative string and byte coordinates, invalid-input policy, diagnostics, cancellation, and resource limits. No production Shared extension was required. The audit deliberately leaves empty-pattern reuse, address/substitution context, match iteration, replacement grammar, output encoding, and Sed diagnostics in `Icod.LineEditor.Sed`; these become the adapter and migration responsibilities of LE3.

Phase LE3 completed that migration through a private Sed adapter over the Shared managed GNU BRE/ERE providers. Empty-expression reuse, modifiers, GNU escape preprocessing, POSIX interpretation, replacement context, zero-length progression, and diagnostic presentation remain command-owned.

Phase LE4 completed the record and text-semantic correction. Sed now consumes Shared byte-record framing, preserves CR and explicit final termination, selects C/POSIX byte or UTF-8 decoding from the Shared locale environment, round-trips malformed UTF-8 deterministically, and emits LF or NUL separators explicitly. Pattern and hold spaces remain Sed-owned mutable text states and may grow according to command semantics; unrelated completed records are not retained beyond the one-record lookahead required for `$`.

Phase LE5 completed the orchestration and side-effect hardening. `CommandContext` is the primary entry path, script inputs retain source identity and LF-only composition, shell and auxiliary-file operations are injectable, sandbox denial exists at compile and runtime layers, Shared `ProcessRunner` remains the host process mechanism, and current in-place replacement is isolated behind `IInPlaceEditor` with secure temporary-object cleanup characterization. Final publication through E6 remains assigned to LE10.

## 1. The current state is already beyond a raw rename

The Sed project already uses:

```text
Project:       Icod.LineEditor.Sed
Assembly:      sed
Namespace:     Icod.LineEditor.Sed
Command class: Icod.LineEditor.Sed.Command
```

It targets `net10.0`, references the current Shared incubation project, and has already been recognized in the roadmap as structurally migrated.

The immediate question is therefore not how to rename Sed. It is how to turn the existing implementation into a maintainable `Icod.LineEditor.Sed` engine without breaking behavior that already works.

Historical Batch 2 should remain complete. The later LineEditor work is a re-audit and architectural modernization, not an erasure of earlier work.

# Principal areas of concern

## 2. Before LE1, `Command.cs` owned almost everything

The pre-LE1 Sed implementation was concentrated in one very large `Command.cs`. It contains:

- command orchestration;
- options;
- script parsing;
- addresses;
- instructions;
- pattern and hold spaces;
- record reading;
- regular expressions;
- substitutions;
- shell execution;
- auxiliary file access;
- in-place editing.

This has several consequences.

### Large blast radius

A change to regex compilation, record framing, or file replacement occurs in the same source unit as the parser and execution state.

### Poor isolation

Command-level tests are useful, but they do not make it easy to isolate parser failures, address-state bugs, substitution behavior, sandboxing, or transaction failures.

### Accidental sharing risk

When Ed is implemented, it would be easy to move a Sed helper into common code merely because it looks reusable. Similar command syntax does not necessarily imply shared semantics.

### Completed first step

LE1 decomposed Sed without changing public behavior:

```text
Command
├── Options
├── Scripting
├── Addresses
├── Execution
├── Records
├── RegularExpressions
├── Substitution
├── Files
└── Processes
```

The public class remains:

```text
Icod.LineEditor.Sed.Command
```

This exposes ownership, enables focused tests, reduces later merge conflicts, and lets sharing decisions be based on cohesive components rather than textual proximity.

## 3. Do not create `Icod.LineEditor.Shared` too early

The current Shared project already contains or incubates:

- argument parsing;
- diagnostics;
- delimiter and escape handling;
- records;
- text and locale abstractions;
- regular expressions;
- process execution;
- temporary workspaces;
- filesystem services;
- platform capability reporting.

Most of the initially imagined LineEditor-shared features are actually cross-suite:

```text
record readers
regex providers
source diagnostics
process launching
temporary workspaces
filesystem transactions
```

They are also useful to Grep, Diffutils, Patch, Coreutils, Tar, and other suites. They are therefore better treated as eventual `Icod.CommandFramework` material.

Creating `Icod.LineEditor.Shared` immediately could produce a redundant layer:

```text
Icod.LineEditor.Sed
        ↓
Icod.LineEditor.Shared
        ↓
current Shared
```

without a clear independent responsibility.

The revised plan therefore makes `Icod.LineEditor.Ed.Shared` definite, because Ed and Red unquestionably share one engine, but makes `Icod.LineEditor.Shared` optional and evidence-based.

It should be created only when completed Ed and decomposed Sed implementations reveal code that is:

1. genuinely consumed by both;
2. not general enough for `Icod.CommandFramework`;
3. not specific to Ed;
4. not specific to Sed;
5. cohesive enough to justify another assembly.

## 4. Sed and Ed share syntax mechanics, not execution models

They share concepts such as regular expressions, delimiter-scanned patterns, substitutions, script diagnostics, and file or process effects.

Their execution models are fundamentally different.

### Ed

```text
persistent mutable line buffer
current address
last address
marks
cut buffer
undo
arbitrary insertion and deletion
global operations over selected lines
modified-file state
```

### Sed

```text
input record cycle
pattern space
hold space
automatic printing
append queue
labels and branches
per-record address evaluation
streaming input progression
```

An Ed address identifies a line in a mutable collection. A Sed address is generally a predicate over the current input cycle, possibly with range state across cycles.

They should not share:

- one address hierarchy;
- one command AST;
- one execution state;
- one global-command engine;
- one file-state model;
- one editor session.

Only lower-level mechanics should be considered for sharing, such as source spans, delimiter scanning, replacement-template tokenization, and adapters over the common regex engine.

## 5. The former regular-expression approach was a major concern

Before LE3, Sed translated GNU-style expressions into .NET regex syntax and invoked `System.Text.RegularExpressions`. LE3 removed that path and now consumes the Shared managed GNU provider through a Sed-specific adapter.

GNU/POSIX and .NET matching can differ in:

- leftmost-longest behavior;
- bracket expressions;
- locale character classes;
- back-references;
- malformed-expression handling;
- GNU extensions;
- BRE versus ERE semantics.

Completion Gate R1 now provides the managed GNU/POSIX BRE and ERE foundation. Phase LE2 verified its syntax, selection, capture, locale, byte/text coordinate, diagnostic, cancellation, and resource contracts against the pinned Sed and Ed baselines. No additional cross-suite production API was required.

The completed sequence is:

```text
Gate R1 Shared BRE/ERE contract (complete and LE2-validated)
        ↓
SedRegularExpressionCompiler owns Sed-specific policy
        ↓
private .NET translation layer removed in LE3
```

Shared should own:

- syntax profile;
- compilation;
- matching;
- captures;
- locale integration;
- cancellation;
- diagnostics.

Sed should own:

- BRE or ERE selection;
- empty-pattern reuse;
- address versus substitution context;
- option interactions;
- replacement iteration;
- Sed-specific diagnostics.

This is cross-suite work because Grep also needs BRE and ERE.

## 6. Sed's authoritative model should not be only `string` lines

The Shared project already has byte-preserving record abstractions that distinguish:

- record content;
- separator;
- terminated versus unterminated final record;
- LF and NUL framing.

Sed data semantics require preserving distinctions such as:

```text
abc\r\n
abc\n
abc\r
unterminated final record
invalid UTF-8
NUL-delimited records
embedded newlines in pattern space
```

For Sed:

```text
LF is framing data in ordinary mode
NUL is framing data under -z
CR is normally data
```

A `TextReader`-only model can erase important distinctions.

The plan does not require an immediate byte-only rewrite. It introduces a Sed record model that retains:

```text
authoritative bytes
separator kind
termination state
source identity
record number
optional decoded representation
byte-to-text mapping
```

The engine may still use text where appropriate, but it no longer loses facts needed for exact output.

This infrastructure will also benefit Ed without forcing Sed and Ed into one editor engine.

## 7. Script-source composition must not depend on host newlines

Sed scripts may come from:

```text
-e expression
-f script file
implicit first operand
```

Combining fragments with `Environment.NewLine` makes grammar host-dependent.

Each source should instead be represented explicitly:

```text
ScriptSource
├── source kind
├── source name
├── content
├── original line and column information
└── synthetic-boundary policy
```

The parser may consume a composite program, while diagnostics still report the correct source.

Any inserted separator should be an explicit Sed grammar separator, not whichever newline the host uses.

## 8. In-place editing is a data-integrity boundary

Sed's current in-place editing already includes useful support for backups, modes, and symlink options, but it still uses command-local replacement mechanics.

Concerns include:

- exclusive temporary creation;
- backup creation;
- original removal;
- final installation;
- metadata restoration;
- cancellation between stages;
- flush or write failures;
- symlink and reparse-point behavior;
- rollback;
- orphaned temporary files.

A sequence such as:

```text
move original to backup
move temporary into place
```

can leave the original pathname absent if the second operation fails.

The plan therefore:

1. isolates current behavior behind an internal `InPlaceEditor`;
2. adds characterization and failure-injection tests;
3. keeps parser and execution code independent of commit mechanics;
4. later replaces the implementation with the shared Completion Gate E6 transaction service.

This avoids creating a permanent Sed-only transaction layer just before a general repository-wide one is scheduled.

## 9. Sed sandboxing and Red restrictions are related, not identical

Both restrict dangerous capabilities, but their policies differ.

### Red

- denies shell execution;
- restricts filenames to the permitted current-directory form;
- otherwise uses the normal Ed engine.

### Sed sandbox mode

- denies shell execution;
- denies external file reads and writes defined by GNU Sed sandbox policy.

They may share low-level process and filesystem mechanisms, but they should not share one policy object.

Sed should use defense in depth:

```text
compile-time rejection
        +
runtime denied capability
```

For example:

```text
ISedShellExecutor
├── ProcessRunnerShellExecutor
└── DeniedShellExecutor
```

Red should use the same design principle inside `Icod.LineEditor.Ed.Shared`, with Ed-specific file and process policies.

## 10. Preserve tests before semantic correction

The existing Sed implementation already has significant behavior and a historical completed batch.

The refactor should therefore proceed in this order:

### Characterize

Add tests for current behavior and edge cases not already covered.

### Decompose

Move types into focused modules without changing semantics.

### Replace one subsystem at a time

For example:

```text
private .NET regex translation
        ↓
Shared BRE and ERE provider
```

Then run the full Sed suite.

Next:

```text
decoded record path
        ↓
byte-preserving record path
```

Then run the full suite.

Next:

```text
command-local replacement
        ↓
shared transaction service
```

This keeps regressions attributable to one change.

# Why `Icod.LineEditor.Ed.Shared` is different

## 11. Ed and Red have proven engine-level reuse

Red is restricted Ed. Both require the same:

- mutable line buffer;
- address model;
- marks;
- global commands;
- substitutions;
- undo;
- file state;
- command parser;
- diagnostics and status model.

The difference is security profile and executable identity.

Therefore this is justified immediately:

```text
Icod.LineEditor.Ed.Shared
├── complete Ed engine
├── standard security profile
└── restricted security profile
```

with thin entry points:

```text
Icod.LineEditor.Ed.Command
Icod.LineEditor.Red.Command
```

# Why the present plan is a good approach

## 12. It avoids a big-bang rewrite

A big-bang change would combine:

- namespace and project changes;
- parser decomposition;
- regex replacement;
- record-model replacement;
- security changes;
- in-place editing changes;
- Ed implementation;
- Red implementation.

When tests failed, attribution would be difficult. The phased plan keeps each step reviewable.

## 13. It follows the repository's incubation philosophy

The repository is intentionally a multi-suite development workspace.

The plan keeps:

- cross-suite regex and record mechanics in the current Shared incubation project;
- Ed and Red state in `Icod.LineEditor.Ed.Shared`;
- Sed state in `Icod.LineEditor.Sed`;
- `Icod.LineEditor.Shared` optional until actual residual reuse is demonstrated.

This provides evidence for the final package split.

## 14. It preserves narrow dependency direction

The desired dependencies are:

```text
current Shared incubation project
        ↓
Icod.LineEditor.Sed
```

and:

```text
current Shared incubation project
        ↓
Icod.LineEditor.Ed.Shared
        ↓
Icod.LineEditor.Ed
Icod.LineEditor.Red
```

There is no Sed dependency on the Ed engine, no Ed dependency on Sed, no circular family package, and no duplicate regex or record foundation.

## 15. It lets code move to the correct eventual owner

### Likely `Icod.CommandFramework`

- command contexts;
- option parser;
- diagnostics;
- records;
- text decoding;
- GNU regex;
- process execution;
- temporary files;
- filesystem capabilities;
- transactions.

### Definitely `Icod.LineEditor.Ed.Shared`

- mutable Ed buffer;
- Ed addresses;
- marks;
- global-command state;
- undo;
- Ed file state;
- Red restrictions.

### Definitely Sed-specific

- pattern space;
- hold space;
- Sed range state;
- labels and branching;
- cycle control;
- append queues;
- Sed sandbox policy;
- Sed in-place option policy.

### Possible `Icod.LineEditor.Shared`

Only when proven:

- replacement-template lexing;
- common delimiter scanning;
- editing-script source diagnostics.

## 16. It respects the established public names

The architecture keeps:

```text
Icod.LineEditor.Ed.Command
Icod.LineEditor.Red.Command
Icod.LineEditor.Sed.Command
```

Supporting types use responsibility-oriented names such as:

```text
Icod.LineEditor.Ed.EditorSession
Icod.LineEditor.Ed.EditorBuffer
Icod.LineEditor.Sed.ScriptParser
Icod.LineEditor.Sed.PatternSpace
Icod.LineEditor.Sed.InPlaceEditor
```

# Recommended practical sequence

## Stage 1 — Repository and characterization cleanup

- normalize stale project and test names;
- confirm the Sed baseline;
- add missing characterization tests;
- make no major semantic changes.

## Stage 2 — Decompose Sed

- retain `Icod.LineEditor.Sed.Command`;
- move implementation into internal modules;
- preserve behavior.

## Stage 3 — Complete Shared BRE and ERE infrastructure — completed

- Completion Gate R1 extended the Shared regex engine;
- Phase LE2 added LineEditor-oriented cross-suite acceptance tests;
- the audit found no need for a duplicate Sed regex engine or another Shared contract.

## Stage 4 — Migrate Sed regular-expression behavior — completed

- replaced the .NET translation layer with `SedRegularExpressionCompiler` over the Shared managed GNU BRE/ERE provider;
- preserved Sed-specific empty-expression state, modifiers, GNU escape preprocessing, GNU/POSIX policy, match iteration, replacement context, and diagnostics;
- added GNU sed 4.10 differential coverage for BRE, ERE, captures, locale classes, control and numeric escapes, strict-POSIX bracket behavior, repeated zero-length matches, and leftmost-longest behavior.

## Stage 5 — Correct record and encoding semantics — completed

- `SedInputRecord` preserves authoritative bytes, source identity, aggregate and per-source numbers, separator kind, and final termination;
- Shared LF/NUL byte-record framing preserves CR and malformed input;
- the CLI uses raw standard streams and the public text facade uses compatibility adapters;
- C/POSIX byte and UTF-8 profiles define matching, byte/text mapping, and replacement encoding;
- LF and NUL are emitted explicitly as Sed data;
- LF/NUL also select the internal pattern-space separator for `N`, `D`, `P`, `H`, `G`, `W`, and multiline anchors;
- the Shared regex contract gained only the consumer-proven configurable line separator and NUL-dot options, with source-compatible defaults;
- CRLF, lone CR, invalid UTF-8, NUL, empty, huge, multiline, hold-space, and unterminated inputs are covered.

## Stage 6 — Harden process, sandbox, and file effects — completed

- added compile-time and runtime sandbox denial capabilities;
- isolated in-place editing behind `IInPlaceEditor`;
- preserved Shared `ProcessRunner`;
- added source-aware script composition, injectable auxiliary files, failure injection, and temporary cleanup coverage.

## Stage 7 — Implement `Icod.LineEditor.Ed.Shared`

- design the mutable buffer and state machine;
- consume shared regex, record, process, and filesystem services.

## Stage 8 — Implement Ed

- use the standard security profile;
- complete GNU Ed behavior.

## Stage 9 — Implement Red

- use the same engine;
- apply restricted file and process capabilities;
- add adversarial tests.

## Stage 10 — Audit residual sharing

- compare completed Sed and Ed components;
- create `Icod.LineEditor.Shared` only if justified.

## Stage 11 — Integrate shared transaction infrastructure

- migrate Sed in-place editing;
- migrate Ed write replacement where applicable;
- test rollback and metadata behavior.

# Central reasoning

The plan rests on one principle:

> Do not decide final library ownership from command names or apparent syntactic similarity. Decide it from completed implementations and real consumers.

`Icod.LineEditor` is the right namespace family because it gives Ed, Red, and Sed a coherent home and avoids poor namespace and type names.

Namespace-family membership does not imply one shared engine.

The architecture therefore distinguishes:

```text
common command infrastructure
        → eventual Icod.CommandFramework

Ed and Red mutable editor engine
        → Icod.LineEditor.Ed.Shared

Sed streaming cycle engine
        → Icod.LineEditor.Sed

possible residual family mechanics
        → optional Icod.LineEditor.Shared
```

This approach minimizes regression risk, avoids duplicate foundations, protects GNU semantics, supports strong Red restrictions, improves Sed's regex and record fidelity, and leaves clean package boundaries for the final repository split.
