# LineEditor Phase LE9 — Evidence-Based Sharing Audit

## Status

Phase LE9 is complete.

The audit found no cohesive residual library that justifies creating
`Icod.LineEditor.Shared`. Cross-suite facilities already belong to the current
`Icod.CoreUtils.Shared` incubation project, the mutable Ed/Red engine remains
in `Icod.LineEditor.Ed.Shared`, and Sed's streaming program model remains in
`Icod.LineEditor.Sed`.

No production API was moved during LE9. That is the intended result of the
evidence-based audit: every plausible cross-suite API had already been moved
or consumed during earlier gates and LineEditor phases, while the remaining
similar-looking code has different grammar, state, diagnostic, and security
semantics.

## Audit method

The audit compared the completed LE5 Sed boundary with the completed LE6–LE8
Ed/Red implementation. A candidate was considered shareable only when all of
the following were true:

1. at least two completed consumers need the same semantic contract;
2. the consumers agree on parsing, state, diagnostics, cancellation, and
   resource ownership;
3. the candidate is not already a cross-suite Shared responsibility;
4. extracting it reduces duplication without introducing an editor-family
   dependency into a broader suite;
5. the resulting API is cohesive enough to justify an assembly and eventual
   package boundary.

Similarity of command letters, delimiter characters, or helper method names
was not treated as consumer evidence.

## Classification result

| Candidate | Classification | Current owner | Consumer evidence and decision |
|---|---|---|---|
| command context and standard-stream ownership | cross-suite framework candidate | `Icod.CoreUtils.Shared.Diagnostics` | Sed, Ed, Red, and non-editor commands consume the same command boundary. Keep in Shared incubation. |
| option parsing and common command diagnostics | cross-suite framework candidate | current Shared command-line and diagnostics areas | Used across suites. Editor-specific wording and source locations remain command-owned. |
| LF/NUL record framing and final-record termination | cross-suite framework candidate | `Icod.CoreUtils.Shared.Records` | Sed consumes byte-preserving stream records; Ed consumes LF-framed script and file records. The framing contract is shared, not editor-specific. |
| managed GNU BRE/ERE matching | cross-suite framework candidate | `Icod.CoreUtils.Shared.RegularExpressions` | Sed and Ed both consume the Shared provider, as do regex-oriented commands outside LineEditor. Keep matching semantics neutral; keep command-specific pattern reuse and replacement policy local. |
| process execution | cross-suite framework candidate | `Icod.CoreUtils.Shared.Processes` | Sed shell commands and Ed standard process capability use Shared `ProcessRunner`; Red wraps that boundary with denial. No LineEditor wrapper is justified. |
| secure temporary objects and filesystem durability | cross-suite framework candidate | current Shared temporary and filesystem areas | Ed file access already consumes these contracts. Sed's provisional in-place editor is scheduled to consume the E6 transaction model in LE10. |
| text decoding, locale, width, and byte/text policy | cross-suite framework candidate | current Shared text and related areas | Sed and Ed select different command policies over neutral text primitives. No family layer is needed. |
| mutable line buffer, stable identities, marks, cut buffer, undo, remembered filename and shell state | Ed-family-specific | `Icod.LineEditor.Ed.Shared` | Required by both Ed and Red. Sed has pattern/hold spaces and cycle state rather than a persistent addressed line buffer. |
| standard and restricted Ed capabilities, including Red pathname policy | Ed-family-specific | `Icod.LineEditor.Ed.Shared` | `ed --restricted` and `red` are proven consumers of the same immutable profile. Sed sandbox and in-place policies are different. |
| Ed address parser | Ed-family-specific | `Icod.LineEditor.Ed.Shared` | Coupled to current/last buffer addresses, marks, forward/reverse searches, address arithmetic, and semicolon current-address mutation. |
| Sed address and range state | Sed-specific | `Icod.LineEditor.Sed` | Coupled to input record number, last-input state, first~step selection, relative range ends, range activation, negation, and command-cycle execution. |
| Sed program, labels, branches, groups, pattern/hold spaces, and cycle control | Sed-specific | `Icod.LineEditor.Sed` | No Ed/Red consumer exists. These are the defining semantics of the streaming Sed engine. |
| Sed sandbox and in-place-editing policy | Sed-specific | `Icod.LineEditor.Sed` | Sandbox compile/runtime restrictions and per-file replacement policy do not match Red's pathname-only restricted editor profile. Transaction mechanics move to Shared in LE10, but policy remains Sed-owned. |
| Ed command-line/session presentation | command-local | `Icod.LineEditor.Ed` | Prompts, byte counts, initial addresses, verbose diagnostics, signal mapping, and executable options belong to the command facade. |
| Red command-line/session presentation | command-local | `Icod.LineEditor.Red` | The command permanently selects the restricted profile but otherwise remains a thin executable boundary. |
| Sed command-line and script-source orchestration | command-local/Sed-specific | `Icod.LineEditor.Sed` | Ordered `-e`, `-f`, and implicit scripts, source-relative locations, operand handling, and in-place command policy remain Sed-owned. |

## Parser comparison

### Ed

Ed parses one interactive or scripted command against a persistent mutable
buffer. Its address grammar depends on:

- the current and last line addresses;
- marks that identify stable buffer lines;
- forward and reverse regular-expression search;
- relative arithmetic;
- comma and semicolon ranges, where semicolon changes the search origin;
- commands that may subsequently mutate the addressed buffer.

