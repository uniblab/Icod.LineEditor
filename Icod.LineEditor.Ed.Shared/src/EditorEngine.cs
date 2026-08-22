namespace Icod.LineEditor.Ed;

using System.Globalization;
using System.Text;
using Icod.CommandFramework.Records;
using Icod.CommandFramework.RegularExpressions;

/// <summary>
/// Executes Ed scripts over a mutable, stable-identity line buffer using injectable regular-expression,
/// file, process, and security capabilities.
/// </summary>
public sealed class EditorEngine {
	private static readonly ReadOnlyMemory<byte> LineFeed = new byte[] { (byte)'\n' };
	private readonly IRegularExpressionProvider regularExpressionProvider;
	private readonly IEditorFileAccess fileAccess;
	private readonly IEditorProcessAccess processAccess;
	private readonly Dictionary<char, long> marks = new();
	private readonly List<ReadOnlyMemory<byte>> cutBuffer = new();
	private EditorSnapshot? undoSnapshot;
	private string? lastRegularExpression;
	private string? lastReplacement;
	private string? lastShellCommand;
	private EditorSignal pendingSignal;
	private bool globalExecutionActive;

	/// <summary>Initializes an engine with the standard security profile and system capabilities.</summary>
	public EditorEngine() : this(
		EditorSecurityPolicy.Standard,
		new StandardEditorFileAccess(),
		new StandardEditorProcessAccess(),
		GnuBasicRegularExpressionProvider.Default
	) {
	}

	/// <summary>Initializes an engine from one immutable capability profile.</summary>
	/// <param name="profile">The policy and capabilities exposed to the engine.</param>
	/// <param name="regularExpressionProvider">The Shared GNU regular-expression provider.</param>
	public EditorEngine(
		EditorCapabilityProfile profile,
		IRegularExpressionProvider regularExpressionProvider
	) : this(
		profile?.SecurityPolicy ?? throw new ArgumentNullException( nameof( profile ) ),
		profile?.FileAccess ?? throw new ArgumentNullException( nameof( profile ) ),
		profile?.ProcessAccess ?? throw new ArgumentNullException( nameof( profile ) ),
		regularExpressionProvider
	) {
	}

	/// <summary>Initializes an engine with explicit policy and capabilities.</summary>
	/// <param name="securityPolicy">The immutable parser and dispatch policy.</param>
	/// <param name="fileAccess">The filename-bearing capability.</param>
	/// <param name="processAccess">The process capability.</param>
	/// <param name="regularExpressionProvider">The Shared GNU BRE provider.</param>
	public EditorEngine(
		EditorSecurityPolicy securityPolicy,
		IEditorFileAccess fileAccess,
		IEditorProcessAccess processAccess,
		IRegularExpressionProvider regularExpressionProvider
	) {
		ArgumentNullException.ThrowIfNull( securityPolicy );
		ArgumentNullException.ThrowIfNull( fileAccess );
		ArgumentNullException.ThrowIfNull( processAccess );
		ArgumentNullException.ThrowIfNull( regularExpressionProvider );
		this.SecurityPolicy = securityPolicy;
		this.fileAccess = fileAccess;
		this.processAccess = processAccess;
		this.regularExpressionProvider = regularExpressionProvider;
		this.Buffer = new EditorBuffer();
	}

	/// <summary>Creates a restricted engine rooted at the supplied working directory.</summary>
	/// <param name="workingDirectory">The captured working directory.</param>
	/// <param name="fileAccess">The underlying file capability to constrain beneath the captured directory.</param>
	/// <param name="regularExpressionProvider">An optional Shared GNU BRE provider.</param>
	/// <returns>The configured engine.</returns>
	public static EditorEngine CreateRestricted(
		string workingDirectory,
		IEditorFileAccess fileAccess,
		IRegularExpressionProvider? regularExpressionProvider = null
	) => new(
		EditorCapabilityProfile.Restricted( workingDirectory, fileAccess ),
		regularExpressionProvider ?? GnuBasicRegularExpressionProvider.Default
	);

	/// <summary>Creates a system-backed restricted engine rooted at the supplied working directory.</summary>
	/// <param name="workingDirectory">The captured working directory.</param>
	/// <returns>The configured engine.</returns>
	public static EditorEngine CreateRestricted(
		string workingDirectory
	) => CreateRestricted(
		workingDirectory,
		new StandardEditorFileAccess()
	);

	/// <summary>Gets the mutable line buffer.</summary>
	public EditorBuffer Buffer {
		get;
	}

	/// <summary>Gets the immutable security policy.</summary>
	public EditorSecurityPolicy SecurityPolicy {
		get;
	}

	/// <summary>Gets the current one-based line address, or zero for an empty buffer.</summary>
	public int CurrentAddress {
		get;
		private set;
	}

	/// <summary>Gets whether the buffer contains changes not cleared by an edit or write command.</summary>
	public bool IsModified {
		get;
		private set;
	}

	/// <summary>Gets the remembered filename, when one is permitted and established.</summary>
	public string? RememberedFileName {
		get;
		private set;
	}

	/// <summary>Gets whether the final buffered record should be terminated when written.</summary>
	public bool FinalRecordTerminated {
		get;
		private set;
	} = true;

	/// <summary>Gets the last controlled diagnostic.</summary>
	public EditorDiagnostic? LastDiagnostic {
		get;
		private set;
	}

	/// <summary>Sets the current address for command-line initial-address selection.</summary>
	/// <param name="address">A line address from zero through the current buffer size.</param>
	public void SetCurrentAddress(
		int address
	) {
		if ( ( 0 > address ) || ( this.Buffer.Count < address ) ) {
			throw new ArgumentOutOfRangeException(
				nameof( address ),
				"The current address must identify the empty position or an existing line."
			);
		}
		this.CurrentAddress = address;
	}

	/// <summary>Requests cooperative signal handling before the next command transition.</summary>
	/// <param name="signal">The requested signal.</param>
	public void RequestSignal(
		EditorSignal signal
	) {
		if ( EditorSignal.None == signal ) {
			throw new ArgumentOutOfRangeException( nameof( signal ) );
		}
		this.pendingSignal = signal;
	}

	/// <summary>Loads initial records without creating an undo unit.</summary>
	/// <param name="lines">The initial line content.</param>
	/// <param name="finalRecordTerminated">Whether the final record was terminated.</param>
	/// <param name="rememberedFileName">The initial remembered filename.</param>
	public void Load(
		IEnumerable<ReadOnlyMemory<byte>> lines,
		bool finalRecordTerminated = true,
		string? rememberedFileName = null
	) {
		ArgumentNullException.ThrowIfNull( lines );
		if ( null != rememberedFileName ) {
			if ( !this.SecurityPolicy.AllowRememberedFileName ) {
				throw new UnauthorizedAccessException(
					"The editor security profile denies remembered filenames."
				);
			}
			if ( this.SecurityPolicy.IsRestricted && !IsRestrictedFileName( rememberedFileName ) ) {
				throw new UnauthorizedAccessException(
					"Restricted mode permits only a simple remembered filename."
				);
			}
		}
		this.Buffer.Reset( lines );
		this.CurrentAddress = this.Buffer.Count;
		this.FinalRecordTerminated = finalRecordTerminated;
		this.RememberedFileName = rememberedFileName;
		this.IsModified = false;
		this.marks.Clear();
		this.cutBuffer.Clear();
		this.undoSnapshot = null;
		this.LastDiagnostic = null;
	}

