namespace Icod.LineEditor.Red;

using Icod.LineEditor.Ed;

using System.Globalization;
using System.Text;
using Icod.CommandFramework.CommandLine;
using Icod.CommandFramework.Diagnostics;
using Icod.CommandFramework.Records;
using Icod.CommandFramework.RegularExpressions;

/// <summary>
/// Implements the GNU-compatible restricted line editor over <see cref="EditorEngine"/>.
/// <para>Usage: <c>red [OPTION]... [[+LINE] FILE]</c>.</para>
/// </summary>
public static class Command {
	private const string ProgramName = "red";
	private const string Version = "red (Icod.CoreUtils) 1.0; GNU ed 1.22.5 restricted compatibility profile";
	private static readonly ReadOnlyMemory<byte> LineFeed = new byte[] { (byte)'\n' };

	/// <summary>Runs the command synchronously for compatibility.</summary>
	public static int Run(
		string[] args,
		TextReader? stdin = null,
		TextWriter? stdout = null,
		TextWriter? stderr = null
	) => RunAsync(
		args,
		stdin,
		stdout,
		stderr
	).GetAwaiter().GetResult();

	/// <summary>Runs the command asynchronously with injectable text streams.</summary>
	public static Task<int> RunAsync(
		string[] args,
		TextReader? stdin = null,
		TextWriter? stdout = null,
		TextWriter? stderr = null,
		CancellationToken cancellationToken = default
	) => RunAsync(
		args ?? [],
		new CommandContext(
			ProgramName,
			stdin ?? Console.In,
			stdout ?? Console.Out,
			stderr ?? Console.Error,
			cancellationToken: cancellationToken
		)
	);

	/// <summary>Runs the command asynchronously with byte-preserving streams.</summary>
	public static Task<int> RunAsync(
		string[] args,
		Stream standardInput,
		Stream standardOutput,
		Stream standardError,
		CancellationToken cancellationToken = default
	) {
		ArgumentNullException.ThrowIfNull( standardInput );
		ArgumentNullException.ThrowIfNull( standardOutput );
		ArgumentNullException.ThrowIfNull( standardError );
		return RunCoreAsync(
			args ?? [],
			standardInput,
			standardOutput,
			standardError,
			isInteractive: false,
			cancellationToken
		);
	}

	/// <summary>Runs the command asynchronously with a complete command context.</summary>
	public static async Task<int> RunAsync(
		string[] args,
		CommandContext context
	) {
		ArgumentNullException.ThrowIfNull( context );
		if (
			null != context.StandardInputStream
			&& null != context.StandardOutputStream
			&& null != context.StandardErrorStream
		) {
			return await RunCoreAsync(
				args ?? [],
				context.StandardInputStream,
				context.StandardOutputStream,
				context.StandardErrorStream,
				isInteractive: ReferenceEquals( context.StandardInput, Console.In ) && !Console.IsInputRedirected,
				context.CancellationToken
			).ConfigureAwait( false );
		}

		var inputText = await context.StandardInput.ReadToEndAsync(
			context.CancellationToken
		).ConfigureAwait( false );
		await using var input = new MemoryStream( Encoding.UTF8.GetBytes( inputText ), writable: false );
		await using var output = new MemoryStream();
		await using var error = new MemoryStream();
		var status = await RunCoreAsync(
			args ?? [],
			input,
			output,
			error,
			isInteractive: false,
			context.CancellationToken
		).ConfigureAwait( false );
		await context.StandardOutput.WriteAsync(
			Encoding.UTF8.GetString( output.ToArray() ).AsMemory(),
			context.CancellationToken
		).ConfigureAwait( false );
		await context.StandardError.WriteAsync(
			Encoding.UTF8.GetString( error.ToArray() ).AsMemory(),
			context.CancellationToken
		).ConfigureAwait( false );
		return status;
	}

