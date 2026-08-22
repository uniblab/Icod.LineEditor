namespace Icod.LineEditor.Sed.Tests;

using System.Text;
using SedCommand = Icod.LineEditor.Sed.Command;
using Xunit;

/// <summary>
/// Captures command behavior that Phase LE1 must preserve while the Sed implementation is decomposed.
/// </summary>
public sealed class SedCharacterizationTests {

	/// <summary>
	/// Verifies that option placement around explicit script sources does not change the compiled program.
	/// </summary>
	[Fact]
	public async Task OptionOrderingAroundExplicitScriptsIsStable() {
		var leading = await RunAsync(
			new string[] { "-n", "-e", "s/a/A/", "-e", "p" },
			"a\n"
		);
		var trailing = await RunAsync(
			new string[] { "-e", "s/a/A/", "-e", "p", "-n" },
			"a\n"
		);

		Assert.Equal( 0, leading.ExitCode );
		Assert.Equal( leading.ExitCode, trailing.ExitCode );
		Assert.Equal( "A\n", leading.Output );
		Assert.Equal( leading.Output, trailing.Output );
	}

	/// <summary>
	/// Verifies that expression and script-file sources are compiled in command-line encounter order.
	/// </summary>
	[Fact]
	public async Task MultipleScriptSourcesRetainEncounterOrder() {
		var scriptPath = await CreateFileAsync( "s/b/c/" );
		try {
			var expressionThenFile = await RunAsync(
				new string[] { "-e", "s/a/b/", "-f", scriptPath },
				"a\n"
			);
			var fileThenExpression = await RunAsync(
				new string[] { "-f", scriptPath, "-e", "s/a/b/" },
				"a\n"
			);

			Assert.Equal( 0, expressionThenFile.ExitCode );
			Assert.Equal( 0, fileThenExpression.ExitCode );
			Assert.Equal( "c\n", expressionThenFile.Output );
			Assert.Equal( "b\n", fileThenExpression.Output );
		} finally {
			File.Delete( scriptPath );
		}
	}

	/// <summary>
	/// Verifies that malformed script files produce a controlled usage failure rather than escaping the command boundary.
	/// </summary>
	[Fact]
	public async Task MalformedScriptFileProducesControlledDiagnostic() {
		var scriptPath = await CreateFileAsync( "s/[a-/x/\n" );
		try {
			var result = await RunAsync(
				new string[] { "-f", scriptPath },
				"alpha\n"
			);

			Assert.Equal( 2, result.ExitCode );
			Assert.Empty( result.Output );
			Assert.Contains( "invalid regular expression", result.Error );
		} finally {
			File.Delete( scriptPath );
		}
	}

	/// <summary>
	/// Verifies that the first non-option operand remains the implicit script when no explicit script source is present.
	/// </summary>
	[Fact]
	public async Task ImplicitScriptOperandRemainsTheProgram() {
		var result = await RunAsync(
			new string[] { "-n", "p" },
			"alpha\n"
		);

		Assert.Equal( 0, result.ExitCode );
		Assert.Equal( "alpha\n", result.Output );
	}

	/// <summary>
	/// Verifies the LE4 contract that an unterminated final input record remains unterminated.
	/// </summary>
	[Fact]
	public async Task UnterminatedFinalRecordRemainsUnterminatedOnOutput() {
		var result = await RunAsync(
			new string[] { "s/alpha/beta/" },
			"alpha"
		);

		Assert.Equal( 0, result.ExitCode );
		Assert.Equal( "beta", result.Output );
	}

	/// <summary>
	/// Verifies that sandbox rejection happens during script compilation and does not create the requested output file.
	/// </summary>
	[Fact]
	public async Task SandboxRejectsFileEffectsFromScriptFiles() {
		var outputPath = $"icod-sed-sandbox-{Guid.NewGuid():N}.txt";
		var scriptPath = await CreateFileAsync( $"w {outputPath}\n" );
		try {
			var result = await RunAsync(
				new string[] { "--sandbox", "-f", scriptPath },
				"alpha\n"
			);

			Assert.Equal( 2, result.ExitCode );
			Assert.Contains( "disabled in sandbox mode", result.Error );
			Assert.False( File.Exists( outputPath ) );
		} finally {
			File.Delete( scriptPath );
			File.Delete( outputPath );
		}
	}

	/// <summary>
	/// Verifies that a script-compilation failure cannot begin an in-place edit or create its backup.
	/// </summary>
	[Fact]
	public async Task InvalidProgramCannotBeginInPlaceEditing() {
		var inputPath = await CreateFileAsync( "alpha\n" );
		var backupPath = inputPath + ".bak";
		try {
			var result = await RunAsync(
				new string[] { "-i.bak", "s/[a-/x/", inputPath },
				string.Empty
			);

			Assert.Equal( 2, result.ExitCode );
			Assert.Equal( "alpha\n", await File.ReadAllTextAsync( inputPath ) );
			Assert.False( File.Exists( backupPath ) );
		} finally {
			File.Delete( inputPath );
			File.Delete( backupPath );
		}
	}

	private static async Task<string> CreateFileAsync(
		string contents
	) {
		var path = System.IO.Path.Combine(
			System.IO.Path.GetTempPath(),
			$"icod-sed-characterization-{Guid.NewGuid():N}.txt"
		);
		await File.WriteAllTextAsync(
			path,
			contents,
			new UTF8Encoding( false )
		);
		return path;
	}

	private static async Task<CommandResult> RunAsync(
		string[] args,
		string input
	) {
		using var output = new StringWriter { NewLine = "\n" };
		using var error = new StringWriter { NewLine = "\n" };
		var exitCode = await SedCommand.RunAsync(
			args,
			new StringReader( input ),
			output,
			error
		);
		return new CommandResult(
			exitCode,
			output.ToString(),
			error.ToString()
		);
	}

	private sealed record CommandResult(
		int ExitCode,
		string Output,
		string Error
	);

}
