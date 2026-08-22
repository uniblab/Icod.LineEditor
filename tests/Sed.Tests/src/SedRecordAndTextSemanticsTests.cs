namespace Icod.LineEditor.Sed.Tests;

using System.Globalization;
using System.Reflection;
using System.Text;
using SedCommand = Icod.LineEditor.Sed.Command;
using Xunit;

/// <summary>Verifies the byte-preserving record and text contracts introduced by LE4.</summary>
[Collection( "Sed environment" )]
public sealed class SedRecordAndTextSemanticsTests {

	/// <summary>Verifies that LF framing preserves carriage returns as ordinary data.</summary>
	[Theory]
	[InlineData( "one\r\ntwo\r\n" )]
	[InlineData( "one\rtwo\n" )]
	[InlineData( "\n\n" )]
	public async Task LineFeedModePreservesRecordBytes(
		string input
	) {
		var result = await RunAsync(
			new string[] { string.Empty },
			input
		);

		Assert.Equal( 0, result.ExitCode );
		Assert.Equal( input, result.Output );
	}

	/// <summary>Verifies that output framing is independent of the host writer newline.</summary>
	[Fact]
	public async Task OutputUsesExplicitLineFeedRatherThanWriterNewLine() {
		using var output = new StringWriter { NewLine = "\r\n" };
		using var error = new StringWriter { NewLine = "\r\n" };
		var exitCode = await SedCommand.RunAsync(
			new string[] { string.Empty },
			new StringReader( "alpha\n" ),
			output,
			error
		);

		Assert.Equal( 0, exitCode );
		Assert.Equal( "alpha\n", output.ToString() );
	}

	/// <summary>Verifies that NUL framing preserves a final unterminated record.</summary>
	[Fact]
	public async Task NullDataPreservesTerminationMetadata() {
		var result = await RunAsync(
			new string[] { "-z", string.Empty },
			"one\0two"
		);

		Assert.Equal( 0, result.ExitCode );
		Assert.Equal( "one\0two", result.Output );
	}

	/// <summary>Verifies that empty input produces no synthetic record.</summary>
	[Fact]
	public async Task EmptyInputProducesNoRecord() {
		var result = await RunAsync(
			new string[] { string.Empty },
			string.Empty
		);

		Assert.Equal( 0, result.ExitCode );
		Assert.Empty( result.Output );
	}

	/// <summary>Verifies that N at end of input still completes the current automatic-print cycle.</summary>
	[Fact]
	public async Task AppendNextAtEndPreservesCurrentRecord() {
		var result = await RunAsync(
			new string[] { "N" },
			"alpha"
		);

		Assert.Equal( 0, result.ExitCode );
		Assert.Equal( "alpha", result.Output );
	}

	/// <summary>Verifies that multiline pattern space inherits the last contributing record's termination.</summary>
	[Fact]
	public async Task MultilinePatternSpaceRetainsFinalTermination() {
		var result = await RunAsync(
			new string[] { "-n", "N;p" },
			"one\ntwo"
		);

		Assert.Equal( 0, result.ExitCode );
		Assert.Equal( "one\ntwo", result.Output );
	}

	/// <summary>Verifies that hold-space growth retains the selected pattern-space termination state.</summary>
	[Fact]
	public async Task HoldSpaceGrowthRetainsTermination() {
		var result = await RunAsync(
			new string[] { "-n", "H;g;p" },
			"alpha"
		);

		Assert.Equal( 0, result.ExitCode );
		Assert.Equal( "\nalpha", result.Output );
	}

	/// <summary>Verifies GNU separation between consecutive outputs after an unterminated record.</summary>
	[Theory]
	[InlineData( "-n", "p;p", "alpha\nalpha" )]
	[InlineData( "-n", "p;=", "alpha\n1\n" )]
	[InlineData( "", "a appended", "alpha\nappended\n" )]
	public async Task LaterOutputSeparatesAnUnterminatedRecord(
		string option,
		string script,
		string expected
	) {
		var arguments = string.IsNullOrEmpty( option )
			? new string[] { script }
			: new string[] { option, script }
		;
		var result = await RunAsync( arguments, "alpha" );

		Assert.Equal( 0, result.ExitCode );
		Assert.Equal( expected, result.Output );
	}

	/// <summary>Verifies that NUL framing separates repeated output after an unterminated record.</summary>
	[Fact]
	public async Task NullDataSeparatesRepeatedUnterminatedOutput() {
		var result = await RunAsync(
			new string[] { "-z", "-n", "p;p" },
			"alpha"
		);

		Assert.Equal( 0, result.ExitCode );
		Assert.Equal( "alpha\0alpha", result.Output );
	}