	/// <summary>Writes the complete command usage and option reference.</summary>
	public static async Task WriteUsageAsync(
		CommandContext context
	) {
		ArgumentNullException.ThrowIfNull( context );
		const string usage = """
Usage: red [OPTION]... [[+LINE] FILE]
Edit text line by line under the immutable restricted-ed capability profile.

  -E, --extended-regexp   use extended regular expressions
  -G, --traditional       run in traditional compatibility mode
  -l, --loose-exit-status exit successfully after command errors
  -p, --prompt=STRING     use STRING as the command prompt
  -q, --quiet, --silent   suppress diagnostic messages
  -r, --restricted        accepted for compatibility; red is always restricted
  -s, --script            suppress byte counts and shell completion prompts
  -v, --verbose           print diagnostic explanations
      --strip-trailing-cr remove a trailing CR from each input record
      --unsafe-names      permit control characters in filenames
  -h, --help              display this help and exit
  -V, --version           output version information and exit

LINE may be a line number, '+', '/REGEXP/', or '?REGEXP?'.
Commands and edited records are LF-delimited data; CRLF command input is accepted.
""";
		await context.StandardOutput.WriteAsync(
			usage.AsMemory(),
			context.CancellationToken
		).ConfigureAwait( false );
	}

	private static async Task<int> RunCoreAsync(
		string[] args,
		Stream standardInput,
		Stream standardOutput,
		Stream standardError,
		bool isInteractive,
		CancellationToken cancellationToken
	) {
		var quietDiagnostics = false;
		try {
			var parser = CreateParser();
			var parsed = parser.Parse( args );
			if ( !parsed.IsSuccess ) {
				foreach ( var error in parsed.Errors ) {
					await WriteTextLineAsync(
						standardError,
						OptionDiagnosticFormatter.Format( ProgramName, error ),
						cancellationToken
					).ConfigureAwait( false );
				}
				return 1;
			}
			if ( parsed.HasOption( "help" ) ) {
				await WriteUsageAsync( standardOutput, cancellationToken ).ConfigureAwait( false );
				return 0;
			}
			if ( parsed.HasOption( "version" ) ) {
				await WriteTextLineAsync( standardOutput, Version, cancellationToken ).ConfigureAwait( false );
				return 0;
			}

			var options = RedOptions.From( parsed );
			quietDiagnostics = options.QuietDiagnostics;
			if ( !TryCreateInvocation( parsed.Operands, out var invocation, out var invocationError ) ) {
				if ( !quietDiagnostics ) {
					await WriteDiagnosticAsync( standardError, invocationError!, cancellationToken ).ConfigureAwait( false );
				}
				return 1;
			}
			if (
				null != invocation.FileName
				&& !invocation.FileName.StartsWith( '!' )
				&& !IsAllowedFileName( invocation.FileName, options.UnsafeNames )
			) {
				if ( !quietDiagnostics ) {
					await WriteDiagnosticAsync(
						standardError,
						"filename contains a disallowed control character",
						cancellationToken
					).ConfigureAwait( false );
				}
				return 1;
			}

			IEditorFileAccess fileAccess = new StandardEditorFileAccess();
			if ( options.StripTrailingCarriageReturn ) {
				fileAccess = new CarriageReturnStrippingFileAccess( fileAccess );
			}
			fileAccess = new FileNamePolicyEditorFileAccess(
				fileAccess,
				options.UnsafeNames
			);
			var workingDirectory = Directory.GetCurrentDirectory();
			var profile = EditorCapabilityProfile.Restricted( workingDirectory, fileAccess );
			fileAccess = profile.FileAccess;
			var expressionProvider = options.ExtendedRegularExpressions
				? (IRegularExpressionProvider)GnuExtendedRegularExpressionProvider.Default
				: GnuBasicRegularExpressionProvider.Default;
			var engine = new EditorEngine( profile, expressionProvider );

			var initialFileError = await LoadInitialFileAsync(
				engine,
				fileAccess,
				invocation,
				options,
				standardOutput,
				standardError,
				cancellationToken
			).ConfigureAwait( false );
			if ( null != invocation.InitialAddress ) {
				if ( !await TryApplyInitialAddressAsync(
					engine,
					invocation.InitialAddress,
					standardError,
					options.QuietDiagnostics,
					cancellationToken
				).ConfigureAwait( false ) ) {
					return 1;
				}
			}

			var sessionStatus = await RunSessionAsync(
				engine,
				standardInput,
				standardOutput,
				standardError,
				options,
				isInteractive,
				cancellationToken
			).ConfigureAwait( false );
			if ( 0 != sessionStatus ) {
				return sessionStatus;
			}
			return initialFileError && !isInteractive ? 2 : 0;
		} catch ( OperationCanceledException ) {
			return 2;
		} catch ( IOException exception ) {
			if ( !quietDiagnostics ) {
				try {
					await WriteDiagnosticAsync( standardError, exception.Message, CancellationToken.None ).ConfigureAwait( false );
				} catch ( IOException ) {
				}
			}
			return 1;
		} catch ( Exception exception ) when (
			exception is UnauthorizedAccessException
				or NotSupportedException
				or ArgumentException
				or InvalidOperationException
		) {
			if ( !quietDiagnostics ) {
				await WriteDiagnosticAsync( standardError, exception.Message, cancellationToken ).ConfigureAwait( false );
			}
			return 1;
		} catch ( Exception exception ) {
			if ( !quietDiagnostics ) {
				try {
					await WriteDiagnosticAsync(
						standardError,
						string.Concat( "internal editor failure: ", exception.Message ),
						CancellationToken.None
					).ConfigureAwait( false );
				} catch ( IOException ) {
				}
			}
			return 3;
		}
	}

