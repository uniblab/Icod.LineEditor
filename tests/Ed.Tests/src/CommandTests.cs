namespace Icod.LineEditor.Ed.Tests;

using System.Text;
using Icod.CommandFramework.Diagnostics;
using Icod.LineEditor.Ed;
using Xunit;

/// <summary>Exercises the Phase LE7 command boundary over the reusable editor engine.</summary>
public sealed class CommandTests {
	/// <summary>Verifies the public help and version surfaces.</summary>
	[Theory]
	[InlineData( "--help", "Usage: ed" )]
	[InlineData( "--version", "GNU ed 1.22.5" )]
	public async Task ReportsHelpAndVersion(
		string option,
		string expected
	) {
		var result = await RunAsync( string.Empty, option );

		Assert.Equal( 0, result.Status );
		Assert.Contains( expected, result.StandardOutput, StringComparison.Ordinal );
		Assert.Equal( string.Empty, result.StandardError );
	}

	/// <summary>Verifies append, address ranges, printing, and forced quit through the shared engine.</summary>
	[Fact]
	public async Task ExecutesStandardProfileScript() {
		var result = await RunAsync(
			"a\nalpha\nbeta\n.\n1,2p\nQ\n"
		);

		Assert.Equal( 0, result.Status );
		Assert.Equal( "alpha\nbeta\n", result.StandardOutput );
		Assert.Equal( string.Empty, result.StandardError );
	}

	/// <summary>Verifies that the GNU traditional-mode option is accepted at the command boundary.</summary>
	[Fact]
	public async Task AcceptsTraditionalCompatibilityMode() {
		var result = await RunAsync(
			"a\nalpha\n.\np\nQ\n",
			"--traditional"
		);

		Assert.Equal( 0, result.Status );
		Assert.Equal( "alpha\n", result.StandardOutput );
		Assert.Equal( string.Empty, result.StandardError );
	}

	/// <summary>Verifies that -E selects the Shared GNU extended-expression provider.</summary>
	[Fact]
	public async Task UsesExtendedRegularExpressions() {
		var result = await RunAsync(
			"a\nalpha\n.\ns/(alpha|beta)/X/\np\nQ\n",
			"-E"
		);

		Assert.Equal( 0, result.Status );
		Assert.Equal( "X\n", result.StandardOutput );
		Assert.Equal( string.Empty, result.StandardError );
	}

	/// <summary>Verifies a missing initial file remains editable but produces the GNU input-file status.</summary>
	[Fact]
	public async Task MissingInitialFileReturnsStatusTwo() {
		var path = CreateTemporaryPath();
		var result = await RunAsync(
			"Q\n",
			"-s",
			path
		);

		Assert.Equal( 2, result.Status );
		Assert.Equal( string.Empty, result.StandardOutput );
		Assert.Contains( "No such file or directory", result.StandardError, StringComparison.Ordinal );
	}

	/// <summary>Verifies GNU +line initial-address selection without emitting byte counts.</summary>
	[Fact]
	public async Task SelectsInitialAddress() {
		var path = CreateTemporaryPath();
		try {
			await File.WriteAllTextAsync( path, "one\ntwo\nthree\n" );
			var result = await RunAsync(
				"p\nQ\n",
				"-s",
				"+2",
				path
			);

			Assert.Equal( 0, result.Status );
			Assert.Equal( "two\n", result.StandardOutput );
			Assert.Equal( string.Empty, result.StandardError );
		} finally {
			DeleteIfPresent( path );
		}
	}

	/// <summary>Verifies GNU behavior that an oversized +line selects the last line.</summary>
	[Fact]
	public async Task OversizedInitialAddressSelectsLastLine() {
		var path = CreateTemporaryPath();
		try {
			await File.WriteAllTextAsync( path, "one\ntwo\nthree\n" );
			var result = await RunAsync(
				"p\nQ\n",
				"-s",
				"+999",
				path
			);

			Assert.Equal( 0, result.Status );
			Assert.Equal( "three\n", result.StandardOutput );
			Assert.Equal( string.Empty, result.StandardError );
		} finally {
			DeleteIfPresent( path );
		}
	}

	/// <summary>Verifies that script mode suppresses initial-read and write byte counts.</summary>
	[Fact]
	public async Task ScriptModeSuppressesByteCounts() {
		var inputPath = CreateTemporaryPath();
		var outputPath = CreateTemporaryPath();
		try {
			await File.WriteAllTextAsync( inputPath, "one\ntwo\n" );
			var result = await RunAsync(
				string.Concat( "w ", outputPath, "\nQ\n" ),
				"-s",
				inputPath
			);

			Assert.Equal( 0, result.Status );
			Assert.Equal( string.Empty, result.StandardOutput );
			Assert.Equal( string.Empty, result.StandardError );
			Assert.Equal( "one\ntwo\n", await File.ReadAllTextAsync( outputPath ) );
		} finally {
			DeleteIfPresent( inputPath );
			DeleteIfPresent( outputPath );
		}
	}

