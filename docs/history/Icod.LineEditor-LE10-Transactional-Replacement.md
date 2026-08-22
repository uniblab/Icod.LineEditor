# Phase LE10 — LineEditor transactional-replacement integration

Phase LE10 replaces the provisional command-local file-replacement mechanics in
Sed and Ed with the shared Completion Gate E6 transaction model. The editor
parsers, mutable-buffer semantics, command diagnostics, backup selection, and
security profiles remain owned by their existing LineEditor projects.

## Integration boundary

```text
Icod.CoreUtils.Shared.FileSystem.TransactionalReplacement
        ↑
        ├── Icod.LineEditor.Ed.Shared.StandardEditorFileAccess
        └── Icod.LineEditor.Sed.Command.SystemInPlaceEditor
```

No new LineEditor assembly is introduced. This preserves the Phase LE9 decision
that neutral filesystem transactions belong in the current Shared incubation
project while Ed and Sed policy remains in their respective engines.

## Ed write policy

`StandardEditorFileAccess` now routes complete-file overwrite and creation
through `TransactionalFileReplacementTransaction`:

1. resolve a terminal symbolic link because Ed has no no-follow write profile;
2. obtain an authoritative E3 observation of the resolved destination;
3. freeze an E4 no-follow identity precondition, or an absent-destination
   precondition for a new file;
4. request best-effort preservation of mode, ownership, and attributes;
5. write the complete LF-oriented editor buffer into an E6 secure sibling
   staging file;
6. require staged-file durability, revalidate identity, publish the replacement,
   apply metadata, and clean recovery artifacts through the shared transaction.

Ed append commands remain direct append operations. Append is not whole-file
replacement and therefore retains its existing write-and-flush policy rather
than staging a second complete file. Ed command-level force, modified-buffer,
remembered-filename, and byte-count behavior remains above `IEditorFileAccess`.

The existing constructor that accepts `SecureTemporaryObjectCreator` and
`IFileSystemOperations` remains source compatible, but it now composes a
`SystemTransactionalReplacementFileSystem`; it no longer contains a private
move/delete replacement algorithm.

## Sed in-place policy

`SystemInPlaceEditor` now maps one Sed input file to one E6 recovery unit:

- the transform callback writes the complete edited result into the transaction's
  staging stream;
- a nonempty `-i` backup suffix becomes an explicit retained backup pathname;
- a pre-existing backup is staged and restored with the destination if a later
  transaction stage fails;
- mode, ownership, and attributes are requested as best-effort metadata;
- cancellation and failure use E6 rollback and deterministic cleanup;
- the transform's `ExecutionResult` is returned only after the transaction
  commits successfully.

Sed retains ownership of GNU backup-suffix expansion, including `*` replacement,
and of `--follow-symlinks`. When `--follow-symlinks` is selected, Sed resolves
the final target before constructing E6's mandatory no-follow artifact. Without
that option, a terminal symbolic link, junction, or other non-ordinary object is
rejected by the E6 ordinary-file contract. LE10 deliberately supplies no
nontransactional fallback that would silently weaken rollback or race safety.

## Transaction and failure semantics

Both integrations consume the E6 lifecycle rather than reproducing it:

- exclusive cryptographically named sibling staging files;
- data-and-metadata flush before namespace publication;
- stable-identity revalidation immediately before commit;
- atomic replacement where the provider supports it, with controlled E6
  diagnostics for unavailable or fallback atomicity;
- retained-backup publication from recoverable original content;
- restoration of both destination and pre-existing backup after a later failure;
- reverse-order rollback and deterministic cleanup after failure or cancellation.

Transaction failures are projected through each existing capability as
`IOException` with the final structured E6 diagnostic as the message and inner
exception. Cooperative cancellation remains `OperationCanceledException` so the
command boundary can retain its existing exit-status mapping.

## Validation matrix

Dedicated integration tests now cover:

| Consumer | Coverage |
|---|---|
| Ed | staged overwrite lifecycle, creation/byte count, metadata preservation, post-commit rollback, cancellation cleanup, direct append policy, and terminal-symbolic-link target resolution |
| Sed | staged in-place lifecycle, retained backups, restoration of a pre-existing backup, metadata preservation, cancellation cleanup, default no-follow rejection, and explicit followed-link editing |

The tests use the system E6 provider for host integration and inject
`ITransactionalReplacementFailureInjector` at named lifecycle stages for
deterministic rollback verification. Directory contents are checked after each
success, failure, and cancellation case to detect leaked staging or recovery
files.

## Removed mechanisms

The following provisional replacement behavior is removed from the editor
implementations:

- Ed's private sibling-name loop and direct `File.Move(..., overwrite: true)`
  publication;
- Ed's command-local temporary cleanup helper;
- Sed's private temporary-file creation, backup deletion/move sequence, and
  best-effort local cleanup path.

Secure temporary creation remains available only as a compatibility constructor
input that is immediately composed into the shared system transaction provider.

## Completion

Phase LE10 completes the contiguous in-solution LineEditor incubation sequence.
Completion Gate F1 is the next active roadmap milestone before Batch 46. Final
repository extraction and package-boundary work remains deferred to Completion
Gate G.