	private static async Task<bool> LoadInitialFileAsync(
		EditorEngine engine,
		IEditorFileAccess fileAccess,
		RedInvocation invocation,
		RedOptions options,
		Stream standardOutput,
		Stream standardError,
		CancellationToken cancellationToken
	) {
		if ( null == invocation.FileName ) {
			engine.Load( [] );
			return false;
		}
		var fileName = invocation.FileName;
		try {
			EditorFileReadResult read;
			if ( fileName.StartsWith( '!' ) ) {
				throw new UnauthorizedAccessException( "Shell input is disabled in restricted mode." );
			} else {
				read = await fileAccess.ReadAsync( fileName, cancellationToken ).ConfigureAwait( false );
				engine.Load( read.Lines, read.FinalRecordTerminated, fileName );
			}
			if ( !options.ScriptMode ) {
				await WriteTextLineAsync(
					standardOutput,
					read.ByteCount.ToString( CultureInfo.InvariantCulture ),
					cancellationToken
				).ConfigureAwait( false );
			}
			return false;
		} catch ( FileNotFoundException ) {
			engine.Load( [], rememberedFileName: fileName );
			if ( !options.QuietDiagnostics ) {
				await WriteDiagnosticAsync(
					standardError,
					string.Concat( fileName, ": No such file or directory" ),
					cancellationToken
				).ConfigureAwait( false );
			}
			return true;
		} catch ( DirectoryNotFoundException ) {
			engine.Load( [], rememberedFileName: fileName );
			if ( !options.QuietDiagnostics ) {
				await WriteDiagnosticAsync(
					standardError,
					string.Concat( fileName, ": No such file or directory" ),
					cancellationToken
				).ConfigureAwait( false );
			}
			return true;
		}
	}