	/// <summary>Verifies that separate-file mode retains output framing across input boundaries.</summary>
	[Fact]
	public async Task SeparateFilesShareOutputTerminationState() {
		var firstPath = CreateTemporaryPath();
		var secondPath = CreateTemporaryPath();
		try {
			await File.WriteAllTextAsync( firstPath, "one" );
			await File.WriteAllTextAsync( secondPath, "two" );
			var result = await RunAsync(
				new string[] { "-s", string.Empty, firstPath, secondPath },
				string.Empty
			);

			Assert.Equal( 0, result.ExitCode );
			Assert.Equal( "one\ntwo", result.Output );
		} finally {
			File.Delete( firstPath );
			File.Delete( secondPath );
		}
	}

	/// <summary>Verifies that end-of-file next commands finish one separate input without terminating later files.</summary>
	[Theory]
	[InlineData( "n" )]
	[InlineData( "N" )]
	public async Task SeparateFilesContinueAfterNextCommandReachesEndOfFile(
		string script
	) {
		var firstPath = CreateTemporaryPath();
		var secondPath = CreateTemporaryPath();
		try {
			await File.WriteAllTextAsync( firstPath, "one\n" );
			await File.WriteAllTextAsync( secondPath, "two" );
			var result = await RunAsync(
				new string[] { "-s", script, firstPath, secondPath },
				string.Empty
			);

			Assert.Equal( 0, result.ExitCode );
			Assert.Equal( "one\ntwo", result.Output );
		} finally {
			File.Delete( firstPath );
			File.Delete( secondPath );
		}
	}

	/// <summary>Verifies that in-place processing continues after a next command reaches one file's end.</summary>
	[Fact]
	public async Task InPlaceFilesContinueAfterNextCommandReachesEndOfFile() {
		var firstPath = CreateTemporaryPath();
		var secondPath = CreateTemporaryPath();
		try {
			await File.WriteAllTextAsync( firstPath, "one" );
			await File.WriteAllTextAsync( secondPath, "two" );
			var result = await RunAsync(
				new string[] { "-i", "s/o/O/;n", firstPath, secondPath },
				string.Empty
			);

			Assert.Equal( 0, result.ExitCode );
			Assert.Equal( "One", await File.ReadAllTextAsync( firstPath ) );
			Assert.Equal( "twO", await File.ReadAllTextAsync( secondPath ) );
		} finally {
			File.Delete( firstPath );
			File.Delete( secondPath );
		}
	}

	/// <summary>Verifies P termination for an internal line and for a final unterminated record.</summary>
	[Theory]
	[InlineData( "one\ntwo", "N;P", "one\n" )]
	[InlineData( "one", "P", "one" )]
	public async Task PrintFirstUsesLogicalLineTermination(
		string input,
		string script,
		string expected
	) {
		var result = await RunAsync( new string[] { "-n", script }, input );

		Assert.Equal( 0, result.ExitCode );
		Assert.Equal( expected, result.Output );
	}

	/// <summary>Verifies that NUL mode uses NUL for multiline pattern-space operations.</summary>
	[Fact]
	public async Task NullDataUsesNulAsThePatternSpaceSeparator() {
		var printFirst = await RunAsync(
			new string[] { "-z", "-n", "N;P" },
			"one\0two"
		);
		var hold = await RunAsync(
			new string[] { "-z", "-n", "H;g;p" },
			"alpha"
		);

		Assert.Equal( 0, printFirst.ExitCode );
		Assert.Equal( "one\0", printFirst.Output );
		Assert.Equal( 0, hold.ExitCode );
		Assert.Equal( "\0alpha", hold.Output );
	}

	/// <summary>Verifies that D removes the first NUL-delimited portion of pattern space.</summary>
	[Fact]
	public async Task NullDataDeleteFirstUsesNulAsTheInternalSeparator() {
		var result = await RunAsync(
			new string[] { "-z", "-n", "N;D" },
			"one\0two"
		);

		Assert.Equal( 0, result.ExitCode );
		Assert.Empty( result.Output );
	}

	/// <summary>Verifies that W writes the first NUL-delimited portion with explicit termination.</summary>
	[Fact]
	public async Task NullDataWriteFirstUsesNulAsTheInternalSeparator() {
		var path = CreateTemporaryPath();
		try {
			var result = await RunAsync(
				new string[] { "-z", "-n", $"N;W {path}" },
				"one\0two"
			);

			Assert.Equal( 0, result.ExitCode );
			Assert.Empty( result.Output );
			Assert.Equal( "one\0", await File.ReadAllTextAsync( path ) );
		} finally {
			File.Delete( path );
		}
	}

