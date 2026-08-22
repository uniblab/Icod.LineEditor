namespace Icod.LineEditor.Sed;

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Icod.CommandFramework.Records;
using Icod.CommandFramework.Text;

// Responsibility: byte-preserving input, text mapping, and explicit record framing.
public static partial class Command {

	private const int InvalidByteCharacterBase = 0xDC00;

	private enum SedRecordSeparatorKind {
		LineFeed,
		Null
	}

	private sealed class SedInputSourceIdentity {

		public int Index {
			get;
		}

		public bool IsStandardInput {
			get;
		}

		public string Name {
			get;
		}

		public SedInputSourceIdentity(
			int index,
			string name,
			bool isStandardInput
		) {
			this.Index = index;
			this.Name = name ?? throw new ArgumentNullException( nameof( name ) );
			this.IsStandardInput = isStandardInput;
		}

	}

	private sealed class SedInputRecord {

		private readonly long[] myTextBoundaryByteOffsets;

		public long AggregateRecordNumber {
			get;
		}

		public ReadOnlyMemory<byte> Bytes {
			get;
		}

		public bool IsTerminated {
			get;
		}

		public SedRecordSeparatorKind SeparatorKind {
			get;
		}

		public SedInputSourceIdentity Source {
			get;
		}

		public long SourceRecordNumber {
			get;
		}

		public string Text {
			get;
		}

		public SedInputRecord(
			ReadOnlyMemory<byte> bytes,
			string text,
			long[] textBoundaryByteOffsets,
			SedInputSourceIdentity source,
			long aggregateRecordNumber,
			long sourceRecordNumber,
			SedRecordSeparatorKind separatorKind,
			bool isTerminated
		) {
			this.Bytes = bytes;
			this.Text = text ?? throw new ArgumentNullException( nameof( text ) );
			this.myTextBoundaryByteOffsets = textBoundaryByteOffsets
				?? throw new ArgumentNullException( nameof( textBoundaryByteOffsets ) )
			;
			this.Source = source ?? throw new ArgumentNullException( nameof( source ) );
			this.AggregateRecordNumber = aggregateRecordNumber;
			this.SourceRecordNumber = sourceRecordNumber;
			this.SeparatorKind = separatorKind;
			this.IsTerminated = isTerminated;
		}

		public bool TryGetByteOffset(
			int textBoundary,
			out long byteOffset
		) {
			if (
				textBoundary < 0
				|| this.myTextBoundaryByteOffsets.Length <= textBoundary
			) {
				byteOffset = -1;
				return false;
			}
			byteOffset = this.myTextBoundaryByteOffsets[ textBoundary ];
			return 0 <= byteOffset;
		}

	}

	private sealed class SedTextCodec {

		private readonly bool myByteLocale;

		public ITextLocaleProvider Locale {
			get;
		}

		private SedTextCodec(
			ITextLocaleProvider locale
		) {
			this.Locale = locale ?? throw new ArgumentNullException( nameof( locale ) );
			this.myByteLocale = TextDecodingMode.Bytes == locale.DecodingMode;
		}

		public static SedTextCodec CreateCurrent() {
			return new SedTextCodec(
				TextLocaleEnvironment.Resolve()
			);
		}

		public SedInputRecord DecodeRecord(
			ByteRecord record,
			SedInputSourceIdentity source,
			long aggregateRecordNumber,
			long sourceRecordNumber,
			SedRecordSeparatorKind separatorKind
		) {
			ArgumentNullException.ThrowIfNull( record );
			var decoded = this.Decode(
				record.Content.Span
			);
			return new SedInputRecord(
				record.Content,
				decoded.Text,
				decoded.BoundaryOffsets,
				source,
				aggregateRecordNumber,
				sourceRecordNumber,
				separatorKind,
				record.IsTerminated
			);
		}

