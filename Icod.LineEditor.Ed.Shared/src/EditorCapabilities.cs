namespace Icod.LineEditor.Ed;

using System.Text;
using Icod.CommandFramework.FileSystem;
using Icod.CommandFramework.FileSystem.Metadata;
using Icod.CommandFramework.FileSystem.Mutation;
using Icod.CommandFramework.FileSystem.RecursiveMutation;
using Icod.CommandFramework.FileSystem.TransactionalReplacement;
using Icod.CommandFramework.FileSystem.Traversal;
using Icod.CommandFramework.Records;
using Icod.CommandFramework.Processes;
using Icod.CommandFramework.Temporary;

/// <summary>Defines immutable parser and capability policy for an Ed engine instance.</summary>
public sealed record EditorSecurityPolicy {
	/// <summary>Gets the unrestricted standard editor policy.</summary>
	public static EditorSecurityPolicy Standard { get; } = new(
		isRestricted: false,
		allowShellCommands: true,
		allowPathnames: true,
		allowRememberedFileName: true,
		workingDirectory: null
	);

	/// <summary>Creates a restricted editor policy rooted at one captured working directory.</summary>
	/// <param name="workingDirectory">The captured working directory.</param>
	/// <returns>The immutable restricted policy.</returns>
	public static EditorSecurityPolicy Restricted(
		string workingDirectory
	) {
		ArgumentException.ThrowIfNullOrWhiteSpace( workingDirectory );
		return new EditorSecurityPolicy(
			isRestricted: true,
			allowShellCommands: false,
			allowPathnames: false,
			allowRememberedFileName: true,
			workingDirectory: System.IO.Path.GetFullPath( workingDirectory )
		);
	}

	/// <summary>Initializes an editor security policy.</summary>
	/// <param name="isRestricted">Whether restricted-mode parsing and dispatch are enabled.</param>
	/// <param name="allowShellCommands">Whether shell-bearing commands may be dispatched.</param>
	/// <param name="allowPathnames">Whether arbitrary pathnames may be supplied.</param>
	/// <param name="allowRememberedFileName">Whether commands may establish a remembered filename.</param>
	/// <param name="workingDirectory">The captured restricted directory, when applicable.</param>
	public EditorSecurityPolicy(
		bool isRestricted,
		bool allowShellCommands,
		bool allowPathnames,
		bool allowRememberedFileName,
		string? workingDirectory
	) {
		if ( isRestricted && string.IsNullOrWhiteSpace( workingDirectory ) ) {
			throw new ArgumentException(
				"A restricted policy requires a captured working directory.",
				nameof( workingDirectory )
			);
		}
		this.IsRestricted = isRestricted;
		this.AllowShellCommands = allowShellCommands;
		this.AllowPathnames = allowPathnames;
		this.AllowRememberedFileName = allowRememberedFileName;
		this.WorkingDirectory = workingDirectory;
	}

	/// <summary>Gets whether restricted-mode parsing and dispatch are enabled.</summary>
	public bool IsRestricted {
		get;
	}

	/// <summary>Gets whether shell-bearing commands may be dispatched.</summary>
	public bool AllowShellCommands {
		get;
	}

	/// <summary>Gets whether arbitrary pathnames may be supplied.</summary>
	public bool AllowPathnames {
		get;
	}

	/// <summary>Gets whether commands may establish a remembered filename.</summary>
	public bool AllowRememberedFileName {
		get;
	}

	/// <summary>Gets the captured restricted working directory.</summary>
	public string? WorkingDirectory {
		get;
	}
}

