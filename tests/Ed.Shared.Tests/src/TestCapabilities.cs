namespace Icod.LineEditor.Ed.Shared.Tests;

using Icod.LineEditor.Ed;

/// <summary>Provides deterministic in-memory file effects for engine tests.</summary>
internal sealed class MemoryFileAccess : IEditorFileAccess {
	/// <summary>Gets the configured readable files by logical path.</summary>
	internal Dictionary<string, EditorFileReadResult> Files {
		get;
	} = new( StringComparer.Ordinal );

	/// <summary>Gets the paths requested for reading.</summary>
	internal List<string> ReadPaths {
		get;
	} = new();

	/// <summary>Gets the paths requested for writing.</summary>
	internal List<string> WrittenPaths {
		get;
	} = new();

	/// <summary>Gets the lines supplied to the most recent write.</summary>
	internal IReadOnlyList<ReadOnlyMemory<byte>> LastWrittenLines {
		get;
		private set;
	} = Array.Empty<ReadOnlyMemory<byte>>();

	/// <inheritdoc/>
	public ValueTask<EditorFileReadResult> ReadAsync(
		string path,
		CancellationToken cancellationToken = default
	) {
		cancellationToken.ThrowIfCancellationRequested();
		this.ReadPaths.Add( path );
		if ( !this.Files.TryGetValue( path, out var value ) ) {
			throw new FileNotFoundException( path );
		}
		return ValueTask.FromResult( value );
	}

	/// <inheritdoc/>
	public ValueTask<EditorFileWriteResult> WriteAsync(
		string path,
		IReadOnlyList<ReadOnlyMemory<byte>> lines,
		bool append,
		bool terminateFinalRecord,
		CancellationToken cancellationToken = default
	) {
		cancellationToken.ThrowIfCancellationRequested();
		this.WrittenPaths.Add( path );
		this.LastWrittenLines = lines.Select( line => new ReadOnlyMemory<byte>( line.ToArray() ) ).ToArray();
		var bytes = lines.Sum( line => (long)line.Length )
			+ Math.Max( 0, lines.Count - ( terminateFinalRecord ? 0 : 1 ) );
		return ValueTask.FromResult( new EditorFileWriteResult( bytes ) );
	}
}

/// <summary>Provides deterministic in-memory process effects for engine tests.</summary>
internal sealed class MemoryProcessAccess : IEditorProcessAccess {
	/// <summary>Gets the number of process invocations.</summary>
	internal int CallCount {
		get;
		private set;
	}

	/// <summary>Gets the most recently requested shell command.</summary>
	internal string? LastCommand {
		get;
		private set;
	}

	/// <summary>Gets the standard input supplied to the most recent process.</summary>
	internal ReadOnlyMemory<byte> LastInput {
		get;
		private set;
	}

	/// <summary>Gets or sets the deterministic process result.</summary>
	internal EditorProcessResult Result {
		get;
		set;
	} = new( 0, false, ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty );

	/// <inheritdoc/>
	public ValueTask<EditorProcessResult> RunShellAsync(
		string command,
		ReadOnlyMemory<byte> standardInput,
		CancellationToken cancellationToken = default
	) {
		cancellationToken.ThrowIfCancellationRequested();
		this.CallCount++;
		this.LastCommand = command;
		this.LastInput = standardInput.ToArray();
		return ValueTask.FromResult( this.Result );
	}
}

/// <summary>Throws a deterministic process-start failure for diagnostic tests.</summary>
internal sealed class FailingProcessAccess : IEditorProcessAccess {
	/// <inheritdoc/>
	public ValueTask<EditorProcessResult> RunShellAsync(
		string command,
		ReadOnlyMemory<byte> standardInput,
		CancellationToken cancellationToken = default
	) {
		cancellationToken.ThrowIfCancellationRequested();
		throw new InvalidOperationException( "The process could not be started." );
	}
}
