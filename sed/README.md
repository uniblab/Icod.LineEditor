# SED(1)

## NAME

**sed** — stream editor for filtering and transforming text

## SYNOPSIS

```text
sed [OPTION]... {script-only-if-no-other-script} [input-file]...
```

## DESCRIPTION

`Icod.LineEditor.Sed` is a managed .NET implementation of the GNU-style `sed(1)` stream editor.

The command parses and executes sed programs in process. It supports addressed commands, inclusive ranges, negation, command groups, labels and branches, pattern and hold spaces, substitution, transliteration, explicit printing, auxiliary file reads and writes, shell execution where permitted, in-place editing, sandbox mode, and NUL-delimited records.

Sed remains a separate execution engine from `Icod.LineEditor.Ed.Shared`; its streaming cycle, address/range state, script model, sandbox policy, and in-place orchestration are Sed-specific concerns.

## OPTIONS

```text
-?, --help
    Display command help.

-V, --version
    Display version information.

-n, --quiet, --silent
    Suppress automatic printing of the pattern space.

--debug
    Annotate program execution.

-e SCRIPT, --expression=SCRIPT
    Add SCRIPT to the program.

-f FILE, --file=FILE
    Add commands from script FILE.

-i[SUFFIX], --in-place[=SUFFIX]
    Edit files in place and optionally retain a backup with SUFFIX.

--follow-symlinks
    Follow symbolic links when editing in place.

--posix
    Disable supported GNU syntax extensions.

-E, --regexp-extended
-r
    Use extended regular expressions.

-s, --separate
    Treat input files as separate streams for address and record state.

-u, --unbuffered
    Flush output more frequently.

-z, --null-data
    Separate input and output records with NUL rather than LF.

-l N, --line-length=N
    Set the wrap width used by the l command.

--sandbox
    Reject e, r, R, w, W, and s///e operations.
```

Long options may be abbreviated where the shared command-line parser can resolve the abbreviation unambiguously.

## ADDRESSES

Implemented address forms include:

```text
N        line N
$        last input line
/expr/   regular-expression address
M,N      inclusive address range
F~S      every Sth line beginning with F
A,+N     address A and the following N lines
A,~N     address A through the next line-number multiple of N
```

An address or range may be followed by `!` to negate its selection.

## COMMANDS

The implemented command set includes:

```text
=        print the input line number
a        append text
b        branch unconditionally
c        replace selected pattern spaces with text
d, D     delete pattern space / delete through first internal separator
e        execute a shell command, where permitted
g,G,h,H,x
         manipulate pattern and hold spaces
i        insert text
l        list pattern space unambiguously
n, N     read next record / append next record
p, P     print pattern space / first pattern-space line
q, Q     quit with / without automatic printing
r, R     append a file / successive line from a file
s        substitute; supports occurrence, e, g, p, i/I, m/M, and w flags
t, T     branch after successful / unsuccessful substitution
w, W     write pattern space / first pattern-space line
y        transliterate characters
:        define a label
{ ... }  group commands
#        comment
```

## REGULAR EXPRESSIONS

Regular expressions use the managed GNU BRE/ERE providers supplied by `Icod.CommandFramework`. Sed retains command-specific handling for empty-expression reuse, address and substitution modifiers, GNU escape preprocessing, occurrence selection, zero-length match progression, replacement expansion, and diagnostic presentation.

`--posix` narrows GNU extension handling without replacing the managed regular-expression engine. Locale-sensitive text behavior is selected through the command framework's text and regular-expression contracts.

## RECORD AND TEXT SEMANTICS

Primary input and files are processed as byte-preserving records rather than being normalized through `Environment.NewLine`.

- LF is the default record separator.
- `-z` selects NUL-delimited records.
- final-record termination is retained explicitly;
- malformed UTF-8 bytes can be preserved through the reversible text representation used by the command; and
- output serialization uses the selected record separator explicitly.

The current record plus one-record lookahead are retained; the command does not need to materialize the complete input stream.

## IN-PLACE EDITING AND SANDBOX

In-place editing uses the transactional replacement foundation supplied through `Icod.CommandFramework`. The command stages complete output before publication and uses the established filesystem capability layer for replacement and cleanup behavior.

`--sandbox` is enforced at script compilation and through denied runtime capabilities. It disables commands that can execute external processes or read/write auxiliary paths (`e`, `r`, `R`, `w`, `W`, and `s///e`).

## EXIT STATUS

```text
0      Program completed successfully.
1      General operational failure.
2      Invalid command usage or script invocation.
130    Operation was cancelled.
```

## PLATFORM NOTES

The project targets .NET 10 with C# 13 and is tested on Windows, Linux, and macOS. The core stream-editing engine is managed and does not invoke an external `sed` executable. Host-dependent process, filesystem, temporary-file, regular-expression, record, and transactional-replacement mechanics are supplied through .NET and `Icod.CommandFramework`.

## AUTHORS

The original Unix `sed` was written by **Lee E. McMahon**.

GNU sed 4.9 credits **Jay Fenlason, Tom Lord, Ken Pizzini, Paolo Bonzini, Jim Meyering, and Assaf Gordon** as its authors. This managed implementation is modeled on the GNU command behavior while retaining the original sed lineage credit in the source tree.

Migrated to .Net by Timothy J. Bruce <uniblab@hotmail.com>.

## COPYRIGHT

Copyright (c) 2026 Timothy J. Bruce

## LICENSE

This managed `sed` project is distributed under the GNU General Public License, version 3 or later. See `sed.LICENSE.txt` in build output and this directory's `LICENSE` file.

## SEE ALSO

`sed(1)`, `ed(1)`, `red(1)`