/// <summary>Bundles the immutable editor policy with the only file and process capabilities available to an engine.</summary>
public sealed class EditorCapabilityProfile {
	/// <summary>Creates the standard unrestricted capability profile.</summary>
	/// <param name="fileAccess">The file capability.</param>
	/// <param name="processAccess">The process capability.</param>
	/// <returns>The immutable standard profile.</returns>
	public static EditorCapabilityProfile Standard(
		IEditorFileAccess fileAccess,
		IEditorProcessAccess processAccess
	) {
		ArgumentNullException.ThrowIfNull( fileAccess );
		ArgumentNullException.ThrowIfNull( processAccess );
		return new EditorCapabilityProfile(
			EditorSecurityPolicy.Standard,
			fileAccess,
			processAccess
		);
	}

	/// <summary>Creates the shared restricted profile used by both <c>red</c> and <c>ed --restricted</c>.</summary>
	/// <param name="workingDirectory">The working directory captured once when the profile is constructed.</param>
	/// <param name="fileAccess">The underlying file capability.</param>
	/// <returns>The immutable restricted profile.</returns>
	public static EditorCapabilityProfile Restricted(
		string workingDirectory,
		IEditorFileAccess fileAccess
	) {
		ArgumentException.ThrowIfNullOrWhiteSpace( workingDirectory );
		ArgumentNullException.ThrowIfNull( fileAccess );
		var capturedDirectory = System.IO.Path.GetFullPath( workingDirectory );
		return new EditorCapabilityProfile(
			EditorSecurityPolicy.Restricted( capturedDirectory ),
			new RestrictedEditorFileAccess( capturedDirectory, fileAccess ),
			new DeniedEditorProcessAccess()
		);
	}

	private EditorCapabilityProfile(
		EditorSecurityPolicy securityPolicy,
		IEditorFileAccess fileAccess,
		IEditorProcessAccess processAccess
	) {
		ArgumentNullException.ThrowIfNull( securityPolicy );
		ArgumentNullException.ThrowIfNull( fileAccess );
		ArgumentNullException.ThrowIfNull( processAccess );
		this.SecurityPolicy = securityPolicy;
		this.FileAccess = fileAccess;
		this.ProcessAccess = processAccess;
	}

	/// <summary>Gets the immutable parser and dispatcher policy.</summary>
	public EditorSecurityPolicy SecurityPolicy { get; }

	/// <summary>Gets the file capability exposed to the engine.</summary>
	public IEditorFileAccess FileAccess { get; }

	/// <summary>Gets the process capability exposed to the engine.</summary>
	public IEditorProcessAccess ProcessAccess { get; }
}

/// <summary>Supplies all filename-bearing effects used by the editor engine.</summary>
public interface IEditorFileAccess {
	/// <summary>Reads LF-delimited records from a file.</summary>
	/// <param name="path">The requested pathname.</param>
	/// <param name="cancellationToken">A cancellation token.</param>
	/// <returns>The file records and byte count.</returns>
	ValueTask<EditorFileReadResult> ReadAsync(
		string path,
		CancellationToken cancellationToken = default
	);

	/// <summary>Writes LF-delimited records to a file.</summary>
	/// <param name="path">The requested pathname.</param>
	/// <param name="lines">The line content without separators.</param>
	/// <param name="append">Whether output is appended.</param>
	/// <param name="terminateFinalRecord">Whether a final LF is written.</param>
	/// <param name="cancellationToken">A cancellation token.</param>
	/// <returns>The number of bytes written.</returns>
	ValueTask<EditorFileWriteResult> WriteAsync(
		string path,
		IReadOnlyList<ReadOnlyMemory<byte>> lines,
		bool append,
		bool terminateFinalRecord,
		CancellationToken cancellationToken = default
	);
}

/// <summary>Supplies all child-process effects used by the editor engine.</summary>
public interface IEditorProcessAccess {
	/// <summary>Runs a command through the host command interpreter.</summary>
	/// <param name="command">The command text.</param>
	/// <param name="standardInput">The optional standard-input bytes.</param>
	/// <param name="cancellationToken">A cancellation token.</param>
	/// <returns>The process result.</returns>
	ValueTask<EditorProcessResult> RunShellAsync(
		string command,
		ReadOnlyMemory<byte> standardInput,
		CancellationToken cancellationToken = default
	);
}

