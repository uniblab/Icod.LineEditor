// Original behavior/reference: sed (Lee E. McMahon)
// Ported to .NET by Timothy J. Bruce <uniblab@hotmail.com>

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

/// <summary>
/// Implements a portable GNU-compatible <c>sed</c> stream editor using Shared
/// managed GNU regular expressions and byte-preserving record processing.
/// </summary>
/// <remarks>
/// <para>
/// The command processor implements addressed commands, inclusive address
/// ranges, negation, command groups, labels and branches, pattern space,
/// hold space, substitution, transliteration, explicit printing, file
/// reads and writes, next-cycle commands, and in-place editing. Primary
/// input, script files, auxiliary files, and output are processed with TAP
/// operations. Input uses one-record lookahead and is never fully materialized.
/// </para>
/// <para>
/// In syntax descriptions, <c>M</c> and <c>N</c> are metavariables for
/// non-negative or positive decimal line numbers as required by the command.
/// They are not literal characters in a sed program.
/// </para>
/// <para>
/// Supported command-line options include <c>-n</c>, <c>-e</c>, <c>-f</c>,
/// <c>-i[SUFFIX]</c>, <c>-E</c>/<c>-r</c>, <c>-s</c>, <c>-u</c>,
/// <c>-z</c>, <c>-l N</c>, <c>--sandbox</c>, <c>--help</c>, and
/// <c>--version</c>.
/// </para>
/// <para>
/// Supported addresses include line numbers, <c>$</c>, regular-expression
/// addresses, GNU-style <c>first~step</c> addresses, and range ends
/// <c>+N</c> and <c>~N</c>. An address or range may be followed by
/// <c>!</c> to negate its selection.
/// </para>
/// <para>
/// Supported commands are <c>= a b c d D e g G h H i l n N p P q Q r R
/// s t T w W x y</c>, labels introduced with <c>:</c>, comments introduced
/// with <c>#</c>, and grouped commands enclosed in braces.
/// </para>
/// <para>
/// Regular expressions are compiled through the Shared managed GNU BRE/ERE
/// provider. Sed retains command-local policy for empty-expression reuse,
/// address and substitution modifiers, occurrence selection, zero-length
/// match iteration, replacement expansion, and diagnostic presentation.
/// </para>
/// </remarks>
public static partial class Command {

	#region fields
	private const int DefaultListWidth = 70;
	private const int ErrorExitCode = CommandExitCodes.Failure;
	private const int UsageExitCode = CommandExitCodes.UsageError;
	private const string VersionText = "Icod.LineEditor.Sed 1.0";
	#endregion fields

	/// <summary>
	/// Executes <c>sed</c> synchronously with optional standard-stream substitution.
	/// </summary>
	/// <remarks>
	/// This compatibility entry point blocks on the TAP implementation. A <see langword="null"/> text stream selects the corresponding <see cref="Console"/> stream; caller-supplied streams remain caller-owned.
	/// </remarks>
	/// <param name="args">The command-line arguments, excluding the executable name.</param>
	/// <param name="stdin">The text reader to use as standard input, or <see langword="null"/> to use <see cref="Console.In"/>.</param>
	/// <param name="stdout">The text writer to use as standard output, or <see langword="null"/> to use <see cref="Console.Out"/>.</param>
	/// <param name="stderr">The text writer to use as standard error, or <see langword="null"/> to use <see cref="Console.Error"/>.</param>
	/// <returns>The GNU-compatible process exit status: zero for successful command execution and nonzero for a usage or operational failure.</returns>
	public static int Run(
		string[] args,
		TextReader? stdin = null,
		TextWriter? stdout = null,
		TextWriter? stderr = null
	) {
		return RunAsync(
			args,
			stdin,
			stdout,
			stderr,
			CancellationToken.None
		).GetAwaiter().GetResult();
	}

	/// <summary>
	/// Executes <c>sed</c> asynchronously with optional injected standard streams.
	/// </summary>
	/// <remarks>
	/// A <see langword="null"/> text stream selects the corresponding <see cref="Console"/> stream. Caller-supplied streams remain caller-owned.
	/// </remarks>
	/// <param name="args">The command-line arguments, excluding the executable name.</param>
	/// <param name="stdin">The text reader to use as standard input, or <see langword="null"/> to use <see cref="Console.In"/>.</param>
	/// <param name="stdout">The text writer to use as standard output, or <see langword="null"/> to use <see cref="Console.Out"/>.</param>
	/// <param name="stderr">The text writer to use as standard error, or <see langword="null"/> to use <see cref="Console.Error"/>.</param>
	/// <param name="cancellationToken">The token used to cancel parsing, platform queries, and asynchronous I/O.</param>
	/// <returns>The GNU-compatible process exit status: zero for successful command execution and nonzero for a usage or operational failure.</returns>
	public static Task<int> RunAsync(
		string[] args,
		TextReader? stdin = null,
		TextWriter? stdout = null,
		TextWriter? stderr = null,
		CancellationToken cancellationToken = default
	) {
		return RunAsync(
			args,
			new CommandContext(
				"sed",
				stdin ?? Console.In,
				stdout ?? Console.Out,
				stderr ?? Console.Error,
				cancellationToken: cancellationToken
			)
		);
	}

