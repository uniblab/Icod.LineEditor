namespace Icod.LineEditor.Ed;

/// <summary>Parses Ed addresses and ranges independently from the Sed address model.</summary>
internal sealed class EditorAddressParser {
	private readonly string text;
	private readonly Func<char, int> markResolver;
	private readonly Func<string, bool, int, int> searchResolver;
	private readonly int lastAddress;
	private int currentAddress;
	private int index;

	/// <summary>Initializes an address parser.</summary>
	internal EditorAddressParser(
		string text,
		int currentAddress,
		int lastAddress,
		Func<char, int> markResolver,
		Func<string, bool, int, int> searchResolver
	) {
		ArgumentNullException.ThrowIfNull( text );
		ArgumentNullException.ThrowIfNull( markResolver );
		ArgumentNullException.ThrowIfNull( searchResolver );
		this.text = text;
		this.currentAddress = currentAddress;
		this.lastAddress = lastAddress;
		this.markResolver = markResolver;
		this.searchResolver = searchResolver;
	}

	/// <summary>Gets the first unconsumed character index.</summary>
	internal int Position => this.index;

	/// <summary>Parses an optional address range.</summary>
	/// <returns>The parsed range and whether any address was supplied.</returns>
	internal ParsedEditorRange ParseRange() {
		this.SkipSpaces();
		if ( this.TryConsume( '%' ) ) {
			return new ParsedEditorRange(
				true,
				new EditorAddressRange( 1, this.lastAddress )
			);
		}

		var first = this.ParseAddress();
		this.SkipSpaces();
		if ( this.TryConsume( ',' ) || this.TryConsume( ';' ) ) {
			var delimiter = this.text[ this.index - 1 ];
			var resolvedFirst = first ?? ( ',' == delimiter ? 1 : this.currentAddress );
			if ( ';' == delimiter ) {
				this.currentAddress = resolvedFirst;
			}
			this.SkipSpaces();
			var second = this.ParseAddress() ?? this.lastAddress;
			return new ParsedEditorRange(
				true,
				new EditorAddressRange( resolvedFirst, second )
			);
		}
		if ( null == first ) {
			return new ParsedEditorRange( false, default );
		}
		return new ParsedEditorRange(
			true,
			new EditorAddressRange( first.Value, first.Value )
		);
	}

	private int? ParseAddress() {
		this.SkipSpaces();
		if ( this.text.Length <= this.index ) {
			return null;
		}
		int? value = null;
		var current = this.text[ this.index ];
		if ( char.IsAsciiDigit( current ) ) {
			value = this.ReadNumber();
		} else if ( '.' == current ) {
			this.index++;
			value = this.currentAddress;
		} else if ( '$' == current ) {
			this.index++;
			value = this.lastAddress;
		} else if ( '\'' == current ) {
			this.index++;
			if ( this.text.Length <= this.index ) {
				throw new EditorParseException( "Missing mark name." );
			}
			value = this.markResolver( this.text[ this.index++ ] );
		} else if ( ( '/' == current ) || ( '?' == current ) ) {
			this.index++;
			var pattern = this.ReadDelimited( current );
			value = this.searchResolver( pattern, '?' == current, this.currentAddress );
		} else if ( ( '+' == current ) || ( '-' == current ) || ( '^' == current ) ) {
			value = this.currentAddress;
		} else {
			return null;
		}

		while ( true ) {
			this.SkipSpaces();
			if ( this.text.Length <= this.index ) {
				break;
			}
			var sign = this.text[ this.index ];
			if ( ( '+' != sign ) && ( '-' != sign ) && ( '^' != sign ) ) {
				break;
			}
			this.index++;
			this.SkipSpaces();
			var amount = 1;
			if ( ( this.text.Length > this.index ) && char.IsAsciiDigit( this.text[ this.index ] ) ) {
				amount = this.ReadNumber();
			}
			try {
				value = checked( value.Value + ( '+' == sign ? amount : -amount ) );
			} catch ( OverflowException ) {
				throw new EditorParseException( "The address is outside the supported range." );
			}
		}
		return value;
	}

	private int ReadNumber() {
		var start = this.index;
		while ( ( this.text.Length > this.index ) && char.IsAsciiDigit( this.text[ this.index ] ) ) {
			this.index++;
		}
		if ( !int.TryParse(
			this.text.AsSpan( start, this.index - start ),
			System.Globalization.NumberStyles.None,
			System.Globalization.CultureInfo.InvariantCulture,
			out var value
		) ) {
			throw new EditorParseException( "The address is outside the supported range." );
		}
		return value;
	}

	private string ReadDelimited(
		char delimiter
	) {
		var result = new System.Text.StringBuilder();
		var escaped = false;
		while ( this.text.Length > this.index ) {
			var character = this.text[ this.index++ ];
			if ( escaped ) {
				result.Append( '\\' );
				result.Append( character );
				escaped = false;
				continue;
			}
			if ( '\\' == character ) {
				escaped = true;
				continue;
			}
			if ( delimiter == character ) {
				return result.ToString();
			}
			result.Append( character );
		}
		throw new EditorParseException( "Unterminated regular expression." );
	}

	private void SkipSpaces() {
		while ( ( this.text.Length > this.index ) && char.IsWhiteSpace( this.text[ this.index ] ) ) {
			this.index++;
		}
	}

	private bool TryConsume(
		char character
	) {
		if ( ( this.text.Length <= this.index ) || ( character != this.text[ this.index ] ) ) {
			return false;
		}
		this.index++;
		return true;
	}
}

/// <summary>Represents an optional parsed editor range.</summary>
/// <param name="IsSpecified">Whether the command supplied an address.</param>
/// <param name="Range">The parsed inclusive range.</param>
internal readonly record struct ParsedEditorRange(
	bool IsSpecified,
	EditorAddressRange Range
);

/// <summary>Represents a controlled editor command-parse failure.</summary>
internal sealed class EditorParseException : Exception {
	/// <summary>Initializes a parse exception.</summary>
	/// <param name="message">The controlled parse diagnostic.</param>
	internal EditorParseException(
		string message
	) : base( message ) {
	}
}