	/// <summary>Executes an LF-delimited Ed command stream.</summary>
	/// <param name="script">The script stream.</param>
	/// <param name="standardOutput">The output destination.</param>
	/// <param name="standardError">The diagnostic and shell-error destination.</param>
	/// <param name="sourceName">The stable script source name.</param>
	/// <param name="cancellationToken">A cancellation token.</param>
	/// <returns>The controlled execution result.</returns>
	public async ValueTask<EditorExecutionResult> ExecuteScriptAsync(
		Stream script,
		Stream standardOutput,
		Stream standardError,
		string sourceName = "<stdin>",
		CancellationToken cancellationToken = default
	) {
		ArgumentNullException.ThrowIfNull( script );
		ArgumentNullException.ThrowIfNull( standardOutput );
		ArgumentNullException.ThrowIfNull( standardError );
		ArgumentException.ThrowIfNullOrWhiteSpace( sourceName );

		IReadOnlyList<ReadOnlyMemory<byte>> records;
		try {
			records = await ReadScriptAsync( script, cancellationToken ).ConfigureAwait( false );
		} catch ( OperationCanceledException ) {
			var signal = EditorSignal.None == this.pendingSignal
				? EditorSignal.Interrupt
				: this.pendingSignal;
			this.pendingSignal = EditorSignal.None;
			var diagnostic = new EditorDiagnostic(
				EditorDiagnosticCode.Interrupted,
				"Editor execution was interrupted.",
				sourceName,
				1
			);
			this.LastDiagnostic = diagnostic;
			return new EditorExecutionResult(
				EditorExitStatus.Interrupted,
				diagnostic,
				false,
				signal
			);
		}
		for ( var index = 0; records.Count > index; index++ ) {
			try {
				this.ThrowIfInterrupted( cancellationToken );
				var lineNumber = checked( (long)index + 1 );
				var command = Encoding.UTF8.GetString( records[ index ].Span );
				var outcome = await this.ExecuteCommandAsync(
					command,
					records,
					index,
					standardOutput,
					standardError,
					sourceName,
					lineNumber,
					captureUndo: true,
					cancellationToken
				).ConfigureAwait( false );
				index = outcome.LastConsumedRecord;
				if ( outcome.QuitRequested ) {
					await standardOutput.FlushAsync( cancellationToken ).ConfigureAwait( false );
					await standardError.FlushAsync( cancellationToken ).ConfigureAwait( false );
					return new EditorExecutionResult(
						EditorExitStatus.Success,
						null,
						true,
						EditorSignal.None
					);
				}
			} catch ( OperationCanceledException ) {
				var signal = EditorSignal.None == this.pendingSignal
					? EditorSignal.Interrupt
					: this.pendingSignal;
				this.pendingSignal = EditorSignal.None;
				var diagnostic = new EditorDiagnostic(
					EditorDiagnosticCode.Interrupted,
					"Editor execution was interrupted.",
					sourceName,
					checked( (long)index + 1 )
				);
				this.LastDiagnostic = diagnostic;
				return new EditorExecutionResult(
					EditorExitStatus.Interrupted,
					diagnostic,
					false,
					signal
				);
			} catch ( EditorCommandException exception ) {
				var diagnostic = new EditorDiagnostic(
					exception.Code,
					exception.Message,
					sourceName,
					checked( (long)index + 1 )
				);
				this.LastDiagnostic = diagnostic;
				await standardError.WriteAsync( new byte[] { (byte)'?', (byte)'\n' }, cancellationToken ).ConfigureAwait( false );
				await standardError.FlushAsync( cancellationToken ).ConfigureAwait( false );
				return new EditorExecutionResult(
					EditorExitStatus.Error,
					diagnostic,
					false,
					EditorSignal.None
				);
			} catch ( Exception exception ) when (
				exception is IOException
				or UnauthorizedAccessException
				or System.ComponentModel.Win32Exception
			) {
				var diagnostic = new EditorDiagnostic(
					exception is UnauthorizedAccessException
						? EditorDiagnosticCode.RestrictedOperation
						: EditorDiagnosticCode.FileOperation,
					exception.Message,
					sourceName,
					checked( (long)index + 1 )
				);
				this.LastDiagnostic = diagnostic;
				await standardError.WriteAsync( new byte[] { (byte)'?', (byte)'\n' }, cancellationToken ).ConfigureAwait( false );
				await standardError.FlushAsync( cancellationToken ).ConfigureAwait( false );
				return new EditorExecutionResult(
					EditorExitStatus.Error,
					diagnostic,
					false,
					EditorSignal.None
				);
			}
		}
		await standardOutput.FlushAsync( cancellationToken ).ConfigureAwait( false );
		await standardError.FlushAsync( cancellationToken ).ConfigureAwait( false );
		return new EditorExecutionResult(
			EditorExitStatus.Success,
			null,
			false,
			EditorSignal.None
		);
	}

