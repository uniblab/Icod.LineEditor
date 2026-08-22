# Icod.LineEditor Phase LE2 Regex Contract Audit

## Purpose

Phase LE2 verifies that the regular-expression foundation completed by Completion Gate R1 is sufficient for the pinned LineEditor consumers before Sed migrates away from its private .NET-regex translation layer.

Authoritative LineEditor baselines:

- GNU Sed 4.10;
- GNU Ed 1.22.5.

This phase is a contract audit. It does not migrate Sed; that work remains Phase LE3.

## Result

No production Shared API extension was required.

The existing `Icod.CoreUtils.Shared.RegularExpressions` contract already provides the cross-suite mechanics required by Sed and Ed:

| Contract | LE2 finding |
|---|---|
| Syntax | Explicit GNU Basic and Extended profiles exist; Basic remains the source-compatible default. |
| Parsing | BRE and ERE are parsed directly by the managed parser rather than translated to .NET regular expressions. |
| Selection | Whole matches use leftmost-longest selection with deterministic GNU/Gnulib capture-register behavior. |
| Operators | Grouping, alternation, repetition, intervals, bracket expressions, anchors, captures, and GNU extensions are available under the selected profile. |
| Locale | Classification, comparison, collation, equivalence, and case policy are injected through `IRegularExpressionCharacterClassProvider`; the deterministic C/POSIX provider is suitable for LineEditor conformance tests. |
| String coordinates | Matches and captures over .NET strings expose UTF-16 indices and lengths. |
| Byte coordinates | Matches and captures over authoritative input bytes expose exact source-byte offsets, lengths, and slices. |
| Invalid input | UTF-8 matching has explicit preserve, replace, and throw policies; preserved malformed bytes remain authoritative. |
| Diagnostics | Compile and match failures use stable structured diagnostics. |
| Cancellation | Compile, decode, and match operations honor `CancellationToken`. |
| Resources | Syntax nesting and match-state growth are bounded by explicit options and controlled diagnostics. |
| Replacement boundary | Shared returns exact matches and captures but does not define a replacement language or output-encoding policy. |

## Verification added by LE2

`LineEditorRegularExpressionContractTests` directly verifies:

- Basic default compatibility and Extended operator spelling;
- ERE grouping, alternation, repetition, intervals, brackets, and captures;
- leftmost-longest selection;
- line-sensitive anchors;
- C/POSIX versus Unicode classification;
- UTF-16 string coordinates;
- exact UTF-8 source-byte coordinates and capture slices;
- malformed-byte preservation;
- invalid byte-boundary diagnostics;
- deterministic ERE compile diagnostics;
- match-state resource limits;
- compile and match cancellation.

The existing Gate R1 tests remain authoritative for the detailed BRE, GNU Emacs, Gnulib register, bracket, and compatibility profiles. LE2 adds a consumer-oriented acceptance layer rather than duplicating those exhaustive suites.

## Deliberately excluded from Shared

The following policies remain in `Icod.LineEditor.Sed`:

- selecting BRE or ERE from Sed options;
- remembering and reusing an empty regular expression;
- distinguishing address and substitution compilation context;
- POSIX and GNU mode interactions;
- repeated-match and zero-length-match progression;
- substitution occurrence selection;
- replacement-template parsing;
- replacement-output encoding;
- Sed-specific diagnostics.

Ed likewise owns editor command context, remembered expressions, substitutions, and mutable-buffer effects. These policies are not general regex-engine mechanics.

## LE3 handoff

Phase LE3 may now introduce a Sed-specific adapter over the Shared provider. The migration should compare the current private translator and Shared engine with GNU Sed differential fixtures before deleting the old path. Any discrepancy found during that migration should first be classified as either:

1. a genuine cross-suite regex defect, which belongs in Shared; or
2. Sed-specific orchestration or replacement policy, which remains in `Icod.LineEditor.Sed`.

## LE4 consumer-evidence follow-up

The LE2 conclusion was correct for the LF-oriented consumers and fixtures available at that phase. Phase LE4 supplied the first concrete GNU Sed `--null-data` multiline consumer and exposed one narrow cross-suite gap: line-sensitive matching had hard-coded LF, while GNU Sed `-z` uses NUL as the pattern-space line separator. LE4 therefore adds two command-neutral options to the existing Shared contract:

- `RegularExpressionOptions.LineSeparator`, defaulting to LF;
- `RegularExpressionOptions.DotMatchesNull`, defaulting to `false`.

The defaults preserve every pre-LE4 consumer. Sed selects NUL and enables NUL dot matching only for `-z`; when the `M` modifier enables line sensitivity, the configured NUL separator is again excluded by dot and negated bracket expressions and is used by `^` and `$`. This extension is general regex matching policy backed by real consumer evidence. Sed still owns `-z`, pattern-space construction, modifier syntax, and empty-expression state.
