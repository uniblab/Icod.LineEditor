namespace Icod.LineEditor.Ed;

using System.Text;

/// <summary>Identifies the exit status returned by the reusable Ed engine.</summary>
public enum EditorExitStatus {
	/// <summary>The script completed successfully.</summary>
	Success = 0,
	/// <summary>The script encountered a controlled command or data error.</summary>
	Error = 1,
	/// <summary>The script was interrupted or canceled.</summary>
	Interrupted = 2
}

/// <summary>Identifies a signal delivered to an editor session.</summary>
public enum EditorSignal {
	/// <summary>No signal is pending.</summary>
	None,
	/// <summary>An interrupt was requested.</summary>
	Interrupt,
	/// <summary>A hangup was requested.</summary>
	Hangup,
	/// <summary>Termination was requested.</summary>
	Terminate
}

/// <summary>Identifies a controlled Ed-engine diagnostic.</summary>
public enum EditorDiagnosticCode {
	/// <summary>An address or range is invalid.</summary>
	InvalidAddress,
	/// <summary>A command is unknown or malformed.</summary>
	InvalidCommand,
	/// <summary>A regular expression could not be compiled or matched.</summary>
	RegularExpression,
	/// <summary>A filename is required or denied.</summary>
	FileName,
	/// <summary>A file operation failed.</summary>
	FileOperation,
	/// <summary>A process operation is denied or failed.</summary>
	ProcessOperation,
	/// <summary>A restricted security policy denied an operation.</summary>
	RestrictedOperation,
	/// <summary>The buffer has unsaved changes.</summary>
	ModifiedBuffer,
	/// <summary>The script ended while command data was incomplete.</summary>
	UnexpectedEndOfInput,
	/// <summary>The operation was canceled or interrupted.</summary>
	Interrupted
}

/// <summary>Represents one deterministic editor diagnostic.</summary>
/// <param name="Code">The stable diagnostic category.</param>
/// <param name="Message">The diagnostic text.</param>
/// <param name="SourceName">The script source name, when known.</param>
/// <param name="LineNumber">The one-based script line number, when known.</param>
public sealed record EditorDiagnostic(
	EditorDiagnosticCode Code,
	string Message,
	string? SourceName = null,
	long? LineNumber = null
);

/// <summary>Represents a stable line stored in the mutable editor buffer.</summary>
public sealed class EditorLine {
	private readonly byte[] content;

	/// <summary>Initializes a line from authoritative bytes.</summary>
	/// <param name="id">The stable nonzero line identity.</param>
	/// <param name="content">The line content without its record separator.</param>
	public EditorLine(
		long id,
		ReadOnlyMemory<byte> content
	) {
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero( id );
		this.Id = id;
		this.content = content.ToArray();
	}

	/// <summary>Gets the stable identity retained across moves and undo snapshots.</summary>
	public long Id {
		get;
	}

	/// <summary>Gets a copy-free view of the line bytes.</summary>
	public ReadOnlyMemory<byte> Content => this.content;

	/// <summary>Decodes the line using UTF-8 with replacement fallback.</summary>
	/// <returns>The decoded line.</returns>
	public string GetText() => Encoding.UTF8.GetString( this.content );
}

/// <summary>Represents an inclusive one-based editor address range.</summary>
/// <param name="Start">The first line address.</param>
/// <param name="End">The last line address.</param>
public readonly record struct EditorAddressRange(
	int Start,
	int End
) {
	/// <summary>Gets the number of addressed lines.</summary>
	public int Count => checked( this.End - this.Start + 1 );
}

/// <summary>Represents the result of executing an editor script.</summary>
/// <param name="ExitStatus">The controlled exit status.</param>
/// <param name="Diagnostic">The last diagnostic, when execution failed.</param>
/// <param name="QuitRequested">Whether a quit command ended execution.</param>
/// <param name="Signal">The signal that ended execution, when applicable.</param>
public sealed record EditorExecutionResult(
	EditorExitStatus ExitStatus,
	EditorDiagnostic? Diagnostic,
	bool QuitRequested,
	EditorSignal Signal
) {
	/// <summary>Gets whether execution completed successfully.</summary>
	public bool IsSuccess => EditorExitStatus.Success == this.ExitStatus;
}

/// <summary>Represents file content returned through an editor file capability.</summary>
/// <param name="Lines">The records without line separators.</param>
/// <param name="FinalRecordTerminated">Whether the final record was terminated.</param>
/// <param name="ByteCount">The number of bytes read.</param>
public sealed record EditorFileReadResult(
	IReadOnlyList<ReadOnlyMemory<byte>> Lines,
	bool FinalRecordTerminated,
	long ByteCount
);

/// <summary>Represents the result of an editor write operation.</summary>
/// <param name="ByteCount">The number of content and separator bytes written.</param>
public sealed record EditorFileWriteResult(
	long ByteCount
);

/// <summary>Represents the result of an editor shell or filter process.</summary>
/// <param name="ExitCode">The child exit code, when one was produced.</param>
/// <param name="Canceled">Whether the process was canceled.</param>
/// <param name="StandardOutput">The captured standard-output bytes.</param>
/// <param name="StandardError">The captured standard-error bytes.</param>
public sealed record EditorProcessResult(
	int? ExitCode,
	bool Canceled,
	ReadOnlyMemory<byte> StandardOutput,
	ReadOnlyMemory<byte> StandardError
);
