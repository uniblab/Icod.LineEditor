namespace Icod.LineEditor.Ed;

/// <summary>
/// Stores mutable editor lines in bounded segments while preserving stable line identities.
/// </summary>
public sealed class EditorBuffer {
	private const int MaximumSegmentSize = 256;
	private const int MinimumSegmentSize = MaximumSegmentSize / 4;
	private readonly List<List<EditorLine>> segments = new();
	private long nextLineId = 1;
	private int count;

	/// <summary>Gets the number of lines in the buffer.</summary>
	public int Count => this.count;

	/// <summary>Gets the line at a one-based address.</summary>
	/// <param name="address">The one-based line address.</param>
	/// <returns>The addressed line.</returns>
	public EditorLine GetLine(
		int address
	) {
		var location = this.LocateExisting( address );
		return this.segments[ location.Segment ][ location.Offset ];
	}

	/// <summary>Gets a stable snapshot of all lines in address order.</summary>
	/// <returns>The current lines.</returns>
	public IReadOnlyList<EditorLine> GetLines() {
		var result = new List<EditorLine>( this.count );
		foreach ( var segment in this.segments ) {
			result.AddRange( segment );
		}
		return result.AsReadOnly();
	}

	/// <summary>Finds the current one-based address for a stable line identity.</summary>
	/// <param name="lineId">The stable line identity.</param>
	/// <returns>The address, or zero when the line no longer exists.</returns>
	public int FindAddress(
		long lineId
	) {
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero( lineId );
		var address = 1;
		foreach ( var segment in this.segments ) {
			foreach ( var line in segment ) {
				if ( lineId == line.Id ) {
					return address;
				}
				address++;
			}
		}
		return 0;
	}

	/// <summary>Replaces one line's content while retaining its stable identity.</summary>
	/// <param name="address">The one-based line address.</param>
	/// <param name="content">The replacement content.</param>
	public void SetContent(
		int address,
		ReadOnlyMemory<byte> content
	) {
		var location = this.LocateExisting( address );
		var existing = this.segments[ location.Segment ][ location.Offset ];
		this.segments[ location.Segment ][ location.Offset ] = new EditorLine(
			existing.Id,
			content
		);
	}

	/// <summary>Appends lines to the end of the buffer.</summary>
	/// <param name="lines">The line contents.</param>
	/// <returns>The inclusive range occupied by the inserted lines, or an empty zero range.</returns>
	public EditorAddressRange Append(
		IEnumerable<ReadOnlyMemory<byte>> lines
	) => this.InsertAfter( this.count, lines );

	/// <summary>Inserts lines after a zero-based insertion address.</summary>
	/// <param name="address">Zero inserts before the first line; otherwise insertion follows the addressed line.</param>
	/// <param name="lines">The line contents.</param>
	/// <returns>The inclusive range occupied by the inserted lines, or an empty zero range.</returns>
	public EditorAddressRange InsertAfter(
		int address,
		IEnumerable<ReadOnlyMemory<byte>> lines
	) {
		ArgumentNullException.ThrowIfNull( lines );
		if ( ( 0 > address ) || ( this.count < address ) ) {
			throw new ArgumentOutOfRangeException( nameof( address ) );
		}
		var inserted = lines.Select( this.CreateLine ).ToList();
		var insertedCount = inserted.Count;
		if ( 0 == insertedCount ) {
			return new EditorAddressRange( 0, -1 );
		}
		var start = checked( address + 1 );
		if ( 0 == this.segments.Count ) {
			this.segments.Add( inserted );
			this.SplitOversizedSegments();
		} else if ( 0 == address ) {
			this.segments[ 0 ].InsertRange( 0, inserted );
			this.SplitOversizedSegments();
		} else {
			var location = this.LocateExisting( address );
			this.segments[ location.Segment ].InsertRange( location.Offset + 1, inserted );
			this.SplitOversizedSegments();
		}
		this.count = checked( this.count + insertedCount );
		return new EditorAddressRange( start, checked( start + insertedCount - 1 ) );
	}

