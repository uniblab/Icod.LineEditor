namespace Icod.LineEditor.Ed.Shared.Tests;

using System.Text;
using Icod.CommandFramework.RegularExpressions;
using Icod.LineEditor.Ed;

public sealed class EditorEngineTests {
	[Fact]
	public async Task ExecutesMutationAddressMarkCutAndUndoCommands() {
		var engine = CreateEngine();
		engine.Load( Lines( "one", "two", "three" ) );
		var output = new MemoryStream();
		var error = new MemoryStream();
		var script = string.Join(
			'\n',
			"1ka",
			"2d",
			"1x",
			"'ap",
			"u",
			"1,$p",
			string.Empty
		);

		var result = await engine.ExecuteScriptAsync(
			StreamOf( script ),
			output,
			error
		);

		Assert.True( result.IsSuccess );
		Assert.Equal( "one\none\nthree\n", TextOf( output ) );
		Assert.Equal( new[] { "one", "three" }, BufferText( engine ) );
	}

	[Fact]
	public async Task SemicolonRangeSearchUsesTheFirstAddressAsTheSearchOrigin() {
		var engine = CreateEngine();
		engine.Load( Lines( "target", "middle", "target", "tail" ) );
		var output = new MemoryStream();

		var result = await engine.ExecuteScriptAsync(
			StreamOf( "1;/target/p\n" ),
			output,
			new MemoryStream()
		);

		Assert.True( result.IsSuccess, result.Diagnostic?.Message );
		Assert.Equal( "target\nmiddle\ntarget\n", TextOf( output ) );
	}

	[Fact]
	public async Task SubstitutionUsesSharedBasicRegularExpressionsAndBackReferences() {
		var engine = CreateEngine();
		engine.Load( Lines( "ab12 ab34", "nothing" ) );
		var output = new MemoryStream();

		var result = await engine.ExecuteScriptAsync(
			StreamOf( "1s/\\([a-z][a-z]*\\)\\([0-9][0-9]*\\)/\\2-\\1/gp\n" ),
			output,
			new MemoryStream()
		);

		Assert.True( result.IsSuccess );
		Assert.Equal( "12-ab 34-ab\n", TextOf( output ) );
		Assert.Equal( "12-ab 34-ab", engine.Buffer.GetLine( 1 ).GetText() );
	}

	[Fact]
	public async Task CrLfScriptRecognizesSinglePeriodDataBlockTerminator() {
		var engine = CreateEngine();
		engine.Load( Lines( "one" ) );
		var output = new MemoryStream();

		var result = await engine.ExecuteScriptAsync(
			StreamOf( "1c\r\nONE\r\n.\r\n1p\r\n" ),
			output,
			new MemoryStream()
		);

		Assert.True( result.IsSuccess, result.Diagnostic?.Message );
		Assert.Equal( "ONE\n", TextOf( output ) );
		Assert.Equal( new[] { "ONE" }, BufferText( engine ) );
	}

	[Fact]
	public async Task GlobalCommandUsesStableSelectedLineIdentitiesDuringDeletion() {
		var engine = CreateEngine();
		engine.Load( Lines( "keep", "drop one", "drop two", "keep again" ) );

		var result = await engine.ExecuteScriptAsync(
			StreamOf( "g/drop/d\n1,$p\n" ),
			new MemoryStream(),
			new MemoryStream()
		);

		Assert.True( result.IsSuccess );
		Assert.Equal( new[] { "keep", "keep again" }, BufferText( engine ) );
	}

	[Fact]
	public async Task FileReadWriteAndRememberedNameUseInjectedCapability() {
		var files = new MemoryFileAccess();
		files.Files[ "input.txt" ] = new EditorFileReadResult(
			Lines( "alpha", "beta" ),
			true,
			11
		);
		var engine = CreateEngine( files: files );
		var output = new MemoryStream();

		var result = await engine.ExecuteScriptAsync(
			StreamOf( "e input.txt\n1s/alpha/ALPHA/\nw\n" ),
			output,
			new MemoryStream()
		);

		Assert.True( result.IsSuccess );
		Assert.Equal( "input.txt", engine.RememberedFileName );
		Assert.Equal( new[] { "input.txt" }, files.ReadPaths );
		Assert.Equal( new[] { "input.txt" }, files.WrittenPaths );
		Assert.Equal( new[] { "ALPHA", "beta" }, files.LastWrittenLines.Select( line => Encoding.UTF8.GetString( line.Span ) ) );
		Assert.False( engine.IsModified );
	}

	[Fact]
	public async Task RangeFilterReplacesLinesWithCapturedProcessOutput() {
		var process = new MemoryProcessAccess {
			Result = new EditorProcessResult(
				0,
				false,
				Encoding.UTF8.GetBytes( "ONE\nTWO\n" ),
				ReadOnlyMemory<byte>.Empty
			)
		};
		var engine = CreateEngine( process: process );
		engine.Load( Lines( "one", "two", "three" ) );

		var result = await engine.ExecuteScriptAsync(
			StreamOf( "1,2!upper\n" ),
			new MemoryStream(),
			new MemoryStream()
		);

		Assert.True( result.IsSuccess );
		Assert.Equal( "upper", process.LastCommand );
		Assert.Equal( "one\ntwo\n", Encoding.UTF8.GetString( process.LastInput.Span ) );
		Assert.Equal( new[] { "ONE", "TWO", "three" }, BufferText( engine ) );
	}

