# Ed shared engine tests

- `EditorBufferTests.cs` validates scalable segmented storage and stable identities.
- `EditorEngineTests.cs` validates editor state, commands, BRE substitutions, global execution, file effects, filters, undo, and cancellation.
- `SecurityAndCompatibilityTests.cs` validates restricted profiles and Diffutils ed-script fixtures.
- `TransactionalReplacementIntegrationTests.cs` validates Phase LE10 E6 overwrite, creation, metadata, rollback, cancellation, append, cleanup, and link behavior.
- `TestCapabilities.cs` supplies deterministic in-memory file and process capabilities.
