# Icod.LineEditor.Red

`red` is the permanently restricted GNU-compatible line-editor command.

The executable retains the public `Icod.LineEditor.Red.Command` facade and lowercase `red` assembly while delegating mutable editor behavior to `Icod.LineEditor.Ed.Shared`. It always selects the same immutable restricted capability profile used by `ed --restricted`; `-r` is accepted only as compatibility syntax.

Restricted mode denies shell commands before address resolution or mutable dispatch and supplies a denied process capability as defense in depth. Every filename-bearing operation passes through one captured-working-directory pathname policy. That policy permits only simple leaf names and deliberately promises pathname restriction, not physical confinement across symbolic links, hard links, mount points, reparse points, or validation/open races.