		public byte[] Encode(
			string value
		) {
			ArgumentNullException.ThrowIfNull( value );
			var output = new ArrayBufferWriter<byte>( Math.Max( 1, value.Length ) );
			for ( var index = 0; index < value.Length; index++ ) {
				var character = value[ index ];
				if (
					!this.myByteLocale
					&& InvalidByteCharacterBase <= character
					&& character <= InvalidByteCharacterBase + byte.MaxValue
				) {
					output.GetSpan( 1 )[ 0 ] = (byte)( character - InvalidByteCharacterBase );
					output.Advance( 1 );
					continue;
				}

				Rune rune;
				if (
					char.IsHighSurrogate( character )
					&& index + 1 < value.Length
					&& Rune.TryCreate( character, value[ index + 1 ], out rune )
				) {
					index++;
				} else if ( char.IsSurrogate( character ) ) {
					rune = Rune.ReplacementChar;
				} else {
					rune = new Rune( character );
				}

				if ( this.myByteLocale && rune.Value <= byte.MaxValue ) {
					output.GetSpan( 1 )[ 0 ] = (byte)rune.Value;
					output.Advance( 1 );
					continue;
				}
				var destination = output.GetSpan( 4 );
				var count = rune.EncodeToUtf8( destination );
				output.Advance( count );
			}
			return output.WrittenSpan.ToArray();
		}

		private (string Text, long[] BoundaryOffsets) Decode(
			ReadOnlySpan<byte> bytes
		) {
			if ( this.myByteLocale ) {
				var characters = new char[ bytes.Length ];
				var offsets = new long[ bytes.Length + 1 ];
				for ( var index = 0; index < bytes.Length; index++ ) {
					characters[ index ] = (char)bytes[ index ];
					offsets[ index ] = index;
				}
				offsets[ bytes.Length ] = bytes.Length;
				return (
					new string( characters ),
					offsets
				);
			}

			var text = new StringBuilder( bytes.Length );
			var boundaryOffsets = new List<long>( bytes.Length + 1 ) { 0 };
			var byteIndex = 0;
			while ( byteIndex < bytes.Length ) {
				var status = Rune.DecodeFromUtf8(
					bytes.Slice( byteIndex ),
					out var rune,
					out var consumed
				);
				if (
					OperationStatus.Done != status
					|| consumed <= 0
				) {
					text.Append(
						(char)( InvalidByteCharacterBase + bytes[ byteIndex ] )
					);
					byteIndex++;
					boundaryOffsets.Add( byteIndex );
					continue;
				}

				var runeText = rune.ToString();
				text.Append( runeText );
				for ( var characterIndex = 1; characterIndex < runeText.Length; characterIndex++ ) {
					boundaryOffsets.Add( -1 );
				}
				byteIndex += consumed;
				boundaryOffsets.Add( byteIndex );
			}
			return (
				text.ToString(),
				boundaryOffsets.ToArray()
			);
		}

	}

	private sealed class SourceSpec {

		public string Path {
			get;
		}

		public SourceSpec(
			string path
		) {
			this.Path = path;
		}

	}

	private sealed class AsyncRecordReader : IDisposable {

		private readonly SedTextCodec myCodec;
		private readonly bool myOwnsStream;
		private readonly ByteRecordReader myReader;
		private readonly SedRecordSeparatorKind mySeparatorKind;
		private readonly SedInputSourceIdentity mySource;
		private readonly Stream myStream;
		private long mySourceRecordNumber;

		public AsyncRecordReader(
			Stream stream,
			bool nullData,
			bool ownsStream,
			SedTextCodec codec,
			SedInputSourceIdentity source
		) {
			this.myStream = stream ?? throw new ArgumentNullException( nameof( stream ) );
			this.myOwnsStream = ownsStream;
			this.myCodec = codec ?? throw new ArgumentNullException( nameof( codec ) );
			this.mySource = source ?? throw new ArgumentNullException( nameof( source ) );
			this.mySeparatorKind = nullData
				? SedRecordSeparatorKind.Null
				: SedRecordSeparatorKind.LineFeed
			;
			this.myReader = new ByteRecordReader(
				stream,
				nullData
					? RecordSeparator.Null
					: RecordSeparator.LineFeed,
				bufferSize: 8192
			);
		}

		public async Task<SedInputRecord?> ReadAsync(
			long aggregateRecordNumber,
			CancellationToken cancellationToken
		) {
			var record = await this.myReader.ReadAsync(
				cancellationToken
			).ConfigureAwait( false );
			if ( null == record ) {
				return null;
			}
			this.mySourceRecordNumber++;
			return this.myCodec.DecodeRecord(
				record,
				this.mySource,
				aggregateRecordNumber,
				this.mySourceRecordNumber,
				this.mySeparatorKind
			);
		}

		public void Dispose() {
			this.myReader.Dispose();
			if ( this.myOwnsStream ) {
				this.myStream.Dispose();
			}
		}

	}

	private sealed class InputSequence : IDisposable {