	private async ValueTask<CommandOutcome> ExecuteCommandAsync(
		string commandText,
		IReadOnlyList<ReadOnlyMemory<byte>> scriptRecords,
		int scriptIndex,
		Stream standardOutput,
		Stream standardError,
		string sourceName,
		long lineNumber,
		bool captureUndo,
		CancellationToken cancellationToken
	) {
		this.ThrowIfInterrupted( cancellationToken );
		this.ValidateRestrictedCommandText( commandText );
		var parser = new EditorAddressParser(
			commandText,
			this.CurrentAddress,
			this.Buffer.Count,
			this.ResolveMark,
			( pattern, reverse, startAddress ) => this.SearchAddress(
				pattern,
				reverse,
				startAddress,
				cancellationToken
			)
		);
		ParsedEditorRange parsedRange;
		try {
			parsedRange = parser.ParseRange();
		} catch ( EditorParseException exception ) {
			throw new EditorCommandException( EditorDiagnosticCode.InvalidAddress, exception.Message );
		}
		var position = parser.Position;
		while ( ( commandText.Length > position ) && char.IsWhiteSpace( commandText[ position ] ) ) {
			position++;
		}
		if ( commandText.Length <= position ) {
			var next = this.CurrentAddress + 1;
			if ( ( 1 > next ) || ( this.Buffer.Count < next ) ) {
				throw new EditorCommandException(
					EditorDiagnosticCode.InvalidAddress,
					0 == this.Buffer.Count
						? "The buffer is empty."
						: "There is no next line."
				);
			}
			await this.PrintRangeAsync(
				new EditorAddressRange( next, next ),
				standardOutput,
				PrintMode.Plain,
				cancellationToken
			).ConfigureAwait( false );
			return new CommandOutcome( scriptIndex, false );
		}

		var command = commandText[ position++ ];
		var arguments = commandText[ position.. ];
		switch ( command ) {
			case '#':
				return new CommandOutcome( scriptIndex, false );
			case 'a': {
				var address = this.ResolveSingleAddress( parsedRange, this.CurrentAddress, allowZero: true );
				var block = ReadDataBlock( scriptRecords, scriptIndex );
				if ( captureUndo ) {
					this.CaptureUndo();
				}
				var inserted = this.Buffer.InsertAfter( address, block.Lines );
				this.CurrentAddress = 0 == inserted.Start ? address : inserted.End;
				this.IsModified = 0 < block.Lines.Count || this.IsModified;
				return new CommandOutcome( block.LastConsumedRecord, false );
			}
			case 'i': {
				var address = this.ResolveSingleAddress( parsedRange, this.CurrentAddress, allowZero: false );
				var block = ReadDataBlock( scriptRecords, scriptIndex );
				if ( captureUndo ) {
					this.CaptureUndo();
				}
				var inserted = this.Buffer.InsertAfter( Math.Max( 0, address - 1 ), block.Lines );
				this.CurrentAddress = 0 == inserted.Start ? address : inserted.End;
				this.IsModified = 0 < block.Lines.Count || this.IsModified;
				return new CommandOutcome( block.LastConsumedRecord, false );
			}
			case 'c': {
				var range = this.ResolveRange( parsedRange, this.CurrentAddress, this.CurrentAddress );
				var block = ReadDataBlock( scriptRecords, scriptIndex );
				if ( captureUndo ) {
					this.CaptureUndo();
				}
				this.cutBuffer.Clear();
				this.cutBuffer.AddRange( this.Buffer.Delete( range ).Select( line => new ReadOnlyMemory<byte>( line.Content.ToArray() ) ) );
				var inserted = this.Buffer.InsertAfter( range.Start - 1, block.Lines );
				this.CurrentAddress = 0 == inserted.Start
					? Math.Min( this.Buffer.Count, range.Start - 1 )
					: inserted.End;
				this.RemoveDanglingMarks();
				this.IsModified = true;
				return new CommandOutcome( block.LastConsumedRecord, false );
			}
			case 'd': {
				var range = this.ResolveRange( parsedRange, this.CurrentAddress, this.CurrentAddress );
				if ( captureUndo ) {
					this.CaptureUndo();
				}
				this.cutBuffer.Clear();
				this.cutBuffer.AddRange( this.Buffer.Delete( range ).Select( line => new ReadOnlyMemory<byte>( line.Content.ToArray() ) ) );
				this.CurrentAddress = 0 == this.Buffer.Count
					? 0
					: Math.Min( range.Start, this.Buffer.Count );
				this.RemoveDanglingMarks();
				this.IsModified = true;
				return new CommandOutcome( scriptIndex, false );
			}
			case 'p':
			case 'n':
			case 'l': {
				var range = this.ResolveRange( parsedRange, this.CurrentAddress, this.CurrentAddress );
				await this.PrintRangeAsync(
					range,
					standardOutput,
					'p' == command ? PrintMode.Plain : 'n' == command ? PrintMode.Numbered : PrintMode.List,
					cancellationToken
				).ConfigureAwait( false );
				return new CommandOutcome( scriptIndex, false );
			}
			case '=': {
				var address = this.ResolveSingleAddress( parsedRange, this.Buffer.Count, allowZero: true );
				await WriteTextLineAsync(
					standardOutput,
					address.ToString( CultureInfo.InvariantCulture ),
					cancellationToken
				).ConfigureAwait( false );
				return new CommandOutcome( scriptIndex, false );
			}
			case 'k': {
				var address = this.ResolveSingleAddress( parsedRange, this.CurrentAddress, allowZero: false );
				var mark = arguments.Trim();
				if ( ( 1 != mark.Length ) || ( 'a' > mark[ 0 ] ) || ( 'z' < mark[ 0 ] ) ) {
					throw new EditorCommandException( EditorDiagnosticCode.InvalidCommand, "A mark name from a through z is required." );
				}
				this.marks[ mark[ 0 ] ] = this.Buffer.GetLine( address ).Id;
				this.CurrentAddress = address;
				return new CommandOutcome( scriptIndex, false );
			}
			case 'm':
			case 't': {
				var range = this.ResolveRange( parsedRange, this.CurrentAddress, this.CurrentAddress );
				var destination = this.ParseDestinationAddress( arguments, cancellationToken );
				if (
					( 'm' == command )
					&& ( range.Start <= destination )
					&& ( range.End >= destination )
				) {
					throw new EditorCommandException(
						EditorDiagnosticCode.InvalidAddress,
						"The move destination is inside the addressed range."
					);
				}
				if ( captureUndo ) {
					this.CaptureUndo();
				}
				var moved = 'm' == command
					? this.Buffer.Move( range, destination )
					: this.Buffer.Copy( range, destination );
				this.CurrentAddress = moved.End;
				this.IsModified = true;
				return new CommandOutcome( scriptIndex, false );
			}
			case 'j': {
				var defaultStart = Math.Max( 1, this.CurrentAddress );
				var defaultEnd = Math.Min( this.Buffer.Count, defaultStart + 1 );
				var range = this.ResolveRange( parsedRange, defaultStart, defaultEnd );
				if ( captureUndo ) {
					this.CaptureUndo();
				}
				this.CurrentAddress = this.Buffer.Join( range );
				this.RemoveDanglingMarks();
				this.IsModified = true;
				return new CommandOutcome( scriptIndex, false );
			}
			case 'y': {
				var range = this.ResolveRange( parsedRange, this.CurrentAddress, this.CurrentAddress );
				this.cutBuffer.Clear();
				for ( var address = range.Start; range.End >= address; address++ ) {
					this.cutBuffer.Add( this.Buffer.GetLine( address ).Content.ToArray() );
				}
				this.CurrentAddress = range.End;
				return new CommandOutcome( scriptIndex, false );
			}
			case 'x': {
				var address = this.ResolveSingleAddress( parsedRange, this.CurrentAddress, allowZero: true );
				if ( 0 == this.cutBuffer.Count ) {
					throw new EditorCommandException( EditorDiagnosticCode.InvalidCommand, "The cut buffer is empty." );
				}
				if ( captureUndo ) {
					this.CaptureUndo();
				}
				var inserted = this.Buffer.InsertAfter( address, this.cutBuffer );
				this.CurrentAddress = inserted.End;
				this.IsModified = true;
				return new CommandOutcome( scriptIndex, false );
			}
			case 's': {
				var range = this.ResolveRange( parsedRange, this.CurrentAddress, this.CurrentAddress );
				var previousUndo = this.undoSnapshot;
				if ( captureUndo ) {
					this.CaptureUndo();
				}
				var changed = this.Substitute( range, arguments, cancellationToken );
				if ( !changed.IsMatch ) {
					if ( captureUndo ) {
						this.undoSnapshot = previousUndo;
					}
					throw new EditorCommandException( EditorDiagnosticCode.RegularExpression, "No match." );
				}
				this.CurrentAddress = changed.LastChangedAddress;
				this.IsModified = true;
				if ( changed.PrintChanged ) {
					await this.PrintRangeAsync(
						new EditorAddressRange( this.CurrentAddress, this.CurrentAddress ),
						standardOutput,
						PrintMode.Plain,
						cancellationToken
					).ConfigureAwait( false );
				}
				return new CommandOutcome( scriptIndex, false );
			}
			case 'g':
			case 'v': {
				var range = this.ResolveRange( parsedRange, 1, this.Buffer.Count );
				if ( captureUndo ) {
					this.CaptureUndo();
				}
				await this.ExecuteGlobalAsync(
					range,
					arguments,
					'v' == command,
					scriptRecords,
					scriptIndex,
					standardOutput,
					standardError,
					sourceName,
					lineNumber,
					cancellationToken
				).ConfigureAwait( false );
				return new CommandOutcome( scriptIndex, false );
			}
			case 'u':
				this.Undo();
				return new CommandOutcome( scriptIndex, false );
			case 'e':
			case 'E': {
				if ( ( 'e' == command ) && this.IsModified ) {
					throw new EditorCommandException( EditorDiagnosticCode.ModifiedBuffer, "The buffer has unsaved changes." );
				}
				var path = this.ResolveFileName( arguments, requireName: true );
				var read = await this.fileAccess.ReadAsync( path, cancellationToken ).ConfigureAwait( false );
				if ( captureUndo ) {
					this.CaptureUndo();
				}
				this.Buffer.Reset( read.Lines );
				this.CurrentAddress = this.Buffer.Count;
				this.FinalRecordTerminated = read.FinalRecordTerminated;
				this.SetRememberedFileName( path );
				this.IsModified = false;
				this.marks.Clear();
				this.cutBuffer.Clear();
				await WriteTextLineAsync(
					standardOutput,
					read.ByteCount.ToString( CultureInfo.InvariantCulture ),
					cancellationToken
				).ConfigureAwait( false );
				return new CommandOutcome( scriptIndex, false );
			}
			case 'r': {
				var address = this.ResolveSingleAddress( parsedRange, this.Buffer.Count, allowZero: true );
				var path = this.ResolveFileName( arguments, requireName: true );
				var read = await this.fileAccess.ReadAsync( path, cancellationToken ).ConfigureAwait( false );
				if ( captureUndo ) {
					this.CaptureUndo();
				}
				var inserted = this.Buffer.InsertAfter( address, read.Lines );
				this.CurrentAddress = 0 == inserted.Start ? address : inserted.End;
				if ( ( 0 < read.Lines.Count ) && ( this.Buffer.Count == inserted.End ) ) {
					this.FinalRecordTerminated = read.FinalRecordTerminated;
				}
				this.IsModified = 0 < read.Lines.Count || this.IsModified;
				if ( null == this.RememberedFileName ) {
					this.SetRememberedFileName( path );
				}
				await WriteTextLineAsync(
					standardOutput,
					read.ByteCount.ToString( CultureInfo.InvariantCulture ),
					cancellationToken
				).ConfigureAwait( false );
				return new CommandOutcome( scriptIndex, false );
			}
			case 'w':
			case 'W': {
				var range = this.ResolveRange( parsedRange, 1, this.Buffer.Count );
				var path = this.ResolveFileName( arguments, requireName: true );
				var lines = new List<ReadOnlyMemory<byte>>( range.Count );
				for ( var address = range.Start; range.End >= address; address++ ) {
					lines.Add( this.Buffer.GetLine( address ).Content );
				}
				var write = await this.fileAccess.WriteAsync(
					path,
					lines.AsReadOnly(),
					'W' == command,
					this.FinalRecordTerminated,
					cancellationToken
				).ConfigureAwait( false );
				if ( null == this.RememberedFileName ) {
					this.SetRememberedFileName( path );
				}
				if ( ( 1 == range.Start ) && ( this.Buffer.Count == range.End ) && ( 'w' == command ) ) {
					this.IsModified = false;
				}
				await WriteTextLineAsync(
					standardOutput,
					write.ByteCount.ToString( CultureInfo.InvariantCulture ),
					cancellationToken
				).ConfigureAwait( false );
				return new CommandOutcome( scriptIndex, false );
			}
			case 'f': {
				var candidate = ParseFileNameArgument( arguments );
				if ( 0 < candidate.Length ) {
					this.SetRememberedFileName( candidate );
				}
				if ( null == this.RememberedFileName ) {
					throw new EditorCommandException( EditorDiagnosticCode.FileName, "No current filename." );
				}
				await WriteTextLineAsync( standardOutput, this.RememberedFileName, cancellationToken ).ConfigureAwait( false );
				return new CommandOutcome( scriptIndex, false );
			}
			case '!': {
				await this.ExecuteShellAsync(
					parsedRange,
					arguments,
					standardOutput,
					standardError,
					captureUndo,
					cancellationToken
				).ConfigureAwait( false );
				return new CommandOutcome( scriptIndex, false );
			}
			case 'q':
				if ( this.IsModified ) {
					throw new EditorCommandException( EditorDiagnosticCode.ModifiedBuffer, "The buffer has unsaved changes." );
				}
				return new CommandOutcome( scriptIndex, true );
			case 'Q':
				return new CommandOutcome( scriptIndex, true );
			case 'h':
				if ( null != this.LastDiagnostic ) {
					await WriteTextLineAsync( standardError, this.LastDiagnostic.Message, cancellationToken ).ConfigureAwait( false );
				}
				return new CommandOutcome( scriptIndex, false );
			case 'H':
				return new CommandOutcome( scriptIndex, false );
			default:
				throw new EditorCommandException(
					EditorDiagnosticCode.InvalidCommand,
					string.Concat( "Unknown command: ", command )
				);
		}
	}