	/// <summary>Deletes an inclusive address range.</summary>
	/// <param name="range">The range to remove.</param>
	/// <returns>The deleted stable lines.</returns>
	public IReadOnlyList<EditorLine> Delete(
		EditorAddressRange range
	) {
		this.ValidateRange( range );
		var deleted = new List<EditorLine>( range.Count );
		for ( var index = 0; range.Count > index; index++ ) {
			var location = this.LocateExisting( range.Start );
			var segment = this.segments[ location.Segment ];
			deleted.Add( segment[ location.Offset ] );
			segment.RemoveAt( location.Offset );
			this.count--;
			if ( 0 == segment.Count ) {
				this.segments.RemoveAt( location.Segment );
			}
		}
		this.MergeSmallSegments();
		return deleted.AsReadOnly();
	}

	/// <summary>Replaces an inclusive range with new lines.</summary>
	/// <param name="range">The range to replace.</param>
	/// <param name="lines">The replacement content.</param>
	/// <returns>The new inclusive range.</returns>
	public EditorAddressRange Replace(
		EditorAddressRange range,
		IEnumerable<ReadOnlyMemory<byte>> lines
	) {
		this.ValidateRange( range );
		var insertionAddress = range.Start - 1;
		this.Delete( range );
		return this.InsertAfter( insertionAddress, lines );
	}

	/// <summary>Moves an inclusive range after a destination address while retaining line identities.</summary>
	/// <param name="range">The source range.</param>
	/// <param name="destination">The destination address in the pre-move buffer; zero means before the first line.</param>
	/// <returns>The new range occupied by the moved lines.</returns>
	public EditorAddressRange Move(
		EditorAddressRange range,
		int destination
	) {
		this.ValidateRange( range );
		if ( ( 0 > destination ) || ( this.count < destination ) ) {
			throw new ArgumentOutOfRangeException( nameof( destination ) );
		}
		if ( ( range.Start <= destination ) && ( range.End >= destination ) ) {
			throw new ArgumentException( "The destination is inside the moved range.", nameof( destination ) );
		}
		var moved = this.Delete( range ).ToList();
		if ( destination > range.End ) {
			destination -= range.Count;
		}
		return this.InsertExistingAfter( destination, moved );
	}

	/// <summary>Copies an inclusive range after a destination using new stable identities.</summary>
	/// <param name="range">The source range.</param>
	/// <param name="destination">The destination address; zero means before the first line.</param>
	/// <returns>The new range occupied by the copies.</returns>
	public EditorAddressRange Copy(
		EditorAddressRange range,
		int destination
	) {
		this.ValidateRange( range );
		if ( ( 0 > destination ) || ( this.count < destination ) ) {
			throw new ArgumentOutOfRangeException( nameof( destination ) );
		}
		var content = new List<ReadOnlyMemory<byte>>( range.Count );
		for ( var address = range.Start; range.End >= address; address++ ) {
			content.Add( this.GetLine( address ).Content );
		}
		return this.InsertAfter( destination, content );
	}

	/// <summary>Joins an inclusive range using no separator.</summary>
	/// <param name="range">The range to join.</param>
	/// <returns>The address of the joined line.</returns>
	public int Join(
		EditorAddressRange range
	) {
		this.ValidateRange( range );
		var length = 0;
		for ( var address = range.Start; range.End >= address; address++ ) {
			length = checked( length + this.GetLine( address ).Content.Length );
		}
		var content = new byte[ length ];
		var offset = 0;
		for ( var address = range.Start; range.End >= address; address++ ) {
			var line = this.GetLine( address ).Content;
			line.Span.CopyTo( content.AsSpan( offset ) );
			offset += line.Length;
		}
		this.SetContent( range.Start, content );
		if ( range.End > range.Start ) {
			this.Delete( new EditorAddressRange( range.Start + 1, range.End ) );
		}
		return range.Start;
	}