	/// <summary>Verifies modified-buffer refusal maps to the GNU input/buffer problem status.</summary>
	[Fact]
	public async Task ModifiedBufferRefusalReturnsStatusTwo() {
		var result = await RunAsync(
			"a\nalpha\n.\nq\n"
		);

		Assert.Equal( 2, result.Status );
		Assert.Equal( string.Empty, result.StandardOutput );
		Assert.Equal( "?\n", result.StandardError );
	}

	/// <summary>Verifies that the restricted profile denies shell dispatch before process creation.</summary>
	[Fact]
	public async Task RestrictedModeDeniesShellCommands() {
		var result = await RunAsync(
			"! echo should-not-run\n",
			"--restricted"
		);

		Assert.Equal( 1, result.Status );
		Assert.Equal( string.Empty, result.StandardOutput );
		Assert.Equal( "?\n", result.StandardError );
	}

	/// <summary>Verifies verbose diagnostic expansion while retaining the leading question mark.</summary>
	[Fact]
	public async Task VerboseModeExplainsControlledErrors() {
		var result = await RunAsync(
			"Z\n",
			"--verbose"
		);

		Assert.Equal( 1, result.Status );
		Assert.StartsWith( "?\n", result.StandardError );
		Assert.Contains( "Unknown command", result.StandardError, StringComparison.Ordinal );
	}

	/// <summary>Verifies that quiet mode suppresses diagnostics without changing failure status.</summary>
	[Fact]
	public async Task QuietModeSuppressesDiagnostics() {
		var result = await RunAsync(
			"Z\n",
			"--quiet"
		);

		Assert.Equal( 1, result.Status );
		Assert.Equal( string.Empty, result.StandardOutput );
		Assert.Equal( string.Empty, result.StandardError );
	}

	/// <summary>Verifies that script mode does not suppress diagnostics.</summary>
	[Fact]
	public async Task ScriptModeRetainsDiagnostics() {
		var result = await RunAsync(
			"Z\n",
			"--script"
		);

		Assert.Equal( 1, result.Status );
		Assert.Equal( "?\n", result.StandardError );
	}

	/// <summary>Verifies loose exit status continues through a failed command.</summary>
	[Fact]
	public async Task LooseExitStatusContinuesAfterCommandFailure() {
		var result = await RunAsync(
			"Z\na\nalpha\n.\np\nQ\n",
			"--loose-exit-status"
		);

		Assert.Equal( 0, result.Status );
		Assert.Equal( "alpha\n", result.StandardOutput );
		Assert.Equal( "?\n", result.StandardError );
	}

	/// <summary>Verifies the P command toggles the default prompt for subsequent commands.</summary>
	[Fact]
	public async Task PromptCommandTogglesPrompting() {
		var result = await RunAsync(
			"P\nQ\n"
		);

		Assert.Equal( 0, result.Status );
		Assert.Equal( "*", result.StandardOutput );
		Assert.Equal( string.Empty, result.StandardError );
	}

	/// <summary>Verifies an explicit prompt is emitted for scripted input.</summary>
	[Fact]
	public async Task ExplicitPromptTurnsPromptingOn() {
		var result = await RunAsync(
			"Q\n",
			"--prompt",
			"ed> "
		);

		Assert.Equal( 0, result.Status );
		Assert.Equal( "ed> ", result.StandardOutput );
	}

	/// <summary>Verifies explicit CR stripping for CRLF-oriented input files.</summary>
	[Fact]
	public async Task StripsTrailingCarriageReturns() {
		var path = CreateTemporaryPath();
		try {
			await File.WriteAllBytesAsync( path, Encoding.UTF8.GetBytes( "alpha\r\nbeta\r\n" ) );
			var result = await RunAsync(
				"1,2p\nQ\n",
				"-s",
				"--strip-trailing-cr",
				path
			);

			Assert.Equal( 0, result.Status );
			Assert.Equal( "alpha\nbeta\n", result.StandardOutput );
			Assert.Equal( string.Empty, result.StandardError );
		} finally {
			DeleteIfPresent( path );
		}
	}

	/// <summary>Verifies that strip-trailing-cr preserves CR on an unterminated final record.</summary>
	[Fact]
	public async Task PreservesTrailingCarriageReturnOnUnterminatedRecord() {
		var path = CreateTemporaryPath();
		try {
			await File.WriteAllBytesAsync( path, Encoding.UTF8.GetBytes( "alpha\r" ) );
			var result = await RunAsync(
				"1p\nQ\n",
				"-s",
				"--strip-trailing-cr",
				path
			);

			Assert.Equal( 0, result.Status );
			Assert.Equal( "alpha\r\n", result.StandardOutput );
			Assert.Equal( string.Empty, result.StandardError );
		} finally {
			DeleteIfPresent( path );
		}
	}

	/// <summary>Verifies deterministic cancellation status before command input is consumed.</summary>
	[Fact]
	public async Task CancellationReturnsInterruptedStatus() {
		using var cancellationSource = new CancellationTokenSource();
		cancellationSource.Cancel();
		await using var input = new MemoryStream( Encoding.UTF8.GetBytes( "p\n" ), writable: false );
		await using var output = new MemoryStream();
		await using var error = new MemoryStream();

		var status = await Command.RunAsync(
			[],
			input,
			output,
			error,
			cancellationSource.Token
		);

		Assert.Equal( 2, status );
	}