		private long myAggregateRecordNumber;
		private AsyncRecordReader? myCurrentReader;
		private bool myInitialized;
		private SedInputRecord? myLookahead;
		private bool myLookaheadAvailable;
		private readonly bool myNullData;
		private int mySourceIndex = -1;
		private readonly IReadOnlyList<SourceSpec> mySources;
		private readonly Stream myStandardInput;
		private readonly SedTextCodec myTextCodec;

		public SedInputRecord Current {
			get;
			private set;
		} = null!;

		public bool IsLast {
			get;
			private set;
		}

		public long LineNumber => this.Current.AggregateRecordNumber;

		public InputSequence(
			IReadOnlyList<SourceSpec> sources,
			Stream standardInput,
			bool nullData,
			SedTextCodec textCodec
		) {
			this.mySources = sources ?? throw new ArgumentNullException( nameof( sources ) );
			this.myStandardInput = standardInput ?? throw new ArgumentNullException( nameof( standardInput ) );
			this.myNullData = nullData;
			this.myTextCodec = textCodec ?? throw new ArgumentNullException( nameof( textCodec ) );
		}

		public async Task<bool> MoveNextAsync(
			CancellationToken cancellationToken
		) {
			if ( !this.myInitialized ) {
				this.myInitialized = true;
				this.myLookahead = await this.ReadRawAsync( cancellationToken ).ConfigureAwait( false );
				this.myLookaheadAvailable = null != this.myLookahead;
			}
			if ( !this.myLookaheadAvailable ) {
				return false;
			}
			this.Current = this.myLookahead!;
			this.myLookahead = await this.ReadRawAsync( cancellationToken ).ConfigureAwait( false );
			this.myLookaheadAvailable = null != this.myLookahead;
			this.IsLast = !this.myLookaheadAvailable;
			return true;
		}

		private async Task<SedInputRecord?> ReadRawAsync(
			CancellationToken cancellationToken
		) {
			while ( true ) {
				if ( null == this.myCurrentReader && !this.OpenNextSource() ) {
					return null;
				}
				var value = await this.myCurrentReader!.ReadAsync(
					this.myAggregateRecordNumber + 1,
					cancellationToken
				).ConfigureAwait( false );
				if ( null != value ) {
					this.myAggregateRecordNumber++;
					return value;
				}
				this.CloseCurrentReader();
			}
		}

		private bool OpenNextSource() {
			this.mySourceIndex++;
			if ( this.mySources.Count <= this.mySourceIndex ) {
				return false;
			}
			var source = this.mySources[ this.mySourceIndex ];
			var isStandardInput = "-" == source.Path;
			var identity = new SedInputSourceIdentity(
				this.mySourceIndex,
				source.Path,
				isStandardInput
			);
			var stream = isStandardInput
				? this.myStandardInput
				: new FileStream(
					source.Path,
					FileMode.Open,
					FileAccess.Read,
					FileShare.Read,
					8192,
					useAsync: true
				)
			;
			this.myCurrentReader = new AsyncRecordReader(
				stream,
				this.myNullData,
				ownsStream: !isStandardInput,
				this.myTextCodec,
				identity
			);
			return true;
		}

		private void CloseCurrentReader() {
			this.myCurrentReader?.Dispose();
			this.myCurrentReader = null;
		}

		public void Dispose() {
			this.CloseCurrentReader();
		}

	}

	private sealed class SedOutputWriter {

		private readonly SedTextCodec myCodec;
		private bool myPendingRecordSeparator;
		private readonly DelimitedByteRecordWriter myWriter;

		public bool AutoFlush {
			get;
			set;
		}

		public SedOutputWriter(
			Stream stream,
			SedTextCodec codec,
			bool nullData
		) {
			this.myCodec = codec ?? throw new ArgumentNullException( nameof( codec ) );
			this.myWriter = new DelimitedByteRecordWriter(
				stream ?? throw new ArgumentNullException( nameof( stream ) ),
				nullData ? RecordSeparator.Null : RecordSeparator.LineFeed
			);
		}

		public async Task BeginOutputAsync(
			CancellationToken cancellationToken
		) {
			if ( this.myPendingRecordSeparator ) {
				await this.myWriter.WriteSeparatorAsync( cancellationToken ).ConfigureAwait( false );
				this.myPendingRecordSeparator = false;
				if ( this.AutoFlush ) {
					await this.myWriter.FlushAsync( cancellationToken ).ConfigureAwait( false );
				}
			}
		}