	private async ValueTask ExecuteGlobalAsync(
		EditorAddressRange range,
		string arguments,
		bool invert,
		IReadOnlyList<ReadOnlyMemory<byte>> scriptRecords,
		int scriptIndex,
		Stream standardOutput,
		Stream standardError,
		string sourceName,
		long lineNumber,
		CancellationToken cancellationToken
	) {
		if ( this.globalExecutionActive ) {
			throw new EditorCommandException( EditorDiagnosticCode.InvalidCommand, "Nested global commands are not permitted." );
		}
		var parsed = ParseDelimitedCommand( arguments, allowFlags: false );
		var expression = this.CompileRegularExpression( parsed.Pattern, cancellationToken );
		var command = string.IsNullOrWhiteSpace( parsed.Remainder ) ? "p" : parsed.Remainder;
		var selectedIds = new List<long>();
		for ( var address = range.Start; range.End >= address; address++ ) {
			this.ThrowIfInterrupted( cancellationToken );
			var line = this.Buffer.GetLine( address );
			var result = expression.Match( line.GetText(), cancellationToken: cancellationToken );
			if ( !result.IsSuccess ) {
				throw new EditorCommandException( EditorDiagnosticCode.RegularExpression, result.Diagnostic?.ToString() ?? "Regular-expression matching failed." );
			}
			if ( invert != result.IsMatch ) {
				selectedIds.Add( line.Id );
			}
		}

		this.globalExecutionActive = true;
		try {
			foreach ( var lineId in selectedIds ) {
				this.ThrowIfInterrupted( cancellationToken );
				var address = this.Buffer.FindAddress( lineId );
				if ( 0 == address ) {
					continue;
				}
				this.CurrentAddress = address;
				await this.ExecuteCommandAsync(
					command,
					scriptRecords,
					scriptIndex,
					standardOutput,
					standardError,
					sourceName,
					lineNumber,
					captureUndo: false,
					cancellationToken
				).ConfigureAwait( false );
			}
		} finally {
			this.globalExecutionActive = false;
		}
	}

