# Icod.LineEditor Phase LE3 — Sed regular-expression migration

## Status

Phase LE3 is complete. `Icod.LineEditor.Sed` no longer translates BRE text or POSIX classes into `System.Text.RegularExpressions`. Address and substitution expressions now compile through `Icod.CoreUtils.Shared.RegularExpressions`.

The authoritative behavior baseline is GNU sed 4.10, with the Shared provider contract verified during Phase LE2.

## Ownership boundary

The migration deliberately separates reusable regex mechanics from Sed policy.

### Shared provider owns

- direct GNU Basic and Extended syntax parsing;
- leftmost-longest match selection;
- numbered captures and back-references;
- locale-aware character classes and collation through an injected provider;
- line-sensitive anchors and dot/bracket policy over a caller-selected logical separator;
- controlled compile and match diagnostics;
- cancellation and resource limits.

### `Icod.LineEditor.Sed` owns

- BRE versus ERE selection from command options;
- the last-compiled-expression state shared by addresses and substitutions;
- exact empty-expression reuse, including prior `I` and `M` policy;
- address `I`/`M` and substitution `i`/`I`/`m`/`M` syntax;
- GNU versus POSIX interpretation of command-local extensions;
- GNU Sed escape preprocessing before BRE/ERE parsing, including strict-POSIX bracket behavior;
- global and numbered substitution occurrence selection;
- GNU empty-match iteration;
- replacement expansion and Sed diagnostic presentation.

No general `Icod.LineEditor.Shared` project was introduced.

## Implementation

`SedRegularExpressionCompiler` is a private nested implementation type behind the established public `Icod.LineEditor.Sed.Command` facade. One compiler is created for each parsed Sed program. It selects either `GnuBasicRegularExpressionProvider` or `GnuExtendedRegularExpressionProvider`, injects the applicable character-class provider, and retains the last successful compiled expression.

A nonempty address or substitution expression replaces that retained object. An empty expression returns the exact object rather than recompiling the prior pattern. This preserves the modifier policy under which the expression was originally compiled. GNU Sed rejects new modifiers on an empty expression, and the adapter does the same.

Before compilation, the adapter performs GNU Sed escape preprocessing for control, decimal, octal, and hexadecimal escapes. The preprocessing occurs before BRE/ERE parsing, so numeric escapes may generate regular-expression metacharacters. Under `--posix`, GNU escape processing remains active outside raw bracket expressions but is disabled inside them. This policy remains command-local because it is Sed source-language behavior rather than a property of the Shared regex grammar.

Invariant .NET culture selects `PosixCLocaleRegularExpressionCharacterClassProvider`; other cultures use `UnicodeRegularExpressionCharacterClassProvider` for the process culture. Phase LE4 will replace the current decoded-string record path with explicit byte/text and encoding policy.

## GNU empty-match progression

Shared returns one leftmost-longest match from a requested start index. Sed layers global iteration above that primitive.

The iterator:

1. accepts a zero-length match when it is not immediately adjacent to a preceding accepted nonempty match;
2. advances one input character after an accepted zero-length match;
3. suppresses an empty match immediately following an accepted nonempty match;
4. continues after the suppressed position when input remains;
5. retains exact capture data from Shared for replacement expansion.

This reproduces GNU Sed cases such as:

| Program | Input | Output |
|---|---|---|
| `s/x*/X/g` | `abc` | `XaXbXcX` |
| `s/a*/X/g` | `ab` | `XbX` |
| `s/b*/X/g` | `ab` | `XaX` |
| `s/[a-z]*/X/g` | `abc` | `X` |

## Differential and acceptance coverage

`SedRegularExpressionMigrationTests` includes GNU sed 4.10 expected results for:

- BRE grouping, captures, and replacement back-references;
- ERE syntax;
- leftmost-longest alternation where .NET's default leftmost-first engine chooses a shorter branch;
- exact empty-expression reuse across address and substitution contexts;
- rejection of modifiers on an empty expression;
- multiline anchors;
- C-locale POSIX character classes;
- POSIX-mode handling of GNU-only BRE operators and GNU escapes inside raw bracket expressions;
- GNU control, decimal, octal, hexadecimal, tab, and newline escape preprocessing;
- repeated zero-length global substitutions;
- translation of Shared diagnostics into Sed usage diagnostics.

The pre-existing command and LE1 characterization suites remain in place.

## Removed implementation

The following command-local compatibility layer is removed:

- `CreateRegex`;
- `TranslateBasicRegularExpression`;
- `TranslatePosixClasses`;
- all production references to `System.Text.RegularExpressions`.

## Deferred to LE4 and later

LE3 intentionally does not claim byte-perfect Sed input semantics. The following remain scheduled:

- byte-preserving input records and exact final-record termination;
- explicit LF/NUL output serialization;
- invalid UTF-8 and C-locale byte behavior;
- CR preservation;
- byte-to-text and replacement-encoding policy;
- hardened script-source, sandbox, process, and in-place replacement phases.

## LE4 completion handoff

Phase LE4 has now resolved the byte-record items deferred by this migration. Sed regex matching receives text from the explicit C/POSIX byte or UTF-8 profile, malformed bytes survive through deterministic placeholders, LF/NUL framing and final termination are authoritative record metadata, and replacement output is encoded through the same selected profile. Concrete `-z` multiline use also justified a narrow Shared extension: Sed selects NUL through `RegularExpressionOptions.LineSeparator` and explicitly allows dot to consume NUL outside multiline mode. The detailed contract is recorded in `Icod.LineEditor-LE4-Record-and-Text-Semantics.md`.
