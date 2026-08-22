namespace Icod.LineEditor.Ed.Shared.Tests;

using System.Text;
using Icod.CommandFramework.FileSystem;
using Icod.CommandFramework.FileSystem.TransactionalReplacement;
using Icod.LineEditor.Ed;
using Xunit;

/// <summary>Validates the Phase LE10 integration between Ed writes and Completion Gate E6.</summary>
public sealed class TransactionalReplacementIntegrationTests {
	/// <summary>Verifies that replacement preserves representable source metadata.</summary>
	[Fact]
	public async Task OverwriteUsesTransactionAndPreservesMetadata() {
		using var directory = new TemporaryDirectory();
		var path = System.IO.Path.Combine( directory.Path, "buffer.txt" );
		await File.WriteAllTextAsync( path, "original\n" );
		UnixFileMode? originalMode = null;
		if ( !OperatingSystem.IsWindows() ) {
			originalMode = UnixFileMode.UserRead
				| UnixFileMode.UserWrite
				| UnixFileMode.GroupRead;
			File.SetUnixFileMode( path, originalMode.Value );
		}

		var injector = new RecordingFailureInjector();
		var access = new StandardEditorFileAccess(
			SystemTransactionalReplacementFileSystem.Instance,
			SystemFileSystemOperations.Instance,
			injector
		);
		var result = await access.WriteAsync(
			path,
			Lines( "replacement" ),
			append: false,
			terminateFinalRecord: true
		);

		Assert.Equal( (long)Encoding.UTF8.GetByteCount( "replacement\n" ), result.ByteCount );
		Assert.Equal( "replacement\n", await File.ReadAllTextAsync( path ) );
		if ( originalMode.HasValue ) {
#pragma warning disable CA1416
			Assert.Equal( originalMode.Value, File.GetUnixFileMode( path ) );
#pragma warning restore CA1416
		}
		Assert.Equal( new string[] { "buffer.txt" }, EntryNames( directory.Path ) );
		Assert.Contains( TransactionalReplacementStage.WriteTemporary, injector.ObservedStages );
		Assert.Contains( TransactionalReplacementStage.FlushTemporary, injector.ObservedStages );
		Assert.Contains( TransactionalReplacementStage.Commit, injector.ObservedStages );
	}

	/// <summary>Verifies that an absent Ed destination is published through the E6 transaction.</summary>
	[Fact]
	public async Task WriteCreatesAbsentDestinationTransactionally() {
		using var directory = new TemporaryDirectory();
		var path = System.IO.Path.Combine( directory.Path, "new.txt" );
		var injector = new RecordingFailureInjector();
		var access = new StandardEditorFileAccess(
			SystemTransactionalReplacementFileSystem.Instance,
			SystemFileSystemOperations.Instance,
			injector
		);

		var result = await access.WriteAsync(
			path,
			Lines( "new" ),
			append: false,
			terminateFinalRecord: true
		);

		Assert.Equal( 4L, result.ByteCount );
		Assert.Equal( "new\n", await File.ReadAllTextAsync( path ) );
		Assert.Contains( TransactionalReplacementStage.Commit, injector.ObservedStages );
		Assert.Equal( new string[] { "new.txt" }, EntryNames( directory.Path ) );
	}

	/// <summary>Verifies rollback after a failure injected after destination publication.</summary>
	[Fact]
	public async Task PostCommitFailureRollsBackAndCleansTransactionArtifacts() {
		using var directory = new TemporaryDirectory();
		var path = System.IO.Path.Combine( directory.Path, "buffer.txt" );
		await File.WriteAllTextAsync( path, "original\n" );
		var injector = new ThrowAtStageFailureInjector(
			TransactionalReplacementStage.ApplyMetadata
		);
		var access = new StandardEditorFileAccess(
			SystemTransactionalReplacementFileSystem.Instance,
			SystemFileSystemOperations.Instance,
			injector
		);

		await Assert.ThrowsAsync<IOException>(
			() => access.WriteAsync(
				path,
				Lines( "replacement" ),
				append: false,
				terminateFinalRecord: true
			).AsTask()
		);

		Assert.Equal( "original\n", await File.ReadAllTextAsync( path ) );
		Assert.Contains( TransactionalReplacementStage.ApplyMetadata, injector.ObservedStages );
		Assert.Equal( new string[] { "buffer.txt" }, EntryNames( directory.Path ) );
	}

	/// <summary>Verifies that cancellation leaves the observed destination unchanged.</summary>
	[Fact]
	public async Task CanceledOverwritePreservesOriginalAndCleansArtifacts() {
		using var directory = new TemporaryDirectory();
		var path = System.IO.Path.Combine( directory.Path, "buffer.txt" );
		await File.WriteAllTextAsync( path, "original\n" );
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		var access = new StandardEditorFileAccess();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(
			() => access.WriteAsync(
				path,
				Lines( "replacement" ),
				append: false,
				terminateFinalRecord: true,
				cancellation.Token
			).AsTask()
		);

		Assert.Equal( "original\n", await File.ReadAllTextAsync( path ) );
		Assert.Equal( new string[] { "buffer.txt" }, EntryNames( directory.Path ) );
	}

	/// <summary>Verifies that Ed append remains a direct append policy rather than replacement.</summary>
	[Fact]
	public async Task AppendBypassesTransactionalReplacement() {
		using var directory = new TemporaryDirectory();
		var path = System.IO.Path.Combine( directory.Path, "buffer.txt" );
		await File.WriteAllTextAsync( path, "original\n" );
		var injector = new ThrowAtStageFailureInjector(
			TransactionalReplacementStage.Validate
		);
		var access = new StandardEditorFileAccess(
			SystemTransactionalReplacementFileSystem.Instance,
			SystemFileSystemOperations.Instance,
			injector
		);

		await access.WriteAsync(
			path,
			Lines( "appended" ),
			append: true,
			terminateFinalRecord: true
		);

		Assert.Equal( "original\nappended\n", await File.ReadAllTextAsync( path ) );
		Assert.Empty( injector.ObservedStages );
	}

	/// <summary>Verifies that Ed resolves terminal symbolic links before no-follow E6 planning.</summary>
	[Fact]
	public async Task TransactionalOverwriteFollowsTerminalSymbolicLink() {
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
		var access = new StandardEditorFileAccess();

		await access.WriteAsync(
			link,
			Lines( "replacement" ),
			append: false,
			terminateFinalRecord: true
		);

		Assert.Equal( "replacement\n", await File.ReadAllTextAsync( target ) );
		Assert.NotNull( new FileInfo( link ).LinkTarget );
	}

	private static IReadOnlyList<ReadOnlyMemory<byte>> Lines(
		params string[] values
	) => values.Select(
		value => new ReadOnlyMemory<byte>( Encoding.UTF8.GetBytes( value ) )
	).ToArray();

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
				$".icod-ed-le10-{Guid.NewGuid():N}"
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
