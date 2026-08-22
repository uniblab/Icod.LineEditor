namespace Icod.LineEditor.Sed;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Icod.CommandFramework.FileSystem;
using Icod.CommandFramework.FileSystem.Metadata;
using Icod.CommandFramework.FileSystem.Mutation;
using Icod.CommandFramework.FileSystem.RecursiveMutation;
using Icod.CommandFramework.FileSystem.TransactionalReplacement;
using Icod.CommandFramework.FileSystem.Traversal;
using Icod.CommandFramework.Temporary;

// Responsibility: in-place editing and pathname handling.
public static partial class Command {
	private static Task<ExecutionResult> ProcessInPlaceAsync(
		string path,
		Options options,
		SedProgram program,
		SedTextCodec textCodec,
		TextWriter stderr,
		SedRuntimeCapabilities capabilities,
		CancellationToken cancellationToken
	) {
		return capabilities.InPlaceEditor.EditAsync(
			new SedInPlaceEditRequest(
				path,
				options.FollowSymlinks,
				options.BackupSuffix
			),
			async (
				editPath,
				outputStream,
				transformCancellationToken
			) => {
				using var input = new InputSequence(
					new SourceSpec[] { new SourceSpec( editPath ) },
					Stream.Null,
					options.NullData,
					textCodec
				);
				var environment = new ExecutionEnvironment(
					outputStream,
					textCodec,
					stderr,
					options.SuppressAutomaticPrint,
					options.NullData,
					options.ListWidth,
					options.Debug,
					options.Unbuffered,
					capabilities.Shell,
					capabilities.AuxiliaryFiles
				);
				try {
					return await ExecuteAsync(
						program,
						input,
						environment,
						transformCancellationToken
					).ConfigureAwait( false );
				} finally {
					await environment.DisposeAsync(
						transformCancellationToken
					).ConfigureAwait( false );
				}
			},
			cancellationToken
		);
	}

	/// <summary>
	/// Implements Sed in-place replacement through the shared E6 transaction model.
	/// </summary>
	internal sealed class SystemInPlaceEditor : IInPlaceEditor {
		private const RecursiveMetadataFields ReplacementMetadata =
			RecursiveMetadataFields.Mode
			| RecursiveMetadataFields.Ownership
			| RecursiveMetadataFields.Attributes;

		private readonly ITransactionalReplacementFileSystem myFileSystem;
		private readonly ITransactionalReplacementFailureInjector myFailureInjector;

		/// <summary>Gets the host-backed singleton editor.</summary>
		public static SystemInPlaceEditor Instance { get; } = new(
			SystemTransactionalReplacementFileSystem.Instance,
			NullTransactionalReplacementFailureInjector.Instance
		);

		/// <summary>Initializes an editor over an injectable secure temporary-object creator.</summary>
		public SystemInPlaceEditor(
			SecureTemporaryObjectCreator temporaryObjects
		) : this(
			new SystemTransactionalReplacementFileSystem(
				SystemFileSystemMetadataProvider.Instance,
				SystemFileSystemMutationProvider.Instance,
				SystemFileSystemOperations.Instance,
				temporaryObjects
			),
			NullTransactionalReplacementFailureInjector.Instance
		) {
		}

		/// <summary>Initializes an editor over an injectable E6 filesystem and failure boundary.</summary>
		public SystemInPlaceEditor(
			ITransactionalReplacementFileSystem fileSystem,
			ITransactionalReplacementFailureInjector? failureInjector = null
		) {
			ArgumentNullException.ThrowIfNull( fileSystem );
			this.myFileSystem = fileSystem;
			this.myFailureInjector = failureInjector
				?? NullTransactionalReplacementFailureInjector.Instance;
		}

		/// <inheritdoc />
		public async Task<ExecutionResult> EditAsync(
			SedInPlaceEditRequest request,
			Func<string, Stream, CancellationToken, Task<ExecutionResult>> transformAsync,
			CancellationToken cancellationToken
		) {
			ArgumentNullException.ThrowIfNull( request );
			ArgumentNullException.ThrowIfNull( transformAsync );
			cancellationToken.ThrowIfCancellationRequested();
			var editPath = System.IO.Path.GetFullPath(
				ResolveInPlacePath( request.Path, request.FollowSymlinks )
			);
			var observation = await this.myFileSystem.ObserveAsync(
				editPath,
				PathDereferenceMode.NoFollow,
				cancellationToken
			).ConfigureAwait( false );
			if ( !observation.Exists || null == observation.Metadata ) {
				throw new FileNotFoundException(
					"The in-place input file does not exist.",
					editPath
				);
			}
			var metadata = observation.Metadata;
			var precondition = FileSystemMutationPrecondition.FromObservation(
				metadata.Kind,
				metadata.EntryIdentity,
				PathDereferenceMode.NoFollow
			);
			var metadataPlan = RecursiveMetadataPreservationPlan.Create(
				metadata,
				ReplacementMetadata,
				RecursiveMetadataFields.None
			);
			ExecutionResult? executionResult = null;
			var hasBackup = !string.IsNullOrEmpty( request.BackupSuffix );
			var backupPath = hasBackup
				? BuildBackupPath( editPath, request.BackupSuffix! )
				: null;
			var artifact = new TransactionalReplacementArtifact(
				recoveryUnitId: "sed-in-place",
				path: editPath,
				action: TransactionalReplacementAction.Replace,
				precondition: precondition,
				contentWriter: async ( destination, token ) => {
					executionResult = await transformAsync(
						editPath,
						destination,
						token
					).ConfigureAwait( false );
				},
				displayName: request.Path,
				sourceMetadata: metadata,
				metadataPlan: metadataPlan,
				explicitBackupPath: backupPath,
				retainBackup: hasBackup
			);
			await using var transaction = new TransactionalFileReplacementTransaction(
				new TransactionalReplacementArtifact[] { artifact },
				this.myFileSystem,
				TransactionalReplacementOptions.Default,
				failureInjector: this.myFailureInjector
			);
			var transactionResult = await transaction.CommitAsync(
				cancellationToken
			).ConfigureAwait( false );
			if ( !transactionResult.Succeeded ) {
				throw CreateTransactionException( "sed in-place edit", transactionResult );
			}
			return executionResult
				?? throw new IOException( "The in-place transform produced no execution result." );
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
	}

	private static string BuildBackupPath(
		string path,
		string suffix
	) {
		return suffix.Contains(
			"*",
			StringComparison.Ordinal
		)
			? suffix.Replace(
				"*",
				path,
				StringComparison.Ordinal
			)
			: string.Concat(
				path,
				suffix
			)
		;
	}

	private static string ResolveInPlacePath(
		string path,
		bool followSymlinks
	) {
		if ( !followSymlinks ) {
			return path;
		}
		var info = new FileInfo( path );
		var target = info.ResolveLinkTarget( returnFinalTarget: true );
		return target?.FullName ?? path;
	}
}
