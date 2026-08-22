namespace Icod.LineEditor.Red.Tests;

using System.Text;
using EdCommand = Icod.LineEditor.Ed.Command;
using RedCommand = Icod.LineEditor.Red.Command;
using Xunit;

/// <summary>Exercises the permanently restricted <c>red</c> command boundary.</summary>
public sealed class CommandTests {
	[Theory]
	[InlineData( "--help", "Usage: red" )]
	[InlineData( "--version", "red (Icod.CoreUtils)" )]
	public async Task ReportsRedHelpAndVersion(
		string option,
		string expected
	) {
		var result = await RunRedAsync( string.Empty, option );

		Assert.Equal( 0, result.Status );
		Assert.Contains( expected, result.StandardOutput, StringComparison.Ordinal );
		Assert.Equal( string.Empty, result.StandardError );
	}

	[Fact]
	public async Task ExecutesOrdinaryEditorCommands() {
		var result = await RunRedAsync( "a\nalpha\nbeta\n.\n1,2p\nQ\n" );

		Assert.Equal( 0, result.Status );
		Assert.Equal( "alpha\nbeta\n", result.StandardOutput );
		Assert.Equal( string.Empty, result.StandardError );
	}

	[Theory]
	[InlineData( "a\none\ntwo\n.\n2s/two/TWO/\n1,2p\nQ\n" )]
	[InlineData( "a\none\n.\nZ\n" )]
	public async Task MatchesEdRestrictedProfile(
		string script
	) {
		var red = await RunRedAsync( script );
		var ed = await RunEdAsync( script, "--restricted" );

		Assert.Equal( ed, red );
	}

	[Fact]
	public async Task AcceptsExplicitRestrictedOption() {
		var result = await RunRedAsync( "a\none\n.\np\nQ\n", "-r" );

		Assert.Equal( 0, result.Status );
		Assert.Equal( "one\n", result.StandardOutput );
	}

	[Theory]
	[InlineData( "! echo should-not-run\n" )]
	[InlineData( "!!\n" )]
	[InlineData( "a\none\n.\n1! cat\n" )]
	public async Task DeniesShellCommands(
		string script
	) {
		var result = await RunRedAsync( script );

		Assert.Equal( 1, result.Status );
		Assert.Equal( string.Empty, result.StandardOutput );
		Assert.Equal( "?\n", result.StandardError );
	}

	[Fact]
	public async Task DeniesShellNestedInsideGlobalCommand() {
		var result = await RunRedAsync(
			"a\none\ntwo\n.\ng/two/! echo should-not-run\n"
		);

		Assert.Equal( 1, result.Status );
		Assert.Equal( string.Empty, result.StandardOutput );
		Assert.Equal( "?\n", result.StandardError );
	}

	[Fact]
	public async Task DeniesShellInitialFileOperand() {
		var result = await RunRedAsync( "Q\n", "!echo should-not-run" );

		Assert.Equal( 1, result.Status );
		Assert.Equal( string.Empty, result.StandardOutput );
		Assert.StartsWith( "red: ", result.StandardError );
		Assert.Contains( "Shell input is disabled", result.StandardError, StringComparison.Ordinal );
	}

	[Theory]
	[InlineData( "../outside" )]
	[InlineData( "/absolute" )]
	[InlineData( "dir/file" )]
	[InlineData( "dir\\file" )]
	[InlineData( "C:relative" )]
	[InlineData( "C:\\absolute" )]
	[InlineData( "\\\\server\\share" )]
	[InlineData( "\\\\?\\C:\\device" )]
	[InlineData( "stream:name" )]
	[InlineData( "CON" )]
	[InlineData( "nul.txt" )]
	[InlineData( "COM1.log" )]
	[InlineData( "LPT9" )]
	[InlineData( "leaf." )]
	[InlineData( "leaf " )]
	public async Task DeniesPathBearingFileOperands(
		string path
	) {
		var result = await RunRedAsync( "Q\n", path );

		Assert.Equal( 1, result.Status );
		Assert.Equal( string.Empty, result.StandardOutput );
		Assert.StartsWith( "red: ", result.StandardError );
		Assert.Contains( "simple filename", result.StandardError, StringComparison.Ordinal );
	}

	[Fact]
	public async Task UnsafeNamesDoesNotBypassRestrictedPathPolicy() {
		var result = await RunRedAsync( "Q\n", "--unsafe-names", "../outside" );

		Assert.Equal( 1, result.Status );
		Assert.StartsWith( "red: ", result.StandardError );
		Assert.Contains( "simple filename", result.StandardError, StringComparison.Ordinal );
	}

	[Fact]
	public async Task AllowsSimpleNamesInCapturedWorkingDirectory() {
		var inputName = string.Concat( ".icod-red-input-", Guid.NewGuid().ToString( "N" ), ".txt" );
		var outputName = string.Concat( ".icod-red-output-", Guid.NewGuid().ToString( "N" ), ".txt" );
		var inputPath = System.IO.Path.Combine( Directory.GetCurrentDirectory(), inputName );
		var outputPath = System.IO.Path.Combine( Directory.GetCurrentDirectory(), outputName );
		try {
			await File.WriteAllTextAsync( inputPath, "one\ntwo\n" );
			var result = await RunRedAsync(
				string.Concat( "2s/two/TWO/\nw ", outputName, "\nQ\n" ),
				"-s",
				inputName
			);

			Assert.Equal( 0, result.Status );
			Assert.Equal( string.Empty, result.StandardOutput );
			Assert.Equal( string.Empty, result.StandardError );
			Assert.Equal( "one\nTWO\n", await File.ReadAllTextAsync( outputPath ) );
		} finally {
			DeleteIfPresent( inputPath );
			DeleteIfPresent( outputPath );
		}
	}

	private static Task<RunResult> RunRedAsync(
		string script,
		params string[] args
	) => RunAsync( RedCommand.RunAsync, script, args );

	private static Task<RunResult> RunEdAsync(
		string script,
		params string[] args
	) => RunAsync( EdCommand.RunAsync, script, args );

	private static async Task<RunResult> RunAsync(
		Func<string[], Stream, Stream, Stream, CancellationToken, Task<int>> command,
		string script,
		string[] args
	) {
		await using var input = new MemoryStream( Encoding.UTF8.GetBytes( script ), writable: false );
		await using var output = new MemoryStream();
		await using var error = new MemoryStream();
		var status = await command( args, input, output, error, CancellationToken.None );
		return new RunResult(
			status,
			Encoding.UTF8.GetString( output.ToArray() ),
			Encoding.UTF8.GetString( error.ToArray() )
		);
	}

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
}
