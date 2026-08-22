# RED(1)

## NAME

**red** — restricted line-oriented text editor

## SYNOPSIS

```text
red [OPTION]... [[+LINE] FILE]
```

## DESCRIPTION

`Icod.LineEditor.Red` is the permanently restricted GNU-compatible `ed(1)` command profile. It uses the same repository-local `Icod.LineEditor.Ed.Shared` mutable editor engine as `ed`, but constructs that engine with the immutable restricted capability profile on every invocation.

`red` is therefore not a forked or reduced editor implementation. Editing semantics remain shared with `ed`; the command boundary changes the capabilities that may be exercised. The lowercase executable assembly name remains `red`.

## OPTIONS

```text
-E, --extended-regexp
    Use extended regular expressions.

-G, --traditional
    Run in traditional compatibility mode.

-l, --loose-exit-status
    Permit successful process exit after editor-command errors according to the
    implemented loose-status policy.

-p, --prompt=STRING
    Use STRING as the command prompt.

-q, --quiet, --silent
    Suppress diagnostic messages.

-r, --restricted
    Accepted for command-line compatibility. red is already permanently
    restricted.

-s, --script
    Suppress byte counts and shell-completion prompts for scripted operation.

-v, --verbose
    Print diagnostic explanations.

--strip-trailing-cr
    Remove one trailing carriage return from each input record.

--unsafe-names
    Permit control characters within names that otherwise satisfy the restricted
    leaf-name policy. This does not disable the restricted pathname boundary.

-h, --help
    Display command help and exit.

-V, --version
    Display version information and exit.
```

The optional `+LINE` selector may be a line number, `+`, `/REGEXP/`, or `?REGEXP?`.

## RESTRICTION MODEL

Restricted mode is enforced before mutable editor dispatch and is also represented in the capabilities supplied to the shared engine:

- shell commands and shell-bearing file commands are denied;
- the process capability is a denied capability rather than merely an unchecked code path;
- filename-bearing operations pass through one policy rooted at the working directory captured when the restricted profile is created; and
- only simple leaf filenames are accepted by that pathname policy.

This deliberately promises **pathname restriction**, not physical filesystem confinement. It does not claim to defeat symbolic-link substitution, hard links, mount points, Windows reparse points, or validation/open races. Applications requiring a stronger sandbox must provide one at the operating-system or container boundary.

## EDITOR MODEL

All mutable editing behavior is provided by `Icod.LineEditor.Ed.Shared`: buffer operations, addresses, marks, cut/yank state, substitutions, global commands, regular expressions, undo, file operations allowed by the restricted capability, and editor diagnostics are the same engine behavior used by `ed`.

Commands and edited records are LF-delimited data. CRLF command input is accepted.

## EXIT STATUS

```text
0    Editing session completed successfully.
1    Invalid invocation or a controlled editor, file, or I/O failure occurred.
2    A non-interactive initial-file failure or cancellation condition occurred.
3    An unexpected internal editor failure occurred.
```

## PLATFORM NOTES

The project targets .NET 10 with C# 13 and is tested on Windows, Linux, and macOS. Restriction semantics are implemented in managed policy and capability boundaries; they are not dependent on the host having a native `red` executable.

## AUTHORS

GNU `red` is the restricted form of GNU `ed`, not an independently authored editor. GNU `ed` was originally written by **Andrew L. Moore** and is currently maintained by **Antonio Diaz Diaz**; `red` shares that implementation lineage.

Migrated to .Net by Timothy J. Bruce <uniblab@hotmail.com>.

## COPYRIGHT

Copyright (c) 2026 Timothy J. Bruce

## LICENSE

This managed `red` project is distributed under the GNU General Public License, version 3 or later. See `red.LICENSE.txt` in build output and this directory's `LICENSE` file.

## SEE ALSO

`red(1)`, `ed(1)`, `sed(1)`