	private static async Task<int> RunSessionAsync(
		EditorEngine engine,
		Stream standardInput,
		Stream standardOutput,
		Stream standardError,
		RedOptions options,
		bool isInteractive,
		CancellationToken cancellationToken
	) {
		using var reader = new ByteRecordReader( standardInput );
		var verbose = options.Verbose;
		var prompt = options.Prompt;
		var modifiedQuitWarning = false;
		var modifiedEditWarning = false;
		var hadError = false;
		while ( true ) {
			if ( null != prompt ) {
				await standardOutput.WriteAsync( Encoding.UTF8.GetBytes( prompt ), cancellationToken ).ConfigureAwait( false );
				await standardOutput.FlushAsync( cancellationToken ).ConfigureAwait( false );
			}
			var record = await reader.ReadAsync( cancellationToken ).ConfigureAwait( false );
			if ( null == record ) {
				break;
			}
			var commandBytes = NormalizeRecord( record.Content, record.IsTerminated );
			var commandText = Encoding.UTF8.GetString( commandBytes.Span );
			var commandCharacter = FindCommandCharacter( commandText );
			if ( 'H' == commandCharacter && "H" == commandText.Trim() ) {
				verbose = !verbose;
				continue;
			}
			if ( 'P' == commandCharacter && "P" == commandText.Trim() ) {
				prompt = null == prompt ? "*" : null;
				continue;
			}
			if ( 'q' == commandCharacter && modifiedQuitWarning && "q" == commandText.Trim() ) {
				commandText = "Q";
				commandBytes = Encoding.UTF8.GetBytes( commandText );
				commandCharacter = 'Q';
			}
			if ( 'e' == commandCharacter && modifiedEditWarning ) {
				commandText = ReplaceCommandCharacter( commandText, 'E' );
				commandBytes = Encoding.UTF8.GetBytes( commandText );
				commandCharacter = 'E';
			}

			await using var commandStream = new MemoryStream();
			await commandStream.WriteAsync( commandBytes, cancellationToken ).ConfigureAwait( false );
			await commandStream.WriteAsync( LineFeed, cancellationToken ).ConfigureAwait( false );
			if ( commandCharacter is 'a' or 'i' or 'c' ) {
				var terminated = false;
				while ( true ) {
					var data = await reader.ReadAsync( cancellationToken ).ConfigureAwait( false );
					if ( null == data ) {
						break;
					}
					var dataBytes = NormalizeRecord( data.Content, data.IsTerminated );
					await commandStream.WriteAsync( dataBytes, cancellationToken ).ConfigureAwait( false );
					await commandStream.WriteAsync( LineFeed, cancellationToken ).ConfigureAwait( false );
					if ( dataBytes.Span.SequenceEqual( new byte[] { (byte)'.' } ) ) {
						terminated = true;
						break;
					}
				}
				if ( !terminated ) {
					if ( !options.QuietDiagnostics ) {
						await WriteQuestionAsync( standardError, cancellationToken ).ConfigureAwait( false );
					}
					if ( verbose && !options.QuietDiagnostics ) {
						await WriteTextLineAsync(
							standardError,
							"The command data block is not terminated by a single period.",
							cancellationToken
						).ConfigureAwait( false );
					}
					return options.LooseExitStatus ? 0 : 1;
				}
			}
			commandStream.Position = 0;

			var suppressInformational = options.ScriptMode && ( commandCharacter is 'e' or 'E' or 'r' or 'w' or 'W' );
			using var discardedOutput = suppressInformational ? new MemoryStream() : null;
			var commandOutput = suppressInformational ? discardedOutput! : standardOutput;
			await using var commandError = new MemoryStream();
			var result = await engine.ExecuteScriptAsync(
				commandStream,
				commandOutput,
				commandError,
				"<stdin>",
				cancellationToken
			).ConfigureAwait( false );
			await ForwardEngineErrorAsync(
				commandCharacter,
				result,
				commandError.ToArray(),
				standardOutput,
				standardError,
				options.QuietDiagnostics,
				cancellationToken
			).ConfigureAwait( false );
			if ( result.IsSuccess ) {
				modifiedQuitWarning = false;
				modifiedEditWarning = false;
				if ( result.QuitRequested ) {
					return options.LooseExitStatus ? 0 : hadError ? 1 : 0;
				}
				continue;
			}
			hadError = true;
			if ( EditorDiagnosticCode.ModifiedBuffer == result.Diagnostic?.Code ) {
				if ( 'q' == commandCharacter ) {
					modifiedQuitWarning = true;
				}
				if ( 'e' == commandCharacter ) {
					modifiedEditWarning = true;
				}
			}
			if ( verbose && !options.QuietDiagnostics && null != result.Diagnostic ) {
				await WriteTextLineAsync(
					standardError,
					result.Diagnostic.Message,
					cancellationToken
				).ConfigureAwait( false );
			}
			if ( EditorExitStatus.Interrupted == result.ExitStatus ) {
				return 2;
			}
			if ( !isInteractive && !options.LooseExitStatus ) {
				return EditorDiagnosticCode.ModifiedBuffer == result.Diagnostic?.Code ? 2 : 1;
			}
		}
		return options.LooseExitStatus ? 0 : hadError ? 1 : 0;
	}

