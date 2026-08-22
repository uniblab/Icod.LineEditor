# G7 extraction provenance

- Destination baseline: `Icod.LineEditor` commit `41fe8ee23b55fd4ad4987bb059754c553cf5aabf`.
- Source repository: `uniblab/Icod.CoreUtils`.
- Reviewed source commit: `4ee41aa1dc1c549f85efab6e5fa156a3dfc7271b`.
- Dependency cut: all eight project references to `Icod.CoreUtils.Shared` are replaced by `Icod.CommandFramework` package version `1.1.0`.
- Preserved local architecture: `ed` and `red` reference `Icod.LineEditor.Ed.Shared` as a project; `sed` remains separate.
- Tests and fixtures: `Ed.Shared.Tests`, `Ed.Tests`, `Red.Tests`, and `Sed.Tests` are imported with their fixtures.
- Historical LineEditor architecture, audit, migration, and Batch 34 notes are retained in `docs/history`.
- No CoreUtils files are deleted by this bootstrap.