namespace Icod.LineEditor.Sed;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Icod.CommandFramework.CommandLine;
using Icod.CommandFramework.Diagnostics;
using Icod.CommandFramework.IO;
using Icod.CommandFramework.Processes;

// Responsibility: command-line option parsing and usage presentation.
public static partial class Command {

	private sealed class Options {

		public bool Debug {
			get;
			set;
		}

		public bool ExtendedRegularExpressions {
			get;
			set;
		}

		public bool FollowSymlinks {
			get;
			set;
		}

		public bool InPlace {
			get;
			set;
		}

		public bool Posix {
			get;
			set;
		}

		public string? BackupSuffix {
			get;
			set;
		}

		public int ListWidth {
			get;
			set;
		} = DefaultListWidth;

		public bool NullData {
			get;
			set;
		}

		public bool Sandbox {
			get;
			set;
		}

		public bool Separate {
			get;
			set;
		}

		public bool SuppressAutomaticPrint {
			get;
			set;
		}

		public bool Unbuffered {
			get;
			set;
		}

	}

	private static async Task<int?> ParseArgumentsAsync(
		string[] args,
		Options options,
		ICollection<SedScriptSource> scripts,
		ICollection<string> files,
		TextWriter stdout,
		TextWriter stderr,
		CancellationToken cancellationToken
	) {
		var parser = new OptionParser(
			new OptionDefinition[] {
				new OptionDefinition( "quiet", 'n', new string[] { "quiet", "silent" } ),
				new OptionDefinition( "debug", longNames: new string[] { "debug" } ),
				new OptionDefinition( "expression", 'e', new string[] { "expression" }, OptionValueArity.Required ),
				new OptionDefinition( "file", 'f', new string[] { "file" }, OptionValueArity.Required ),
				new OptionDefinition( "follow-symlinks", longNames: new string[] { "follow-symlinks" } ),
				new OptionDefinition( "in-place", 'i', new string[] { "in-place" }, OptionValueArity.Optional ),
				new OptionDefinition( "line-length", 'l', new string[] { "line-length" }, OptionValueArity.Required ),
				new OptionDefinition( "posix", longNames: new string[] { "posix" } ),
				new OptionDefinition( "regexp-extended", 'E', new string[] { "regexp-extended" } ),
				new OptionDefinition( "regexp-extended-r", 'r' ),
				new OptionDefinition( "separate", 's', new string[] { "separate" } ),
				new OptionDefinition( "sandbox", longNames: new string[] { "sandbox" } ),
				new OptionDefinition( "unbuffered", 'u', new string[] { "unbuffered" } ),
				new OptionDefinition( "null-data", 'z', new string[] { "null-data" } ),
				new OptionDefinition( "help", '?', new string[] { "help" } ),
				new OptionDefinition( "version", 'V', new string[] { "version" } )
			},
			new OptionParserSettings {
				AllowLongOptionAbbreviations = true,
				Ordering = OptionOrdering.Permute
			}
		);
		var result = parser.Parse(
			args
		);
		if ( !result.IsSuccess ) {
			foreach ( var error in result.Errors ) {
				await stderr.WriteLineAsync(
					OptionDiagnosticFormatter.Format(
						"sed",
						error
					)
				).ConfigureAwait( false );
			}
			return UsageExitCode;
		}

		foreach ( var occurrence in result.Options ) {
			cancellationToken.ThrowIfCancellationRequested();
			switch ( occurrence.Definition.Key ) {
				case "quiet":
					options.SuppressAutomaticPrint = true;
					break;
				case "debug":
					options.Debug = true;
					break;
				case "expression": {
						var order = scripts.Count;
						scripts.Add(
							new SedScriptSource(
								SedScriptSourceKind.Expression,
								$"-e expression #{order + 1}",
								occurrence.Value ?? string.Empty,
								order
							)
						);
						break;
					}
				case "file": {
						var path = occurrence.Value ?? string.Empty;
						var order = scripts.Count;
						scripts.Add(
							new SedScriptSource(
								SedScriptSourceKind.File,
								path,
								await ReadScriptFileAsync(
									path,
									cancellationToken
								).ConfigureAwait( false ),
								order
							)
						);
						break;
					}
				case "follow-symlinks":
					options.FollowSymlinks = true;
					break;
				case "in-place":
					options.InPlace = true;
					options.BackupSuffix = occurrence.Value ?? string.Empty;
					break;
				case "line-length":
					if (
						!int.TryParse(
							occurrence.Value,
							NumberStyles.None,
							CultureInfo.InvariantCulture,
							out var listWidth
						)
						|| listWidth <= 0
					) {
						await stderr.WriteLineAsync(
							"sed: option --line-length requires a positive integer"
						).ConfigureAwait( false );
						return UsageExitCode;
					}
					options.ListWidth = listWidth;
					break;
				case "posix":
					options.Posix = true;
					break;
				case "regexp-extended":
				case "regexp-extended-r":
					options.ExtendedRegularExpressions = true;
					break;
				case "separate":
					options.Separate = true;
					break;
				case "sandbox":
					options.Sandbox = true;
					break;
				case "unbuffered":
					options.Unbuffered = true;
					break;
				case "null-data":
					options.NullData = true;
					break;
				case "help":
					await PrintUsageAsync(
						stdout
					).ConfigureAwait( false );
					return CommandExitCodes.Success;
				case "version":
					await stdout.WriteLineAsync(
						VersionText
					).ConfigureAwait( false );
					return CommandExitCodes.Success;
			}
		}

		foreach ( var operand in result.Operands ) {
			files.Add(
				operand
			);
		}
		return null;
	}

