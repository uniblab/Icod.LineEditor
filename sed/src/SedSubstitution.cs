namespace Icod.LineEditor.Sed;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Icod.CommandFramework.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Icod.CommandFramework.CommandLine;
using Icod.CommandFramework.Diagnostics;
using Icod.CommandFramework.IO;
using Icod.CommandFramework.Processes;

// Responsibility: substitution and transliteration.
public static partial class Command {

	private sealed class Substitution {

		public string Flags {
			get;
		}

		public SedCompiledRegularExpression RegularExpression {
			get;
		}

		public string Replacement {
			get;
		}

		public Substitution(
			SedCompiledRegularExpression regularExpression,
			string replacement,
			string flags
		) {
			this.RegularExpression = regularExpression
				?? throw new ArgumentNullException(
					nameof( regularExpression )
				)
			;
			this.Replacement = replacement;
			this.Flags = flags;
		}

	}

	private sealed class Transliteration {

		public string Destination {
			get;
		}

		public string Source {
			get;
		}

		public Transliteration(
			string source,
			string destination
		) {
			this.Source = source;
			this.Destination = destination;
		}

	}


	private sealed class SubstitutionFlags {

		public bool Execute {
			get;
			set;
		}

		public bool Global {
			get;
			set;
		}

		public bool IgnoreCase {
			get;
			set;
		}

		public bool Multiline {
			get;
			set;
		}

		public int? Occurrence {
			get;
			set;
		}

		public bool Print {
			get;
			set;
		}

		public string? WriteFile {
			get;
			set;
		}

	}

	private static SubstitutionFlags ParseSubstitutionFlags(
		string flags
	) {
		var output = new SubstitutionFlags();
		var index = 0;

		while ( index < flags.Length ) {
			var character = flags[ index ];
			if ( char.IsWhiteSpace( character ) ) {
				index++;
			} else if ( char.IsDigit( character ) ) {
				var start = index;
				while (
					index < flags.Length
					&& char.IsDigit(
						flags[ index ]
					)
				) {
					index++;
				}
				output.Occurrence = int.Parse(
					flags.Substring(
						start,
						index - start
					),
					CultureInfo.InvariantCulture
				);
			} else if ( 'e' == character ) {
				output.Execute = true;
				index++;
			} else if ( 'g' == character ) {
				output.Global = true;
				index++;
			} else if ( 'p' == character ) {
				output.Print = true;
				index++;
			} else if (
				'i' == character
				|| 'I' == character
			) {
				output.IgnoreCase = true;
				index++;
			} else if (
				'm' == character
				|| 'M' == character
			) {
				output.Multiline = true;
				index++;
			} else if ( 'w' == character ) {
				index++;
				while (
					index < flags.Length
					&& char.IsWhiteSpace(
						flags[ index ]
					)
				) {
					index++;
				}
				output.WriteFile = flags.Substring(
					index
				).Trim();
				break;
			} else {
				index++;
			}
		}

		return output;
	}

	private static string ApplySubstitution(
		string input,
		Substitution substitution,
		out bool replaced,
		CancellationToken cancellationToken
	) {
		var flags = ParseSubstitutionFlags(
			substitution.Flags
		);
		var matches = substitution.RegularExpression.FindMatches(
			input,
			cancellationToken
		);
		if ( 0 == matches.Count ) {
			replaced = false;
			return input;
		}

		var first = flags.Occurrence ?? 1;
		if (
			first <= 0
			|| matches.Count < first
		) {
			replaced = false;
			return input;
		}

		var output = new StringBuilder(
			input.Length
		);
		var cursor = 0;
		var replacementCount = 0;

		for ( var index = 0; index < matches.Count; index++ ) {
			cancellationToken.ThrowIfCancellationRequested();
			var matchNumber = index + 1;
			var shouldReplace = flags.Global
				? first <= matchNumber
				: first == matchNumber
			;
			if ( !shouldReplace ) {
				continue;
			}

			var match = matches[ index ];
			output.Append(
				input,
				cursor,
				match.Index - cursor
			);
			output.Append(
				ExpandReplacement(
					substitution.Replacement,
					match
				)
			);
			cursor = match.Index + match.Length;
			replacementCount++;

			if ( !flags.Global ) {
				break;
			}
		}

		if ( 0 == replacementCount ) {
			replaced = false;
			return input;
		}

		output.Append(
			input,
			cursor,
			input.Length - cursor
		);
		replaced = true;
		return output.ToString();
	}

	private static string ExpandReplacement(
		string replacement,
		RegularExpressionMatch match
	) {
		var output = new StringBuilder();

		for (
			var index = 0;
			index < replacement.Length;
			index++
		) {
			var character = replacement[ index ];
			if ( '&' == character ) {
				output.Append(
					match.Value
				);
			} else if (
				'\\' == character
				&& index + 1 < replacement.Length
			) {
				index++;
				var escaped = replacement[ index ];
				if (
					'0' <= escaped
					&& escaped <= '9'
				) {
					var groupNumber = escaped - '0';
					if ( 0 == groupNumber ) {
						output.Append(
							match.Value
						);
					} else if ( groupNumber <= match.Captures.Count ) {
						var capture = match.Captures[ groupNumber - 1 ];
						if ( capture.Success ) {
							output.Append(
								capture.Value
							);
						}
					}
				} else {
					switch ( escaped ) {
						case 'n':
							output.Append(
								'\n'
							);
							break;
						case 'r':
							output.Append(
								'\r'
							);
							break;
						case 't':
							output.Append(
								'\t'
							);
							break;
						default:
							output.Append(
								escaped
							);
							break;
					}
				}
			} else {
				output.Append(
					character
				);
			}
		}

		return output.ToString();
	}

	private static string Transliterate(
		string input,
		Transliteration transliteration
	) {
		var source = ExpandCharacterSet(
			transliteration.Source
		);
		var destination = ExpandCharacterSet(
			transliteration.Destination
		);
		if ( source.Length != destination.Length ) {
			throw new ScriptParseException(
				"the y command source and destination must have equal lengths"
			);
		}

		var map = new Dictionary<char, char>();
		for (
			var index = 0;
			index < source.Length;
			index++
		) {
			map[ source[ index ] ] = destination[ index ];
		}

		var output = input.ToCharArray();
		for (
			var index = 0;
			index < output.Length;
			index++
		) {
			if (
				map.TryGetValue(
					output[ index ],
					out var replacement
				)
			) {
				output[ index ] = replacement;
			}
		}
		return new string(
			output
		);
	}

	private static string ExpandCharacterSet(
		string value
	) {
		var output = new StringBuilder();

		for (
			var index = 0;
			index < value.Length;
			index++
		) {
			var character = value[ index ];
			if (
				index + 2 < value.Length
				&& '-' == value[ index + 1 ]
				&& character <= value[ index + 2 ]
			) {
				var end = value[ index + 2 ];
				for (
					var current = character;
					current <= end;
					current++
				) {
					output.Append(
						current
					);
				}
				index += 2;
			} else if (
				'\\' == character
				&& index + 1 < value.Length
			) {
				index++;
				output.Append(
					UnescapeCharacter(
						value[ index ]
					)
				);
			} else {
				output.Append(
					character
				);
			}
		}

		return output.ToString();
	}


}