	/// <summary>Executes Sed through the repository-standard command context.</summary>
	/// <param name="args">The command-line arguments, excluding the executable name.</param>
	/// <param name="context">The caller-owned standard streams, diagnostics, identity, and cancellation context.</param>
	/// <returns>The GNU-compatible process exit status.</returns>
	public static Task<int> RunAsync(
		string[] args,
		CommandContext context
	) {
		return RunAsync( args, context, SedRuntimeCapabilities.System );
	}

	/// <summary>Executes Sed through an injectable capability profile.</summary>
	internal static async Task<int> RunAsync(
		string[] args,
		CommandContext context,
		SedRuntimeCapabilities capabilities
	) {
		ArgumentNullException.ThrowIfNull( context );
		ArgumentNullException.ThrowIfNull( capabilities );

		using var inputAdapter = null == context.StandardInputStream
			? new TextReaderInputStream( context.StandardInput )
			: null
		;
		using var outputAdapter = null == context.StandardOutputStream
			? new TextWriterOutputStream( context.StandardOutput )
			: null
		;
		using var presentationAdapter = null != context.StandardOutputStream
			? new StreamWriter(
				context.StandardOutputStream,
				new UTF8Encoding( encoderShouldEmitUTF8Identifier: false ),
				8192,
				leaveOpen: true
			) {
				NewLine = "\n"
			}
			: null
		;

		try {
			return await RunCoreAsync(
				args,
				context.StandardInputStream ?? inputAdapter!,
				context.StandardOutputStream ?? outputAdapter!,
				presentationAdapter ?? context.StandardOutput,
				context.StandardError,
				capabilities,
				context.CancellationToken
			).ConfigureAwait( false );
		} finally {
			if ( null != presentationAdapter ) {
				await presentationAdapter.FlushAsync().ConfigureAwait( false );
			}
		}
	}

	/// <summary>Executes Sed against caller-owned byte streams.</summary>
	/// <param name="args">The command-line arguments, excluding the executable name.</param>
	/// <param name="stdin">The caller-owned standard-input byte stream.</param>
	/// <param name="stdout">The caller-owned standard-output byte stream.</param>
	/// <param name="stderr">The caller-owned standard-error text writer.</param>
	/// <param name="cancellationToken">The token used to cancel parsing and asynchronous I/O.</param>
	/// <returns>The GNU-compatible process exit status.</returns>
	internal static async Task<int> RunStreamAsync(
		string[] args,
		Stream stdin,
		Stream stdout,
		TextWriter stderr,
		CancellationToken cancellationToken = default
	) {
		ArgumentNullException.ThrowIfNull( stdin );
		ArgumentNullException.ThrowIfNull( stdout );
		ArgumentNullException.ThrowIfNull( stderr );
		using var presentationOutput = new StreamWriter(
			stdout,
			new UTF8Encoding( encoderShouldEmitUTF8Identifier: false ),
			8192,
			leaveOpen: true
		) {
			NewLine = "\n"
		};
		try {
			return await RunCoreAsync(
				args,
				stdin,
				stdout,
				presentationOutput,
				stderr,
				SedRuntimeCapabilities.System,
				cancellationToken
			).ConfigureAwait( false );
		} finally {
			await presentationOutput.FlushAsync().ConfigureAwait( false );
		}
	}

