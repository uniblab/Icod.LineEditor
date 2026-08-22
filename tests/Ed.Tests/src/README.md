# Ed command tests

These tests exercise the LE7 `Icod.LineEditor.Ed.Command` orchestration boundary.
They intentionally use the public command API rather than reaching into engine
internals. The suite covers GNU invocation options, the standard and restricted
capability profiles, byte-oriented command input, output and error status
mapping, resource-scale cases, and textual compatibility with independent GNU
Diffutils and Icod Diffutils ed-script fixtures.
