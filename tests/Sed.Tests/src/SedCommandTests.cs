namespace Icod.LineEditor.Sed.Tests;

using System.Text;
using SedCommand = Icod.LineEditor.Sed.Command;
using Xunit;

public sealed class SedCommandTests {

	[Fact]
	public async Task SubstitutionUsesAutomaticPrinting() {
		var result = await RunAsync(
			new string[] { "s/alpha/beta/" },
			"alpha\nother\n"
		);
		Assert.Equal( 0, result.ExitCode );
		Assert.Equal( "beta\nother\n", result.Output );
	}

	[Fact]
	public async Task QuietAndPrintSelectMatchingLines() {
		var result = await RunAsync(
			new string[] { "-n", "/two/p" },
			"one\ntwo\nthree\n"
		);
		Assert.Equal( "two\n", result.Output );
	}

	[Fact]
	public async Task NumericRangeAndNegationAreInclusive() {
		var range = await RunAsync(
			new string[] { "2,3d" },
			"one\ntwo\nthree\nfour\n"
		);
		var negated = await RunAsync(
			new string[] { "-n", "2,3p" },
			"one\ntwo\nthree\nfour\n"
		);
		Assert.Equal( "one\nfour\n", range.Output );
		Assert.Equal( "two\nthree\n", negated.Output );
	}

	[Fact]
	public async Task LastAddressAndGroupedCommandsWork() {
		var result = await RunAsync(
			new string[] { "-n", "${s/three/THREE/;p;}" },
			"one\ntwo\nthree\n"
		);
		Assert.Equal( "THREE\n", result.Output );
	}

	[Fact]
	public async Task HoldSpaceCommandsWork() {
		var result = await RunAsync(
			new string[] { "-n", "1h;2{g;p;}" },
			"alpha\nbeta\n"
		);
		Assert.Equal( "alpha\n", result.Output );
	}

	[Fact]
	public async Task BranchesAndLabelsWork() {
		var result = await RunAsync(
			new string[] { "-n", ":again;s/aa/a/;t again;p" },
			"aaaa\n"
		);
		Assert.Equal( "a\n", result.Output );
	}

	[Fact]
	public async Task MultiLinePatternCommandsWork() {
		var result = await RunAsync(
			new string[] { "-n", "N;P;D" },
			"one\ntwo\nthree\n"
		);
		Assert.Equal( "one\ntwo\n", result.Output );
	}

	[Fact]
	public async Task TransliterationWorks() {
		var result = await RunAsync(
			new string[] { "y/abc/ABC/" },
			"cab\n"
		);
		Assert.Equal( "CAB\n", result.Output );
	}

	[Fact]
	public async Task SubstitutionOccurrenceGlobalAndBackReferenceWork() {
		var occurrence = await RunAsync(
			new string[] { "s/a/A/2g" },
			"aaaa\n"
		);
		var backReference = await RunAsync(
			new string[] { @"s/\(ab\)/[\1]/" },
			"ab\n"
		);
		Assert.Equal( "aAAA\n", occurrence.Output );
		Assert.Equal( "[ab]\n", backReference.Output );
	}

	[Fact]
	public async Task ExtendedRegularExpressionsAreSupported() {
		var result = await RunAsync(
			new string[] { "-E", "s/(ab)+/X/" },
			"abab\n"
		);
		Assert.Equal( "X\n", result.Output );
	}

	[Fact]
	public async Task AppendInsertAndChangeCommandsPreserveCycleOrdering() {
		var append = await RunAsync(
			new string[] { "1a appended" },
			"one\ntwo\n"
		);
		var insert = await RunAsync(
			new string[] { "1i inserted" },
			"one\n"
		);
		var change = await RunAsync(
			new string[] { "1,2c changed" },
			"one\ntwo\nthree\n"
		);
		Assert.Equal( "one\nappended\ntwo\n", append.Output );
		Assert.Equal( "inserted\none\n", insert.Output );
		Assert.Equal( "changed\nthree\n", change.Output );
	}

