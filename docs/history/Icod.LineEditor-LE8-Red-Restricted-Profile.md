# Phase LE8 — Red restricted profile

Phase LE8 migrates the lowercase `red` executable onto the mutable engine created in LE6 while retaining the public `Icod.LineEditor.Red.Command` identity.

## Shared profile

`red` and `ed --restricted` now construct the same immutable `EditorCapabilityProfile`. The profile binds together:

- `EditorSecurityPolicy.Restricted`, with one working directory captured at construction;
- `RestrictedEditorFileAccess`, which is the only file capability exposed to the engine; and
- `DeniedEditorProcessAccess`, which refuses every child-process request.

The executable has no unrestricted composition branch. The `-r`/`--restricted` option is accepted only for GNU-compatible invocation syntax.

## Shell denial

Restricted shell denial is enforced at two layers:

1. The engine preflights command text before address parsing, global selection, undo capture, current-address movement, remembered-command lookup, or any other mutable transition. Direct `!`, remembered `!!`, addressed filters, and shell commands nested inside `g` or `v` are rejected there.
2. The immutable profile exposes `DeniedEditorProcessAccess`, so a future parser or dispatcher defect still cannot start a child process through the engine capability.

The early check is recursive for global-command bodies and is intentionally performed before resolving marks or search addresses. Denied commands therefore preserve buffer identities and content, current address, marks, modified state, remembered filename, and the prior undo unit.

## Filename policy

`EditorRestrictedPath.IsSimpleFileName` applies the same classification on every host. It rejects:

- Unix rooted and slash-bearing paths;
- Windows drive-relative and rooted paths;
- backslash-bearing, UNC, and device paths;
- alternate data streams and other colon-bearing names;
- `.` and `..`;
- shell-bearing names;
- Windows reserved device stems; and
- trailing-dot and trailing-space aliases.

Permitted names are resolved beneath the directory captured when the profile is constructed. The logical remembered filename remains the simple name rather than the resolved absolute path.

## Confinement contract

The GNU-compatible profile provides pathname restriction. It does **not** claim physical filesystem confinement. A permitted leaf can refer to a symbolic link, hard link, mount point, or reparse point, and the operating system resolves it according to normal filesystem rules. The capability deliberately avoids a separate link/reparse precheck because a check followed by open would add a validation/open race without establishing a reliable sandbox.

Callers requiring physical confinement must provide a stronger filesystem capability with handle-relative, no-follow, or equivalent platform-specific guarantees. That stronger boundary is outside GNU `red` compatibility and is not claimed by LE8.

## Validation

The LE8 tests cover:

- `red` identity and ordinary editing;
- equivalence with `ed --restricted` for successful and failing scripts;
- direct, remembered, addressed-filter, initial-operand, and global-nested shell denial;
- Unix, Windows drive, UNC, device, alternate-stream, reserved-device, and trailing-alias paths;
- permitted simple-name reads and writes;
- preservation of mutable editor state and the prior undo unit after denial;
- captured-directory behavior and host-independent classification; and
- characterization of symbolic-link, hard-link/reparse, and validation/open-race behavior under the documented pathname-only contract.

Command-host sharing between `ed` and `red` remains intentionally unrefactored until the evidence-based LE9 sharing audit.
