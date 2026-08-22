# Icod.LineEditor Phase LE5 — Sed orchestration and capability boundary

## Status

Phase LE5 is complete. The public `Icod.LineEditor.Sed.Command` identity and established text-stream compatibility overloads remain available, while the repository-standard `RunAsync(string[] args, CommandContext context)` path is now the primary command entry point.

LE5 is an orchestration and side-effect-boundary phase. It does not redesign Sed's regex, record, pattern-space, or hold-space semantics established by LE3 and LE4, and it does not perform the final E6 transactional-replacement migration scheduled for LE10.

## CommandContext core

`Command.RunAsync(string[] args, CommandContext context)` now carries the command's standard streams and cancellation token. When `CommandContext` supplies a binary standard-input or standard-output stream, Sed uses that stream independently as the authoritative side of the data path so the LE4 byte-preserving contract is retained. A missing binary side uses the established text compatibility adapter. The text streams remain available for diagnostics and, when no binary output stream is present, presentation output.

The executable entry point now constructs `CommandContext` and invokes this overload directly. The synchronous `Run`, text-stream `RunAsync`, and internal byte-stream entry points remain compatibility facades.

## Script-source model

Command-line `-e` expressions, `-f` script files, and the implicit first script operand are represented by separate `SedScriptSource` objects. Each source retains:

- its source kind;
- a stable diagnostic name;
- its original text;
- its invocation order.

`SedScriptDocument` provides one parser view while preserving source spans. Adjacent sources are separated with a literal LF only when the preceding source does not already end in LF. `Environment.NewLine` is not used for script composition. Parser diagnostics map aggregate positions back to a source name and one-based line and column.

## Runtime capabilities

`SedRuntimeCapabilities` groups three private command capabilities:

- `ISedShellCapability` for `e` and `s///e`;
- `ISedAuxiliaryFileCapability` for `r`, `R`, `w`, `W`, and substitution `w`;
- `IInPlaceEditor` for `-i` publication.

The system shell implementation retains Shared `ProcessRunner`; LE5 does not introduce direct process spawning. The system auxiliary-file capability retains asynchronous `FileStream` operations behind the boundary. Tests can inject deterministic in-memory or failure-producing capabilities without touching the host shell or filesystem.

## Sandbox enforcement

Sandbox restrictions remain enforced by the script compiler: shell-bearing and auxiliary-file commands are rejected before execution. LE5 also supplies a denied runtime capability profile. Consequently, a command that reaches either capability through a future parser or dispatcher regression still receives a controlled denial instead of host access.

The in-place editor remains available in sandbox mode because GNU Sed sandbox restrictions apply to `e`, `r`, and `w` command families rather than to the command-line `-i` mode itself.

## In-place editing boundary

The existing command-local replacement mechanics now live entirely inside `SystemInPlaceEditor`. It:

- resolves `--follow-symlinks` according to the existing policy;
- creates a private sibling temporary file through Shared `SecureTemporaryObjectCreator`;
- invokes the Sed transformation against that file;
- preserves the existing backup, replacement, attribute, and Unix-mode behavior;
- removes the temporary file after a failed or canceled transformation.

This is intentionally a boundary and characterization step. LE10 remains responsible for replacing these provisional publication internals with the shared E6 transaction model and its complete rollback, metadata, durability, and indirection policy.

## Acceptance coverage

`SedOrchestrationAndCapabilityTests` verifies:

- independent binary-input and binary-output selection by `CommandContext`;
- LF-only script composition and source-location mapping;
- stable diagnostics for later `-e` sources;
- injected shell execution;
- injected auxiliary reads and writes;
- compile-time sandbox rejection and runtime denied-capability backstops;
- delegation of `-i` to `IInPlaceEditor`;
- source preservation and temporary-file cleanup after an injected in-place transformation failure.

The established command, LE3 regex, and LE4 record/text suites remain in place.

## Handoff to LE6

LE6 may now design `Icod.LineEditor.Ed.Shared` against the proven Shared regex, record, process, temporary-object, filesystem, and capability patterns without coupling Ed/Red to Sed's streaming engine. Sed's final E6 replacement migration remains deferred to LE10 as planned.