	[Fact]
	public async Task LineNumberAndListCommandsWork() {
		var numbered = await RunAsync(
			new string[] { "-n", "2=;2p" },
			"one\ntwo\n"
		);
		var listed = await RunAsync(
			new string[] { "-n", "l" },
			"a\tb\n"
		);
		Assert.Equal( "2\ntwo\n", numbered.Output );
		Assert.Equal( "a\\tb$\n", listed.Output );
	}

	[Fact]
	public async Task GnuAddressExtensionsWorkOutsidePosixMode() {
		var step = await RunAsync(
			new string[] { "-n", "1~2p" },
			"one\ntwo\nthree\nfour\nfive\n"
		);
		var relative = await RunAsync(
			new string[] { "-n", "2,+2p" },
			"one\ntwo\nthree\nfour\nfive\n"
		);
		var zero = await RunAsync(
			new string[] { "-n", "0,/two/p" },
			"one\ntwo\nthree\n"
		);
		Assert.Equal( "one\nthree\nfive\n", step.Output );
		Assert.Equal( "two\nthree\nfour\n", relative.Output );
		Assert.Equal( "one\ntwo\n", zero.Output );
	}

	[Fact]
	public async Task EmptyRegularExpressionReusesPreviousExpression() {
		var result = await RunAsync(
			new string[] { "-n", "/two/p;//p" },
			"one\ntwo\nthree\n"
		);
		Assert.Equal( "two\ntwo\n", result.Output );
	}

	[Fact]
	public async Task QuitCommandsReturnRequestedExitCodes() {
		var normal = await RunAsync(
			new string[] { "q7" },
			"alpha\nbeta\n"
		);
		var silent = await RunAsync(
			new string[] { "Q9" },
			"alpha\nbeta\n"
		);
		Assert.Equal( 7, normal.ExitCode );
		Assert.Equal( "alpha\n", normal.Output );
		Assert.Equal( 9, silent.ExitCode );
		Assert.Equal( string.Empty, silent.Output );
	}

	[Fact]
	public async Task ScriptFileIsReadAsynchronously() {
		var scriptPath = await CreateFileAsync(
			"s/alpha/beta/\n"
		);
		try {
			var result = await RunAsync(
				new string[] { "-f", scriptPath },
				"alpha\n"
			);
			Assert.Equal( "beta\n", result.Output );
		} finally {
			File.Delete( scriptPath );
		}
	}

	[Fact]
	public async Task SeparateModeResetsLastAddressForEachFile() {
		var first = await CreateFileAsync( "one\ntwo\n" );
		var second = await CreateFileAsync( "three\nfour\n" );
		try {
			var result = await RunAsync(
				new string[] { "-s", "-n", "$p", first, second },
				string.Empty
			);
			Assert.Equal( "two\nfour\n", result.Output );
		} finally {
			File.Delete( first );
			File.Delete( second );
		}
	}

	[Fact]
	public async Task NullDataUsesNulDelimitedRecords() {
		var result = await RunAsync(
			new string[] { "-z", "s/beta/BETA/" },
			"alpha\0beta\0"
		);
		Assert.Equal( "alpha\0BETA\0", result.Output );
	}

	[Fact]
	public async Task ReadAndWriteCommandsStreamAuxiliaryFiles() {
		var readPath = await CreateFileAsync( "extra\n" );
		var writePath = System.IO.Path.Combine(
			System.IO.Path.GetTempPath(),
			$"icod-sed-write-{Guid.NewGuid():N}.txt"
		);
		try {
			var result = await RunAsync(
				new string[] { $"1r {readPath};1w {writePath}" },
				"main\n"
			);
			Assert.Equal( "main\nextra\n", result.Output );
			Assert.Equal( "main\n", await File.ReadAllTextAsync( writePath ) );
		} finally {
			File.Delete( readPath );
			File.Delete( writePath );
		}
	}

