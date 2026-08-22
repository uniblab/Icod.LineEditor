# Icod.LineEditor.Sed tests

This directory contains the established command-level Sed suite, the LE1 decomposition coverage, the LE3 Shared-regex migration suite, the LE4 byte-record/text-semantic suite, and the LE5 orchestration/capability suite.

| File | Purpose |
|---|---|
| `SedCommandTests.cs` | Existing command-level behavior and conformance coverage retained unchanged. |
| `SedCharacterizationTests.cs` | LE1 behavior freeze for option ordering, script-source ordering, diagnostics, implicit script mode, current record termination, sandbox denial, and in-place-edit startup. |
| `SedModuleBoundaryTests.cs` | Focused structural tests preserving the public `Command` signatures and private implementation boundary during decomposition and regex migration. |
| `SedRegularExpressionMigrationTests.cs` | GNU Sed 4.10 differential cases for BRE, ERE, captures, leftmost-longest selection, repeated zero-length matches, empty-expression reuse, modifiers, GNU escape preprocessing, strict-POSIX bracket policy, locale classes, and controlled diagnostics. |
| `SedRecordAndTextSemanticsTests.cs` | LE4 coverage for CR preservation, explicit LF/NUL framing, final termination, invalid UTF-8, C-byte versus UTF-8 profiles, huge records, LF/NUL multiline pattern and hold space, NUL-aware dot/anchors/list output, repeated-output separation, separate-file framing, and record metadata. |
| `SedOrchestrationAndCapabilityTests.cs` | LE5 coverage for the `CommandContext` byte path, named script sources, LF-only composition, injected shell and auxiliary files, sandbox runtime denial, in-place delegation, failure injection, and temporary cleanup. |

LE4 intentionally updates the LE1 unterminated-final-record characterization: a missing final separator is now preserved. Later semantic phases should update only characterization assertions whose behavior is intentionally changed by the roadmap. They must not delete the command-level suite merely because equivalent lower-level coverage is introduced.

LE3 removes the private `System.Text.RegularExpressions` translation path. The migration tests deliberately exercise behavior where .NET leftmost-first selection and default Unicode character classes differ from GNU/POSIX Sed expectations.

LE4 tests that modify `LC_ALL`, `LC_CTYPE`, or `LANG` are placed in a nonparallel xUnit collection because those values are process-wide.


LE5 capability tests use internals only through the test assembly's `InternalsVisibleTo` grant. No new implementation type is part of Sed's public API.