	private async ValueTask<EditorProcessResult> RunShellCapabilityAsync(
		string command,
		ReadOnlyMemory<byte> standardInput,
		CancellationToken cancellationToken
	) {
		try {
			return await this.processAccess.RunShellAsync(
				command,
				standardInput,
				cancellationToken
			).ConfigureAwait( false );
		} catch ( OperationCanceledException ) {
			throw;
		} catch ( Exception exception ) when (
			exception is IOException
				or UnauthorizedAccessException
				or System.ComponentModel.Win32Exception
				or InvalidOperationException
		) {
			throw new EditorCommandException(
				exception is UnauthorizedAccessException
					? EditorDiagnosticCode.RestrictedOperation
					: EditorDiagnosticCode.ProcessOperation,
				exception.Message
			);
		}
	}

	private async ValueTask ExecuteShellAsync(
		ParsedEditorRange parsedRange,
		string arguments,
		Stream standardOutput,
		Stream standardError,
		bool captureUndo,
		CancellationToken cancellationToken
	) {
		if ( !this.SecurityPolicy.AllowShellCommands ) {
			throw new EditorCommandException( EditorDiagnosticCode.RestrictedOperation, "Shell commands are disabled by the editor security profile." );
		}
		var command = arguments.Trim();
		if ( "!" == command ) {
			command = this.lastShellCommand ?? throw new EditorCommandException(
				EditorDiagnosticCode.ProcessOperation,
				"No previous shell command."
			);
		} else if ( 0 == command.Length ) {
			throw new EditorCommandException( EditorDiagnosticCode.ProcessOperation, "A shell command is required." );
		}
		this.lastShellCommand = command;

		if ( !parsedRange.IsSpecified ) {
			var result = await this.RunShellCapabilityAsync(
				command,
				ReadOnlyMemory<byte>.Empty,
				cancellationToken
			).ConfigureAwait( false );
			if ( result.Canceled ) {
				throw new OperationCanceledException( cancellationToken );
			}
			await standardOutput.WriteAsync( result.StandardOutput, cancellationToken ).ConfigureAwait( false );
			await standardError.WriteAsync( result.StandardError, cancellationToken ).ConfigureAwait( false );
			if ( 0 != ( result.ExitCode ?? 1 ) ) {
				throw new EditorCommandException( EditorDiagnosticCode.ProcessOperation, "The shell command failed." );
			}
			return;
		}

		var range = this.ValidateRange( parsedRange.Range );
		await using var input = new MemoryStream();
		for ( var address = range.Start; range.End >= address; address++ ) {
			await input.WriteAsync( this.Buffer.GetLine( address ).Content, cancellationToken ).ConfigureAwait( false );
			await input.WriteAsync( LineFeed, cancellationToken ).ConfigureAwait( false );
		}
		var process = await this.RunShellCapabilityAsync(
			command,
			input.ToArray(),
			cancellationToken
		).ConfigureAwait( false );
		await standardError.WriteAsync( process.StandardError, cancellationToken ).ConfigureAwait( false );
		if ( process.Canceled ) {
			throw new OperationCanceledException( cancellationToken );
		}
		if ( 0 != ( process.ExitCode ?? 1 ) ) {
			throw new EditorCommandException( EditorDiagnosticCode.ProcessOperation, "The filter command failed." );
		}
		var replacement = await ReadRecordsFromMemoryAsync( process.StandardOutput, cancellationToken ).ConfigureAwait( false );
		if ( captureUndo ) {
			this.CaptureUndo();
		}
		var inserted = this.Buffer.Replace( range, replacement.Lines );
		this.CurrentAddress = 0 == inserted.Start
			? Math.Min( this.Buffer.Count, range.Start - 1 )
			: inserted.End;
		this.FinalRecordTerminated = replacement.FinalRecordTerminated;
		this.RemoveDanglingMarks();
		this.IsModified = true;
	}

	private SubstitutionOutcome Substitute(
		EditorAddressRange range,
		string arguments,
		CancellationToken cancellationToken
	) {
		var parsed = ParseDelimitedCommand( arguments, allowFlags: true );
		var pattern = 0 == parsed.Pattern.Length
			? this.lastRegularExpression ?? throw new EditorCommandException( EditorDiagnosticCode.RegularExpression, "No previous regular expression." )
			: parsed.Pattern;
		this.lastRegularExpression = pattern;
		var replacement = parsed.Replacement ?? this.lastReplacement ?? string.Empty;
		this.lastReplacement = replacement;
		var flags = ParseSubstitutionFlags( parsed.Remainder );
		var expression = this.CompileRegularExpression( pattern, cancellationToken );
		var anyMatch = false;
		var lastChanged = range.Start;
		for ( var address = range.Start; range.End >= address; address++ ) {
			this.ThrowIfInterrupted( cancellationToken );
			var original = this.Buffer.GetLine( address ).GetText();
			var changed = ReplaceMatches(
				original,
				expression,
				replacement,
				flags,
				cancellationToken
			);
			if ( null == changed ) {
				continue;
			}
			this.Buffer.SetContent( address, Encoding.UTF8.GetBytes( changed ) );
			anyMatch = true;
			lastChanged = address;
		}
		return new SubstitutionOutcome(
			anyMatch,
			lastChanged,
			flags.PrintChanged
		);
	}

	private static string? ReplaceMatches(
		string input,
		ICompiledRegularExpression expression,
		string replacement,
		SubstitutionFlags flags,
		CancellationToken cancellationToken
	) {
		var output = new StringBuilder( input.Length );
		var searchStart = 0;
		var copyStart = 0;
		var occurrence = 0;
		var replaced = false;
		while ( input.Length >= searchStart ) {
			cancellationToken.ThrowIfCancellationRequested();
			var remainder = input[ searchStart.. ];
			var result = expression.Match( remainder, cancellationToken: cancellationToken );
			if ( !result.IsSuccess ) {
				throw new EditorCommandException( EditorDiagnosticCode.RegularExpression, result.Diagnostic?.ToString() ?? "Regular-expression matching failed." );
			}
			if ( !result.IsMatch ) {
				break;
			}
			var match = result.Match!;
			var absoluteIndex = checked( searchStart + match.Index );
			occurrence++;
			var shouldReplace = flags.Global
				? ( 0 == flags.Occurrence || occurrence >= flags.Occurrence )
				: ( 0 == flags.Occurrence ? 1 == occurrence : flags.Occurrence == occurrence );
			if ( shouldReplace ) {
				output.Append( input, copyStart, absoluteIndex - copyStart );
				AppendReplacement( output, replacement, match );
				copyStart = checked( absoluteIndex + match.Length );
				replaced = true;
				if ( !flags.Global ) {
					break;
				}
			}
			var advance = 0 == match.Length ? 1 : match.Length;
			searchStart = checked( absoluteIndex + advance );
			if ( input.Length < searchStart ) {
				break;
			}
		}
		if ( !replaced ) {
			return null;
		}
		output.Append( input, copyStart, input.Length - copyStart );
		return output.ToString();
	}