		public async Task WriteRecordAsync(
			string value,
			bool terminate,
			CancellationToken cancellationToken
		) {
			await this.BeginOutputAsync( cancellationToken ).ConfigureAwait( false );
			var bytes = this.myCodec.Encode( value );
			await this.myWriter.WriteRecordAsync(
				bytes,
				terminate,
				cancellationToken
			).ConfigureAwait( false );
			this.myPendingRecordSeparator = !terminate;
			if ( this.AutoFlush ) {
				await this.myWriter.FlushAsync( cancellationToken ).ConfigureAwait( false );
			}
		}

		public async Task WriteRawTextAsync(
			string value,
			CancellationToken cancellationToken
		) {
			await this.BeginOutputAsync( cancellationToken ).ConfigureAwait( false );
			var bytes = this.myCodec.Encode( value );
			await this.myWriter.WriteContentAsync(
				bytes,
				cancellationToken
			).ConfigureAwait( false );
			if ( this.AutoFlush ) {
				await this.myWriter.FlushAsync( cancellationToken ).ConfigureAwait( false );
			}
		}

		public Task FlushAsync(
			CancellationToken cancellationToken
		) {
			return this.myWriter.FlushAsync( cancellationToken ).AsTask();
		}

	}

	private sealed class SedOutputTextWriter : TextWriter {

		private readonly SedOutputWriter myWriter;

		public override Encoding Encoding => Encoding.UTF8;

		public SedOutputTextWriter(
			SedOutputWriter writer
		) {
			this.myWriter = writer ?? throw new ArgumentNullException( nameof( writer ) );
		}

		public override void Write(
			char value
		) {
			this.Write( value.ToString() );
		}

		public override void Write(
			char[] buffer,
			int index,
			int count
		) {
			ArgumentNullException.ThrowIfNull( buffer );
			this.Write( new string( buffer, index, count ) );
		}

		public override void Write(
			string? value
		) {
			if ( null != value ) {
				this.myWriter.WriteRawTextAsync( value, CancellationToken.None ).GetAwaiter().GetResult();
			}
		}

		public override Task WriteAsync(
			string? value
		) {
			return null == value
				? Task.CompletedTask
				: this.myWriter.WriteRawTextAsync( value, CancellationToken.None )
			;
		}

		public override Task WriteAsync(
			ReadOnlyMemory<char> buffer,
			CancellationToken cancellationToken = default
		) {
			return this.myWriter.WriteRawTextAsync( buffer.ToString(), cancellationToken );
		}

		public override Task FlushAsync() {
			return this.myWriter.FlushAsync( CancellationToken.None );
		}

		public override Task FlushAsync(
			CancellationToken cancellationToken
		) {
			return this.myWriter.FlushAsync( cancellationToken );
		}

	}

	private sealed class TextReaderInputStream : Stream {

		private readonly byte[] myByteBuffer = new byte[ 16384 ];
		private int myByteCount;
		private int myByteOffset;
		private readonly char[] myCharacterBuffer = new char[ 4096 ];
		private readonly Encoder myEncoder = new UTF8Encoding(
			encoderShouldEmitUTF8Identifier: false
		).GetEncoder();
		private bool myEndOfInput;
		private readonly TextReader myReader;

		public TextReaderInputStream(
			TextReader reader
		) {
			this.myReader = reader ?? throw new ArgumentNullException( nameof( reader ) );
		}

		public override bool CanRead => true;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => throw new NotSupportedException();
		public override long Position {
			get => throw new NotSupportedException();
			set => throw new NotSupportedException();
		}
		public override void Flush() {
		}
		public override int Read( byte[] buffer, int offset, int count ) => this.ReadAsync(
			buffer.AsMemory( offset, count ),
			CancellationToken.None
		).AsTask().GetAwaiter().GetResult();
		public override async ValueTask<int> ReadAsync(
			Memory<byte> buffer,
			CancellationToken cancellationToken = default
		) {
			if ( buffer.IsEmpty ) {
				return 0;
			}
			while ( this.myByteOffset >= this.myByteCount ) {
				if ( this.myEndOfInput ) {
					return 0;
				}
				var characterCount = await this.myReader.ReadAsync(
					this.myCharacterBuffer.AsMemory(),
					cancellationToken
				).ConfigureAwait( false );
				this.myEndOfInput = 0 == characterCount;
				this.myEncoder.Convert(
					this.myCharacterBuffer.AsSpan( 0, characterCount ),
					this.myByteBuffer,
					flush: this.myEndOfInput,
					out _,
					out this.myByteCount,
					out _
				);
				this.myByteOffset = 0;
			}
			var count = Math.Min( buffer.Length, this.myByteCount - this.myByteOffset );
			this.myByteBuffer.AsMemory( this.myByteOffset, count ).CopyTo( buffer );
			this.myByteOffset += count;
			return count;
		}
		public override long Seek( long offset, SeekOrigin origin ) => throw new NotSupportedException();
		public override void SetLength( long value ) => throw new NotSupportedException();
		public override void Write( byte[] buffer, int offset, int count ) => throw new NotSupportedException();

	}

