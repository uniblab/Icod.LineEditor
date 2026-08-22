namespace Icod.LineEditor.Sed;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Icod.CommandFramework.RegularExpressions;
using Icod.CommandFramework.Text;

// Responsibility: Sed-specific regular-expression policy over the Shared GNU provider.
public static partial class Command {

	private enum SedRegularExpressionContext {
		Address,
		Substitution
	}

	private sealed class SedCompiledRegularExpression {

		private readonly ICompiledRegularExpression myExpression;

		public SedRegularExpressionContext Context {
			get;
		}

		public string Pattern {
			get {
				return this.myExpression.Pattern;
			}
		}

		public SedCompiledRegularExpression(
			ICompiledRegularExpression expression,
			SedRegularExpressionContext context
		) {
			this.myExpression = expression ?? throw new ArgumentNullException(
				nameof( expression )
			);
			this.Context = context;
		}

		public bool IsMatch(
			string input,
			CancellationToken cancellationToken
		) {
			return null != this.FindMatch(
				input,
				0,
				cancellationToken
			);
		}

		public IReadOnlyList<RegularExpressionMatch> FindMatches(
			string input,
			CancellationToken cancellationToken
		) {
			ArgumentNullException.ThrowIfNull( input );
			var output = new List<RegularExpressionMatch>();
			var searchStart = 0;
			int? precedingNonEmptyEnd = null;

			while ( searchStart <= input.Length ) {
				cancellationToken.ThrowIfCancellationRequested();
				var match = this.FindMatch(
					input,
					searchStart,
					cancellationToken
				);
				if ( null == match ) {
					break;
				}

				if (
					0 == match.Length
					&& precedingNonEmptyEnd.HasValue
					&& precedingNonEmptyEnd.Value == match.Index
				) {
					if ( match.Index >= input.Length ) {
						break;
					}
					searchStart = AdvanceStringIndex(
						input,
						match.Index
					);
					continue;
				}

				output.Add(
					match
				);
				if ( 0 < match.Length ) {
					searchStart = match.Index + match.Length;
					precedingNonEmptyEnd = searchStart;
				} else {
					precedingNonEmptyEnd = null;
					if ( match.Index >= input.Length ) {
						break;
					}
					searchStart = AdvanceStringIndex(
						input,
						match.Index
					);
				}
			}

			return output;
		}

		private static int AdvanceStringIndex(
			string input,
			int index
		) {
			if (
				index + 1 < input.Length
				&& char.IsHighSurrogate( input[ index ] )
				&& char.IsLowSurrogate( input[ index + 1 ] )
			) {
				return index + 2;
			}
			return index + 1;
		}

		private RegularExpressionMatch? FindMatch(
			string input,
			int startIndex,
			CancellationToken cancellationToken
		) {
			var result = this.myExpression.Match(
				input,
				new RegularExpressionMatchOptions {
					StartIndex = startIndex
				},
				cancellationToken
			);
			if ( !result.IsSuccess ) {
				var diagnostic = result.Diagnostic
					?? throw new InvalidOperationException(
						"regular-expression matching failed without a diagnostic"
					)
				;
				throw new InvalidOperationException(
					$"regular expression match failed: {diagnostic.Message}"
				);
			}
			return result.Match;
		}

	}

	private sealed class SedRegularExpressionCompiler {

		private readonly CancellationToken myCancellationToken;
		private readonly IRegularExpressionProvider myProvider;
		private readonly bool myNullData;
		private readonly bool myPosix;
		private readonly bool myExtendedRegularExpressions;
		private SedCompiledRegularExpression? myLastExpression;

		public SedRegularExpressionCompiler(
			bool extendedRegularExpressions,
			bool posix,
			bool nullData,
			ITextLocaleProvider textLocale,
			CancellationToken cancellationToken
		) {
			this.myExtendedRegularExpressions = extendedRegularExpressions;
			this.myPosix = posix;
			this.myNullData = nullData;
			this.myCancellationToken = cancellationToken;
			var characterClasses = CreateSedCharacterClassProvider( textLocale );
			this.myProvider = extendedRegularExpressions
				? new GnuExtendedRegularExpressionProvider(
					characterClasses
				)
				: new GnuBasicRegularExpressionProvider(
					characterClasses
				)
			;
		}

