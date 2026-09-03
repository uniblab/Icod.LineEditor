# Icod.LineEditor build and packaging workflow

This repository follows the canonical `uniblab/.github` C#/.NET lifecycle: local wrappers use `Debug`; pull requests use `Staging` on Windows, Linux, and macOS; `main` uses six-runner `Release` validation; and `v<semver>` tags use `Release` packaging/publication.

The metadata helpers discover the root solution and executable projects from MSBuild. With the router project present, release archives contain `lineeditor`, `ed`, `red`, and `sed` (or `.exe` equivalents on Windows) together with the repository README and LICENSE.

Package verification permits repositories with zero packages. The `Icod.LineEditor.Tools` router is a .NET tool package whose installed command is `lineeditor`; other project packaging remains governed by project metadata.