	private static async Task PrintUsageAsync(
		TextWriter stdout
	) {
		using ( var buffer = new StringWriter(
			CultureInfo.InvariantCulture
		) ) {
			PrintUsage(
				buffer
			);
			await stdout.WriteAsync(
				buffer.ToString()
			).ConfigureAwait( false );
		}
	}

	private static void PrintUsage(
		TextWriter stdout
	) {
		stdout.WriteLine(
			"Usage: sed [OPTION]... {script-only-if-no-other-script} [input-file]..."
		);
		stdout.WriteLine(
			"  -?, --help                  display this help"
		);
		stdout.WriteLine(
			"  -V, --version               display version information"
		);
		stdout.WriteLine(
			"  -n, --quiet, --silent       suppress automatic printing"
		);
		stdout.WriteLine(
			"      --debug                  annotate program execution"
		);
		stdout.WriteLine(
			"  -e SCRIPT                   add SCRIPT to the program"
		);
		stdout.WriteLine(
			"  -f FILE                     add commands from script FILE"
		);
		stdout.WriteLine(
			"  -i[SUFFIX]                  edit files in place; optionally back up"
		);
		stdout.WriteLine(
			"      --follow-symlinks        follow symlinks when editing in place"
		);
		stdout.WriteLine(
			"      --posix                  disable GNU extensions"
		);
		stdout.WriteLine(
			"  -E, -r                      use extended regular expressions"
		);
		stdout.WriteLine(
			"  -s, --separate              treat input files separately"
		);
		stdout.WriteLine(
			"  -u, --unbuffered            flush output more frequently"
		);
		stdout.WriteLine(
			"  -z, --null-data             separate records with NUL"
		);
		stdout.WriteLine(
			"  -l N, --line-length=N       set the l-command wrap width"
		);
		stdout.WriteLine(
			"      --sandbox                disable e, r, R, w, W, and s///e"
		);
		stdout.WriteLine();
		stdout.WriteLine(
			"Addresses:"
		);
		stdout.WriteLine(
			"  N        line N; $ last line; /expr/ matching pattern space"
		);
		stdout.WriteLine(
			"  M,N      inclusive address range; append ! to negate"
		);
		stdout.WriteLine(
			"  F~S      every Sth line beginning with F"
		);
		stdout.WriteLine(
			"  A,+N     address A and the following N lines"
		);
		stdout.WriteLine(
			"  A,~N     address A through the next line-number multiple of N"
		);
		stdout.WriteLine();
		stdout.WriteLine(
			"Commands:"
		);
		stdout.WriteLine(
			"  =        print input line number"
		);
		stdout.WriteLine(
			"  a TEXT   append TEXT after the current cycle"
		);
		stdout.WriteLine(
			"  b LABEL  branch unconditionally"
		);
		stdout.WriteLine(
			"  c TEXT   replace selected pattern spaces with TEXT"
		);
		stdout.WriteLine(
			"  d, D     delete pattern space / delete through first newline"
		);
		stdout.WriteLine(
			"  e [CMD]  execute CMD, or execute pattern space when omitted"
		);
		stdout.WriteLine(
			"  g,G,h,H,x manipulate pattern and hold spaces"
		);
		stdout.WriteLine(
			"  i TEXT   insert TEXT before the current pattern space"
		);
		stdout.WriteLine(
			"  l [N]    list pattern space unambiguously"
		);
		stdout.WriteLine(
			"  n, N     read next record / append next record"
		);
		stdout.WriteLine(
			"  p, P     print pattern space / first pattern-space line"
		);
		stdout.WriteLine(
			"  q, Q     quit with / without automatic printing"
		);
		stdout.WriteLine(
			"  r,R FILE append FILE / one successive line from FILE"
		);
		stdout.WriteLine(
			"  sXreXreplacementXFLAGS  substitute using delimiter X"
		);
		stdout.WriteLine(
			"           FLAGS: N, e, g, p, i/I, m/M, w FILE"
		);
		stdout.WriteLine(
			"  t,T LABEL branch after successful / unsuccessful substitution"
		);
		stdout.WriteLine(
			"  w,W FILE write pattern space / first pattern-space line"
		);
		stdout.WriteLine(
			"  yXsrcXdstX transliterate characters"
		);
		stdout.WriteLine(
			"  :LABEL   define a label; { ... } group commands; # comment"
		);
	}

}