		public SedCompiledRegularExpression Compile(
			string pattern,
			SedRegularExpressionContext context,
			bool ignoreCase = false,
			bool multiline = false
		) {
			ArgumentNullException.ThrowIfNull( pattern );
			this.myCancellationToken.ThrowIfCancellationRequested();

			if ( 0 == pattern.Length ) {
				if ( ignoreCase || multiline ) {
					throw new ScriptParseException(
						"cannot specify modifiers on an empty regular expression"
					);
				}
				return this.myLastExpression
					?? throw new ScriptParseException(
						"no previous regular expression"
					)
				;
			}

			var sedPattern = ExpandSedRegularExpressionEscapes(
				pattern,
				this.myPosix
			);
			var effectivePattern = this.myPosix
				? NormalizePosixRegularExpression(
					sedPattern,
					this.myExtendedRegularExpressions
				)
				: sedPattern
			;
			var result = this.myProvider.Compile(
				effectivePattern,
				new RegularExpressionOptions {
					Syntax = this.myExtendedRegularExpressions
						? GnuRegularExpressionSyntax.Extended
						: GnuRegularExpressionSyntax.Basic,
					IgnoreCase = ignoreCase,
					NewLineSensitive = multiline,
					LineSeparator = new System.Text.Rune( this.myNullData ? '\0' : '\n' ),
					DotMatchesNull = this.myNullData
				},
				this.myCancellationToken
			);
			if ( !result.IsSuccess ) {
				var diagnostic = result.Diagnostic
					?? throw new ScriptParseException(
						"invalid regular expression"
					)
				;
				var contextText = SedRegularExpressionContext.Address == context
					? "address"
					: "substitution"
				;
				throw new ScriptParseException(
					$"invalid regular expression in {contextText}: {diagnostic.Message}"
				);
			}

			var output = new SedCompiledRegularExpression(
				result.Expression
					?? throw new ScriptParseException(
						"regular-expression compilation succeeded without an expression"
					),
				context
			);
			this.myLastExpression = output;
			return output;
		}

		private static IRegularExpressionCharacterClassProvider CreateSedCharacterClassProvider(
			ITextLocaleProvider textLocale
		) {
			ArgumentNullException.ThrowIfNull( textLocale );
			return TextDecodingMode.Bytes == textLocale.DecodingMode
				? PosixCLocaleRegularExpressionCharacterClassProvider.Instance
				: new UnicodeRegularExpressionCharacterClassProvider(
					CultureInfo.CurrentCulture
				)
			;
		}

		private static string ExpandSedRegularExpressionEscapes(
			string pattern,
			bool posix
		) {
			var rawBracketPositions = FindRawBracketPositions(
				pattern
			);
			var output = new System.Text.StringBuilder(
				pattern.Length
			);
			for ( var index = 0; index < pattern.Length; index++ ) {
				var character = pattern[ index ];
				if (
					'\\' != character
					|| index + 1 >= pattern.Length
				) {
					output.Append( character );
					continue;
				}

				if (
					posix
					&& rawBracketPositions[ index ]
				) {
					output.Append( character );
					continue;
				}

				var escaped = pattern[ ++index ];
				switch ( escaped ) {
					case 'a':
						output.Append( '\a' );
						break;
					case 'f':
						output.Append( '\f' );
						break;
					case 'n':
						output.Append( '\n' );
						break;
					case 'r':
						output.Append( '\r' );
						break;
					case 't':
						output.Append( '\t' );
						break;
					case 'v':
						output.Append( '\v' );
						break;
					case 'c':
						if ( index + 1 >= pattern.Length ) {
							throw new ScriptParseException(
								"unterminated control-character escape in regular expression"
							);
						}
						var control = pattern[ ++index ];
						if ( control is >= 'a' and <= 'z' ) {
							control = char.ToUpperInvariant( control );
						}
						output.Append(
							(char)( control ^ 0x40 )
						);
						break;
					case 'd':
						AppendNumericEscape(
							pattern,
							ref index,
							10,
							3,
							escaped,
							output
						);
						break;
					case 'o':
						AppendNumericEscape(
							pattern,
							ref index,
							8,
							3,
							escaped,
							output
						);
						break;
					case 'x':
						AppendNumericEscape(
							pattern,
							ref index,
							16,
							2,
							escaped,
							output
						);
						break;
					default:
						output.Append( '\\' );
						output.Append( escaped );
						break;
				}
			}
			return output.ToString();
		}

		private static void AppendNumericEscape(
			string pattern,
			ref int index,
			int numberBase,
			int maximumDigits,
			char escape,
			System.Text.StringBuilder output
		) {
			var value = 0;
			var digits = 0;
			while (
				digits < maximumDigits
				&& index + 1 < pattern.Length
			) {
				var digit = GetDigitValue(
					pattern[ index + 1 ]
				);
				if ( digit < 0 || digit >= numberBase ) {
					break;
				}
				value = checked( value * numberBase + digit );
				index++;
				digits++;
			}
			if ( 0 == digits ) {
				output.Append( escape );
				return;
			}
			output.Append(
				(char)( value & 0xff )
			);
		}