A parser failure is reported as an Ed diagnostic and must preserve the editor
state required by the command's failure contract.

### Sed

Sed compiles a program that is evaluated repeatedly against an input stream.
Its address and selection model depends on:

- current input-record number and last-input state;
- regular-expression addresses evaluated against pattern space;
- GNU first~step selection;
- active range state retained by each compiled command;
- relative and multiple-based range ends;
- command negation, groups, labels, and branches;
- the Sed cycle and pattern/hold-space transitions.

Parser diagnostics retain script-source identity and source-relative line and
column information. Runtime address state belongs to compiled commands rather
than to a mutable line-address object.

### Decision

Delimiter scanning is embedded in two different grammars and failure models.
Extracting a common scanner would either expose policy switches for delimiter,
escape, command-separator, source-span, and end-of-script behavior, or erase
information required by one consumer. The small lexical overlap does not form
a stable family API. Ed parsing stays in `Icod.LineEditor.Ed.Shared`; Sed
parsing stays in `Icod.LineEditor.Sed`.

## Replacement-template comparison

Both commands recognize an unescaped `&` as the complete match and support
numeric capture references, but their complete contracts differ.

### Ed replacement contract

- replacement is part of an addressed mutable-buffer command;
- occurrence selection, global replacement, printing, and error behavior are
  Ed command flags;
- replacement updates one or more stored lines and participates in undo and
  modified-buffer state;
- diagnostics use the Ed execution result model;
- the command may reuse remembered substitution state according to Ed rules.

### Sed replacement contract

- replacement is a compiled command executed during the streaming cycle;
- replacement flags interact with automatic printing, explicit printing,
  branching-on-substitution, and optional file output;
- escape preprocessing and replacement interpretation are Sed policy;
- the result updates pattern space rather than a persistent line buffer;
- diagnostics retain script-source position and Sed command context.

### Decision

The Shared regular-expression provider should continue to return matches and
capture coordinates. It must not own Ed or Sed replacement templates. A common
replacement tokenizer would require command-specific token meaning,
source-position, diagnostics, and mutation callbacks and would not be a
cohesive neutral contract. Each implementation remains local.

## Consumer evidence for existing Shared APIs

| Shared area | Ed/Red evidence | Sed evidence | Other-suite direction |
|---|---|---|---|
| diagnostics / command context | Ed command hosts the engine through `CommandContext`; engine returns controlled editor results | LE5 made the `CommandContext` overload primary and retained source-aware diagnostics | command framework concern used throughout the repository |
| records | Ed file and script input use `ByteRecordReader` | LE4 uses Shared LF/NUL framing and explicit termination | Coreutils text commands also require record framing |
| regular expressions | searches and substitutions use `IRegularExpressionProvider` | LE3 uses the Shared managed GNU BRE/ERE providers | Grep and other regex consumers require the same neutral engine |
| processes | standard Ed shell capability delegates to `ProcessRunner`; Red supplies a denied capability | Sed shell capability delegates to `ProcessRunner` and sandbox supplies denial | process execution is cross-suite infrastructure |
| temporary/filesystem | Ed standard file capability uses secure temporary and filesystem operations | Sed's final transaction migration is scheduled for LE10 | Fileutils, Patch, and other mutation commands consume the same durability model |
| text and locale | Ed selects line-editor policy over byte/text primitives | Sed selects C/POSIX byte or UTF-8 profiles and explicit separators | text semantics are shared by Coreutils, Grep, Diffutils, and Patch |

These APIs retain the dependency direction:

```text
Icod.CoreUtils.Shared incubation project
│
├── Icod.LineEditor.Sed
└── Icod.LineEditor.Ed.Shared
    ├── Icod.LineEditor.Ed
    └── Icod.LineEditor.Red
```

There is no dependency from Sed to the Ed engine, from the Ed engine to Sed,
or from either engine to the executable projects.

## `Icod.LineEditor.Shared` decision

Do not create `Icod.LineEditor.Shared` at this time.

After cross-suite responsibilities are assigned to the current Shared
incubation project, the residual candidates are either Ed-family-specific,
Sed-specific, or command-local. No cohesive implementation remains with both
completed engines as consumers.

The decision may be reopened only when a future change produces at least two
real consumers of an identical LineEditor-family contract that is not
appropriate for `Icod.CommandFramework`. A proposal must identify those
consumers, demonstrate equivalent semantics and diagnostics, and show a
meaningful dependency reduction before a new project is added.

## Enforced architecture

LE9 adds architecture-boundary tests to the Ed.Shared and Sed test projects.
They verify that:

- both engines directly consume `Icod.CoreUtils.Shared`;
- `Icod.LineEditor.Ed.Shared` does not reference Sed or an executable;
- `Icod.LineEditor.Sed` does not reference the Ed engine or an executable;
- neither engine references a speculative `Icod.LineEditor.Shared` assembly.

These tests turn the audit's dependency decision into an executable repository
constraint while leaving LE10 free to move transaction mechanics into Shared
without moving command policy.

## LE10 handoff

LE10 may move secure sibling-temporary, backup, rollback, metadata,
symlink/reparse-point, atomic-replacement, and cleanup mechanics into the
existing Shared transaction boundary. It must preserve:

- Sed in-place option, suffix, per-file, sandbox, and failure policy in Sed;
- Ed write, append, force, modified-buffer, and filename policy in the Ed
  family;
- the dependency direction recorded above.