	private static async ValueTask ForwardEngineErrorAsync(
		char commandCharacter,
		EditorExecutionResult result,
		ReadOnlyMemory<byte> bytes,
		Stream standardOutput,
		Stream standardError,
		bool quietDiagnostics,
		CancellationToken cancellationToken
	) {
		if ( bytes.IsEmpty ) {
			return;
		}
		if ( result.IsSuccess && 'h' == commandCharacter ) {
			await standardOutput.WriteAsync( bytes, cancellationToken ).ConfigureAwait( false );
			return;
		}
		var content = bytes;
		if (
			quietDiagnostics
			&& !result.IsSuccess
			&& 2 <= content.Length
			&& (byte)'?' == content.Span[ ^2 ]
			&& (byte)'\n' == content.Span[ ^1 ]
		) {
			content = content[ ..^2 ];
		}
		if ( !content.IsEmpty ) {
			await standardError.WriteAsync( content, cancellationToken ).ConfigureAwait( false );
		}
	}

	private static async Task<bool> TryApplyInitialAddressAsync(
		EditorEngine engine,
		string initialAddress,
		Stream standardError,
		bool quietDiagnostics,
		CancellationToken cancellationToken
	) {
		if ( "+" == initialAddress ) {
			engine.SetCurrentAddress( engine.Buffer.Count );
			return true;
		}
		var text = initialAddress[ 1.. ];
		if ( int.TryParse( text, NumberStyles.None, CultureInfo.InvariantCulture, out var address ) ) {
			try {
				engine.SetCurrentAddress( Math.Min( address, engine.Buffer.Count ) );
				return true;
			} catch ( ArgumentOutOfRangeException ) {
				if ( !quietDiagnostics ) {
					await WriteQuestionAsync( standardError, cancellationToken ).ConfigureAwait( false );
				}
				return false;
			}
		}
		if ( text.StartsWith( '/' ) && !text.EndsWith( '/' ) ) {
			text = string.Concat( text, "/" );
		} else if ( text.StartsWith( '?' ) && !text.EndsWith( '?' ) ) {
			text = string.Concat( text, "?" );
		}
		await using var script = new MemoryStream( Encoding.UTF8.GetBytes( string.Concat( text, "=\n" ) ), writable: false );
		await using var output = new MemoryStream();
		await using var error = new MemoryStream();
		var result = await engine.ExecuteScriptAsync(
			script,
			output,
			error,
			"<command-line>",
			cancellationToken
		).ConfigureAwait( false );
		if ( !result.IsSuccess ) {
			if ( !quietDiagnostics ) {
				await standardError.WriteAsync( error.ToArray(), cancellationToken ).ConfigureAwait( false );
			}
			return false;
		}
		var value = Encoding.UTF8.GetString( output.ToArray() ).Trim();
		if ( !int.TryParse( value, NumberStyles.None, CultureInfo.InvariantCulture, out address ) ) {
			if ( !quietDiagnostics ) {
				await WriteQuestionAsync( standardError, cancellationToken ).ConfigureAwait( false );
			}
			return false;
		}
		engine.SetCurrentAddress( address );
		return true;
	}

	private static bool TryCreateInvocation(
		IReadOnlyList<string> operands,
		out RedInvocation invocation,
		out string? error
	) {
		string? initialAddress = null;
		string? fileName = null;
		var index = 0;
		if ( 0 < operands.Count && operands[ 0 ].StartsWith( '+') ) {
			initialAddress = operands[ 0 ];
			index++;
		}
		if ( index < operands.Count ) {
			fileName = operands[ index++ ];
		}
		if ( index != operands.Count ) {
			invocation = default!;
			error = "too many file operands";
			return false;
		}
		invocation = new RedInvocation( initialAddress, fileName );
		error = null;
		return true;
	}