		private static int GetDigitValue(
			char character
		) {
			if ( character is >= '0' and <= '9' ) {
				return character - '0';
			}
			if ( character is >= 'a' and <= 'f' ) {
				return character - 'a' + 10;
			}
			if ( character is >= 'A' and <= 'F' ) {
				return character - 'A' + 10;
			}
			return -1;
		}

		private static bool[] FindRawBracketPositions(
			string pattern
		) {
			var output = new bool[ pattern.Length ];
			var inBracketExpression = false;
			var bracketPosition = 0;
			var bracketAllowsLeadingCaret = false;
			for ( var index = 0; index < pattern.Length; index++ ) {
				output[ index ] = inBracketExpression;
				var character = pattern[ index ];
				if ( !inBracketExpression ) {
					if (
						'\\' == character
						&& index + 1 < pattern.Length
					) {
						output[ ++index ] = false;
						continue;
					}
					if ( '[' == character ) {
						inBracketExpression = true;
						bracketPosition = 0;
						bracketAllowsLeadingCaret = true;
					}
					continue;
				}

				if (
					bracketAllowsLeadingCaret
					&& '^' == character
				) {
					bracketAllowsLeadingCaret = false;
					continue;
				}
				bracketAllowsLeadingCaret = false;
				if (
					'[' == character
					&& index + 1 < pattern.Length
					&& pattern[ index + 1 ] is ':' or '.' or '='
				) {
					var marker = pattern[ index + 1 ];
					output[ index + 1 ] = true;
					index += 2;
					while ( index < pattern.Length ) {
						output[ index ] = true;
						if (
							marker == pattern[ index ]
							&& index + 1 < pattern.Length
							&& ']' == pattern[ index + 1 ]
						) {
							output[ ++index ] = true;
							break;
						}
						index++;
					}
					bracketPosition++;
					continue;
				}
				if (
					']' == character
					&& 0 < bracketPosition
				) {
					inBracketExpression = false;
				}
				bracketPosition++;
			}
			return output;
		}

		private static string NormalizePosixRegularExpression(
			string pattern,
			bool extendedRegularExpressions
		) {
			var output = new System.Text.StringBuilder(
				pattern.Length
			);
			var inBracketExpression = false;
			var bracketPosition = 0;
			var bracketAllowsLeadingCaret = false;
			for ( var index = 0; index < pattern.Length; index++ ) {
				var character = pattern[ index ];
				if ( inBracketExpression ) {
					if (
						bracketAllowsLeadingCaret
						&& '^' == character
					) {
						output.Append( character );
						bracketAllowsLeadingCaret = false;
						continue;
					}

					bracketAllowsLeadingCaret = false;
					if (
						'[' == character
						&& index + 1 < pattern.Length
						&& pattern[ index + 1 ] is ':' or '.' or '='
					) {
						var marker = pattern[ index + 1 ];
						output.Append( character );
						output.Append( marker );
						index += 2;
						while ( index < pattern.Length ) {
							output.Append( pattern[ index ] );
							if (
								marker == pattern[ index ]
								&& index + 1 < pattern.Length
								&& ']' == pattern[ index + 1 ]
							) {
								output.Append( ']' );
								index++;
								break;
							}
							index++;
						}
						bracketPosition++;
						continue;
					}

					output.Append( character );
					if (
						'\\' == character
						&& index + 1 < pattern.Length
					) {
						output.Append( pattern[ ++index ] );
						bracketPosition++;
						continue;
					}
					if (
						']' == character
						&& 0 < bracketPosition
					) {
						inBracketExpression = false;
					}
					bracketPosition++;
					continue;
				}

				if ( '[' == character ) {
					inBracketExpression = true;
					bracketPosition = 0;
					bracketAllowsLeadingCaret = true;
					output.Append( character );
					continue;
				}

				if (
					'\\' == character
					&& index + 1 < pattern.Length
				) {
					var escaped = pattern[ index + 1 ];
					if ( '\\' == escaped ) {
						output.Append( character );
						output.Append( escaped );
						index++;
						continue;
					}
					if (
						IsGnuAssertionEscape( escaped )
						|| (
							!extendedRegularExpressions
							&& escaped is '+' or '?' or '|'
						)
					) {
						output.Append( escaped );
						index++;
						continue;
					}
					output.Append( character );
					output.Append( escaped );
					index++;
					continue;
				}

				output.Append( character );
			}
			return output.ToString();
		}

		private static bool IsGnuAssertionEscape(
			char character
		) {
			return character is 'w' or 'W' or 's' or 'S'
				or '<' or '>' or 'b' or 'B' or '`' or '\'';
		}

	}

}