	private static async Task<int> RunCoreAsync(
		string[] args,
		Stream stdin,
		Stream stdout,
		TextWriter presentationOutput,
		TextWriter stderr,
		SedRuntimeCapabilities capabilities,
		CancellationToken cancellationToken
	) {
		args ??= Array.Empty<string>();
		try {
			var options = new Options();
			var scriptSources = new List<SedScriptSource>();
			var files = new List<string>();
			var argumentResult = await ParseArgumentsAsync(
				args,
				options,
				scriptSources,
				files,
				presentationOutput,
				stderr,
				cancellationToken
			).ConfigureAwait( false );
			await presentationOutput.FlushAsync( cancellationToken ).ConfigureAwait( false );
			if ( argumentResult.HasValue ) {
				return argumentResult.Value;
			}

			if ( 0 == scriptSources.Count ) {
				if ( 0 == files.Count ) {
					await stderr.WriteLineAsync( "sed: no script was provided" ).ConfigureAwait( false );
					return UsageExitCode;
				}
				scriptSources.Add(
					new SedScriptSource(
						SedScriptSourceKind.ImplicitOperand,
						"command-line script",
						files[ 0 ],
						0
					)
				);
				files.RemoveAt( 0 );
			}
			if ( 0 == files.Count ) {
				files.Add( "-" );
			}
			if ( options.InPlace ) {
				options.Separate = true;
			}
			if ( options.InPlace && files.Any( path => "-" == path ) ) {
				await stderr.WriteLineAsync( "sed: cannot edit standard input in-place" ).ConfigureAwait( false );
				return UsageExitCode;
			}

			var textCodec = SedTextCodec.CreateCurrent();
			var scriptDocument = SedScriptDocument.Create( scriptSources );
			var scriptText = scriptDocument.Text;
			var program = new ScriptParser(
				scriptDocument,
				options.ExtendedRegularExpressions,
				options.Sandbox,
				options.Posix,
				options.NullData,
				textCodec.Locale,
				cancellationToken
			).Parse();
			var runtimeCapabilities = options.Sandbox
				? capabilities.ForSandbox()
				: capabilities
			;

			if ( options.Debug ) {
				await stderr.WriteLineAsync( "SED PROGRAM:" ).ConfigureAwait( false );
				foreach ( var scriptLine in scriptText.Split( '\n' ) ) {
					await stderr.WriteLineAsync( $"  {scriptLine.TrimEnd( '\r' )}" ).ConfigureAwait( false );
				}
			}

			if ( options.InPlace ) {
				foreach ( var path in files ) {
					var result = await ProcessInPlaceAsync(
						path,
						options,
						program,
						textCodec,
						stderr,
						runtimeCapabilities,
						cancellationToken
					).ConfigureAwait( false );
					if ( result.Quit ) {
						return result.ExitCode;
					}
				}
				return 0;
			}

			if ( options.Separate ) {
				var separateOutput = new SedOutputWriter( stdout, textCodec, options.NullData ) {
					AutoFlush = options.Unbuffered
				};
				foreach ( var path in files ) {
					using var input = new InputSequence(
						new SourceSpec[] { new SourceSpec( path ) },
						stdin,
						options.NullData,
						textCodec
					);
					var environment = new ExecutionEnvironment(
						separateOutput,
						textCodec,
						stderr,
						options.SuppressAutomaticPrint,
						options.NullData,
						options.ListWidth,
						options.Debug,
						runtimeCapabilities.Shell,
						runtimeCapabilities.AuxiliaryFiles
					);
					try {
						var result = await ExecuteAsync( program, input, environment, cancellationToken ).ConfigureAwait( false );
						if ( result.Quit ) {
							return result.ExitCode;
						}
					} finally {
						await environment.DisposeAsync( cancellationToken ).ConfigureAwait( false );
					}
				}
				return 0;
			}

			var sharedEnvironment = new ExecutionEnvironment(
				stdout,
				textCodec,
				stderr,
				options.SuppressAutomaticPrint,
				options.NullData,
				options.ListWidth,
				options.Debug,
				options.Unbuffered,
				runtimeCapabilities.Shell,
				runtimeCapabilities.AuxiliaryFiles
			);
			try {
				using var input = new InputSequence(
					files.Select( path => new SourceSpec( path ) ).ToArray(),
					stdin,
					options.NullData,
					textCodec
				);
				return (
					await ExecuteAsync( program, input, sharedEnvironment, cancellationToken ).ConfigureAwait( false )
				).ExitCode;
			} finally {
				await sharedEnvironment.DisposeAsync( cancellationToken ).ConfigureAwait( false );
			}
		} catch ( ScriptParseException ex ) {
			await stderr.WriteLineAsync( $"sed: {ex.Message}" ).ConfigureAwait( false );
			return UsageExitCode;
		} catch ( OperationCanceledException ) {
			await stderr.WriteLineAsync( "sed: operation canceled" ).ConfigureAwait( false );
			return CommandExitCodes.Canceled;
		} catch ( SedCapabilityDeniedException ex ) {
			await stderr.WriteLineAsync( $"sed: {ex.Message}" ).ConfigureAwait( false );
			return ErrorExitCode;
		} catch ( Exception ex ) {
			await stderr.WriteLineAsync( $"sed: {ex.Message}" ).ConfigureAwait( false );
			return ErrorExitCode;
		}
	}

}