/// <summary>
/// Implements standard file access with Shared record reading, secure sibling staging, and durable flush operations.
/// </summary>
public sealed class StandardEditorFileAccess : IEditorFileAccess {
	private const RecursiveMetadataFields ReplacementMetadata =
		RecursiveMetadataFields.Mode
		| RecursiveMetadataFields.Ownership
		| RecursiveMetadataFields.Attributes;

	private readonly ITransactionalReplacementFileSystem transactionalFileSystem;
	private readonly IFileSystemOperations fileSystemOperations;
	private readonly ITransactionalReplacementFailureInjector failureInjector;

	/// <summary>Initializes the system-backed standard file capability.</summary>
	public StandardEditorFileAccess() : this(
		SystemTransactionalReplacementFileSystem.Instance,
		SystemFileSystemOperations.Instance,
		NullTransactionalReplacementFailureInjector.Instance
	) {
	}

	/// <summary>Initializes an injectable standard file capability.</summary>
	/// <param name="temporaryObjectCreator">The secure temporary-object creator.</param>
	/// <param name="fileSystemOperations">The durability operations provider.</param>
	public StandardEditorFileAccess(
		SecureTemporaryObjectCreator temporaryObjectCreator,
		IFileSystemOperations fileSystemOperations
	) : this(
		new SystemTransactionalReplacementFileSystem(
			SystemFileSystemMetadataProvider.Instance,
			SystemFileSystemMutationProvider.Instance,
			fileSystemOperations,
			temporaryObjectCreator
		),
		fileSystemOperations,
		NullTransactionalReplacementFailureInjector.Instance
	) {
	}

	/// <summary>Initializes an editor file capability over an injectable E6 transaction provider.</summary>
	/// <param name="transactionalFileSystem">The shared transactional-replacement filesystem.</param>
	/// <param name="fileSystemOperations">The durability operations provider used by append writes.</param>
	/// <param name="failureInjector">An optional deterministic E6 failure injector.</param>
	public StandardEditorFileAccess(
		ITransactionalReplacementFileSystem transactionalFileSystem,
		IFileSystemOperations fileSystemOperations,
		ITransactionalReplacementFailureInjector? failureInjector = null
	) {
		ArgumentNullException.ThrowIfNull( transactionalFileSystem );
		ArgumentNullException.ThrowIfNull( fileSystemOperations );
		this.transactionalFileSystem = transactionalFileSystem;
		this.fileSystemOperations = fileSystemOperations;
		this.failureInjector = failureInjector
			?? NullTransactionalReplacementFailureInjector.Instance;
	}

	/// <inheritdoc/>
	public async ValueTask<EditorFileReadResult> ReadAsync(
		string path,
		CancellationToken cancellationToken = default
	) {
		ArgumentException.ThrowIfNullOrWhiteSpace( path );
		await using var stream = new FileStream(
			path,
			FileMode.Open,
			FileAccess.Read,
			FileShare.Read,
			65536,
			FileOptions.Asynchronous | FileOptions.SequentialScan
		);
		using var reader = new ByteRecordReader( stream );
		var lines = new List<ReadOnlyMemory<byte>>();
		var finalTerminated = true;
		while ( true ) {
			var record = await reader.ReadAsync( cancellationToken ).ConfigureAwait( false );
			if ( null == record ) {
				break;
			}
			lines.Add( record.Content.ToArray() );
			finalTerminated = record.IsTerminated;
		}
		return new EditorFileReadResult(
			lines.AsReadOnly(),
			0 == lines.Count || finalTerminated,
			stream.Length
		);
	}

