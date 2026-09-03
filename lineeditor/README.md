# `lineeditor` router

`lineeditor` is the distribution router for the managed `Icod.LineEditor` command suite. It multiplexes `ed`, `red`, and `sed` without spawning the standalone executables.

## Usage

```text
lineeditor COMMAND [OPTION]... [ARG]...
```

Supported commands are:

```text
lineeditor ed  [OPTION]...
lineeditor red [OPTION]...
lineeditor sed [OPTION]...
```

Router options are:

```text
-h, --help       display router help and exit
-V, --version    display the router version and exit
```

With no command, or with an unknown command, the router writes a usage diagnostic to standard error and exits with status `2`.

## Dispatch model

The router references the managed `ed`, `red`, and `sed` projects directly and invokes their command entry points in process. Command arguments and caller-owned standard streams are forwarded to the selected command, and the selected command's exit status is returned by the router.

The router is therefore a distribution convenience, not a replacement implementation. The standalone `ed`, `red`, and `sed` executables remain first-class build, test, and release outputs.

## Package

The NuGet package identity is `Icod.LineEditor.Tools`; its installed command is `lineeditor`.

The package intentionally uses the repository root [`README.md`](../README.md) as its NuGet package README so package consumers receive the complete suite overview, installation/distribution contract, command inventory, compatibility notes, and licensing information. This file remains the router-specific repository documentation.

## Runtime

The router targets .NET 10 and is intended for Windows, Linux, and macOS. It uses the same managed implementations and dependencies as the standalone commands.

## Licensing

The router is distributed under the GNU General Public License, version 3 or later. See the repository root [`LICENSE`](../LICENSE).