	private static void AppendReplacement(
		StringBuilder output,
		string replacement,
		RegularExpressionMatch match
	) {
		var escaped = false;
		foreach ( var character in replacement ) {
			if ( escaped ) {
				if ( ( '1' <= character ) && ( '9' >= character ) ) {
					var captureIndex = character - '1';
					if ( match.Captures.Count > captureIndex ) {
						var capture = match.Captures[ captureIndex ];
						if ( capture.Success ) {
							output.Append( capture.Value );
						}
					}
				} else if ( 'n' == character ) {
					output.Append( '\n' );
				} else {
					output.Append( character );
				}
				escaped = false;
				continue;
			}
			if ( '\\' == character ) {
				escaped = true;
			} else if ( '&' == character ) {
				output.Append( match.Value );
			} else {
				output.Append( character );
			}
		}
		if ( escaped ) {
			output.Append( '\\' );
		}
	}

	private ICompiledRegularExpression CompileRegularExpression(
		string pattern,
		CancellationToken cancellationToken
	) {
		if ( 0 == pattern.Length ) {
			pattern = this.lastRegularExpression ?? throw new EditorCommandException(
				EditorDiagnosticCode.RegularExpression,
				"No previous regular expression."
			);
		} else {
			this.lastRegularExpression = pattern;
		}
		var result = this.regularExpressionProvider.Compile(
			pattern,
			cancellationToken: cancellationToken
		);
		if ( !result.IsSuccess || null == result.Expression ) {
			throw new EditorCommandException(
				EditorDiagnosticCode.RegularExpression,
				result.Diagnostic?.ToString() ?? "Invalid regular expression."
			);
		}
		return result.Expression;
	}

	private int SearchAddress(
		string pattern,
		bool reverse,
		int startAddress,
		CancellationToken cancellationToken
	) {
		if ( 0 == this.Buffer.Count ) {
			throw new EditorParseException( "The buffer is empty." );
		}
		var expression = this.CompileRegularExpression( pattern, cancellationToken );
		for ( var offset = 1; this.Buffer.Count >= offset; offset++ ) {
			this.ThrowIfInterrupted( cancellationToken );
			var address = reverse
				? startAddress - offset
				: startAddress + offset;
			while ( 1 > address ) {
				address += this.Buffer.Count;
			}
			while ( this.Buffer.Count < address ) {
				address -= this.Buffer.Count;
			}
			var result = expression.Match( this.Buffer.GetLine( address ).GetText(), cancellationToken: cancellationToken );
			if ( result.IsSuccess && result.IsMatch ) {
				return address;
			}
			if ( !result.IsSuccess ) {
				throw new EditorParseException( result.Diagnostic?.ToString() ?? "Regular-expression matching failed." );
			}
		}
		throw new EditorParseException( "No matching line." );
	}

	private int ResolveMark(
		char mark
	) {
		if ( !this.marks.TryGetValue( mark, out var lineId ) ) {
			throw new EditorParseException( "The mark is not set." );
		}
		var address = this.Buffer.FindAddress( lineId );
		if ( 0 == address ) {
			this.marks.Remove( mark );
			throw new EditorParseException( "The marked line no longer exists." );
		}
		return address;
	}

	private int ParseDestinationAddress(
		string text,
		CancellationToken cancellationToken
	) {
		var parser = new EditorAddressParser(
			text,
			this.CurrentAddress,
			this.Buffer.Count,
			this.ResolveMark,
			( pattern, reverse, startAddress ) => this.SearchAddress(
				pattern,
				reverse,
				startAddress,
				cancellationToken
			)
		);
		ParsedEditorRange parsed;
		try {
			parsed = parser.ParseRange();
		} catch ( EditorParseException exception ) {
			throw new EditorCommandException( EditorDiagnosticCode.InvalidAddress, exception.Message );
		}
		if ( !parsed.IsSpecified || parsed.Range.Start != parsed.Range.End ) {
			throw new EditorCommandException( EditorDiagnosticCode.InvalidAddress, "A destination address is required." );
		}
		if ( text.AsSpan( parser.Position ).Trim().Length != 0 ) {
			throw new EditorCommandException( EditorDiagnosticCode.InvalidAddress, "Invalid text after destination address." );
		}
		return this.ValidateSingleAddress( parsed.Range.Start, allowZero: true );
	}

	private EditorAddressRange ResolveRange(
		ParsedEditorRange parsed,
		int defaultStart,
		int defaultEnd
	) {
		var range = parsed.IsSpecified
			? parsed.Range
			: new EditorAddressRange( defaultStart, defaultEnd );
		return this.ValidateRange( range );
	}

	private int ResolveSingleAddress(
		ParsedEditorRange parsed,
		int defaultAddress,
		bool allowZero
	) {
		if ( parsed.IsSpecified && parsed.Range.Start != parsed.Range.End ) {
			throw new EditorCommandException( EditorDiagnosticCode.InvalidAddress, "Only one address is permitted." );
		}
		return this.ValidateSingleAddress(
			parsed.IsSpecified ? parsed.Range.End : defaultAddress,
			allowZero
		);
	}

	private EditorAddressRange ValidateRange(
		EditorAddressRange range
	) {
		if (
			( 1 > range.Start )
			|| ( range.Start > range.End )
			|| ( this.Buffer.Count < range.End )
		) {
			throw new EditorCommandException( EditorDiagnosticCode.InvalidAddress, "Invalid address range." );
		}
		return range;
	}

	private int ValidateSingleAddress(
		int address,
		bool allowZero
	) {
		var minimum = allowZero ? 0 : 1;
		if ( ( minimum > address ) || ( this.Buffer.Count < address ) ) {
			throw new EditorCommandException( EditorDiagnosticCode.InvalidAddress, "Invalid address." );
		}
		return address;
	}

	private static string ParseFileNameArgument(
		string arguments
	) {
		// Leading whitespace separates the command from its filename. Trailing
		// whitespace is filename data and must remain visible to security policy.
		return arguments.TrimStart();
	}

	private string ResolveFileName(
		string arguments,
		bool requireName
	) {
		var candidate = ParseFileNameArgument( arguments );
		if ( 0 == candidate.Length ) {
			if ( null != this.RememberedFileName ) {
				return this.RememberedFileName;
			}
			if ( requireName ) {
				throw new EditorCommandException( EditorDiagnosticCode.FileName, "A filename is required." );
			}
		}
		if ( this.SecurityPolicy.IsRestricted && !this.SecurityPolicy.AllowPathnames ) {
			ValidateRestrictedFileName( candidate );
		}
		return candidate;
	}

