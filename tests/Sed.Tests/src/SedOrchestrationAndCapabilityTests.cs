namespace Icod.LineEditor.Sed.Tests;

using System.Text;
using Icod.CommandFramework.Diagnostics;
using Icod.CommandFramework.Temporary;
using SedCommand = Icod.LineEditor.Sed.Command;
using Xunit;

/// <summary>Verifies the orchestration and capability boundaries introduced by LE5.</summary>
[Collection( "Sed environment" )]
public sealed class SedOrchestrationAndCapabilityTests {

	/// <summary>Verifies that the CommandContext overload prefers authoritative byte streams.</summary>
	[Fact]
	public async Task CommandContextCoreUsesBinaryStreamsWhenAvailable() {
		using var input = new MemoryStream( Encoding.UTF8.GetBytes( "alpha\n" ) );
		using var output = new MemoryStream();
		using var textOutput = new StringWriter();
		using var error = new StringWriter();
		var context = new CommandContext(
			"sed",
			TextReader.Null,
			textOutput,
			error,
			input,
			output
		);

		var exitCode = await SedCommand.RunAsync(
			new string[] { "s/alpha/beta/" },
			context
		);

		Assert.Equal( 0, exitCode );
		Assert.Equal( "beta\n", Encoding.UTF8.GetString( output.ToArray() ) );
		Assert.Empty( textOutput.ToString() );
		Assert.Empty( error.ToString() );
	}

	/// <summary>Verifies that a binary input stream remains authoritative with text output.</summary>
	[Fact]
	public async Task CommandContextUsesBinaryInputIndependently() {
		using var input = new MemoryStream( Encoding.UTF8.GetBytes( "alpha\n" ) );
		using var output = new StringWriter();
		using var error = new StringWriter();
		var context = new CommandContext(
			"sed",
			new StringReader( "wrong\n" ),
			output,
			error,
			standardInputStream: input
		);

		var exitCode = await SedCommand.RunAsync(
			new string[] { "s/alpha/beta/" },
			context
		);

		Assert.Equal( 0, exitCode );
		Assert.Equal( "beta\n", output.ToString() );
		Assert.Empty( error.ToString() );
	}

	/// <summary>Verifies that a binary output stream remains authoritative with text input.</summary>
	[Fact]
	public async Task CommandContextUsesBinaryOutputIndependently() {
		using var output = new MemoryStream();
		using var textOutput = new StringWriter();
		using var error = new StringWriter();
		var context = new CommandContext(
			"sed",
			new StringReader( "alpha\n" ),
			textOutput,
			error,
			standardOutputStream: output
		);

		var exitCode = await SedCommand.RunAsync(
			new string[] { "s/alpha/beta/" },
			context
		);

		Assert.Equal( 0, exitCode );
		Assert.Equal( "beta\n", Encoding.UTF8.GetString( output.ToArray() ) );
		Assert.Empty( textOutput.ToString() );
		Assert.Empty( error.ToString() );
	}

	/// <summary>Verifies LF-only composition and aggregate-to-source location mapping.</summary>
	[Fact]
	public void ScriptDocumentPreservesNamedSourcesAndUsesLineFeedJoining() {
		var first = new SedCommand.SedScriptSource(
			SedCommand.SedScriptSourceKind.Expression,
			"first expression",
			"p",
			0
		);
		var second = new SedCommand.SedScriptSource(
			SedCommand.SedScriptSourceKind.File,
			"commands.sed",
			"d\r\nq",
			1
		);

		var document = SedCommand.SedScriptDocument.Create(
			new SedCommand.SedScriptSource[] { first, second }
		);
		var location = document.GetLocation( 2 );

		Assert.Equal( "p\nd\r\nq", document.Text );
		Assert.Equal(
			new string[] { "first expression", "commands.sed" },
			document.Sources.Select( source => source.Name ).ToArray()
		);
		Assert.Equal( "commands.sed", location.SourceName );
		Assert.Equal( 1, location.Line );
		Assert.Equal( 1, location.Column );
	}

	/// <summary>Verifies that parser diagnostics identify the responsible script source.</summary>
	[Fact]
	public async Task InvalidLaterExpressionReportsItsStableSourceName() {
		using var output = new StringWriter();
		using var error = new StringWriter();

		var exitCode = await SedCommand.RunAsync(
			new string[] { "-e", "p", "-e", "{" },
			new StringReader( "alpha\n" ),
			output,
			error
		);

		Assert.NotEqual( 0, exitCode );
		Assert.Contains( "-e expression #2", error.ToString(), StringComparison.Ordinal );
		Assert.Contains( ":1:", error.ToString(), StringComparison.Ordinal );
	}

