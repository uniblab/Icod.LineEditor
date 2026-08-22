namespace Icod.LineEditor.Sed;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

// Responsibility: injectable side-effect capabilities and sandbox runtime policy.
public static partial class Command {

	/// <summary>Executes a shell command requested by a Sed command or substitution flag.</summary>
	internal interface ISedShellCapability {

		/// <summary>Executes one shell command through the configured process capability.</summary>
		Task<ShellResult> ExecuteAsync(
			string command,
			TextWriter output,
			TextWriter error,
			bool captureStandardOutput,
			CancellationToken cancellationToken
		);

	}

	/// <summary>Opens files named by Sed's auxiliary read and write commands.</summary>
	internal interface ISedAuxiliaryFileCapability {

		/// <summary>Opens an auxiliary file for asynchronous reading.</summary>
		ValueTask<Stream> OpenReadAsync(
			string path,
			CancellationToken cancellationToken
		);

		/// <summary>Creates or truncates an auxiliary file for asynchronous writing.</summary>
		ValueTask<Stream> OpenWriteAsync(
			string path,
			CancellationToken cancellationToken
		);

	}

	/// <summary>Performs command-local in-place replacement until LE10 adopts E6.</summary>
	internal interface IInPlaceEditor {

		/// <summary>Runs one in-place transformation and publishes its temporary output.</summary>
		Task<ExecutionResult> EditAsync(
			SedInPlaceEditRequest request,
			Func<string, Stream, CancellationToken, Task<ExecutionResult>> transformAsync,
			CancellationToken cancellationToken
		);

	}

	/// <summary>Describes one command-local in-place edit request.</summary>
	internal sealed record SedInPlaceEditRequest(
		string Path,
		bool FollowSymlinks,
		string? BackupSuffix
	);

	/// <summary>Collects all side-effect capabilities used by one Sed invocation.</summary>
	internal sealed class SedRuntimeCapabilities {

		/// <summary>Gets the host-backed production capability set.</summary>
		public static SedRuntimeCapabilities System { get; } = new(
			SystemSedShellCapability.Instance,
			SystemSedAuxiliaryFileCapability.Instance,
			SystemInPlaceEditor.Instance
		);

		/// <summary>Gets the auxiliary-file capability.</summary>
		public ISedAuxiliaryFileCapability AuxiliaryFiles {
			get;
		}

		/// <summary>Gets the in-place-edit capability.</summary>
		public IInPlaceEditor InPlaceEditor {
			get;
		}

		/// <summary>Gets the shell capability.</summary>
		public ISedShellCapability Shell {
			get;
		}

		/// <summary>Initializes an injectable Sed capability set.</summary>
		public SedRuntimeCapabilities(
			ISedShellCapability shell,
			ISedAuxiliaryFileCapability auxiliaryFiles,
			IInPlaceEditor inPlaceEditor
		) {
			this.Shell = shell ?? throw new ArgumentNullException( nameof( shell ) );
			this.AuxiliaryFiles = auxiliaryFiles ?? throw new ArgumentNullException( nameof( auxiliaryFiles ) );
			this.InPlaceEditor = inPlaceEditor ?? throw new ArgumentNullException( nameof( inPlaceEditor ) );
		}

		/// <summary>Returns the runtime-denied sandbox profile while retaining in-place editing.</summary>
		public SedRuntimeCapabilities ForSandbox() {
			return new SedRuntimeCapabilities(
				DeniedSedShellCapability.Instance,
				DeniedSedAuxiliaryFileCapability.Instance,
				this.InPlaceEditor
			);
		}

	}

	/// <summary>Signals that a runtime capability was denied by Sed's sandbox profile.</summary>
	internal sealed class SedCapabilityDeniedException : InvalidOperationException {

		/// <summary>Initializes a denied-capability diagnostic.</summary>
		public SedCapabilityDeniedException(
			string message
		) : base( message ) {
		}

	}

	/// <summary>Denies shell execution as a runtime sandbox backstop.</summary>
	internal sealed class DeniedSedShellCapability : ISedShellCapability {

		/// <summary>Gets the singleton denied capability.</summary>
		public static DeniedSedShellCapability Instance { get; } = new();

		private DeniedSedShellCapability() {
		}

		/// <inheritdoc />
		public Task<ShellResult> ExecuteAsync(
			string command,
			TextWriter output,
			TextWriter error,
			bool captureStandardOutput,
			CancellationToken cancellationToken
		) {
			cancellationToken.ThrowIfCancellationRequested();
			throw new SedCapabilityDeniedException(
				"shell execution is disabled in sandbox mode"
			);
		}

	}

	/// <summary>Denies auxiliary reads and writes as a runtime sandbox backstop.</summary>
	internal sealed class DeniedSedAuxiliaryFileCapability : ISedAuxiliaryFileCapability {

		/// <summary>Gets the singleton denied capability.</summary>
		public static DeniedSedAuxiliaryFileCapability Instance { get; } = new();

		private DeniedSedAuxiliaryFileCapability() {
		}

		/// <inheritdoc />
		public ValueTask<Stream> OpenReadAsync(
			string path,
			CancellationToken cancellationToken
		) {
			cancellationToken.ThrowIfCancellationRequested();
			throw new SedCapabilityDeniedException(
				"auxiliary file access is disabled in sandbox mode"
			);
		}

		/// <inheritdoc />
		public ValueTask<Stream> OpenWriteAsync(
			string path,
			CancellationToken cancellationToken
		) {
			cancellationToken.ThrowIfCancellationRequested();
			throw new SedCapabilityDeniedException(
				"auxiliary file access is disabled in sandbox mode"
			);
		}

	}

	/// <summary>Opens host files for Sed auxiliary commands.</summary>
	internal sealed class SystemSedAuxiliaryFileCapability : ISedAuxiliaryFileCapability {

		/// <summary>Gets the singleton host capability.</summary>
		public static SystemSedAuxiliaryFileCapability Instance { get; } = new();

		private SystemSedAuxiliaryFileCapability() {
		}

		/// <inheritdoc />
		public ValueTask<Stream> OpenReadAsync(
			string path,
			CancellationToken cancellationToken
		) {
			cancellationToken.ThrowIfCancellationRequested();
			return ValueTask.FromResult<Stream>(
				new FileStream(
					path,
					FileMode.Open,
					FileAccess.Read,
					FileShare.Read,
					8192,
					useAsync: true
				)
			);
		}

		/// <inheritdoc />
		public ValueTask<Stream> OpenWriteAsync(
			string path,
			CancellationToken cancellationToken
		) {
			cancellationToken.ThrowIfCancellationRequested();
			return ValueTask.FromResult<Stream>(
				new FileStream(
					path,
					FileMode.Create,
					FileAccess.Write,
					FileShare.Read,
					8192,
					useAsync: true
				)
			);
		}

	}

}