	private sealed class TextWriterOutputStream : Stream {

		private readonly Decoder myDecoder = new UTF8Encoding(
			encoderShouldEmitUTF8Identifier: false
		).GetDecoder();
		private readonly TextWriter myWriter;

		public TextWriterOutputStream(
			TextWriter writer
		) {
			this.myWriter = writer ?? throw new ArgumentNullException( nameof( writer ) );
		}

		public override bool CanRead => false;
		public override bool CanSeek => false;
		public override bool CanWrite => true;
		public override long Length => throw new NotSupportedException();
		public override long Position {
			get => throw new NotSupportedException();
			set => throw new NotSupportedException();
		}
		public override void Flush() {
			this.FlushDecoder( flush: true );
			this.myWriter.Flush();
		}
		public override async Task FlushAsync( CancellationToken cancellationToken ) {
			await this.FlushDecoderAsync( flush: true, cancellationToken ).ConfigureAwait( false );
			await this.myWriter.FlushAsync( cancellationToken ).ConfigureAwait( false );
		}
		public override int Read( byte[] buffer, int offset, int count ) => throw new NotSupportedException();
		public override long Seek( long offset, SeekOrigin origin ) => throw new NotSupportedException();
		public override void SetLength( long value ) => throw new NotSupportedException();
		public override void Write( byte[] buffer, int offset, int count ) {
			ArgumentNullException.ThrowIfNull( buffer );
			this.WriteDecoded( buffer.AsSpan( offset, count ), flush: false );
		}
		public override async ValueTask WriteAsync(
			ReadOnlyMemory<byte> buffer,
			CancellationToken cancellationToken = default
		) {
			await this.WriteDecodedAsync( buffer, flush: false, cancellationToken ).ConfigureAwait( false );
		}

		private void FlushDecoder(
			bool flush
		) {
			this.WriteDecoded( ReadOnlySpan<byte>.Empty, flush );
		}

		private Task FlushDecoderAsync(
			bool flush,
			CancellationToken cancellationToken
		) {
			return this.WriteDecodedAsync( ReadOnlyMemory<byte>.Empty, flush, cancellationToken ).AsTask();
		}

		private void WriteDecoded(
			ReadOnlySpan<byte> bytes,
			bool flush
		) {
			var characters = new char[ Math.Max( 1, Encoding.UTF8.GetMaxCharCount( bytes.Length ) ) ];
			this.myDecoder.Convert(
				bytes,
				characters,
				flush,
				out _,
				out var charactersUsed,
				out _
			);
			if ( 0 < charactersUsed ) {
				this.myWriter.Write( characters, 0, charactersUsed );
			}
		}

		private async ValueTask WriteDecodedAsync(
			ReadOnlyMemory<byte> bytes,
			bool flush,
			CancellationToken cancellationToken
		) {
			cancellationToken.ThrowIfCancellationRequested();
			var characters = new char[ Math.Max( 1, Encoding.UTF8.GetMaxCharCount( bytes.Length ) ) ];
			this.myDecoder.Convert(
				bytes.Span,
				characters,
				flush,
				out _,
				out var charactersUsed,
				out _
			);
			if ( 0 < charactersUsed ) {
				await this.myWriter.WriteAsync(
					characters.AsMemory( 0, charactersUsed ),
					cancellationToken
				).ConfigureAwait( false );
			}
		}

	}

	private static Task WriteRecordAsync(
		SedOutputWriter writer,
		string value,
		bool terminate,
		CancellationToken cancellationToken
	) {
		return writer.WriteRecordAsync(
			value,
			terminate,
			cancellationToken
		);
	}

}
