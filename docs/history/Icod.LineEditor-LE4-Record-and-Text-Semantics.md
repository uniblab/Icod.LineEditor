# Icod.LineEditor LE4 Record and Text Semantics

## Status

Phase LE4 is complete. This phase changes Sed data semantics without changing the public `Icod.LineEditor.Sed.Command.Run` and `RunAsync` text-stream compatibility signatures, the LE3 regular-expression ownership boundary, or the command-local in-place replacement mechanism scheduled for later hardening.

## Record model

`SedInputRecord` is a private command implementation type with the following authoritative state:

- independently owned source bytes excluding the separator;
- decoded working text;
- source identity and source index;
- aggregate record number;
- per-source record number;
- LF or NUL separator kind;
- explicit final-record termination;
- text-boundary-to-source-byte coordinates where a boundary is representable.

Input is framed by Shared `ByteRecordReader`. LF mode removes only byte `0x0A`; a preceding carriage return remains part of the record. NUL mode removes only byte `0x00`. `InputSequence` retains the current record and one lookahead record so `$` can be evaluated without materializing the remainder of the input.

## Text profiles

Sed resolves the process text profile through `TextLocaleEnvironment`.

### C and POSIX

`LC_ALL=C` or `LC_ALL=POSIX` selects the byte profile. Each source byte maps to one working character with the same numeric value. POSIX character classes therefore classify ASCII/C-locale bytes rather than decoded Unicode scalars. Existing bytes from `0x00` through `0xFF` encode back to the same byte. Newly inserted characters outside that range use UTF-8 as the deterministic replacement encoding.

### UTF-8

Other locale names select the UTF-8 profile. Well-formed UTF-8 maps to Unicode scalars. Each malformed source byte maps to a reserved unpaired UTF-16 low-surrogate code unit that cannot arise from well-formed UTF-8. Shared string matching evaluates that opaque unit as U+FFFD while retaining the original source code unit and indices; the Sed adapter therefore preserves it through pattern/hold-space operations and maps it back to the original byte on output. Ordinary inserted text is UTF-8 encoded.

The record retains byte offsets for valid working-text boundaries. Boundaries inside a surrogate pair are deliberately non-authoritative.

## Output and termination

All Sed data output goes through Shared `DelimitedByteRecordWriter`.

- normal mode emits byte `0x0A` only when the record's termination policy requires it;
- `-z` emits byte `0x00` only when termination is required;
- `TextWriter.NewLine`, `Environment.NewLine`, and the host platform do not select data separators;
- generated records such as `=`, `l`, inserted text, changed text, and appended text are explicitly terminated;
- pattern-space printing preserves the active termination state; when another output operation follows an unterminated record, Sed inserts exactly one configured separator before that later output, matching GNU Sed output-stream state;
- `P` and `W` terminate when they emit a complete internal line, otherwise they preserve the active state;
- `N`, `h`, `H`, `g`, `G`, and `x` propagate the termination state associated with the resulting pattern or hold space;
- internal multiline operations use LF in ordinary mode and NUL under `-z`; this includes `N`, `D`, `P`, `H`, `G`, and `W`;
- `l` renders an internal NUL separator as `\000`, matching GNU Sed list output.

The executable entry point uses `Console.OpenStandardInput` and `Console.OpenStandardOutput`, so byte data is not decoded by `Console.In` before Sed receives it. The established public text-stream facade remains available through streaming compatibility adapters for tests and callers that intentionally operate on .NET text streams.

## NUL-aware regular-expression contract

LE4 provided the first concrete consumer evidence that Shared line-sensitive matching could not remain hard-coded to LF. `RegularExpressionOptions.LineSeparator` now defaults to LF but may be set to NUL, and `DotMatchesNull` explicitly controls the Basic/Extended default NUL exclusion. Sed configures both from `-z`:

- without `M`, dot may consume an internal NUL in NUL-data pattern space;
- with `M`, NUL is the logical line boundary, so `^` and `$` recognize positions around NUL and dot or a negated bracket expression does not consume it;
- an ordinary LF remains data in `-z` pattern space and is not treated as a multiline boundary.

The defaults retain the pre-LE4 Shared behavior. Sed continues to own `-z` syntax, pattern-space assembly, and modifier policy.

## Memory invariant

Sed does not retain unrelated completed input records. Shared framing materializes one logical record at a time, and `InputSequence` retains one additional lookahead record solely for last-record addressing.

This is not a fixed-memory guarantee for the current command state. GNU Sed semantics permit:

- one input record to be arbitrarily large;
- `N` to grow pattern space;
- `H`, `G`, `h`, `g`, and `x` to grow or exchange pattern and hold spaces;
- substitutions, transliteration, shell output, and inserted data to expand the active text.

The bounded invariant applies to unrelated stream history, not to the record, pattern space, or hold space that Sed is required to retain.

## Acceptance coverage

`SedRecordAndTextSemanticsTests` covers:

- CRLF without CR normalization;
- lone CR data;
- empty LF records;
- host-newline independence;
- NUL framing and an unterminated final NUL record;
- NUL-backed `N`, `P`, `H`, `G`, `D`, `W`, and `l` pattern-space behavior;
- NUL-aware dot and multiline-anchor behavior, including preservation of embedded LF as ordinary data;
- multiline pattern-space termination and `P` first-line termination;
- separation between consecutive LF and NUL output operations after an unterminated record;
- preservation of that output-stream state across `-s` input-file boundaries;
- hold-space growth and termination;
- a one-megabyte logical record followed by another record;
- malformed UTF-8 round-trip through in-place editing;
- C-byte versus UTF-8 character-class behavior;
- the required private record metadata surface.

The LE1 characterization for unterminated output is intentionally updated: LE4 now preserves the missing final separator rather than synthesizing one.

## Deferred work

LE4 does not:

- replace script-fragment joining with source objects;
- add the final `CommandContext` core overload;
- harden shell and auxiliary-file capabilities;
- complete sandbox runtime denial;
- replace command-local in-place editing with E6;
- create a general `Icod.LineEditor.Shared` project.

Those responsibilities remain assigned to LE5 and the later LineEditor phases.
