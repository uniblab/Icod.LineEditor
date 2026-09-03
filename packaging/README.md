# Icod.LineEditor build and packaging workflow

This repository follows the canonical `uniblab/.github` C#/.NET build and release pattern.

## Validation ladder

| Lifecycle | Configuration | Work |
| --- | --- | --- |
| local `build.cmd` / `build.sh` | `Debug` | clean, restore, build, test, pack, exact package validation |
| pull request | `Staging` | Windows/Linux/macOS build and test; Linux also packs and verifies NuGet artifacts |
| default branch | `Release` | six-runner Windows/Linux/macOS x64/ARM64 distribution validation |
| `v<semver>` tag | `Release` | package selection/publication, six RID archives, checksums, GitHub Release |

The workflows and scripts are metadata driven. The root solution and executable projects are discovered from the repository and MSBuild rather than hard-coded from repository names.

## Repository topology

Production projects are:

- `Icod.LineEditor.Ed.Shared` — repository-local Ed/Red implementation library;
- `ed` — standalone line editor;
- `red` — standalone restricted line editor;
- `sed` — standalone stream editor; and
- `lineeditor` — distribution router for `ed`, `red`, and `sed`.

The router project identity is `Icod.LineEditor.Router`; its assembly and executable name are `lineeditor`. Its NuGet package identity is `Icod.LineEditor.Tools`, and the installed .NET tool command is `lineeditor`.

The `Icod.LineEditor.Tools` package uses the repository root `README.md` as `PackageReadmeFile`. The router-specific `lineeditor/README.md` remains repository documentation and is not the NuGet package README.

## Version contract

Repository versioning is centralized in the root `Directory.Build.props`:

```xml
<VersionPrefix>1.1.0</VersionPrefix>
```

`VersionPrefix` is the single authoritative release-version literal. The repository derives:

```text
Version        = 1.1.0
PackageVersion = 1.1.0
AssemblyVersion = 1.1.0.0
FileVersion     = 1.1.0.0
```

Production projects inherit these values unless a future project has an explicit reason to override them. Release tags must agree with the generated package version; `SelectReleasePackages.ps1` verifies the actual nuspec version before publication.

## Shared scripts

### `RepositoryTools.psm1`

Provides common helpers for locating the root solution, enumerating solution projects, reading MSBuild properties, discovering executable projects, and inspecting generated NuGet metadata.

### `Get-RepositoryMetadata.ps1`

Reports whether the repository has a root solution, its repository-relative path, and whether executable projects are present. Repository-relative solution paths are used so metadata produced on Linux can be consumed safely by Windows and macOS jobs.

### `Invoke-Build.ps1`

Implements the local build contract used by `build.cmd` and `build.sh`. With no section argument the wrappers use `Debug` and run:

```text
clean → restore → build → test → pack → validate
```

Individual stages may be requested as `clean`, `restore`, `build`, `test`, `pack`, or `validate`.

### `VerifyPackageArtifact.ps1`

Validates generated `.nupkg` files supplied by the caller. It verifies package metadata, declared package README presence, and .NET tool metadata shape where applicable. The script supports repositories in which a given configuration legitimately produces no packages.

### `VerifyDistribution.ps1`

Runs the common source-tree distribution gate:

1. restore;
2. build;
3. test;
4. pack without rebuilding; and
5. exact package validation.

This is the authoritative validation used by the six-runner `main` and manually dispatched distribution-validation workflows.

### `SelectReleasePackages.ps1`

Selects only generated packages whose actual nuspec version matches the `v<semver>` tag version. A mismatched package is skipped rather than published accidentally.

### `BuildReleaseArchive.ps1`

Discovers executable projects through MSBuild and publishes them as framework-dependent single-file executables. With the router present, each RID archive contains:

```text
lineeditor
ed
red
sed
README.md
LICENSE
```

Windows executable names use the `.exe` suffix.

The six release RIDs are:

```text
win-x64
win-arm64
linux-x64
linux-arm64
osx-x64
osx-arm64
```

## Pull-request validation

`.github/workflows/pull-request.yaml` uses the `Staging` configuration on:

- `windows-latest`;
- `ubuntu-latest`; and
- `macos-latest`.

All three runners restore, build, and test the solution. Linux additionally packs the solution and performs exact NuGet artifact validation.

## Main-branch validation

`.github/workflows/main.yaml` uses the `Release` configuration across:

- Windows x64;
- Windows ARM64;
- Linux x64;
- Linux ARM64;
- macOS x64; and
- macOS ARM64.

Each runner executes `VerifyDistribution.ps1` so the Release configuration is independently validated on every supported host/architecture combination.

## Tagged release graph

A `v<semver>` tag starts `.github/workflows/release.yaml`. The tagged commit must be contained in the repository default branch.

The release graph is intentionally split so package production and executable archives do not depend on one another unnecessarily:

```text
metadata
  ├── package
  │     ├── publish-nuget
  │     └── publish-github-packages
  └── archives (6 RIDs)

publish-nuget ────────────────┐
publish-github-packages ──────┼── github-release
archives ─────────────────────┘
```

Only packages whose nuspec version matches the release tag are selected. NuGet.org and GitHub Packages consume the same selected package artifact and use `--skip-duplicate`, allowing safe retries after partial publication.

GitHub Release creation waits for all applicable package-publication and archive jobs, writes `SHA256SUMS.txt`, and attaches the selected NuGet packages plus all six executable archives.

## NuGet Trusted Publishing

NuGet.org publication requires:

- a GitHub environment named `Release`;
- an Actions secret named `NUGET_USER`; and
- a NuGet.org Trusted Publishing policy authorizing repository `uniblab/Icod.LineEditor`, workflow `release.yaml`, and environment `Release`.

The package scope must authorize the package actually being published. For the router distribution that package ID is:

```text
Icod.LineEditor.Tools
```

GitHub Packages publication uses the job-scoped `GITHUB_TOKEN` with `packages: write` permission.

## Package/readme contract

The router package declares:

```text
PackageId:       Icod.LineEditor.Tools
ToolCommandName: lineeditor
PackageReadme:   README.md
```

That `README.md` is sourced from the repository root and packed at the NuGet package root. This keeps the package landing page aligned with the complete repository-level suite documentation rather than the narrower router-only README.

## Release checklist

Before pushing release tag `v1.1.0`:

1. confirm `Directory.Build.props` still declares `VersionPrefix` `1.1.0`;
2. confirm `lineeditor --version` reports `1.1.0` from assembly informational version metadata;
3. confirm the root README installation example and package identity are current;
4. confirm PR Staging validation is green;
5. merge to `main` and require the six-runner Release validation to pass; and
6. only then push tag `v1.1.0`.