	/// <summary>Replaces all buffer content and resets generated identities.</summary>
	/// <param name="lines">The new line content.</param>
	public void Reset(
		IEnumerable<ReadOnlyMemory<byte>> lines
	) {
		ArgumentNullException.ThrowIfNull( lines );
		this.segments.Clear();
		this.count = 0;
		this.nextLineId = 1;
		this.Append( lines );
	}

	/// <summary>Captures line identities, content, and the next generated identity for one undo unit.</summary>
	/// <returns>The immutable buffer snapshot.</returns>
	internal BufferSnapshot CaptureSnapshot() => new(
		this.GetLines().ToArray(),
		this.nextLineId
	);

	/// <summary>Restores a previously captured buffer snapshot.</summary>
	/// <param name="snapshot">The snapshot to restore.</param>
	internal void RestoreSnapshot(
		BufferSnapshot snapshot
	) {
		ArgumentNullException.ThrowIfNull( snapshot );
		this.segments.Clear();
		this.count = 0;
		this.nextLineId = snapshot.NextLineId;
		this.InsertExistingAfter( 0, snapshot.Lines );
	}

	private EditorLine CreateLine(
		ReadOnlyMemory<byte> content
	) => new( this.nextLineId++, content );

	private EditorAddressRange InsertExistingAfter(
		int address,
		IReadOnlyList<EditorLine> lines
	) {
		if ( 0 == lines.Count ) {
			return new EditorAddressRange( 0, -1 );
		}
		var start = checked( address + 1 );
		if ( 0 == this.segments.Count ) {
			this.segments.Add( lines.ToList() );
		} else if ( 0 == address ) {
			this.segments[ 0 ].InsertRange( 0, lines );
		} else {
			var location = this.LocateExisting( address );
			this.segments[ location.Segment ].InsertRange( location.Offset + 1, lines );
		}
		this.count = checked( this.count + lines.Count );
		this.SplitOversizedSegments();
		return new EditorAddressRange( start, checked( start + lines.Count - 1 ) );
	}

	private ( int Segment, int Offset ) LocateExisting(
		int address
	) {
		if ( ( 1 > address ) || ( this.count < address ) ) {
			throw new ArgumentOutOfRangeException( nameof( address ) );
		}
		var remaining = address - 1;
		for ( var segmentIndex = 0; this.segments.Count > segmentIndex; segmentIndex++ ) {
			var segment = this.segments[ segmentIndex ];
			if ( segment.Count > remaining ) {
				return ( segmentIndex, remaining );
			}
			remaining -= segment.Count;
		}
		throw new InvalidOperationException( "The buffer segment index is inconsistent." );
	}

	private void ValidateRange(
		EditorAddressRange range
	) {
		if (
			( 1 > range.Start )
			|| ( range.Start > range.End )
			|| ( this.count < range.End )
		) {
			throw new ArgumentOutOfRangeException( nameof( range ) );
		}
	}

	private void SplitOversizedSegments() {
		for ( var index = 0; this.segments.Count > index; index++ ) {
			var segment = this.segments[ index ];
			if ( MaximumSegmentSize >= segment.Count ) {
				continue;
			}
			var tail = segment.GetRange(
				MaximumSegmentSize,
				segment.Count - MaximumSegmentSize
			);
			segment.RemoveRange(
				MaximumSegmentSize,
				segment.Count - MaximumSegmentSize
			);
			this.segments.Insert( index + 1, tail );
		}
	}

	private void MergeSmallSegments() {
		for ( var index = 0; this.segments.Count - 1 > index; ) {
			var current = this.segments[ index ];
			var next = this.segments[ index + 1 ];
			if (
				( MinimumSegmentSize > current.Count )
				&& ( MaximumSegmentSize >= current.Count + next.Count )
			) {
				current.AddRange( next );
				this.segments.RemoveAt( index + 1 );
			} else {
				index++;
			}
		}
	}
}

/// <summary>Represents an internal buffer undo snapshot.</summary>
/// <param name="Lines">The stable lines in address order.</param>
/// <param name="NextLineId">The next identity to allocate.</param>
internal sealed record BufferSnapshot(
	IReadOnlyList<EditorLine> Lines,
	long NextLineId
);