	private void SetRememberedFileName(
		string path
	) {
		if ( !this.SecurityPolicy.AllowRememberedFileName ) {
			return;
		}
		if ( this.SecurityPolicy.IsRestricted ) {
			ValidateRestrictedFileName( path );
		}
		this.RememberedFileName = path;
	}

	private static void ValidateRestrictedFileName(
		string candidate
	) {
		if ( !IsRestrictedFileName( candidate ) ) {
			throw new EditorCommandException(
				EditorDiagnosticCode.RestrictedOperation,
				"Restricted mode permits only a simple filename in the captured working directory."
			);
		}
	}

	private static bool IsRestrictedFileName(
		string candidate
	) => EditorRestrictedPath.IsSimpleFileName( candidate );

	private void ValidateRestrictedDispatch(
		char command,
		string arguments
	) {
		if ( !this.SecurityPolicy.IsRestricted ) {
			return;
		}
		if ( '!' == command ) {
			throw new EditorCommandException(
				EditorDiagnosticCode.RestrictedOperation,
				"Shell commands are disabled by the editor security profile."
			);
		}
		if ( command is 'e' or 'E' or 'r' or 'w' or 'W' or 'f' ) {
			var candidate = ParseFileNameArgument( arguments );
			if ( 0 < candidate.Length ) {
				ValidateRestrictedFileName( candidate );
			}
		}
		if ( command is 'g' or 'v' ) {
			var parsed = ParseDelimitedCommand( arguments, allowFlags: false );
			var nested = string.IsNullOrWhiteSpace( parsed.Remainder ) ? "p" : parsed.Remainder;
			this.ValidateRestrictedCommandText( nested );
		}
	}

	private void ValidateRestrictedCommandText(
		string commandText
	) {
		var position = FindCommandIndex( commandText );
		if ( 0 > position ) {
			return;
		}
		var command = commandText[ position++ ];
		this.ValidateRestrictedDispatch( command, commandText[ position.. ] );
	}

	private static int FindCommandIndex(
		string text
	) {
		var escaped = false;
		var delimiter = '\0';
		var afterMark = false;
		for ( var index = 0; text.Length > index; index++ ) {
			var character = text[ index ];
			if ( '\0' != delimiter ) {
				if ( escaped ) {
					escaped = false;
					continue;
				}
				if ( '\\' == character ) {
					escaped = true;
					continue;
				}
				if ( delimiter == character ) {
					delimiter = '\0';
				}
				continue;
			}
			if ( afterMark ) {
				afterMark = false;
				continue;
			}
			if ( '\'' == character ) {
				afterMark = true;
				continue;
			}
			if ( character is '/' or '?' ) {
				delimiter = character;
				continue;
			}
			if ( char.IsLetter( character ) || character is '!' or '=' or '#' ) {
				return index;
			}
		}
		return -1;
	}

	private void CaptureUndo() {
		this.undoSnapshot = new EditorSnapshot(
			this.Buffer.CaptureSnapshot(),
			this.CurrentAddress,
			this.IsModified,
			this.RememberedFileName,
			this.FinalRecordTerminated,
			this.marks.ToDictionary( pair => pair.Key, pair => pair.Value ),
			this.cutBuffer.Select( item => new ReadOnlyMemory<byte>( item.ToArray() ) ).ToArray()
		);
	}


	private void Undo() {
		var snapshot = this.undoSnapshot ?? throw new EditorCommandException(
			EditorDiagnosticCode.InvalidCommand,
			"Nothing to undo."
		);
		var current = new EditorSnapshot(
			this.Buffer.CaptureSnapshot(),
			this.CurrentAddress,
			this.IsModified,
			this.RememberedFileName,
			this.FinalRecordTerminated,
			this.marks.ToDictionary( pair => pair.Key, pair => pair.Value ),
			this.cutBuffer.Select( item => new ReadOnlyMemory<byte>( item.ToArray() ) ).ToArray()
		);
		this.RestoreSnapshot( snapshot );
		this.undoSnapshot = current;
	}

	private void RestoreSnapshot(
		EditorSnapshot snapshot
	) {
		this.Buffer.RestoreSnapshot( snapshot.Buffer );
		this.CurrentAddress = snapshot.CurrentAddress;
		this.IsModified = snapshot.IsModified;
		this.RememberedFileName = snapshot.RememberedFileName;
		this.FinalRecordTerminated = snapshot.FinalRecordTerminated;
		this.marks.Clear();
		foreach ( var pair in snapshot.Marks ) {
			this.marks[ pair.Key ] = pair.Value;
		}
		this.cutBuffer.Clear();
		this.cutBuffer.AddRange( snapshot.CutBuffer.Select( item => new ReadOnlyMemory<byte>( item.ToArray() ) ) );
	}

	private void RemoveDanglingMarks() {
		foreach ( var mark in this.marks.Keys.ToArray() ) {
			if ( 0 == this.Buffer.FindAddress( this.marks[ mark ] ) ) {
				this.marks.Remove( mark );
			}
		}
	}

	private void ThrowIfInterrupted(
		CancellationToken cancellationToken
	) {
		cancellationToken.ThrowIfCancellationRequested();
		if ( EditorSignal.None != this.pendingSignal ) {
			throw new OperationCanceledException( cancellationToken );
		}
	}

	private async ValueTask PrintRangeAsync(
		EditorAddressRange range,
		Stream output,
		PrintMode mode,
		CancellationToken cancellationToken
	) {
		this.ValidateRange( range );
		for ( var address = range.Start; range.End >= address; address++ ) {
			this.ThrowIfInterrupted( cancellationToken );
			var content = this.Buffer.GetLine( address ).Content;
			if ( PrintMode.Numbered == mode ) {
				await output.WriteAsync(
					Encoding.UTF8.GetBytes( string.Concat( address.ToString( CultureInfo.InvariantCulture ), "\t" ) ),
					cancellationToken
				).ConfigureAwait( false );
			}
			if ( PrintMode.List == mode ) {
				await output.WriteAsync(
					Encoding.UTF8.GetBytes( RenderListLine( content.Span ) ),
					cancellationToken
				).ConfigureAwait( false );
				await output.WriteAsync( new byte[] { (byte)'$', (byte)'\n' }, cancellationToken ).ConfigureAwait( false );
			} else {
				await output.WriteAsync( content, cancellationToken ).ConfigureAwait( false );
				await output.WriteAsync( LineFeed, cancellationToken ).ConfigureAwait( false );
			}
			this.CurrentAddress = address;
		}
	}

	private static string RenderListLine(
		ReadOnlySpan<byte> content
	) {
		var builder = new StringBuilder();
		foreach ( var value in content ) {
			switch ( value ) {
				case (byte)'\\':
					builder.Append( "\\\\" );
					break;
				case (byte)'\t':
					builder.Append( "\\t" );
					break;
				case (byte)'\r':
					builder.Append( "\\r" );
					break;
				case < 0x20:
				case 0x7f:
					builder.Append( "\\x" );
					builder.Append( value.ToString( "x2", CultureInfo.InvariantCulture ) );
					break;
				default:
					builder.Append( (char)value );
					break;
			}
		}
		return builder.ToString();
	}

