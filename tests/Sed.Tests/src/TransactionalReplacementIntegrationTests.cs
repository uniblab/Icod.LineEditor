namespace Icod.LineEditor.Sed.Tests;

using System.Text;
using Icod.CommandFramework.FileSystem.TransactionalReplacement;
using SedCommand = Icod.LineEditor.Sed.Command;
using Xunit;

/// <summary>Validates the Phase LE10 integration between Sed in-place editing and Completion Gate E6.</summary>
public sealed class TransactionalReplacementIntegrationTests {
	/// <summary>Verifies atomic publication, retained backup policy, and metadata preservation.</summary>
	[Fact]
	public async Task InPlaceEditPublishesReplacementAndRetainsRequestedBackup() {
		using var directory = new TemporaryDirectory();
		var path = System.IO.Path.Combine( directory.Path, "input.txt" );
		var backupPath = string.Concat( path, ".bak" );
		await File.WriteAllTextAsync( path, "original\n" );
		UnixFileMode? originalMode = null;
		if ( !OperatingSystem.IsWindows() ) {
			originalMode = UnixFileMode.UserRead
				| UnixFileMode.UserWrite
				| UnixFileMode.GroupRead;
			File.SetUnixFileMode( path, originalMode.Value );
		}
		var injector = new RecordingFailureInjector();
		var editor = new SedCommand.SystemInPlaceEditor(
			SystemTransactionalReplacementFileSystem.Instance,
			injector
		);

		var result = await editor.EditAsync(
			new SedCommand.SedInPlaceEditRequest(
				path,
				FollowSymlinks: false,
				BackupSuffix: ".bak"
			),
			WriteResultAsync( "replacement\n" ),
			CancellationToken.None
		);

		Assert.Equal( 0, result.ExitCode );
		Assert.Equal( "replacement\n", await File.ReadAllTextAsync( path ) );
		Assert.Equal( "original\n", await File.ReadAllTextAsync( backupPath ) );
		if ( originalMode.HasValue ) {
#pragma warning disable CA1416
			Assert.Equal( originalMode.Value, File.GetUnixFileMode( path ) );
#pragma warning restore CA1416
		}
		Assert.Equal(
			new string[] { "input.txt", "input.txt.bak" },
			EntryNames( directory.Path )
		);
		Assert.Contains( TransactionalReplacementStage.WriteTemporary, injector.ObservedStages );
		Assert.Contains( TransactionalReplacementStage.PublishBackup, injector.ObservedStages );
		Assert.Contains( TransactionalReplacementStage.Commit, injector.ObservedStages );
	}

	/// <summary>Verifies restoration of both destination and pre-existing backup after post-commit failure.</summary>
	[Fact]
	public async Task PostCommitFailureRestoresDestinationAndExistingBackup() {
		using var directory = new TemporaryDirectory();
		var path = System.IO.Path.Combine( directory.Path, "input.txt" );
		var backupPath = string.Concat( path, ".bak" );
		await File.WriteAllTextAsync( path, "original\n" );
		await File.WriteAllTextAsync( backupPath, "previous backup\n" );
		var injector = new ThrowAtStageFailureInjector(
			TransactionalReplacementStage.ApplyMetadata
		);
		var editor = new SedCommand.SystemInPlaceEditor(
			SystemTransactionalReplacementFileSystem.Instance,
			injector
		);

		await Assert.ThrowsAsync<IOException>(
			() => editor.EditAsync(
				new SedCommand.SedInPlaceEditRequest(
					path,
					FollowSymlinks: false,
					BackupSuffix: ".bak"
				),
				WriteResultAsync( "replacement\n" ),
				CancellationToken.None
			)
		);

		Assert.Equal( "original\n", await File.ReadAllTextAsync( path ) );
		Assert.Equal( "previous backup\n", await File.ReadAllTextAsync( backupPath ) );
		Assert.Contains( TransactionalReplacementStage.ApplyMetadata, injector.ObservedStages );
		Assert.Equal(
			new string[] { "input.txt", "input.txt.bak" },
			EntryNames( directory.Path )
		);
	}