	/// <inheritdoc/>
	public async ValueTask<EditorFileWriteResult> WriteAsync(
		string path,
		IReadOnlyList<ReadOnlyMemory<byte>> lines,
		bool append,
		bool terminateFinalRecord,
		CancellationToken cancellationToken = default
	) {
		ArgumentException.ThrowIfNullOrWhiteSpace( path );
		ArgumentNullException.ThrowIfNull( lines );
		if ( append ) {
			await using var appendStream = new FileStream(
				path,
				FileMode.Append,
				FileAccess.Write,
				FileShare.Read,
				65536,
				FileOptions.Asynchronous
			);
			var appended = await WriteRecordsAsync(
				appendStream,
				lines,
				terminateFinalRecord,
				cancellationToken
			).ConfigureAwait( false );
			await appendStream.FlushAsync( cancellationToken ).ConfigureAwait( false );
			await this.fileSystemOperations.FlushFileAsync(
				appendStream,
				FileFlushMode.DataAndMetadata,
				cancellationToken
			).ConfigureAwait( false );
			return new EditorFileWriteResult( appended );
		}

		var fullPath = ResolveReplacementPath( path );
		var observation = await this.transactionalFileSystem.ObserveAsync(
			fullPath,
			PathDereferenceMode.NoFollow,
			cancellationToken
		).ConfigureAwait( false );
		var precondition = CreatePrecondition( observation );
		var metadata = observation.Metadata;
		var metadataPlan = null == metadata
			? null
			: RecursiveMetadataPreservationPlan.Create(
				metadata,
				ReplacementMetadata,
				RecursiveMetadataFields.None
			);
		long written = 0;
		var artifact = new TransactionalReplacementArtifact(
			recoveryUnitId: "ed-write",
			path: fullPath,
			action: TransactionalReplacementAction.Replace,
			precondition: precondition,
			contentWriter: async ( destination, token ) => {
				written = await WriteRecordsAsync(
					destination,
					lines,
					terminateFinalRecord,
					token
				).ConfigureAwait( false );
			},
			displayName: path,
			sourceMetadata: metadata,
			metadataPlan: metadataPlan
		);
		await using var transaction = new TransactionalFileReplacementTransaction(
			new TransactionalReplacementArtifact[] { artifact },
			this.transactionalFileSystem,
			TransactionalReplacementOptions.Default,
			failureInjector: this.failureInjector
		);
		var transactionResult = await transaction.CommitAsync(
			cancellationToken
		).ConfigureAwait( false );
		if ( !transactionResult.Succeeded ) {
			throw CreateTransactionException( "ed write", transactionResult );
		}
		return new EditorFileWriteResult( written );
	}

	private static string ResolveReplacementPath(
		string path
	) {
		var fullPath = System.IO.Path.GetFullPath( path );
		var information = new FileInfo( fullPath );
		if ( string.IsNullOrEmpty( information.LinkTarget ) ) {
			return fullPath;
		}
		var target = information.ResolveLinkTarget( returnFinalTarget: true );
		return target?.FullName
			?? throw new IOException( "The editor write target could not be resolved." );
	}

	private static FileSystemMutationPrecondition CreatePrecondition(
		TransactionalReplacementObservation observation
	) {
		if ( !observation.Exists ) {
			return FileSystemMutationPrecondition.DestinationMustNotExist();
		}
		var metadata = observation.Metadata
			?? throw new IOException( "The destination metadata is unavailable." );
		return FileSystemMutationPrecondition.FromObservation(
			metadata.Kind,
			metadata.EntryIdentity,
			PathDereferenceMode.NoFollow
		);
	}

	private static IOException CreateTransactionException(
		string operation,
		TransactionalReplacementResult result
	) {
		var diagnostic = 0 == result.Diagnostics.Count
			? null
			: result.Diagnostics[ result.Diagnostics.Count - 1 ];
		return new IOException(
			null == diagnostic
				? $"{operation} failed with outcome {result.Outcome}."
				: diagnostic.Message,
			diagnostic?.Exception
		);
	}