	private static async ValueTask WriteTextLineAsync(
		Stream output,
		string text,
		CancellationToken cancellationToken
	) {
		await output.WriteAsync( Encoding.UTF8.GetBytes( text ), cancellationToken ).ConfigureAwait( false );
		await output.WriteAsync( LineFeed, cancellationToken ).ConfigureAwait( false );
	}

	private static async ValueTask<IReadOnlyList<ReadOnlyMemory<byte>>> ReadScriptAsync(
		Stream stream,
		CancellationToken cancellationToken
	) {
		using var reader = new ByteRecordReader( stream );
		var records = new List<ReadOnlyMemory<byte>>();
		while ( true ) {
			var record = await reader.ReadAsync( cancellationToken ).ConfigureAwait( false );
			if ( null == record ) {
				break;
			}
			var content = record.Content;
			if ( record.IsTerminated && !content.IsEmpty && (byte)'\r' == content.Span[ ^1 ] ) {
				content = content[ ..^1 ];
			}
			records.Add( content.ToArray() );
		}
		return records.AsReadOnly();
	}

	private static DataBlock ReadDataBlock(
		IReadOnlyList<ReadOnlyMemory<byte>> scriptRecords,
		int commandIndex
	) {
		var lines = new List<ReadOnlyMemory<byte>>();
		for ( var index = commandIndex + 1; scriptRecords.Count > index; index++ ) {
			var record = scriptRecords[ index ];
			if ( record.Span.SequenceEqual( new byte[] { (byte)'.' } ) ) {
				return new DataBlock( lines.AsReadOnly(), index );
			}
			lines.Add( record.ToArray() );
		}
		throw new EditorCommandException(
			EditorDiagnosticCode.UnexpectedEndOfInput,
			"The command data block is not terminated by a single period."
		);
	}

	private static DelimitedCommand ParseDelimitedCommand(
		string text,
		bool allowFlags
	) {
		var position = 0;
		while ( ( text.Length > position ) && char.IsWhiteSpace( text[ position ] ) ) {
			position++;
		}
		if ( text.Length <= position ) {
			throw new EditorCommandException( EditorDiagnosticCode.InvalidCommand, "A delimiter is required." );
		}
		var delimiter = text[ position++ ];
		if ( char.IsLetterOrDigit( delimiter ) || '\\' == delimiter || char.IsWhiteSpace( delimiter ) ) {
			throw new EditorCommandException( EditorDiagnosticCode.InvalidCommand, "Invalid command delimiter." );
		}
		var pattern = ReadDelimitedPart( text, ref position, delimiter );
		if ( !allowFlags ) {
			return new DelimitedCommand( pattern, null, text[ position.. ].TrimStart() );
		}
		var replacement = ReadDelimitedPart( text, ref position, delimiter );
		return new DelimitedCommand( pattern, replacement, text[ position.. ].Trim() );
	}

	private static string ReadDelimitedPart(
		string text,
		ref int position,
		char delimiter
	) {
		var builder = new StringBuilder();
		var escaped = false;
		while ( text.Length > position ) {
			var character = text[ position++ ];
			if ( escaped ) {
				if ( delimiter != character ) {
					builder.Append( '\\' );
				}
				builder.Append( character );
				escaped = false;
				continue;
			}
			if ( '\\' == character ) {
				escaped = true;
				continue;
			}
			if ( delimiter == character ) {
				return builder.ToString();
			}
			builder.Append( character );
		}
		throw new EditorCommandException( EditorDiagnosticCode.InvalidCommand, "Unterminated delimited command." );
	}

	private static SubstitutionFlags ParseSubstitutionFlags(
		string text
	) {
		var global = false;
		var print = false;
		var occurrence = 0;
		foreach ( var character in text ) {
			if ( 'g' == character ) {
				global = true;
			} else if ( 'p' == character ) {
				print = true;
			} else if ( char.IsAsciiDigit( character ) ) {
				var digit = character - '0';
				if ( occurrence > ( int.MaxValue - digit ) / 10 ) {
					throw new EditorCommandException( EditorDiagnosticCode.InvalidCommand, "The substitution occurrence is too large." );
				}
				occurrence = occurrence * 10 + digit;
			} else if ( !char.IsWhiteSpace( character ) ) {
				throw new EditorCommandException( EditorDiagnosticCode.InvalidCommand, "Invalid substitution flag." );
			}
		}
		return new SubstitutionFlags( global, occurrence, print );
	}

	private static async ValueTask<EditorFileReadResult> ReadRecordsFromMemoryAsync(
		ReadOnlyMemory<byte> content,
		CancellationToken cancellationToken
	) {
		await using var stream = new MemoryStream( content.ToArray(), writable: false );
		using var reader = new ByteRecordReader( stream );
		var lines = new List<ReadOnlyMemory<byte>>();
		var terminated = true;
		while ( true ) {
			var record = await reader.ReadAsync( cancellationToken ).ConfigureAwait( false );
			if ( null == record ) {
				break;
			}
			lines.Add( record.Content.ToArray() );
			terminated = record.IsTerminated;
		}
		return new EditorFileReadResult( lines.AsReadOnly(), 0 == lines.Count || terminated, content.Length );
	}

	private sealed record EditorSnapshot(
		BufferSnapshot Buffer,
		int CurrentAddress,
		bool IsModified,
		string? RememberedFileName,
		bool FinalRecordTerminated,
		IReadOnlyDictionary<char, long> Marks,
		IReadOnlyList<ReadOnlyMemory<byte>> CutBuffer
	);

	private readonly record struct CommandOutcome(
		int LastConsumedRecord,
		bool QuitRequested
	);

	private readonly record struct DataBlock(
		IReadOnlyList<ReadOnlyMemory<byte>> Lines,
		int LastConsumedRecord
	);

	private readonly record struct DelimitedCommand(
		string Pattern,
		string? Replacement,
		string Remainder
	);

	private readonly record struct SubstitutionFlags(
		bool Global,
		int Occurrence,
		bool PrintChanged
	);

	private readonly record struct SubstitutionOutcome(
		bool IsMatch,
		int LastChangedAddress,
		bool PrintChanged
	);

	private enum PrintMode {
		Plain,
		Numbered,
		List
	}
}

/// <summary>Represents a controlled Ed command failure.</summary>
internal sealed class EditorCommandException : Exception {
	/// <summary>Initializes a controlled command exception.</summary>
	/// <param name="code">The stable diagnostic category.</param>
	/// <param name="message">The controlled command diagnostic.</param>
	internal EditorCommandException(
		EditorDiagnosticCode code,
		string message
	) : base( message ) {
		this.Code = code;
	}

	/// <summary>Gets the stable diagnostic category.</summary>
	internal EditorDiagnosticCode Code {
		get;
	}
}
