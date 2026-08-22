# Icod.LineEditor.Ed.Shared.Tests

The dedicated Phase LE6 test project covers the reusable Ed/Red engine independently from the `ed` and `red` command executables.

Coverage includes:

- stable line identity across moves, copies, joins, large segmented edits, and undo snapshots;
- address, mark, cut-buffer, mutation, substitution, global-command, and remembered-state behavior;
- Shared GNU BRE integration and replacement back-references;
- injected file and process capabilities;
- restricted parser/dispatcher denial and restricted file resolution;
- cancellation and controlled exit statuses;
- textual ed-script fixtures representing GNU Diffutils and `Icod.DiffUtils` output.

Command-line conformance belongs to LE7 and restricted-command adversarial conformance belongs to LE8.