	private static async ValueTask<long> WriteRecordsAsync(
		Stream stream,
		IReadOnlyList<ReadOnlyMemory<byte>> lines,
		bool terminateFinalRecord,
		CancellationToken cancellationToken
	) {
		long written = 0;
		for ( var index = 0; lines.Count > index; index++ ) {
			var line = lines[ index ];
			await stream.WriteAsync( line, cancellationToken ).ConfigureAwait( false );
			written = checked( written + line.Length );
			if ( terminateFinalRecord || lines.Count - 1 > index ) {
				await stream.WriteAsync(
					new ReadOnlyMemory<byte>( new byte[] { (byte)'\n' } ),
					cancellationToken
				).ConfigureAwait( false );
				written++;
			}
		}
		return written;
	}
}

/// <summary>Implements shell execution through Shared <see cref="ProcessRunner"/>.</summary>
public sealed class StandardEditorProcessAccess : IEditorProcessAccess {
	/// <inheritdoc/>
	public async ValueTask<EditorProcessResult> RunShellAsync(
		string command,
		ReadOnlyMemory<byte> standardInput,
		CancellationToken cancellationToken = default
	) {
		ArgumentException.ThrowIfNullOrWhiteSpace( command );
		await using var input = new MemoryStream( standardInput.ToArray(), writable: false );
		var options = new ProcessRunOptions(
			OperatingSystem.IsWindows()
				? Environment.GetEnvironmentVariable( "COMSPEC" ) ?? "cmd.exe"
				: "/bin/sh"
		) {
			CaptureStandardOutput = true,
			CaptureStandardError = true,
			OutputEncoding = Encoding.UTF8,
			StandardInput = input
		};
		if ( OperatingSystem.IsWindows() ) {
			options.Arguments.Add( "/d" );
			options.Arguments.Add( "/s" );
			options.Arguments.Add( "/c" );
		} else {
			options.Arguments.Add( "-c" );
		}
		options.Arguments.Add( command );
		var result = await ProcessRunner.RunAsync(
			options,
			cancellationToken
		).ConfigureAwait( false );
		return new EditorProcessResult(
			result.ExitCode,
			result.WasCanceled,
			Encoding.UTF8.GetBytes( result.StandardOutput ?? string.Empty ),
			Encoding.UTF8.GetBytes( result.StandardError ?? string.Empty )
		);
	}
}

/// <summary>Rejects every file operation without touching the host filesystem.</summary>
public sealed class DeniedEditorFileAccess : IEditorFileAccess {
	/// <inheritdoc/>
	public ValueTask<EditorFileReadResult> ReadAsync(
		string path,
		CancellationToken cancellationToken = default
	) => ValueTask.FromException<EditorFileReadResult>(
		new UnauthorizedAccessException( "File access is denied by the editor security profile." )
	);

	/// <inheritdoc/>
	public ValueTask<EditorFileWriteResult> WriteAsync(
		string path,
		IReadOnlyList<ReadOnlyMemory<byte>> lines,
		bool append,
		bool terminateFinalRecord,
		CancellationToken cancellationToken = default
	) => ValueTask.FromException<EditorFileWriteResult>(
		new UnauthorizedAccessException( "File access is denied by the editor security profile." )
	);
}

/// <summary>Rejects every process operation without starting a child process.</summary>
public sealed class DeniedEditorProcessAccess : IEditorProcessAccess {
	/// <inheritdoc/>
	public ValueTask<EditorProcessResult> RunShellAsync(
		string command,
		ReadOnlyMemory<byte> standardInput,
		CancellationToken cancellationToken = default
	) => ValueTask.FromException<EditorProcessResult>(
		new UnauthorizedAccessException( "Process access is denied by the editor security profile." )
	);
}

/// <summary>
/// Restricts file operations to simple leaf names beneath one captured working directory.
/// This is a pathname policy compatible with GNU restricted ed; it is not physical filesystem confinement.
/// A permitted leaf may therefore name a hard link, symbolic link, mount point, or reparse point resolved by
/// the underlying filesystem capability. Avoiding a separate link pre-check also avoids introducing a
/// check-then-use race that could be mistaken for a security boundary.
/// </summary>
public sealed class RestrictedEditorFileAccess : IEditorFileAccess {
	private readonly string workingDirectory;
	private readonly IEditorFileAccess inner;