	[Fact]
	public async Task SandboxRejectsFileAndExecutionCommands() {
		var write = await RunAsync(
			new string[] { "--sandbox", "w output.txt" },
			"alpha\n"
		);
		var execute = await RunAsync(
			new string[] { "--sandbox", "e echo alpha" },
			"alpha\n"
		);
		Assert.Equal( 2, write.ExitCode );
		Assert.Equal( 2, execute.ExitCode );
		Assert.Contains( "disabled in sandbox mode", write.Error );
		Assert.Contains( "disabled in sandbox mode", execute.Error );
	}

	[Fact]
	public async Task PosixModeRejectsGnuExtensions() {
		var command = await RunAsync(
			new string[] { "--posix", "Q" },
			"alpha\n"
		);
		var address = await RunAsync(
			new string[] { "--posix", "1~2p" },
			"alpha\n"
		);
		var quitCode = await RunAsync(
			new string[] { "--posix", "q7" },
			"alpha\n"
		);
		Assert.Equal( 2, command.ExitCode );
		Assert.Equal( 2, address.ExitCode );
		Assert.Equal( 2, quitCode.ExitCode );
		Assert.Contains( "POSIX mode", command.Error );
		Assert.Contains( "POSIX mode", address.Error );
		Assert.Contains( "POSIX mode", quitCode.Error );
	}

	[Fact]
	public async Task InvalidSubstitutionFlagIsRejected() {
		var result = await RunAsync(
			new string[] { "s/a/b/x" },
			"alpha\n"
		);
		Assert.Equal( 2, result.ExitCode );
		Assert.Contains( "unknown substitution flag", result.Error );
	}

	[Theory]
	[InlineData( "s/[a-/x/", "invalid regular expression" )]
	[InlineData( "y/ab/c/", "equal lengths" )]
	[InlineData( "s/a/b/0", "positive integer" )]
	public async Task InvalidProgramsReturnUsageErrors(
		string script,
		string expectedDiagnostic
	) {
		var result = await RunAsync(
			new string[] { script },
			"alpha\n"
		);
		Assert.Equal( 2, result.ExitCode );
		Assert.Contains( expectedDiagnostic, result.Error );
	}

	[Fact]
	public async Task DebugAnnotatesProgramAndCycles() {
		var result = await RunAsync(
			new string[] { "--debug", "s/a/b/" },
			"a\n"
		);
		Assert.Equal( "b\n", result.Output );
		Assert.Contains( "SED PROGRAM:", result.Error );
		Assert.Contains( "INPUT:", result.Error );
		Assert.Contains( "PATTERN:", result.Error );
	}

	[Fact]
	public async Task ExecuteCommandRunsThroughPlatformShell() {
		var command = OperatingSystem.IsWindows()
			? "echo batch2"
			: "printf batch2"
		;
		var result = await RunAsync(
			new string[] { "-n", $"e {command}" },
			"ignored\n"
		);
		Assert.Equal( 0, result.ExitCode );
		Assert.Contains( "batch2", result.Output );
	}

	[Fact]
	public async Task SubstitutionExecuteFlagReplacesPatternSpace() {
		var shellText = OperatingSystem.IsWindows()
			? "echo batch2"
			: "printf batch2"
		;
		var result = await RunAsync(
			new string[] { "-n", $"s/.*/{shellText}/e;p" },
			"ignored\n"
		);
		Assert.Equal( 0, result.ExitCode );
		Assert.Equal( "batch2\n", result.Output );
	}

	[Fact]
	public async Task InPlaceEditingCreatesBackupAndPreservesMode() {
		var path = await CreateFileAsync(
			"alpha\n"
		);
		var backup = path + ".bak";
		UnixFileMode? originalMode = null;
		if ( !OperatingSystem.IsWindows() ) {
			originalMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
			File.SetUnixFileMode(
				path,
				originalMode.Value
			);
		}
		try {
			var result = await RunAsync(
				new string[] { "-i.bak", "s/alpha/beta/", path },
				string.Empty
			);
			Assert.Equal( 0, result.ExitCode );
			Assert.Equal( "beta\n", await File.ReadAllTextAsync( path ) );
			Assert.Equal( "alpha\n", await File.ReadAllTextAsync( backup ) );
			if (
				!OperatingSystem.IsWindows()
				&& originalMode.HasValue
			) {
				Assert.Equal(
					originalMode.Value,
					File.GetUnixFileMode(
						path
					)
				);
			}
		} finally {
			File.Delete( path );
			File.Delete( backup );
		}
	}

