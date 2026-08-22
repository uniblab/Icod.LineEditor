# Ed shared engine source

This directory contains the Phase LE6 mutable Ed/Red engine.

| File | Responsibility |
|---|---|
| `EditorModels.cs` | Public result, diagnostic, signal, line, range, file, and process models. |
| `EditorBuffer.cs` | Segmented mutable line storage, stable line identity, movement, copying, joining, and snapshots. |
| `EditorAddressParser.cs` | Ed-specific addresses, offsets, marks, forward/reverse searches, and ranges. |
| `EditorCapabilities.cs` | Immutable security profiles and injected standard, restricted, and denied file/process capabilities. |
| `EditorEngine.cs` | Session state, command dispatch, substitutions, global execution, undo, file/process effects, diagnostics, cancellation, and signals. |

Sed-specific pattern/hold space, range state, and streaming-cycle behavior do not belong here. Cross-suite regex, records, process execution, secure temporary objects, and filesystem durability remain in the current Shared incubation project.