	/// <summary>Verifies that cancellation leaves the input and directory unchanged.</summary>
	[Fact]
	public async Task CanceledInPlaceEditPreservesInputAndCleansArtifacts() {
		using var directory = new TemporaryDirectory();
		var path = System.IO.Path.Combine( directory.Path, "input.txt" );
		await File.WriteAllTextAsync( path, "original\n" );
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		var editor = new SedCommand.SystemInPlaceEditor(
			SystemTransactionalReplacementFileSystem.Instance
		);

		await Assert.ThrowsAnyAsync<OperationCanceledException>(
			() => editor.EditAsync(
				new SedCommand.SedInPlaceEditRequest(
					path,
					FollowSymlinks: false,
					BackupSuffix: null
				),
				WriteResultAsync( "replacement\n" ),
				cancellation.Token
			)
		);

		Assert.Equal( "original\n", await File.ReadAllTextAsync( path ) );
		Assert.Equal( new string[] { "input.txt" }, EntryNames( directory.Path ) );
	}

	/// <summary>Verifies the explicit Sed follow-symlinks policy before no-follow E6 planning.</summary>
	[Fact]
	public async Task FollowSymlinksEditsResolvedTargetWhileDefaultRejectsTerminalLink() {
		using var directory = new TemporaryDirectory();
		var target = System.IO.Path.Combine( directory.Path, "target.txt" );
		var link = System.IO.Path.Combine( directory.Path, "link.txt" );
		await File.WriteAllTextAsync( target, "target\n" );
		try {
			File.CreateSymbolicLink( link, target );
		} catch ( Exception ex ) when (
			ex is UnauthorizedAccessException
			or PlatformNotSupportedException
			or IOException
		) {
			return;
		}
		var editor = new SedCommand.SystemInPlaceEditor(
			SystemTransactionalReplacementFileSystem.Instance
		);

		await Assert.ThrowsAsync<IOException>(
			() => editor.EditAsync(
				new SedCommand.SedInPlaceEditRequest(
					link,
					FollowSymlinks: false,
					BackupSuffix: null
				),
				WriteResultAsync( "not-followed\n" ),
				CancellationToken.None
			)
		);
		Assert.Equal( "target\n", await File.ReadAllTextAsync( target ) );

		await editor.EditAsync(
			new SedCommand.SedInPlaceEditRequest(
				link,
				FollowSymlinks: true,
				BackupSuffix: null
			),
			WriteResultAsync( "followed\n" ),
			CancellationToken.None
		);

		Assert.Equal( "followed\n", await File.ReadAllTextAsync( target ) );
		Assert.NotNull( new FileInfo( link ).LinkTarget );
	}

	private static Func<string, Stream, CancellationToken, Task<SedCommand.ExecutionResult>> WriteResultAsync(
		string content
	) => async ( _, destination, cancellationToken ) => {
		await destination.WriteAsync(
			new ReadOnlyMemory<byte>( Encoding.UTF8.GetBytes( content ) ),
			cancellationToken
		);
		return new SedCommand.ExecutionResult(
			quit: false,
			exitCode: 0
		);
	};

	private static string[] EntryNames(
		string directory
	) => Directory.EnumerateFileSystemEntries( directory )
		.Select( value => System.IO.Path.GetFileName( value ) ?? string.Empty )
		.OrderBy( value => value, StringComparer.Ordinal )
		.ToArray();

	private class RecordingFailureInjector : ITransactionalReplacementFailureInjector {
		public List<TransactionalReplacementStage> ObservedStages { get; } = new();

		public virtual ValueTask OnStageAsync(
			TransactionalReplacementStage stage,
			TransactionalReplacementArtifact artifact,
			CancellationToken cancellationToken = default
		) {
			cancellationToken.ThrowIfCancellationRequested();
			this.ObservedStages.Add( stage );
			return ValueTask.CompletedTask;
		}
	}

	private sealed class ThrowAtStageFailureInjector : RecordingFailureInjector {
		private readonly TransactionalReplacementStage failureStage;

		public ThrowAtStageFailureInjector(
			TransactionalReplacementStage failureStage
		) {
			this.failureStage = failureStage;
		}

		public override async ValueTask OnStageAsync(
			TransactionalReplacementStage stage,
			TransactionalReplacementArtifact artifact,
			CancellationToken cancellationToken = default
		) {
			await base.OnStageAsync( stage, artifact, cancellationToken ).ConfigureAwait( false );
			if ( this.failureStage == stage ) {
				throw new IOException( $"Injected failure at {stage}." );
			}
		}
	}

	private sealed class TemporaryDirectory : IDisposable {
		public TemporaryDirectory() {
			this.Path = System.IO.Path.Combine(
				System.IO.Path.GetTempPath(),
				$".icod-sed-le10-{Guid.NewGuid():N}"
			);
			Directory.CreateDirectory( this.Path );
		}

		public string Path { get; }

		public void Dispose() {
			if ( Directory.Exists( this.Path ) ) {
				Directory.Delete( this.Path, recursive: true );
			}
		}
	}
}