	[Fact]
	public async Task InPlaceBackupSuffixMayContainWildcard() {
		var directory = System.IO.Path.Combine(
			System.IO.Path.GetTempPath(),
			$"icod-sed-dir-{Guid.NewGuid():N}"
		);
		Directory.CreateDirectory( directory );
		var path = System.IO.Path.Combine( directory, "input.txt" );
		await File.WriteAllTextAsync( path, "alpha\n", new UTF8Encoding( false ) );
		var backup = path + ".orig";
		try {
			var result = await RunAsync(
				new string[] { "--in-place=*.orig", "s/alpha/beta/", path },
				string.Empty
			);
			Assert.Equal( 0, result.ExitCode );
			Assert.True( File.Exists( backup ) );
		} finally {
			Directory.Delete( directory, recursive: true );
		}
	}

	[Fact]
	public async Task FollowSymlinksEditsTargetWhenSupported() {
		var directory = System.IO.Path.Combine(
			System.IO.Path.GetTempPath(),
			$"icod-sed-link-{Guid.NewGuid():N}"
		);
		Directory.CreateDirectory( directory );
		var target = System.IO.Path.Combine( directory, "target.txt" );
		var link = System.IO.Path.Combine( directory, "link.txt" );
		await File.WriteAllTextAsync( target, "alpha\n", new UTF8Encoding( false ) );
		try {
			try {
				File.CreateSymbolicLink( link, target );
			} catch (
				Exception ex
			) when (
				ex is UnauthorizedAccessException
				or PlatformNotSupportedException
				or IOException
			) {
				return;
			}
			var result = await RunAsync(
				new string[] { "--follow-symlinks", "-i", "s/alpha/beta/", link },
				string.Empty
			);
			Assert.Equal( 0, result.ExitCode );
			Assert.Equal( "beta\n", await File.ReadAllTextAsync( target ) );
			Assert.NotNull( new FileInfo( link ).LinkTarget );
		} finally {
			Directory.Delete( directory, recursive: true );
		}
	}

	[Fact]
	public async Task HelpVersionAndUnknownOptionsUseSharedParser() {
		var help = await RunAsync( new string[] { "--help" }, string.Empty );
		var version = await RunAsync( new string[] { "--version" }, string.Empty );
		var invalid = await RunAsync( new string[] { "--not-an-option" }, string.Empty );
		Assert.Equal( 0, help.ExitCode );
		Assert.Contains( "Usage: sed", help.Output );
		Assert.Equal( 0, version.ExitCode );
		Assert.Contains( "Icod.LineEditor.Sed", version.Output );
		Assert.Equal( 2, invalid.ExitCode );
		Assert.Contains( "unrecognized option", invalid.Error );
	}

	[Fact]
	public async Task CancellationReturnsConventionalExitCode() {
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		var result = await RunAsync(
			new string[] { "p" },
			"alpha\n",
			cancellation.Token
		);
		Assert.Equal( 130, result.ExitCode );
	}

	private static async Task<string> CreateFileAsync(
		string contents
	) {
		var path = System.IO.Path.Combine(
			System.IO.Path.GetTempPath(),
			$"icod-sed-test-{Guid.NewGuid():N}.txt"
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
		string input,
		CancellationToken cancellationToken = default
	) {
		using var output = new StringWriter { NewLine = "\n" };
		using var error = new StringWriter { NewLine = "\n" };
		var exitCode = await SedCommand.RunAsync(
			args,
			new StringReader( input ),
			output,
			error,
			cancellationToken
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