	private static OptionParser CreateParser() => new(
		[
			new OptionDefinition( "extended", 'E', [ "extended-regexp" ], allowMultiple: false ),
			new OptionDefinition( "traditional", 'G', [ "traditional" ], allowMultiple: false ),
			new OptionDefinition( "loose", 'l', [ "loose-exit-status" ], allowMultiple: false ),
			new OptionDefinition( "prompt", 'p', [ "prompt" ], OptionValueArity.Required, allowMultiple: false ),
			new OptionDefinition( "quiet", 'q', [ "quiet", "silent" ], allowMultiple: false ),
			new OptionDefinition( "restricted", 'r', [ "restricted" ], allowMultiple: false ),
			new OptionDefinition( "script", 's', [ "script" ], allowMultiple: false ),
			new OptionDefinition( "verbose", 'v', [ "verbose" ], allowMultiple: false ),
			new OptionDefinition( "strip-cr", null, [ "strip-trailing-cr" ], allowMultiple: false ),
			new OptionDefinition( "unsafe-names", null, [ "unsafe-names" ], allowMultiple: false ),
			new OptionDefinition( "help", 'h', [ "help" ], allowMultiple: false ),
			new OptionDefinition( "version", 'V', [ "version" ], allowMultiple: false ),
		],
		new OptionParserSettings {
			AllowLongOptionAbbreviations = true,
			Ordering = OptionOrdering.Permute,
		}
	);

	private static char FindCommandCharacter(
		string text
	) {
		var index = FindCommandIndex( text );
		return 0 > index ? '\0' : text[ index ];
	}

	private static int FindCommandIndex(
		string text
	) {
		var escaped = false;
		var delimiter = '\0';
		var afterMark = false;
		for ( var index = 0; text.Length > index; index++ ) {
			var character = text[ index ];
			if ( '\0' != delimiter ) {
				if ( escaped ) {
					escaped = false;
					continue;
				}
				if ( '\\' == character ) {
					escaped = true;
					continue;
				}
				if ( delimiter == character ) {
					delimiter = '\0';
				}
				continue;
			}
			if ( afterMark ) {
				afterMark = false;
				continue;
			}
			if ( '\'' == character ) {
				afterMark = true;
				continue;
			}
			if ( character is '/' or '?' ) {
				delimiter = character;
				continue;
			}
			if ( char.IsLetter( character ) || character is '!' or '=' or '#' ) {
				return index;
			}
		}
		return -1;
	}

	private static string ReplaceCommandCharacter(
		string text,
		char replacement
	) {
		var index = FindCommandIndex( text );
		if ( 0 > index ) {
			return text;
		}
		return string.Concat( text.Substring( 0, index ), replacement.ToString(), text.Substring( index + 1 ) );
	}

	private static ReadOnlyMemory<byte> NormalizeRecord(
		ReadOnlyMemory<byte> content,
		bool terminated
	) {
		if ( terminated && !content.IsEmpty && (byte)'\r' == content.Span[ ^1 ] ) {
			return content[ ..^1 ].ToArray();
		}
		return content.ToArray();
	}

	private static bool IsAllowedFileName(
		string fileName,
		bool allowUnsafeNames
	) {
		if ( fileName.Any( character => character is '\0' or '\n' ) ) {
			return false;
		}
		if ( allowUnsafeNames ) {
			return true;
		}
		return fileName.All(
			character => character is not ( '\a' or '\b' or '\t' or '\v' or '\f' or '\r' or '\u001B' or '\u007F' )
		);
	}

	private static async ValueTask WriteUsageAsync(
		Stream output,
		CancellationToken cancellationToken
	) {
		const string usage = """
Usage: red [OPTION]... [[+LINE] FILE]
Try 'red --help' for more information.
""";
		await output.WriteAsync( Encoding.UTF8.GetBytes( usage ), cancellationToken ).ConfigureAwait( false );
	}

	private static ValueTask WriteDiagnosticAsync(
		Stream error,
		string message,
		CancellationToken cancellationToken
	) => WriteTextLineAsync(
		error,
		string.Concat( ProgramName, ": ", message ),
		cancellationToken
	);

