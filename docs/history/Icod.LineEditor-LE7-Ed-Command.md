# LineEditor Phase LE7 — `ed` command migration

## Scope

Phase LE7 replaces the historical `ed` seed with a command/session host over
`Icod.LineEditor.Ed.Shared`. The reusable engine remains authoritative for Ed
addresses, mutable buffer state, stable line identity, marks, cut buffers,
substitution, global execution, undo, file commands, shell filters, and
controlled engine diagnostics. The executable owns GNU ed 1.22.5 invocation
and process-boundary policy.

The public and packaging identities remain:

```text
Icod.LineEditor.Ed.Command
assembly: ed
framework: net10.0
language: C# 13
```

## Command boundary

`Command` exposes the repository's established forms:

- synchronous `Run` over optional text streams;
- cancellation-aware `RunAsync` over text streams;
- byte-preserving `RunAsync` over `Stream` instances;
- the primary `RunAsync(string[] args, CommandContext context)` path;
- a dedicated usage writer.

When `CommandContext` supplies binary standard streams, those streams are
used directly. The text-only compatibility path bridges through UTF-8 without
taking ownership of caller-provided readers or writers.

`Program.Main` is asynchronous, creates the console `CommandContext`, and
maps Ctrl+C to cooperative cancellation.

## GNU invocation policy

The command parser accepts the GNU ed 1.22.5 options implemented by the
pinned profile:

```text
-E, --extended-regexp
-G, --traditional
-l, --loose-exit-status
-p, --prompt=STRING
-q, --quiet, --silent
-r, --restricted
-s, --script
-v, --verbose
--strip-trailing-cr
--unsafe-names
-h, --help
-V, --version
```

It also accepts the GNU operand shape:

```text
ed [OPTION]... [[+LINE] FILE]
```

Initial address selection supports `+`, numeric addresses, forward regular
expression searches, and reverse regular expression searches. A numeric
address beyond the loaded buffer selects the last line, matching the pinned
GNU behavior.

Option parsing uses the shared `OptionParser` and conventional option
formatting. Long-option abbreviation and option permutation remain aligned
with the repository's GNU command-line policy.

## Session orchestration

The executable reads command records through `ByteRecordReader` and supplies
one complete command unit to the shared engine. Input blocks for `a`, `i`, and
`c` remain attached to the command and require the single-period terminator.
LF is the editor's command and data separator; a CR immediately preceding a
terminated command-record LF is accepted as CRLF command input.

The host owns session-only behavior that is not mutable-engine state:

- prompting and `P` toggling;
- verbose-help mode and `H` toggling;
- the second `q` or `e` modified-buffer override;
- `-s` suppression of byte-count presentation;
- `-q` suppression of diagnostics without suppressing child-process stderr;
- routing the `h` help message to standard output;
- noninteractive versus interactive error continuation;
- cancellation, broken-stream, and final exit-status mapping.

## Capability composition

Normal `ed` composes:

```text
EditorSecurityPolicy.Standard
StandardEditorFileAccess
StandardEditorProcessAccess
Shared GNU BRE provider (or ERE under -E)
```

`ed --restricted` composes the same immutable restricted engine profile that
Phase LE8 will use for `red`. File operations pass through the command's GNU
filename-control policy before reaching the engine capability. Newline and
NUL are always rejected in filenames; `--unsafe-names` permits the remaining
GNU-listed control characters. Shell-bearing initial operands and commands
are denied by the restricted process profile.

`--strip-trailing-cr` removes CR only when it is the CR member of a terminated
CRLF record. A CR ending an unterminated final record remains data.

## Exit-status policy

The executable maps the pinned GNU categories as follows:

```text
0  normal completion, including loose-exit-status completion
1  command-line, command, environment, or output failure
2  interrupted execution, modified-buffer refusal, or initial-file problem
```

The shared engine continues to return structured diagnostics and signal state;
the executable determines whether and where those diagnostics are presented.

## Command-level validation

The dedicated `tests/Ed.Tests` project exercises the public command API and
covers:

- help, version, option, prompt, quiet, script, verbose, and loose modes;
- standard and restricted capability composition;
- BRE and ERE command execution;
- initial line selection and oversized-address clamping;
- file loading, byte-count suppression, writing, and CR policy;
- modified state and controlled diagnostics;
- cancellation and broken output;
- long lines and large line counts;
- text-only `CommandContext` compatibility;
- GNU Diffutils-style and Icod Diffutils-style ed-script fixtures without a
  runtime Diffutils dependency.

## Phase boundary

Phase LE7 does not create a second mutable editor implementation and does not
change `red`. Phase LE8 retains `Icod.LineEditor.Red.Command`, hosts this same
engine, and makes `red` and `ed --restricted` select the same restricted
profile with the required adversarial path and shell tests.