	/// <summary>Initializes a restricted pathname capability.</summary>
	/// <param name="workingDirectory">The working directory captured once for the lifetime of the capability.</param>
	/// <param name="inner">The underlying file capability.</param>
	public RestrictedEditorFileAccess(
		string workingDirectory,
		IEditorFileAccess inner
	) {
		ArgumentException.ThrowIfNullOrWhiteSpace( workingDirectory );
		ArgumentNullException.ThrowIfNull( inner );
		this.workingDirectory = System.IO.Path.GetFullPath( workingDirectory );
		this.inner = inner;
	}

	/// <summary>Gets the working directory captured when this capability was constructed.</summary>
	public string WorkingDirectory => this.workingDirectory;

	/// <summary>Gets whether this capability claims physical confinement.</summary>
	public bool ProvidesPhysicalConfinement => false;

	/// <inheritdoc/>
	public ValueTask<EditorFileReadResult> ReadAsync(
		string path,
		CancellationToken cancellationToken = default
	) => this.inner.ReadAsync( this.Resolve( path ), cancellationToken );

	/// <inheritdoc/>
	public ValueTask<EditorFileWriteResult> WriteAsync(
		string path,
		IReadOnlyList<ReadOnlyMemory<byte>> lines,
		bool append,
		bool terminateFinalRecord,
		CancellationToken cancellationToken = default
	) => this.inner.WriteAsync(
		this.Resolve( path ),
		lines,
		append,
		terminateFinalRecord,
		cancellationToken
	);

	private string Resolve(
		string path
	) {
		ArgumentException.ThrowIfNullOrWhiteSpace( path );
		if ( !EditorRestrictedPath.IsSimpleFileName( path ) ) {
			throw new UnauthorizedAccessException(
				"Restricted editor file access permits only a simple filename."
			);
		}
		var resolved = System.IO.Path.GetFullPath( System.IO.Path.Combine( this.workingDirectory, path ) );
		var comparison = OperatingSystem.IsWindows()
			? StringComparison.OrdinalIgnoreCase
			: StringComparison.Ordinal;
		if ( !string.Equals( System.IO.Path.GetDirectoryName( resolved ), this.workingDirectory, comparison ) ) {
			throw new UnauthorizedAccessException(
				"The resolved filename is outside the captured working directory."
			);
		}
		return resolved;
	}
}

/// <summary>Provides host-independent restricted-ed pathname classification.</summary>
public static class EditorRestrictedPath {
	private static readonly HashSet<string> WindowsDeviceNames = new(
		StringComparer.OrdinalIgnoreCase
	) {
		"AUX", "CLOCK$", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6",
		"COM7", "COM8", "COM9", "CON", "CONIN$", "CONOUT$", "LPT1", "LPT2",
		"LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "NUL", "PRN"
	};

	/// <summary>Returns whether a candidate is a simple filename under both Unix and Windows pathname rules.</summary>
	/// <param name="candidate">The logical filename.</param>
	/// <returns><see langword="true"/> only for a non-special leaf name.</returns>
	public static bool IsSimpleFileName(
		string candidate
	) {
		if (
			string.IsNullOrWhiteSpace( candidate )
			|| System.IO.Path.IsPathRooted( candidate )
			|| candidate.Contains( '/' )
			|| candidate.Contains( '\\' )
			|| candidate.Contains( ':' )
			|| candidate.StartsWith( '!' )
			|| candidate.EndsWith( ' ' )
			|| candidate.EndsWith( '.' )
			|| "." == candidate
			|| ".." == candidate
		) {
			return false;
		}
		var extension = candidate.IndexOf( '.' );
		var stem = 0 > extension ? candidate : candidate[ ..extension ];
		return !WindowsDeviceNames.Contains( stem );
	}
}
