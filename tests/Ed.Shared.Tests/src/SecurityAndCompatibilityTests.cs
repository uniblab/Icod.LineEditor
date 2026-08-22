namespace Icod.LineEditor.Ed.Shared.Tests;

using System.Diagnostics;
using System.Text;
using Icod.CommandFramework.RegularExpressions;
using Icod.LineEditor.Ed;

public sealed class SecurityAndCompatibilityTests {
	[Fact]
	public async Task RestrictedPolicyRejectsShellBeforeInvokingCapabilityAndPreservesState() {
		var process = new MemoryProcessAccess();
		var engine = new EditorEngine(
			EditorSecurityPolicy.Restricted( Directory.GetCurrentDirectory() ),
			new MemoryFileAccess(),
			process,
			GnuBasicRegularExpressionProvider.Default
		);
		engine.Load( Lines( "one", "two" ) );
		var identities = engine.Buffer.GetLines().Select( line => line.Id ).ToArray();

		var result = await engine.ExecuteScriptAsync(
			StreamOf( "1,2!cat\n" ),
			new MemoryStream(),
			new MemoryStream()
		);

		Assert.Equal( EditorExitStatus.Error, result.ExitStatus );
		Assert.Equal( EditorDiagnosticCode.RestrictedOperation, result.Diagnostic?.Code );
		Assert.Equal( 0, process.CallCount );
		Assert.Equal( identities, engine.Buffer.GetLines().Select( line => line.Id ) );
		Assert.False( engine.IsModified );
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
	[InlineData( "!shell" )]
	[InlineData( "." )]
	[InlineData( ".." )]
	public async Task RestrictedPolicyRejectsPathBearingFileCommands(
		string path
	) {
		var files = new MemoryFileAccess();
		var engine = new EditorEngine(
			EditorSecurityPolicy.Restricted( Directory.GetCurrentDirectory() ),
			files,
			new DeniedEditorProcessAccess(),
			GnuBasicRegularExpressionProvider.Default
		);
		engine.Load( Lines( "one" ) );

		var result = await engine.ExecuteScriptAsync(
			StreamOf( string.Concat( "w ", path, "\n" ) ),
			new MemoryStream(),
			new MemoryStream()
		);

		Assert.Equal( EditorExitStatus.Error, result.ExitStatus );
		Assert.Equal( EditorDiagnosticCode.RestrictedOperation, result.Diagnostic?.Code );
		Assert.Empty( files.WrittenPaths );
		Assert.Equal( "one", engine.Buffer.GetLine( 1 ).GetText() );
	}

	[Fact]
	public async Task RestrictedGlobalShellDenialPreservesEditorStateAndPriorUndoUnit() {
		var process = new MemoryProcessAccess();
		var engine = new EditorEngine(
			EditorSecurityPolicy.Restricted( Directory.GetCurrentDirectory() ),
			new MemoryFileAccess(),
			process,
			GnuBasicRegularExpressionProvider.Default
		);
		engine.Load( Lines( "one", "two" ), rememberedFileName: "safe.txt" );
		var setup = await engine.ExecuteScriptAsync(
			StreamOf( "2d\n1ka\n" ),
			new MemoryStream(),
			new MemoryStream()
		);
		Assert.True( setup.IsSuccess, setup.Diagnostic?.Message );
		var identities = engine.Buffer.GetLines().Select( line => line.Id ).ToArray();
		var address = engine.CurrentAddress;

		var result = await engine.ExecuteScriptAsync(
			StreamOf( "g/one/! echo should-not-run\n" ),
			new MemoryStream(),
			new MemoryStream()
		);

		Assert.Equal( EditorExitStatus.Error, result.ExitStatus );
		Assert.Equal( EditorDiagnosticCode.RestrictedOperation, result.Diagnostic?.Code );
		Assert.Equal( 0, process.CallCount );
		Assert.Equal( identities, engine.Buffer.GetLines().Select( line => line.Id ) );
		Assert.Equal( address, engine.CurrentAddress );
		Assert.True( engine.IsModified );
		Assert.Equal( "safe.txt", engine.RememberedFileName );

		await using var markOutput = new MemoryStream();
		var markResult = await engine.ExecuteScriptAsync(
			StreamOf( "'ap\n" ),
			markOutput,
			new MemoryStream()
		);
		Assert.True( markResult.IsSuccess, markResult.Diagnostic?.Message );
		Assert.Equal( "one\n", Encoding.UTF8.GetString( markOutput.ToArray() ) );

		var undo = await engine.ExecuteScriptAsync(
			StreamOf( "u\n" ),
			new MemoryStream(),
			new MemoryStream()
		);
		Assert.True( undo.IsSuccess, undo.Diagnostic?.Message );
		Assert.Equal( new[] { "one", "two" }, engine.Buffer.GetLines().Select( line => line.GetText() ) );
	}

	[Fact]
	public async Task RestrictedShellPreflightOccursBeforeAddressResolution() {
		var process = new MemoryProcessAccess();
		var engine = new EditorEngine(
			EditorSecurityPolicy.Restricted( Directory.GetCurrentDirectory() ),
			new MemoryFileAccess(),
			process,
			GnuBasicRegularExpressionProvider.Default
		);
		engine.Load( Lines( "one", "two" ), rememberedFileName: "safe.txt" );
		engine.SetCurrentAddress( 1 );
		var identities = engine.Buffer.GetLines().Select( line => line.Id ).ToArray();

		var result = await engine.ExecuteScriptAsync(
			StreamOf( "'z! echo should-not-run\n" ),
			new MemoryStream(),
			new MemoryStream()
		);

		Assert.Equal( EditorExitStatus.Error, result.ExitStatus );
		Assert.Equal( EditorDiagnosticCode.RestrictedOperation, result.Diagnostic?.Code );
		Assert.Equal( 0, process.CallCount );
		Assert.Equal( identities, engine.Buffer.GetLines().Select( line => line.Id ) );
		Assert.Equal( 1, engine.CurrentAddress );
		Assert.False( engine.IsModified );
		Assert.Equal( "safe.txt", engine.RememberedFileName );
	}

	[Fact]
	public async Task RestrictedGlobalPathDenialOccursBeforeGlobalIteration() {
		var files = new MemoryFileAccess();
		var engine = new EditorEngine(
			EditorSecurityPolicy.Restricted( Directory.GetCurrentDirectory() ),
			files,
			new DeniedEditorProcessAccess(),
			GnuBasicRegularExpressionProvider.Default
		);
		engine.Load( Lines( "one", "two" ), rememberedFileName: "safe.txt" );
		engine.SetCurrentAddress( 1 );

		var result = await engine.ExecuteScriptAsync(
			StreamOf( "g/two/w ../outside\n" ),
			new MemoryStream(),
			new MemoryStream()
		);

		Assert.Equal( EditorExitStatus.Error, result.ExitStatus );
		Assert.Equal( EditorDiagnosticCode.RestrictedOperation, result.Diagnostic?.Code );
		Assert.Equal( 1, engine.CurrentAddress );
		Assert.False( engine.IsModified );
		Assert.Equal( "safe.txt", engine.RememberedFileName );
		Assert.Empty( files.WrittenPaths );
	}

	[Fact]
	public async Task RestrictedEngineAllowsSimpleNamesAndReusesTheRememberedLogicalName() {
		var files = new MemoryFileAccess();
		files.Files[ "input.txt" ] = new EditorFileReadResult( Lines( "value" ), true, 6 );
		var engine = new EditorEngine(
			EditorSecurityPolicy.Restricted( Directory.GetCurrentDirectory() ),
			files,
			new DeniedEditorProcessAccess(),
			GnuBasicRegularExpressionProvider.Default
		);

		var result = await engine.ExecuteScriptAsync(
			StreamOf( "e input.txt\nw\n" ),
			new MemoryStream(),
			new MemoryStream()
		);

		Assert.True( result.IsSuccess, result.Diagnostic?.Message );
		Assert.Equal( "input.txt", engine.RememberedFileName );
		Assert.Equal( new[] { "input.txt" }, files.ReadPaths );
		Assert.Equal( new[] { "input.txt" }, files.WrittenPaths );
	}

	[Fact]
	public async Task RestrictedFactoryConstrainsInjectedFileCapabilityToCapturedDirectory() {
		var files = new MemoryFileAccess();
		var directory = System.IO.Path.GetFullPath( System.IO.Path.Combine( System.IO.Path.GetTempPath(), Guid.NewGuid().ToString( "N" ) ) );
		Directory.CreateDirectory( directory );
		try {
			var expected = System.IO.Path.Combine( directory, "input.txt" );
			files.Files[ expected ] = new EditorFileReadResult( Lines( "value" ), true, 6 );
			var engine = EditorEngine.CreateRestricted( directory, files );

			var result = await engine.ExecuteScriptAsync(
				StreamOf( "e input.txt\n" ),
				new MemoryStream(),
				new MemoryStream()
			);

			Assert.True( result.IsSuccess, result.Diagnostic?.Message );
			Assert.Equal( expected, Assert.Single( files.ReadPaths ) );
			Assert.Equal( "input.txt", engine.RememberedFileName );
		} finally {
			Directory.Delete( directory, recursive: true );
		}
	}

	[Fact]
	public async Task RestrictedFileCapabilityMapsSimpleNamesIntoCapturedDirectory() {
		var inner = new MemoryFileAccess();
		var directory = System.IO.Path.GetFullPath( System.IO.Path.Combine( System.IO.Path.GetTempPath(), Guid.NewGuid().ToString( "N" ) ) );
		Directory.CreateDirectory( directory );
		try {
			var expected = System.IO.Path.Combine( directory, "file.txt" );
			inner.Files[ expected ] = new EditorFileReadResult( Lines( "value" ), true, 6 );
			var restricted = new RestrictedEditorFileAccess( directory, inner );

			var result = await restricted.ReadAsync( "file.txt" );

			Assert.Equal( expected, Assert.Single( inner.ReadPaths ) );
			Assert.Equal( "value", Encoding.UTF8.GetString( Assert.Single( result.Lines ).Span ) );
		} finally {
			Directory.Delete( directory, recursive: true );
		}
	}

	[Fact]
	public async Task RestrictedCapabilityCapturesDirectoryAndStatesItsPathnameOnlyBoundary() {
		var inner = new MemoryFileAccess();
		var directory = System.IO.Path.GetFullPath( System.IO.Path.Combine( System.IO.Path.GetTempPath(), Guid.NewGuid().ToString( "N" ) ) );
		var expected = System.IO.Path.Combine( directory, "leaf.txt" );
		inner.Files[ expected ] = new EditorFileReadResult( Lines( "value" ), true, 6 );
		var restricted = new RestrictedEditorFileAccess( directory, inner );

		var result = await restricted.ReadAsync( "leaf.txt" );

		Assert.Equal( directory, restricted.WorkingDirectory );
		Assert.False( restricted.ProvidesPhysicalConfinement );
		Assert.Equal( expected, Assert.Single( inner.ReadPaths ) );
		Assert.Equal( "value", Encoding.UTF8.GetString( Assert.Single( result.Lines ).Span ) );
	}

	[Theory]
	[InlineData( "leaf.txt", true )]
	[InlineData( "dir/file", false )]
	[InlineData( "dir\\file", false )]
	[InlineData( "C:relative", false )]
	[InlineData( "C:\\absolute", false )]
	[InlineData( "\\\\server\\share", false )]
	[InlineData( "stream:name", false )]
	[InlineData( "CON", false )]
	[InlineData( "nul.txt", false )]
	[InlineData( "COM1.log", false )]
	[InlineData( "LPT9", false )]
	[InlineData( "COM10", true )]
	[InlineData( "leaf.", false )]
	[InlineData( "leaf ", false )]
	[InlineData( "!shell", false )]
	public void RestrictedPathClassificationIsHostIndependent(
		string candidate,
		bool expected
	) => Assert.Equal( expected, EditorRestrictedPath.IsSimpleFileName( candidate ) );

	[Fact]
	public async Task RestrictedPathnamePolicyCharacterizesLinkAndReparseBehaviorWhenSupported() {
		var root = System.IO.Path.Combine( System.IO.Path.GetTempPath(), string.Concat( ".icod-red-links-", Guid.NewGuid().ToString( "N" ) ) );
		var outside = System.IO.Path.Combine( System.IO.Path.GetTempPath(), string.Concat( ".icod-red-targets-", Guid.NewGuid().ToString( "N" ) ) );
		Directory.CreateDirectory( root );
		Directory.CreateDirectory( outside );
		var target = System.IO.Path.Combine( outside, "target.txt" );
		var symbolicLeaf = System.IO.Path.Combine( root, "symbolic.txt" );
		var hardLeaf = System.IO.Path.Combine( root, "hard.txt" );
		await File.WriteAllTextAsync( target, "outside-through-link\n" );
		var restricted = new RestrictedEditorFileAccess( root, new StandardEditorFileAccess() );
		var exercised = 0;
		try {
			if ( TryCreateSymbolicLink( symbolicLeaf, target ) ) {
				var read = await restricted.ReadAsync( "symbolic.txt" );
				Assert.Equal( "outside-through-link", Encoding.UTF8.GetString( Assert.Single( read.Lines ).Span ) );
				exercised++;
			}
			if ( TryCreateHardLink( hardLeaf, target ) ) {
				var read = await restricted.ReadAsync( "hard.txt" );
				Assert.Equal( "outside-through-link", Encoding.UTF8.GetString( Assert.Single( read.Lines ).Span ) );
				exercised++;
			}
			Assert.False( restricted.ProvidesPhysicalConfinement );
			Assert.InRange( exercised, 0, 2 );
		} finally {
			DeleteFileIfPresent( symbolicLeaf );
			DeleteFileIfPresent( hardLeaf );
			DeleteFileIfPresent( target );
			DeleteDirectoryIfPresent( root );
			DeleteDirectoryIfPresent( outside );
		}
	}

	[Fact]
	public async Task RestrictedPathnamePolicyLeavesValidationOpenRacesToUnderlyingCapabilityWhenSupported() {
		var root = System.IO.Path.Combine( System.IO.Path.GetTempPath(), string.Concat( ".icod-red-race-", Guid.NewGuid().ToString( "N" ) ) );
		var outside = System.IO.Path.Combine( System.IO.Path.GetTempPath(), string.Concat( ".icod-red-race-targets-", Guid.NewGuid().ToString( "N" ) ) );
		Directory.CreateDirectory( root );
		Directory.CreateDirectory( outside );
		var first = System.IO.Path.Combine( outside, "first.txt" );
		var second = System.IO.Path.Combine( outside, "second.txt" );
		var leaf = System.IO.Path.Combine( root, "alias.txt" );
		await File.WriteAllTextAsync( first, "first\n" );
		await File.WriteAllTextAsync( second, "second\n" );
		try {
			if ( !TryCreateSymbolicLink( leaf, first ) ) {
				return;
			}
			var swapping = new SwappingLinkFileAccess( leaf, second );
			var restricted = new RestrictedEditorFileAccess( root, swapping );

			var read = await restricted.ReadAsync( "alias.txt" );

			Assert.True( swapping.Swapped );
			Assert.Equal( "second", Encoding.UTF8.GetString( Assert.Single( read.Lines ).Span ) );
			Assert.False( restricted.ProvidesPhysicalConfinement );
		} finally {
			DeleteFileIfPresent( leaf );
			DeleteFileIfPresent( first );
			DeleteFileIfPresent( second );
			DeleteDirectoryIfPresent( root );
			DeleteDirectoryIfPresent( outside );
		}
	}

	[Theory]
	[InlineData( "gnu-diffutils" )]
	[InlineData( "icod-diffutils" )]
	public async Task AppliesDiffutilsEdScriptCompatibilityFixture(
		string fixtureName
	) {
		var root = System.IO.Path.Combine( AppContext.BaseDirectory, "fixtures", fixtureName );
		var original = await ReadLfLinesAsync( System.IO.Path.Combine( root, "original.txt" ) );
		var expected = await ReadLfLinesAsync( System.IO.Path.Combine( root, "expected.txt" ) );
		await using var script = File.OpenRead( System.IO.Path.Combine( root, "change.ed" ) );
		var engine = new EditorEngine(
			EditorSecurityPolicy.Standard,
			new MemoryFileAccess(),
			new MemoryProcessAccess(),
			GnuBasicRegularExpressionProvider.Default
		);
		engine.Load( original );

		var result = await engine.ExecuteScriptAsync(
			script,
			new MemoryStream(),
			new MemoryStream(),
			System.IO.Path.Combine( fixtureName, "change.ed" )
		);

		Assert.True( result.IsSuccess, result.Diagnostic?.Message );
		Assert.Equal(
			expected.Select( line => Encoding.UTF8.GetString( line.Span ) ),
			engine.Buffer.GetLines().Select( line => line.GetText() )
		);
	}

	private static async Task<IReadOnlyList<ReadOnlyMemory<byte>>> ReadLfLinesAsync(
		string path
	) {
		var lines = await File.ReadAllLinesAsync( path );
		return lines
			.Where( value => 0 != value.Length )
			.Select( value => new ReadOnlyMemory<byte>( Encoding.UTF8.GetBytes( value ) ) )
			.ToArray();
	}

	private static bool TryCreateSymbolicLink(
		string linkPath,
		string targetPath
	) {
		try {
			File.CreateSymbolicLink( linkPath, targetPath );
			return true;
		} catch ( Exception exception ) when (
			exception is IOException
				or UnauthorizedAccessException
				or NotSupportedException
		) {
			return false;
		}
	}

	private static bool TryCreateHardLink(
		string linkPath,
		string targetPath
	) {
		try {
			var startInfo = new ProcessStartInfo {
				FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/ln",
				UseShellExecute = false,
				CreateNoWindow = true
			};
			if ( OperatingSystem.IsWindows() ) {
				startInfo.ArgumentList.Add( "/d" );
				startInfo.ArgumentList.Add( "/s" );
				startInfo.ArgumentList.Add( "/c" );
				startInfo.ArgumentList.Add(
					string.Concat( "mklink /H \"", linkPath, "\" \"", targetPath, "\"" )
				);
			} else {
				startInfo.ArgumentList.Add( targetPath );
				startInfo.ArgumentList.Add( linkPath );
			}
			using var process = Process.Start( startInfo );
			if ( null == process ) {
				return false;
			}
			process.WaitForExit();
			return 0 == process.ExitCode && File.Exists( linkPath );
		} catch ( Exception exception ) when (
			exception is IOException
				or UnauthorizedAccessException
				or System.ComponentModel.Win32Exception
				or InvalidOperationException
		) {
			return false;
		}
	}

	private static void DeleteFileIfPresent(
		string path
	) {
		try {
			File.Delete( path );
		} catch ( IOException ) {
		} catch ( UnauthorizedAccessException ) {
		}
	}

	private static void DeleteDirectoryIfPresent(
		string path
	) {
		try {
			Directory.Delete( path, recursive: true );
		} catch ( IOException ) {
		} catch ( UnauthorizedAccessException ) {
		}
	}

	private sealed class SwappingLinkFileAccess : IEditorFileAccess {
		private readonly string linkPath;
		private readonly string replacementTarget;
		private readonly StandardEditorFileAccess inner = new();

		public SwappingLinkFileAccess(
			string linkPath,
			string replacementTarget
		) {
			this.linkPath = linkPath;
			this.replacementTarget = replacementTarget;
		}

		public bool Swapped { get; private set; }

		public ValueTask<EditorFileReadResult> ReadAsync(
			string path,
			CancellationToken cancellationToken = default
		) {
			File.Delete( this.linkPath );
			File.CreateSymbolicLink( this.linkPath, this.replacementTarget );
			this.Swapped = true;
			return this.inner.ReadAsync( path, cancellationToken );
		}

		public ValueTask<EditorFileWriteResult> WriteAsync(
			string path,
			IReadOnlyList<ReadOnlyMemory<byte>> lines,
			bool append,
			bool terminateFinalRecord,
			CancellationToken cancellationToken = default
		) => this.inner.WriteAsync( path, lines, append, terminateFinalRecord, cancellationToken );
	}

	private static IReadOnlyList<ReadOnlyMemory<byte>> Lines(
		params string[] values
	) => values.Select( value => new ReadOnlyMemory<byte>( Encoding.UTF8.GetBytes( value ) ) ).ToArray();

	private static MemoryStream StreamOf(
		string value
	) => new( Encoding.UTF8.GetBytes( value ), writable: false );
}