	/// <summary>Verifies that substitution shell execution uses the injected capability.</summary>
	[Fact]
	public async Task SubstitutionExecuteUsesInjectedShellCapability() {
		var shell = new RecordingShellCapability(
			new SedCommand.ShellResult( 0, "from-shell\n" )
		);
		var auxiliary = new RecordingAuxiliaryFileCapability();
		var inPlace = new RecordingInPlaceEditor();
		var result = await RunWithCapabilitiesAsync(
			new string[] { "-n", "s/x/y/ep" },
			"x\n",
			new SedCommand.SedRuntimeCapabilities( shell, auxiliary, inPlace )
		);

		Assert.Equal( 0, result.ExitCode );
		Assert.Equal( "from-shell\n", result.Output );
		Assert.Equal( 1, shell.CallCount );
		Assert.Equal( "y", shell.Commands.Single() );
	}

	/// <summary>Verifies that r and w use the injected auxiliary-file capability.</summary>
	[Fact]
	public async Task AuxiliaryReadAndWriteUseInjectedCapability() {
		var shell = new RecordingShellCapability(
			new SedCommand.ShellResult( 0, string.Empty )
		);
		var auxiliary = new RecordingAuxiliaryFileCapability();
		auxiliary.ReadFiles[ "virtual-input" ] = Encoding.UTF8.GetBytes( "auxiliary\n" );
		var capabilities = new SedCommand.SedRuntimeCapabilities(
			shell,
			auxiliary,
			new RecordingInPlaceEditor()
		);

		var read = await RunWithCapabilitiesAsync(
			new string[] { "r virtual-input" },
			"main\n",
			capabilities
		);
		var write = await RunWithCapabilitiesAsync(
			new string[] { "-n", "w virtual-output" },
			"captured\n",
			capabilities
		);

		Assert.Equal( 0, read.ExitCode );
		Assert.Equal( "main\nauxiliary\n", read.Output );
		Assert.Equal( 0, write.ExitCode );
		Assert.Equal(
			"captured\n",
			Encoding.UTF8.GetString( auxiliary.WrittenFiles[ "virtual-output" ].ToArray() )
		);
	}

	/// <summary>Verifies compile-time sandbox rejection and denied runtime backstops.</summary>
	[Fact]
	public async Task SandboxRejectsCommandsAndDeniesRuntimeCapabilities() {
		var shell = new RecordingShellCapability(
			new SedCommand.ShellResult( 0, string.Empty )
		);
		var auxiliary = new RecordingAuxiliaryFileCapability();
		var capabilities = new SedCommand.SedRuntimeCapabilities(
			shell,
			auxiliary,
			new RecordingInPlaceEditor()
		);

		var result = await RunWithCapabilitiesAsync(
			new string[] { "--sandbox", "e echo forbidden" },
			"alpha\n",
			capabilities
		);
		var sandbox = capabilities.ForSandbox();

		Assert.NotEqual( 0, result.ExitCode );
		Assert.Contains( "sandbox", result.Error, StringComparison.OrdinalIgnoreCase );
		Assert.Equal( 0, shell.CallCount );
		await Assert.ThrowsAsync<SedCommand.SedCapabilityDeniedException>(
			() => sandbox.AuxiliaryFiles.OpenReadAsync(
				"forbidden",
				CancellationToken.None
			).AsTask()
		);
		await Assert.ThrowsAsync<SedCommand.SedCapabilityDeniedException>(
			() => sandbox.Shell.ExecuteAsync(
				"forbidden",
				null!,
				TextWriter.Null,
				captureStandardOutput: true,
				CancellationToken.None
			)
		);
	}

	/// <summary>Verifies that in-place orchestration is delegated to the internal editor boundary.</summary>
	[Fact]
	public async Task InPlaceModeUsesInjectedEditorBoundary() {
		var inPlace = new RecordingInPlaceEditor();
		var capabilities = new SedCommand.SedRuntimeCapabilities(
			new RecordingShellCapability( new SedCommand.ShellResult( 0, string.Empty ) ),
			new RecordingAuxiliaryFileCapability(),
			inPlace
		);

		var result = await RunWithCapabilitiesAsync(
			new string[] { "-i.bak", "s/a/A/", "virtual-file" },
			string.Empty,
			capabilities
		);

		Assert.Equal( 0, result.ExitCode );
		var request = Assert.Single( inPlace.Requests );
		Assert.Equal( "virtual-file", request.Path );
		Assert.Equal( ".bak", request.BackupSuffix );
	}