	/// <summary>Verifies GNU list rendering for an internal NUL record separator.</summary>
	[Fact]
	public async Task NullDataListRendersAnInternalSeparatorAsOctal() {
		var result = await RunAsync(
			new string[] { "-z", "-n", "N;l" },
			"a\0b\0"
		);

		Assert.Equal( 0, result.ExitCode );
		Assert.Equal( "a\\000b$\0", result.Output );
	}

	/// <summary>Verifies that multiline anchors use NUL, not embedded line feed, in NUL mode.</summary>
	[Fact]
	public async Task NullDataMultilineAnchorsUseTheConfiguredSeparator() {
		var matchesNulBoundary = await RunAsync(
			new string[] { "-z", "-n", "N;/^b/Mp" },
			"a\0b\0"
		);
		var ignoresEmbeddedLineFeed = await RunAsync(
			new string[] { "-z", "-n", "N;/^x/Mp" },
			"a\nx\0b\0"
		);

		Assert.Equal( 0, matchesNulBoundary.ExitCode );
		Assert.Equal( "a\0b\0", matchesNulBoundary.Output );
		Assert.Equal( 0, ignoresEmbeddedLineFeed.ExitCode );
		Assert.Empty( ignoresEmbeddedLineFeed.Output );
	}

	/// <summary>Verifies GNU dot behavior with NUL data and the multiline modifier.</summary>
	[Fact]
	public async Task NullDataDotMatchesNulExceptWhenItIsAMultilineBoundary() {
		var ordinary = await RunAsync(
			new string[] { "-z", "N;s/./X/g" },
			"a\0b\0"
		);
		var multiline = await RunAsync(
			new string[] { "-z", "N;s/./X/gM" },
			"a\0b\0"
		);

		Assert.Equal( 0, ordinary.ExitCode );
		Assert.Equal( "XXX\0", ordinary.Output );
		Assert.Equal( 0, multiline.ExitCode );
		Assert.Equal( "X\0X\0", multiline.Output );
	}

	/// <summary>Verifies that a large logical record is accepted without materializing unrelated input records.</summary>
	[Fact]
	public async Task LargeRecordRemainsARecord() {
		var input = new string( 'a', 1_048_576 ) + "\nsmall\n";
		var result = await RunAsync(
			new string[] { "s/^a/A/" },
			input
		);

		Assert.Equal( 0, result.ExitCode );
		Assert.Equal( input.Length, result.Output.Length );
		Assert.Equal( 'A', result.Output[ 0 ] );
		Assert.EndsWith( "\nsmall\n", result.Output );
	}

	/// <summary>Verifies that the executable byte-stream path preserves malformed UTF-8 on standard input and output.</summary>
	[Fact]
	public async Task RawStreamPathPreservesMalformedUtf8() {
		var previous = SetLocale( "C.UTF-8" );
		try {
			var runStream = typeof( SedCommand ).GetMethod(
				"RunStreamAsync",
				BindingFlags.NonPublic | BindingFlags.Static
			);
			Assert.NotNull( runStream );
			using var input = new MemoryStream(
				new byte[] { (byte)'a', 0x80, (byte)'b' }
			);
			using var output = new MemoryStream();
			using var error = new StringWriter { NewLine = "\n" };
			var operation = Assert.IsType<Task<int>>(
				runStream!.Invoke(
					null,
					new object[] {
						new string[] { "s/a/A/" },
						input,
						output,
						error,
						CancellationToken.None
					}
				)
			);

			Assert.Equal( 0, await operation );
			Assert.Equal(
				new byte[] { (byte)'A', 0x80, (byte)'b' },
				output.ToArray()
			);
			Assert.Empty( error.ToString() );
		} finally {
			RestoreLocale( previous );
		}
	}

	/// <summary>Verifies deterministic preservation of malformed UTF-8 during in-place replacement.</summary>
	[Fact]
	public async Task InvalidUtf8BytesRoundTripThroughInPlaceEditing() {
		var path = CreateTemporaryPath();
		var previous = SetLocale( "C.UTF-8" );
		try {
			await File.WriteAllBytesAsync(
				path,
				new byte[] { (byte)'a', 0x80, (byte)'b' }
			);
			var result = await RunAsync(
				new string[] { "-i", "s/a/A/", path },
				string.Empty
			);

			Assert.Equal( 0, result.ExitCode );
			Assert.Equal(
				new byte[] { (byte)'A', 0x80, (byte)'b' },
				await File.ReadAllBytesAsync( path )
			);
		} finally {
			RestoreLocale( previous );
			File.Delete( path );
		}
	}

