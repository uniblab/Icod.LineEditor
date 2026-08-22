# ED(1)

## NAME

**ed** — line-oriented text editor

## SYNOPSIS

```text
ed [OPTION]... [[+LINE] FILE]
```

## DESCRIPTION

`Icod.LineEditor.Ed` is a managed .NET implementation of the standard GNU-style `ed(1)` line editor. The command follows the GNU ed 1.22.5 compatibility profile implemented by this repository.

The executable is a command host over the repository-local `Icod.LineEditor.Ed.Shared` mutable editor engine. The command layer owns invocation parsing, initial-file loading, initial-address selection, prompting, presentation, and process exit status. The shared engine owns mutable line storage, address parsing, editing operations, searches, substitutions, global commands, undo, file operations, shell/filter capability use, and controlled diagnostics.

The lowercase executable assembly name remains `ed`.

## OPTIONS

```text
-E, --extended-regexp
    Use extended regular expressions instead of the default GNU basic regular
    expression profile.

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
    Use the same immutable restricted capability profile as red(1).

-s, --script
    Suppress byte counts and shell-completion prompts for scripted operation.

-v, --verbose
    Print diagnostic explanations.

--strip-trailing-cr
    Remove one trailing carriage return from each input record.

--unsafe-names
    Permit control characters in filenames that are rejected by the default
    filename policy.

-h, --help
    Display command help and exit.

-V, --version
    Display version information and exit.
```

The optional `+LINE` selector may be a line number, `+`, `/REGEXP/`, or `?REGEXP?`.

## EDITOR MODEL

The Ed engine maintains a mutable line buffer with current-address state, stable line identities, marks, a cut buffer, and one-level reversible undo. Implemented operations include append, insert, change, delete, print, list, number, mark, move, copy, join, yank, put, substitution, global execution, file operations, shell/filter operations, undo, and quit behavior.

Searches and substitutions use the managed GNU BRE/ERE providers from `Icod.CommandFramework`. Commands and edited records are LF-delimited data. CRLF command input is accepted without making host newline conventions part of the editor data model.

Complete-file writes use the shared transactional-replacement foundation so publication, cleanup, and representable metadata preservation occur through the repository's established filesystem capability boundary. Append remains a direct append operation.

## RESTRICTED MODE

`--restricted` uses the same capability profile as `red`:

- shell commands and filters are disabled;
- arbitrary pathname syntax is rejected;
- filename-bearing operations pass through the restricted filename policy; and
- process access is denied as a second enforcement layer.

For a permanently restricted invocation, use `red(1)`.

## EXIT STATUS

```text
0    Editing session completed successfully.
1    Invalid invocation or a controlled editor, file, or I/O failure occurred.
2    A non-interactive initial-file failure or cancellation condition occurred.
3    An unexpected internal editor failure occurred.
```

Editor-command errors can interact with `--loose-exit-status` as described above.

## PLATFORM NOTES

The project targets .NET 10 with C# 13 and is tested on Windows, Linux, and macOS. Neutral command-line, regular-expression, process, record, filesystem, and transactional-replacement mechanics come from the published `Icod.CommandFramework` package. Mutable Ed-family behavior remains in `Icod.LineEditor.Ed.Shared`.

## AUTHORS

GNU `ed` was originally written by **Andrew L. Moore** and is currently maintained by **Antonio Diaz Diaz**. GNU's current ed project documentation identifies that authorship and maintenance lineage.

Migrated to .Net by Timothy J. Bruce <uniblab@hotmail.com>.

## COPYRIGHT

Copyright (c) 2026 Timothy J. Bruce

## LICENSE

This managed `ed` project is distributed under the GNU General Public License, version 3 or later. See `ed.LICENSE.txt` in build output and this directory's `LICENSE` file.

## SEE ALSO

`ed(1)`, `red(1)`, `sed(1)`
