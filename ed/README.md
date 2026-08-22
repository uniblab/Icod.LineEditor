# Icod.LineEditor.Ed

`Icod.LineEditor.Ed` is the standard-profile command-line host for the reusable
`Icod.LineEditor.Ed.Shared` mutable line-editor engine. The executable retains
the lowercase assembly name `ed`, the public `Icod.LineEditor.Ed.Command`
facade, and the repository's synchronous and cancellation-aware asynchronous
entry contracts.

The command layer owns GNU ed 1.22.5 invocation policy, option parsing,
initial-file loading, initial-address selection, prompting, quiet and verbose
presentation, exit-status mapping, and composition of standard or restricted
file/process capabilities. Address parsing, mutable buffer operations, regular
expressions, substitutions, global commands, undo, file mutation, filters, and
controlled engine diagnostics remain in `Icod.LineEditor.Ed.Shared`.

The binary `CommandContext` streams are authoritative whenever available.
Text-only compatibility overloads bridge through UTF-8 without taking
ownership of caller-supplied readers or writers.