	/// <summary>Verifies the explicit C-byte and UTF-8 locale profiles.</summary>
	[Fact]
	public async Task LocaleSelectsByteOrUtf8TextSemantics() {
		var bytePath = CreateTemporaryPath();
		var utf8Path = CreateTemporaryPath();
		var previous = CaptureLocale();
		var previousCulture = CultureInfo.CurrentCulture;
		var previousUiCulture = CultureInfo.CurrentUICulture;
		try {
			CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
			CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
			var source = new byte[] { 0xC3, 0xA9, (byte)'\n' };
			await File.WriteAllBytesAsync( bytePath, source );
			await File.WriteAllBytesAsync( utf8Path, source );

			SetLocale( "C" );
			var byteResult = await RunAsync(
				new string[] { "-i", "s/[[:alpha:]]/X/g", bytePath },
				string.Empty
			);

			SetLocale( "C.UTF-8" );
			var utf8Result = await RunAsync(
				new string[] { "-i", "s/[[:alpha:]]/X/g", utf8Path },
				string.Empty
			);

			Assert.Equal( 0, byteResult.ExitCode );
			Assert.Equal( 0, utf8Result.ExitCode );
			Assert.Equal( source, await File.ReadAllBytesAsync( bytePath ) );
			Assert.Equal( new byte[] { (byte)'X', (byte)'\n' }, await File.ReadAllBytesAsync( utf8Path ) );
		} finally {
			CultureInfo.CurrentCulture = previousCulture;
			CultureInfo.CurrentUICulture = previousUiCulture;
			RestoreLocale( previous );
			File.Delete( bytePath );
			File.Delete( utf8Path );
		}
	}

	/// <summary>Verifies that the private LE4 record model retains all required metadata.</summary>
	[Fact]
	public void RecordModelContainsRequiredMetadata() {
		var recordType = typeof( SedCommand ).GetNestedType(
			"SedInputRecord",
			BindingFlags.NonPublic
		);

		Assert.NotNull( recordType );
		var properties = recordType!.GetProperties(
			BindingFlags.Instance | BindingFlags.Public
		).Select( property => property.Name ).ToHashSet( StringComparer.Ordinal );
		Assert.Contains( "Bytes", properties );
		Assert.Contains( "Text", properties );
		Assert.Contains( "Source", properties );
		Assert.Contains( "AggregateRecordNumber", properties );
		Assert.Contains( "SourceRecordNumber", properties );
		Assert.Contains( "SeparatorKind", properties );
		Assert.Contains( "IsTerminated", properties );
	}

	private static string CreateTemporaryPath() {
		return System.IO.Path.Combine(
			System.IO.Path.GetTempPath(),
			$"icod-sed-le4-{Guid.NewGuid():N}.dat"
		);
	}

	private static LocaleValues CaptureLocale() {
		return new LocaleValues(
			Environment.GetEnvironmentVariable( "LC_ALL" ),
			Environment.GetEnvironmentVariable( "LC_CTYPE" ),
			Environment.GetEnvironmentVariable( "LANG" )
		);
	}

	private static LocaleValues SetLocale(
		string name
	) {
		var previous = CaptureLocale();
		Environment.SetEnvironmentVariable( "LC_ALL", name );
		Environment.SetEnvironmentVariable( "LC_CTYPE", null );
		Environment.SetEnvironmentVariable( "LANG", null );
		return previous;
	}

	private static void RestoreLocale(
		LocaleValues values
	) {
		Environment.SetEnvironmentVariable( "LC_ALL", values.LcAll );
		Environment.SetEnvironmentVariable( "LC_CTYPE", values.LcCtype );
		Environment.SetEnvironmentVariable( "LANG", values.Lang );
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
		return new CommandResult( exitCode, output.ToString(), error.ToString() );
	}

	private readonly record struct CommandResult(
		int ExitCode,
		string Output,
		string Error
	);

	private readonly record struct LocaleValues(
		string? LcAll,
		string? LcCtype,
		string? Lang
	);

}

/// <summary>Serializes tests that modify the process text-locale environment.</summary>
[CollectionDefinition( "Sed environment", DisableParallelization = true )]
public sealed class SedEnvironmentCollection {
}