	/// <summary>Verifies that a broken output stream becomes a controlled command failure.</summary>
	[Fact]
	public async Task BrokenOutputReturnsFailure() {
		await using var input = new MemoryStream(
			Encoding.UTF8.GetBytes( "a\nalpha\n.\np\nQ\n" ),
			writable: false
		);
		await using var output = new ThrowingWriteStream();
		await using var error = new MemoryStream();

		var status = await Command.RunAsync( [ "--verbose" ], input, output, error );

		Assert.Equal( 1, status );
		Assert.Contains(
			"simulated broken pipe",
			Encoding.UTF8.GetString( error.ToArray() ),
			StringComparison.Ordinal
		);
	}

	/// <summary>Verifies that command orchestration does not impose a small line-length limit.</summary>
	[Fact]
	public async Task PreservesLongLines() {
		var longLine = new string( 'x', 131072 );
		var result = await RunAsync(
			string.Concat( "a\n", longLine, "\n.\np\nQ\n" )
		);

		Assert.Equal( 0, result.Status );
		Assert.Equal( string.Concat( longLine, "\n" ), result.StandardOutput );
	}

	/// <summary>Verifies the segmented buffer through a command-level large-record-count script.</summary>
	[Fact]
	public async Task HandlesLargeBuffers() {
		var builder = new StringBuilder( "a\n" );
		for ( var index = 0; 5000 > index; index++ ) {
			builder.Append( "line-" );
			builder.Append( index );
			builder.Append( '\n' );
		}
		builder.Append( ".\n$=\nQ\n" );

		var result = await RunAsync( builder.ToString() );

		Assert.Equal( 0, result.Status );
		Assert.Equal( "5000\n", result.StandardOutput );
	}

	/// <summary>Verifies textual interoperability with GNU and Icod Diffutils ed-script fixtures.</summary>
	[Theory]
	[InlineData( "gnu-diffutils" )]
	[InlineData( "icod-diffutils" )]
	public async Task AppliesDiffutilsEdScripts(
		string fixtureName
	) {
		var fixtureDirectory = System.IO.Path.Combine(
			AppContext.BaseDirectory,
			"fixtures",
			fixtureName
		);
		var inputPath = CreateTemporaryPath();
		var outputPath = CreateTemporaryPath();
		try {
			File.Copy( System.IO.Path.Combine( fixtureDirectory, "original.txt" ), inputPath, overwrite: true );
			var script = await File.ReadAllTextAsync( System.IO.Path.Combine( fixtureDirectory, "change.ed" ) );
			script = string.Concat( script.TrimEnd( '\r', '\n' ), "\nw ", outputPath, "\nQ\n" );
			var result = await RunAsync( script, "-s", inputPath );

			Assert.Equal( 0, result.Status );
			Assert.Equal( string.Empty, result.StandardError );
			Assert.Equal(
				await File.ReadAllLinesAsync( System.IO.Path.Combine( fixtureDirectory, "expected.txt" ) ),
				await File.ReadAllLinesAsync( outputPath )
			);
		} finally {
			DeleteIfPresent( inputPath );
			DeleteIfPresent( outputPath );
		}
	}

	/// <summary>Verifies that the text-only CommandContext compatibility path remains usable.</summary>
	[Fact]
	public async Task SupportsTextCommandContext() {
		using var input = new StringReader( "a\nalpha\n.\np\nQ\n" );
		using var output = new StringWriter();
		using var error = new StringWriter();
		var context = new CommandContext( "ed", input, output, error );

		var status = await Command.RunAsync( [], context );

		Assert.Equal( 0, status );
		Assert.Equal( "alpha\n", output.ToString() );
		Assert.Equal( string.Empty, error.ToString() );
	}

	private static async Task<RunResult> RunAsync(
		string script,
		params string[] args
	) {
		await using var input = new MemoryStream( Encoding.UTF8.GetBytes( script ), writable: false );
		await using var output = new MemoryStream();
		await using var error = new MemoryStream();
		var status = await Command.RunAsync( args, input, output, error );
		return new RunResult(
			status,
			Encoding.UTF8.GetString( output.ToArray() ),
			Encoding.UTF8.GetString( error.ToArray() )
		);
	}

	private static string CreateTemporaryPath() => System.IO.Path.Combine(
		System.IO.Path.GetTempPath(),
		string.Concat( ".icod-ed-test-", Guid.NewGuid().ToString( "N" ) )
	);

	private static void DeleteIfPresent(
		string path
	) {
		try {
			File.Delete( path );
		} catch ( IOException ) {
		} catch ( UnauthorizedAccessException ) {
		}
	}

	private sealed record RunResult(
		int Status,
		string StandardOutput,
		string StandardError
	);

	private sealed class ThrowingWriteStream : MemoryStream {
		public override ValueTask WriteAsync(
			ReadOnlyMemory<byte> buffer,
			CancellationToken cancellationToken = default
		) => new(
			Task.FromException(
				new IOException( "simulated broken pipe" )
			)
		);
	}
}
