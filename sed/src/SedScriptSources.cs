namespace Icod.LineEditor.Sed;

using System;
using System.Collections.Generic;
using System.Text;

// Responsibility: ordered script-source identity and aggregate location mapping.
public static partial class Command {

	/// <summary>Identifies how one Sed script source entered the invocation.</summary>
	internal enum SedScriptSourceKind {
		/// <summary>A command-line <c>-e</c> expression.</summary>
		Expression,
		/// <summary>A command-line <c>-f</c> file.</summary>
		File,
		/// <summary>The implicit first operand used as the script.</summary>
		ImplicitOperand
	}

	/// <summary>Preserves one independently named Sed script source.</summary>
	internal sealed class SedScriptSource {

		/// <summary>Gets the source kind.</summary>
		public SedScriptSourceKind Kind {
			get;
		}

		/// <summary>Gets the stable display name used in diagnostics.</summary>
		public string Name {
			get;
		}

		/// <summary>Gets the zero-based source order.</summary>
		public int Order {
			get;
		}

		/// <summary>Gets the source text exactly as supplied or read.</summary>
		public string Text {
			get;
		}

		/// <summary>Initializes one script source.</summary>
		public SedScriptSource(
			SedScriptSourceKind kind,
			string name,
			string text,
			int order
		) {
			if ( order < 0 ) {
				throw new ArgumentOutOfRangeException( nameof( order ) );
			}
			this.Kind = kind;
			this.Name = name ?? throw new ArgumentNullException( nameof( name ) );
			this.Text = text ?? throw new ArgumentNullException( nameof( text ) );
			this.Order = order;
		}

	}

	/// <summary>Identifies a one-based line and column inside a named script source.</summary>
	internal readonly record struct SedScriptLocation(
		string SourceName,
		int Line,
		int Column
	);

	/// <summary>Provides one LF-delimited parser view while retaining source boundaries.</summary>
	internal sealed class SedScriptDocument {

		private sealed record SourceSpan(
			SedScriptSource Source,
			int Start,
			int Length
		);

		private readonly IReadOnlyList<SourceSpan> mySpans;

		/// <summary>Gets the ordered original sources.</summary>
		public IReadOnlyList<SedScriptSource> Sources {
			get;
		}

		/// <summary>Gets the aggregate parser text, separated only with LF.</summary>
		public string Text {
			get;
		}

		private SedScriptDocument(
			IReadOnlyList<SedScriptSource> sources,
			string text,
			IReadOnlyList<SourceSpan> spans
		) {
			this.Sources = sources;
			this.Text = text;
			this.mySpans = spans;
		}

		/// <summary>Creates an aggregate parser document without host-newline insertion.</summary>
		public static SedScriptDocument Create(
			IReadOnlyList<SedScriptSource> sources
		) {
			ArgumentNullException.ThrowIfNull( sources );
			if ( 0 == sources.Count ) {
				throw new ArgumentException( "At least one script source is required.", nameof( sources ) );
			}

			var ordered = new List<SedScriptSource>( sources.Count );
			var spans = new List<SourceSpan>( sources.Count );
			var text = new StringBuilder();
			for ( var index = 0; index < sources.Count; index++ ) {
				var source = sources[ index ] ?? throw new ArgumentException(
					"A script source cannot be null.",
					nameof( sources )
				);
				if ( 0 < index && ( 0 == text.Length || '\n' != text[ ^1 ] ) ) {
					text.Append( '\n' );
				}
				var start = text.Length;
				text.Append( source.Text );
				ordered.Add( source );
				spans.Add( new SourceSpan( source, start, source.Text.Length ) );
			}
			return new SedScriptDocument(
				ordered.AsReadOnly(),
				text.ToString(),
				spans.AsReadOnly()
			);
		}

		/// <summary>Maps an aggregate character position back to its named source.</summary>
		public SedScriptLocation GetLocation(
			int position
		) {
			if ( position < 0 ) {
				position = 0;
			} else if ( this.Text.Length < position ) {
				position = this.Text.Length;
			}

			SourceSpan span = this.mySpans[ ^1 ];
			foreach ( var candidate in this.mySpans ) {
				if ( position <= candidate.Start + candidate.Length ) {
					span = candidate;
					break;
				}
			}
			var local = Math.Clamp( position - span.Start, 0, span.Length );
			var line = 1;
			var column = 1;
			for ( var index = 0; index < local; index++ ) {
				if ( '\n' == span.Source.Text[ index ] ) {
					line++;
					column = 1;
				} else {
					column++;
				}
			}
			return new SedScriptLocation( span.Source.Name, line, column );
		}

	}

}