	private static async ValueTask WriteQuestionAsync(
		Stream error,
		CancellationToken cancellationToken
	) {
		await error.WriteAsync( new byte[] { (byte)'?', (byte)'\n' }, cancellationToken ).ConfigureAwait( false );
	}

	private static async ValueTask WriteTextLineAsync(
		Stream output,
		string text,
		CancellationToken cancellationToken
	) {
		await output.WriteAsync( Encoding.UTF8.GetBytes( text ), cancellationToken ).ConfigureAwait( false );
		await output.WriteAsync( LineFeed, cancellationToken ).ConfigureAwait( false );
	}

	private sealed record RedInvocation(
		string? InitialAddress,
		string? FileName
	);

	private sealed record RedOptions(
		bool ExtendedRegularExpressions,
		bool Traditional,
		bool LooseExitStatus,
		string? Prompt,
		bool QuietDiagnostics,
		bool ScriptMode,
		bool Verbose,
		bool StripTrailingCarriageReturn,
		bool UnsafeNames
	) {
		public static RedOptions From(
			OptionParseResult result
		) => new(
			result.HasOption( "extended" ),
			result.HasOption( "traditional" ),
			result.HasOption( "loose" ),
			result.GetLastValue( "prompt" ),
			result.HasOption( "quiet" ),
			result.HasOption( "script" ),
			result.HasOption( "verbose" ),
			result.HasOption( "strip-cr" ),
			result.HasOption( "unsafe-names" )
		);
	}

	private sealed class FileNamePolicyEditorFileAccess : IEditorFileAccess {
		private readonly IEditorFileAccess inner;
		private readonly bool allowUnsafeNames;

		public FileNamePolicyEditorFileAccess(
			IEditorFileAccess inner,
			bool allowUnsafeNames
		) {
			ArgumentNullException.ThrowIfNull( inner );
			this.inner = inner;
			this.allowUnsafeNames = allowUnsafeNames;
		}

		public ValueTask<EditorFileReadResult> ReadAsync(
			string path,
			CancellationToken cancellationToken = default
		) {
			this.Validate( path );
			return this.inner.ReadAsync( path, cancellationToken );
		}

		public ValueTask<EditorFileWriteResult> WriteAsync(
			string path,
			IReadOnlyList<ReadOnlyMemory<byte>> lines,
			bool append,
			bool terminateFinalRecord,
			CancellationToken cancellationToken = default
		) {
			this.Validate( path );
			return this.inner.WriteAsync(
				path,
				lines,
				append,
				terminateFinalRecord,
				cancellationToken
			);
		}

		private void Validate(
			string path
		) {
			if ( !IsAllowedFileName( path, this.allowUnsafeNames ) ) {
				throw new UnauthorizedAccessException( "The filename contains a disallowed control character." );
			}
		}
	}

	private sealed class CarriageReturnStrippingFileAccess : IEditorFileAccess {
		private readonly IEditorFileAccess inner;

		public CarriageReturnStrippingFileAccess(
			IEditorFileAccess inner
		) {
			ArgumentNullException.ThrowIfNull( inner );
			this.inner = inner;
		}

		public async ValueTask<EditorFileReadResult> ReadAsync(
			string path,
			CancellationToken cancellationToken = default
		) {
			var result = await this.inner.ReadAsync( path, cancellationToken ).ConfigureAwait( false );
			return result with {
				Lines = result.Lines.Select(
					( line, index ) =>
						!line.IsEmpty
						&& (byte)'\r' == line.Span[ ^1 ]
						&& ( result.FinalRecordTerminated || result.Lines.Count - 1 != index )
							? new ReadOnlyMemory<byte>( line[ ..^1 ].ToArray() )
							: new ReadOnlyMemory<byte>( line.ToArray() )
				).ToArray()
			};
		}

		public ValueTask<EditorFileWriteResult> WriteAsync(
			string path,
			IReadOnlyList<ReadOnlyMemory<byte>> lines,
			bool append,
			bool terminateFinalRecord,
			CancellationToken cancellationToken = default
		) => this.inner.WriteAsync(
			path,
			lines,
			append,
			terminateFinalRecord,
			cancellationToken
		);
	}
}