	/// <summary>Verifies cleanup and source preservation when a staged transform fails.</summary>
	[Fact]
	public async Task SystemInPlaceEditorCleansTemporaryFileAfterFailure() {
		var directory = System.IO.Path.Combine(
			System.IO.Path.GetTempPath(),
			$".icod-sed-le5-{Guid.NewGuid():N}"
		);
		Directory.CreateDirectory( directory );
		var path = System.IO.Path.Combine( directory, "input.txt" );
		await File.WriteAllTextAsync( path, "original\n" );
		var editor = new SedCommand.SystemInPlaceEditor(
			SecureTemporaryObjectCreator.System
		);
		try {
			await Assert.ThrowsAsync<IOException>(
				() => editor.EditAsync(
					new SedCommand.SedInPlaceEditRequest(
						path,
						FollowSymlinks: false,
						BackupSuffix: null
					),
					async (
						_,
						output,
						cancellationToken
					) => {
						await output.WriteAsync(
							Encoding.UTF8.GetBytes( "partial\n" ).AsMemory(),
							cancellationToken
						);
						throw new IOException( "injected transform failure" );
					},
					CancellationToken.None
				)
			);

			Assert.Equal( "original\n", await File.ReadAllTextAsync( path ) );
			Assert.DoesNotContain(
				Directory.EnumerateFiles( directory ),
				candidate => System.IO.Path.GetFileName( candidate ).StartsWith(
					".sed.",
					StringComparison.Ordinal
				)
			);
		} finally {
			Directory.Delete( directory, recursive: true );
		}
	}

	private static async Task<CommandResult> RunWithCapabilitiesAsync(
		string[] args,
		string input,
		SedCommand.SedRuntimeCapabilities capabilities
	) {
		using var standardInput = new MemoryStream( Encoding.UTF8.GetBytes( input ) );
		using var standardOutput = new MemoryStream();
		using var presentation = new StringWriter();
		using var error = new StringWriter();
		var context = new CommandContext(
			"sed",
			TextReader.Null,
			presentation,
			error,
			standardInput,
			standardOutput
		);
		var exitCode = await SedCommand.RunAsync( args, context, capabilities );
		return new CommandResult(
			exitCode,
			Encoding.UTF8.GetString( standardOutput.ToArray() ),
			error.ToString()
		);
	}

	private sealed record CommandResult(
		int ExitCode,
		string Output,
		string Error
	);

	private sealed class RecordingShellCapability : SedCommand.ISedShellCapability {

		private readonly SedCommand.ShellResult myResult;

		public int CallCount {
			get;
			private set;
		}

		public List<string> Commands {
			get;
		} = new();

		public RecordingShellCapability(
			SedCommand.ShellResult result
		) {
			this.myResult = result;
		}

		public Task<SedCommand.ShellResult> ExecuteAsync(
			string command,
			TextWriter output,
			TextWriter error,
			bool captureStandardOutput,
			CancellationToken cancellationToken
		) {
			cancellationToken.ThrowIfCancellationRequested();
			this.CallCount++;
			this.Commands.Add( command );
			return Task.FromResult( this.myResult );
		}

	}

	private sealed class RecordingAuxiliaryFileCapability : SedCommand.ISedAuxiliaryFileCapability {

		public Dictionary<string, byte[]> ReadFiles {
			get;
		} = new( StringComparer.Ordinal );

		public Dictionary<string, MemoryStream> WrittenFiles {
			get;
		} = new( StringComparer.Ordinal );

		public ValueTask<Stream> OpenReadAsync(
			string path,
			CancellationToken cancellationToken
		) {
			cancellationToken.ThrowIfCancellationRequested();
			if ( !this.ReadFiles.TryGetValue( path, out var contents ) ) {
				throw new FileNotFoundException( "No injected auxiliary file exists.", path );
			}
			return ValueTask.FromResult<Stream>( new MemoryStream( contents, writable: false ) );
		}

		public ValueTask<Stream> OpenWriteAsync(
			string path,
			CancellationToken cancellationToken
		) {
			cancellationToken.ThrowIfCancellationRequested();
			var stream = new MemoryStream();
			this.WrittenFiles[ path ] = stream;
			return ValueTask.FromResult<Stream>( stream );
		}

	}

	private sealed class RecordingInPlaceEditor : SedCommand.IInPlaceEditor {

		public List<SedCommand.SedInPlaceEditRequest> Requests {
			get;
		} = new();

		public Task<SedCommand.ExecutionResult> EditAsync(
			SedCommand.SedInPlaceEditRequest request,
			Func<string, Stream, CancellationToken, Task<SedCommand.ExecutionResult>> transformAsync,
			CancellationToken cancellationToken
		) {
			cancellationToken.ThrowIfCancellationRequested();
			this.Requests.Add( request );
			return Task.FromResult(
				new SedCommand.ExecutionResult( quit: false, exitCode: 0 )
			);
		}

	}

}
