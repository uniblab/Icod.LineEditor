namespace Icod.LineEditor.Sed;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Icod.CommandFramework.CommandLine;
using Icod.CommandFramework.Diagnostics;
using Icod.CommandFramework.IO;
using Icod.CommandFramework.Processes;

// Responsibility: shell execution and stream adaptation.
public static partial class Command {

	/// <summary>Captures one shell process exit status and optional standard output.</summary>
	internal sealed record ShellResult(
		int ExitCode,
		string StandardOutput
	);

	private sealed class TextWriterStream : Stream {

		private readonly Decoder myDecoder;
		private readonly Encoding myEncoding;
		private readonly TextWriter myWriter;

		public override bool CanRead {
			get {
				return false;
			}
		}

		public override bool CanSeek {
			get {
				return false;
			}
		}

		public override bool CanWrite {
			get {
				return true;
			}
		}

		public override long Length {
			get {
				throw new NotSupportedException();
			}
		}

		public override long Position {
			get {
				throw new NotSupportedException();
			}
			set {
				throw new NotSupportedException();
			}
		}

		public TextWriterStream(
			TextWriter writer,
			Encoding encoding
		) {
			this.myWriter = writer ?? throw new ArgumentNullException(
				nameof( writer )
			);
			this.myEncoding = encoding ?? throw new ArgumentNullException(
				nameof( encoding )
			);
			this.myDecoder = encoding.GetDecoder();
		}

		public override void Flush() {
			this.myWriter.Flush();
		}

		public override async Task FlushAsync(
			CancellationToken cancellationToken
		) {
			var characters = new char[
				this.myEncoding.GetMaxCharCount(
					0
				)
			];
			this.myDecoder.Convert(
				ReadOnlySpan<byte>.Empty,
				characters.AsSpan(),
				flush: true,
				out _,
				out var charactersUsed,
				out _
			);
			if ( 0 < charactersUsed ) {
				await this.myWriter.WriteAsync(
					characters.AsMemory(
						0,
						charactersUsed
					),
					cancellationToken
				).ConfigureAwait( false );
			}
			await this.myWriter.FlushAsync(
				cancellationToken
			).ConfigureAwait( false );
		}

		public override int Read(
			byte[] buffer,
			int offset,
			int count
		) {
			throw new NotSupportedException();
		}

		public override long Seek(
			long offset,
			SeekOrigin origin
		) {
			throw new NotSupportedException();
		}

		public override void SetLength(
			long value
		) {
			throw new NotSupportedException();
		}

		public override void Write(
			byte[] buffer,
			int offset,
			int count
		) {
			var characters = new char[
				this.myEncoding.GetMaxCharCount(
					count
				)
			];
			this.myDecoder.Convert(
				buffer.AsSpan(
					offset,
					count
				),
				characters.AsSpan(),
				flush: false,
				out _,
				out var charactersUsed,
				out _
			);
			this.myWriter.Write(
				characters,
				0,
				charactersUsed
			);
		}

		public override async ValueTask WriteAsync(
			ReadOnlyMemory<byte> buffer,
			CancellationToken cancellationToken = default
		) {
			if ( buffer.IsEmpty ) {
				return;
			}
			var characters = new char[
				this.myEncoding.GetMaxCharCount(
					buffer.Length
				)
			];
			this.myDecoder.Convert(
				buffer.Span,
				characters.AsSpan(),
				flush: false,
				out _,
				out var charactersUsed,
				out _
			);
			await this.myWriter.WriteAsync(
				characters.AsMemory(
					0,
					charactersUsed
				),
				cancellationToken
			).ConfigureAwait( false );
		}

	}

	private static async Task<ShellResult> ExecuteShellAsync(
		string command,
		ExecutionEnvironment environment,
		bool captureStandardOutput,
		CancellationToken cancellationToken
	) {
		if ( !captureStandardOutput ) {
			await environment.Output.BeginOutputAsync( cancellationToken ).ConfigureAwait( false );
		}
		return await environment.Shell.ExecuteAsync(
			command,
			new SedOutputTextWriter( environment.Output ),
			environment.Error,
			captureStandardOutput,
			cancellationToken
		).ConfigureAwait( false );
	}

	/// <summary>Executes Sed shell commands through the Shared process runner.</summary>
	internal sealed class SystemSedShellCapability : ISedShellCapability {

		/// <summary>Gets the singleton host-backed shell capability.</summary>
		public static SystemSedShellCapability Instance { get; } = new();

		private SystemSedShellCapability() {
		}

		/// <inheritdoc />
		public async Task<ShellResult> ExecuteAsync(
			string command,
			TextWriter output,
			TextWriter error,
			bool captureStandardOutput,
			CancellationToken cancellationToken
		) {
			ArgumentNullException.ThrowIfNull( command );
			ArgumentNullException.ThrowIfNull( output );
			ArgumentNullException.ThrowIfNull( error );
			cancellationToken.ThrowIfCancellationRequested();

			await using var outputStream = captureStandardOutput
				? null
				: new TextWriterStream(
					output,
					Encoding.UTF8
				)
			;
			await using var errorStream = new TextWriterStream(
				error,
				Encoding.UTF8
			);
			var options = new ProcessRunOptions(
				OperatingSystem.IsWindows()
					? Environment.GetEnvironmentVariable( "COMSPEC" ) ?? "cmd.exe"
					: "/bin/sh"
			) {
				CaptureStandardOutput = captureStandardOutput,
				OutputEncoding = Encoding.UTF8,
				StandardError = errorStream,
				StandardOutput = outputStream
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
			if ( result.WasCanceled ) {
				throw new OperationCanceledException( cancellationToken );
			}
			if ( null != outputStream ) {
				await outputStream.FlushAsync( cancellationToken ).ConfigureAwait( false );
			}
			await errorStream.FlushAsync( cancellationToken ).ConfigureAwait( false );
			return new ShellResult(
				result.ExitCode ?? ErrorExitCode,
				result.StandardOutput ?? string.Empty
			);
		}

	}


}