	[Fact]
	public async Task EmptyCommandAtEndOfBufferReportsControlledAddressError() {
		var engine = CreateEngine();
		engine.Load( Lines( "one" ) );

		var result = await engine.ExecuteScriptAsync(
			StreamOf( "1p\n\n" ),
			new MemoryStream(),
			new MemoryStream()
		);

		Assert.Equal( EditorExitStatus.Error, result.ExitStatus );
		Assert.Equal( EditorDiagnosticCode.InvalidAddress, result.Diagnostic?.Code );
	}

	[Fact]
	public async Task MoveDestinationInsideRangeReportsControlledAddressErrorAndPreservesState() {
		var engine = CreateEngine();
		engine.Load( Lines( "one", "two", "three" ) );

		var result = await engine.ExecuteScriptAsync(
			StreamOf( "1,2m1\n" ),
			new MemoryStream(),
			new MemoryStream()
		);

		Assert.Equal( EditorExitStatus.Error, result.ExitStatus );
		Assert.Equal( EditorDiagnosticCode.InvalidAddress, result.Diagnostic?.Code );
		Assert.Equal( new[] { "one", "two", "three" }, BufferText( engine ) );
		Assert.False( engine.IsModified );
	}

	[Fact]
	public async Task OversizedSubstitutionOccurrenceReportsControlledCommandError() {
		var engine = CreateEngine();
		engine.Load( Lines( "value" ) );

		var result = await engine.ExecuteScriptAsync(
			StreamOf( "1s/value/replacement/999999999999999999999999999999\n" ),
			new MemoryStream(),
			new MemoryStream()
		);

		Assert.Equal( EditorExitStatus.Error, result.ExitStatus );
		Assert.Equal( EditorDiagnosticCode.InvalidCommand, result.Diagnostic?.Code );
		Assert.Equal( "value", engine.Buffer.GetLine( 1 ).GetText() );
		Assert.False( engine.IsModified );
	}

	[Fact]
	public async Task DestinationAddressRejectsTrailingText() {
		var engine = CreateEngine();
		engine.Load( Lines( "one", "two" ) );

		var result = await engine.ExecuteScriptAsync(
			StreamOf( "1m0unexpected\n" ),
			new MemoryStream(),
			new MemoryStream()
		);

		Assert.Equal( EditorExitStatus.Error, result.ExitStatus );
		Assert.Equal( EditorDiagnosticCode.InvalidAddress, result.Diagnostic?.Code );
		Assert.Equal( new[] { "one", "two" }, BufferText( engine ) );
		Assert.False( engine.IsModified );
	}

	[Fact]
	public async Task ProcessStartFailureReportsControlledProcessDiagnostic() {
		var engine = new EditorEngine(
			EditorSecurityPolicy.Standard,
			new MemoryFileAccess(),
			new FailingProcessAccess(),
			GnuBasicRegularExpressionProvider.Default
		);

		var result = await engine.ExecuteScriptAsync(
			StreamOf( "!command\n" ),
			new MemoryStream(),
			new MemoryStream()
		);

		Assert.Equal( EditorExitStatus.Error, result.ExitStatus );
		Assert.Equal( EditorDiagnosticCode.ProcessOperation, result.Diagnostic?.Code );
	}

	[Fact]
	public async Task CancellationReturnsInterruptedStatusWithoutInventingAnError() {
		var engine = CreateEngine();
		engine.Load( Lines( "one" ) );
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();

		var result = await engine.ExecuteScriptAsync(
			StreamOf( "1p\n" ),
			new MemoryStream(),
			new MemoryStream(),
			cancellationToken: cancellation.Token
		);

		Assert.Equal( EditorExitStatus.Interrupted, result.ExitStatus );
		Assert.Equal( EditorDiagnosticCode.Interrupted, result.Diagnostic?.Code );
	}

	private static EditorEngine CreateEngine(
		MemoryFileAccess? files = null,
		MemoryProcessAccess? process = null
	) => new(
		EditorSecurityPolicy.Standard,
		files ?? new MemoryFileAccess(),
		process ?? new MemoryProcessAccess(),
		GnuBasicRegularExpressionProvider.Default
	);

	private static IReadOnlyList<ReadOnlyMemory<byte>> Lines(
		params string[] values
	) => values.Select( value => new ReadOnlyMemory<byte>( Encoding.UTF8.GetBytes( value ) ) ).ToArray();

	private static MemoryStream StreamOf(
		string value
	) => new( Encoding.UTF8.GetBytes( value ), writable: false );

	private static string TextOf(
		MemoryStream stream
	) => Encoding.UTF8.GetString( stream.ToArray() );

	private static string[] BufferText(
		EditorEngine engine
	) => engine.Buffer.GetLines().Select( line => line.GetText() ).ToArray();
}
