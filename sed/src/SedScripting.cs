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
using Icod.CommandFramework.Text;

// Responsibility: script instruction model and parser.
public static partial class Command {

	private enum InstructionKind {
		AppendText,
		AppendHold,
		AppendNext,
		BeginGroup,
		Branch,
		ChangeText,
		Delete,
		DeleteFirst,
		EndGroup,
		Execute,
		Exchange,
		GetHold,
		Label,
		LineNumber,
		List,
		Next,
		Print,
		PrintFirst,
		Quit,
		QuitSilent,
		ReadFile,
		ReadFileLine,
		SetHold,
		Substitute,
		TestBranch,
		TestNoBranch,
		Transliterate,
		WriteFile,
		WriteFirst
	}


	private sealed class Instruction {

		public AddressSelector? Address {
			get;
		}

		public object? Argument {
			get;
		}

		public InstructionKind Kind {
			get;
		}

		public int JumpIndex {
			get;
			set;
		} = -1;

		public Instruction(
			InstructionKind kind,
			AddressSelector? address = null,
			object? argument = null
		) {
			this.Kind = kind;
			this.Address = address;
			this.Argument = argument;
		}

	}

	private sealed class SedProgram {

		private readonly Dictionary<string, int> myLabels;

		public IReadOnlyList<Instruction> Instructions {
			get;
		}

		public SedProgram(
			IReadOnlyList<Instruction> instructions
		) {
			this.Instructions = instructions;
			this.myLabels = new Dictionary<string, int>(
				StringComparer.Ordinal
			);

			for (
				var index = 0;
				index < instructions.Count;
				index++
			) {
				var instruction = instructions[ index ];
				if (
					InstructionKind.Label == instruction.Kind
				) {
					var label = instruction.Argument as string
						?? string.Empty
					;
					if (
						this.myLabels.ContainsKey(
							label
						)
					) {
						throw new ScriptParseException(
							$"duplicate label '{label}'"
						);
					}
					this.myLabels.Add(
						label,
						index
					);
				}
			}

			foreach ( var instruction in instructions ) {
				if (
					(
						InstructionKind.Branch == instruction.Kind
						|| InstructionKind.TestBranch == instruction.Kind
						|| InstructionKind.TestNoBranch == instruction.Kind
					)
					&& instruction.Argument is string label
					&& 0 < label.Length
					&& !this.myLabels.ContainsKey(
						label
					)
				) {
					throw new ScriptParseException(
						$"undefined label '{label}'"
					);
				}
			}
		}

		public int ResolveLabel(
			string? label
		) {
			if ( string.IsNullOrEmpty( label ) ) {
				return this.Instructions.Count;
			}

			if (
				!this.myLabels.TryGetValue(
					label,
					out var index
				)
			) {
				throw new ScriptParseException(
					$"undefined label '{label}'"
				);
			}

			return index;
		}

		public void ResetAddresses() {
			foreach ( var instruction in this.Instructions ) {
				instruction.Address?.Reset();
			}
		}

	}

	private sealed class ScriptParseException : Exception {

		public ScriptParseException(
			string message
		) : base(
			message
		) {
		}

	}

	private sealed class ScriptParser {

		private readonly SedScriptDocument myDocument;
		private readonly List<Instruction> myInstructions;
		private readonly bool myPosix;
		private readonly SedRegularExpressionCompiler myRegularExpressions;
		private readonly bool mySandbox;
		private readonly string myText;
		private int myIndex;

		public ScriptParser(
			SedScriptDocument document,
			bool extendedRegularExpressions,
			bool sandbox,
			bool posix,
			bool nullData,
			ITextLocaleProvider textLocale,
			CancellationToken cancellationToken
		) {
			this.myDocument = document ?? throw new ArgumentNullException(
				nameof( document )
			);
			this.myText = document.Text;
			this.mySandbox = sandbox;
			this.myPosix = posix;
			this.myRegularExpressions = new SedRegularExpressionCompiler(
				extendedRegularExpressions,
				posix,
				nullData,
				textLocale,
				cancellationToken
			);
			this.myInstructions = new List<Instruction>();
		}

		public SedProgram Parse() {
			this.ParseSequence(
				stopAtClosingBrace: false
			);
			this.SkipSeparators();
			if ( this.myIndex != this.myText.Length ) {
				throw this.Error(
					"unexpected script text"
				);
			}
			return new SedProgram(
				this.myInstructions
			);
		}

		private void ParseSequence(
			bool stopAtClosingBrace
		) {
			while ( this.myIndex < this.myText.Length ) {
				this.SkipSeparators();
				if ( this.myIndex >= this.myText.Length ) {
					if ( stopAtClosingBrace ) {
						throw this.Error(
							"unterminated command group"
						);
					}
					return;
				}

				if ( '}' == this.myText[ this.myIndex ] ) {
					if ( !stopAtClosingBrace ) {
						throw this.Error(
							"unexpected closing brace"
						);
					}
					this.myIndex++;
					return;
				}

				if ( '#' == this.myText[ this.myIndex ] ) {
					this.SkipComment();
					continue;
				}

				var selector = this.ParseSelector();
				this.SkipHorizontalWhitespace();

				if ( this.myIndex >= this.myText.Length ) {
					throw this.Error(
						"missing command"
					);
				}

				var command = this.myText[ this.myIndex ];
				switch ( command ) {
					case '#':
						if ( null != selector ) {
							throw this.Error(
								"comments cannot have addresses"
							);
						}
						this.SkipComment();
						break;

					case ':':
						if ( null != selector ) {
							throw this.Error(
								"labels cannot have addresses"
							);
						}
						this.myIndex++;
						this.myInstructions.Add(
							new Instruction(
								InstructionKind.Label,
								argument: this.ReadSimpleArgument()
							)
						);
						break;

					case '{': {
							this.myIndex++;
							var begin = new Instruction(
								InstructionKind.BeginGroup,
								selector
							);
							this.myInstructions.Add(
								begin
							);
							this.ParseSequence(
								stopAtClosingBrace: true
							);
							this.myInstructions.Add(
								new Instruction(
									InstructionKind.EndGroup
								)
							);
							begin.JumpIndex = this.myInstructions.Count;
							break;
						}

					case '=':
						this.RequireAtMostOneAddress(
							selector,
							command
						);
						this.myIndex++;
						this.myInstructions.Add(
							new Instruction(
								InstructionKind.LineNumber,
								selector
							)
						);
						this.RequireBoundary();
						break;

					case 'a':
						this.RequireAtMostOneAddress(
							selector,
							command
						);
						this.myIndex++;
						this.myInstructions.Add(
							new Instruction(
								InstructionKind.AppendText,
								selector,
								this.ReadTextArgument()
							)
						);
						break;

					case 'b':
						this.myIndex++;
						this.myInstructions.Add(
							new Instruction(
								InstructionKind.Branch,
								selector,
								this.ReadSimpleArgument()
							)
						);
						break;

					case 'c':
						this.myIndex++;
						this.myInstructions.Add(
							new Instruction(
								InstructionKind.ChangeText,
								selector,
								this.ReadTextArgument()
							)
						);
						break;

					case 'd':
						this.myIndex++;
						this.myInstructions.Add(
							new Instruction(
								InstructionKind.Delete,
								selector
							)
						);
						this.RequireBoundary();
						break;

					case 'D':
						this.myIndex++;
						this.myInstructions.Add(
							new Instruction(
								InstructionKind.DeleteFirst,
								selector
							)
						);
						this.RequireBoundary();
						break;

					case 'e':
						this.RequireGnuExtension(
							command
						);
						this.RequireFileAccess();
						this.myIndex++;
						this.myInstructions.Add(
							new Instruction(
								InstructionKind.Execute,
								selector,
								this.ReadSimpleArgument()
							)
						);
						break;

					case 'g':
						this.myIndex++;
						this.myInstructions.Add(
							new Instruction(
								InstructionKind.GetHold,
								selector
							)
						);
						this.RequireBoundary();
						break;

					case 'G':
						this.myIndex++;
						this.myInstructions.Add(
							new Instruction(
								InstructionKind.AppendHold,
								selector
							)
						);
						this.RequireBoundary();
						break;

					case 'h':
						this.myIndex++;
						this.myInstructions.Add(
							new Instruction(
								InstructionKind.SetHold,
								selector
							)
						);
						this.RequireBoundary();
						break;

					case 'H':
						this.myIndex++;
						this.myInstructions.Add(
							new Instruction(
								InstructionKind.AppendHold,
								selector,
								argument: true
							)
						);
						this.RequireBoundary();
						break;

					case 'i':
						this.RequireAtMostOneAddress(
							selector,
							command
						);
						this.myIndex++;
						this.myInstructions.Add(
							new Instruction(
								InstructionKind.Print,
								selector,
								new InsertArgument(
									this.ReadTextArgument()
								)
							)
						);
						break;

					case 'l':
						this.myIndex++;
						this.SkipHorizontalWhitespace();
						var listWidth = this.ReadOptionalInteger();
						if (
							this.myPosix
							&& listWidth.HasValue
						) {
							throw this.Error(
								"the l command width is not available in POSIX mode"
							);
						}
						this.myInstructions.Add(
							new Instruction(
								InstructionKind.List,
								selector,
								listWidth
							)
						);
						this.RequireBoundary();
						break;

					case 'n':
						this.myIndex++;
						this.myInstructions.Add(
							new Instruction(
								InstructionKind.Next,
								selector
							)
						);
						this.RequireBoundary();
						break;

					case 'N':
						this.myIndex++;
						this.myInstructions.Add(
							new Instruction(
								InstructionKind.AppendNext,
								selector
							)
						);
						this.RequireBoundary();
						break;

					case 'p':
						this.myIndex++;
						this.myInstructions.Add(
							new Instruction(
								InstructionKind.Print,
								selector
							)
						);
						this.RequireBoundary();
						break;

					case 'P':
						this.myIndex++;
						this.myInstructions.Add(
							new Instruction(
								InstructionKind.PrintFirst,
								selector
							)
						);
						this.RequireBoundary();
						break;

					case 'q':
						this.RequireAtMostOneAddress(
							selector,
							command
						);
						this.myIndex++;
						this.SkipHorizontalWhitespace();
						var quitExitCode = this.ReadOptionalInteger();
						if (
							this.myPosix
							&& quitExitCode.HasValue
						) {
							throw this.Error(
								"the q command exit code is not available in POSIX mode"
							);
						}
						this.myInstructions.Add(
							new Instruction(
								InstructionKind.Quit,
								selector,
								quitExitCode
							)
						);
						this.RequireBoundary();
						break;

					case 'Q':
						this.RequireGnuExtension(
							command
						);
						this.RequireAtMostOneAddress(
							selector,
							command
						);
						this.myIndex++;
						this.SkipHorizontalWhitespace();
						this.myInstructions.Add(
							new Instruction(
								InstructionKind.QuitSilent,
								selector,
								this.ReadOptionalInteger()
							)
						);
						this.RequireBoundary();
						break;

					case 'r':
						this.RequireFileAccess();
						this.RequireAtMostOneAddress(
							selector,
							command
						);
						this.myIndex++;
						this.myInstructions.Add(
							new Instruction(
								InstructionKind.ReadFile,
								selector,
								this.ReadFileArgument()
							)
						);
						break;

					case 'R':
						this.RequireGnuExtension(
							command
						);
						this.RequireFileAccess();
						this.RequireAtMostOneAddress(
							selector,
							command
						);
						this.myIndex++;
						this.myInstructions.Add(
							new Instruction(
								InstructionKind.ReadFileLine,
								selector,
								this.ReadFileArgument()
							)
						);
						break;

					case 's':
						this.myInstructions.Add(
							new Instruction(
								InstructionKind.Substitute,
								selector,
								this.ParseSubstitution()
							)
						);
						break;

					case 't':
						this.myIndex++;
						this.myInstructions.Add(
							new Instruction(
								InstructionKind.TestBranch,
								selector,
								this.ReadSimpleArgument()
							)
						);
						break;

					case 'T':
						this.RequireGnuExtension(
							command
						);
						this.myIndex++;
						this.myInstructions.Add(
							new Instruction(
								InstructionKind.TestNoBranch,
								selector,
								this.ReadSimpleArgument()
							)
						);
						break;

					case 'w':
						this.RequireFileAccess();
						this.myIndex++;
						this.myInstructions.Add(
							new Instruction(
								InstructionKind.WriteFile,
								selector,
								this.ReadFileArgument()
							)
						);
						break;

					case 'W':
						this.RequireGnuExtension(
							command
						);
						this.RequireFileAccess();
						this.myIndex++;
						this.myInstructions.Add(
							new Instruction(
								InstructionKind.WriteFirst,
								selector,
								this.ReadFileArgument()
							)
						);
						break;

					case 'x':
						this.myIndex++;
						this.myInstructions.Add(
							new Instruction(
								InstructionKind.Exchange,
								selector
							)
						);
						this.RequireBoundary();
						break;

					case 'y':
						this.myInstructions.Add(
							new Instruction(
								InstructionKind.Transliterate,
								selector,
								this.ParseTransliteration()
							)
						);
						break;

					default:
						throw this.Error(
							$"unsupported command '{command}'"
						);
				}
			}

			if ( stopAtClosingBrace ) {
				throw this.Error(
					"unterminated command group"
				);
			}
		}

		private AddressSelector? ParseSelector() {
			var save = this.myIndex;
			var first = this.TryParseAddress(
				allowRangeEndSpecialForms: false
			);
			if ( null == first ) {
				this.myIndex = save;
				return null;
			}

			this.SkipHorizontalWhitespace();
			RangeEnd? second = null;
			if (
				this.myIndex < this.myText.Length
				&& ',' == this.myText[ this.myIndex ]
			) {
				this.myIndex++;
				this.SkipHorizontalWhitespace();
				second = this.ParseRangeEnd();
			}

			this.SkipHorizontalWhitespace();
			var negated = false;
			if (
				this.myIndex < this.myText.Length
				&& '!' == this.myText[ this.myIndex ]
			) {
				negated = true;
				this.myIndex++;
			}

			return new AddressSelector(
				first,
				second,
				negated
			);
		}

		private Address? TryParseAddress(
			bool allowRangeEndSpecialForms
		) {
			if ( this.myIndex >= this.myText.Length ) {
				return null;
			}

			var character = this.myText[ this.myIndex ];
			if ( '$' == character ) {
				this.myIndex++;
				return new LastLineAddress();
			}

			if ( char.IsDigit( character ) ) {
				var number = this.ReadInteger(
					allowZero: true
				);
				if (
					this.myIndex < this.myText.Length
					&& '~' == this.myText[ this.myIndex ]
				) {
					if ( this.myPosix ) {
						throw this.Error(
							"step addresses are not available in POSIX mode"
						);
					}
					this.myIndex++;
					var step = this.ReadInteger(
						allowZero: false
					);
					return new StepAddress(
						number,
						step
					);
				}

				if ( 0 == number ) {
					if ( this.myPosix ) {
						throw this.Error(
							"address 0 is not available in POSIX mode"
						);
					}
					return new ZeroAddress();
				}
				return new LineAddress(
					number
				);
			}

			if ( '/' == character ) {
				this.myIndex++;
				var pattern = this.ReadDelimited(
					'/'
				);
				this.ReadAddressRegularExpressionModifiers(
					out var ignoreCase,
					out var multiline
				);
				return this.CreateRegexAddress(
					pattern,
					ignoreCase,
					multiline
				);
			}

			if (
				'\\' == character
				&& this.myIndex + 1 < this.myText.Length
			) {
				this.myIndex++;
				var delimiter = this.myText[ this.myIndex ];
				this.myIndex++;
				var pattern = this.ReadDelimited(
					delimiter
				);
				this.ReadAddressRegularExpressionModifiers(
					out var ignoreCase,
					out var multiline
				);
				return this.CreateRegexAddress(
					pattern,
					ignoreCase,
					multiline
				);
			}

			return null;
		}

		private RangeEnd ParseRangeEnd() {
			if ( this.myIndex >= this.myText.Length ) {
				throw this.Error(
					"missing range end"
				);
			}

			if ( '+' == this.myText[ this.myIndex ] ) {
				if ( this.myPosix ) {
					throw this.Error(
						"relative range addresses are not available in POSIX mode"
					);
				}
				this.myIndex++;
				return new RelativeRangeEnd(
					this.ReadInteger(
						allowZero: true
					)
				);
			}

			if ( '~' == this.myText[ this.myIndex ] ) {
				if ( this.myPosix ) {
					throw this.Error(
						"multiple range addresses are not available in POSIX mode"
					);
				}
				this.myIndex++;
				return new MultipleRangeEnd(
					this.ReadInteger(
						allowZero: false
					)
				);
			}

			var address = this.TryParseAddress(
				allowRangeEndSpecialForms: true
			) ?? throw this.Error(
				"missing range end"
			);
			return new AddressRangeEnd(
				address
			);
		}

		private Substitution ParseSubstitution() {
			this.myIndex++;
			if ( this.myIndex >= this.myText.Length ) {
				throw this.Error(
					"substitution is missing its delimiter"
				);
			}

			var delimiter = this.myText[ this.myIndex ];
			this.myIndex++;

			var pattern = this.ReadDelimited(
				delimiter
			);
			var replacement = this.ReadDelimited(
				delimiter
			);

			var flagStart = this.myIndex;
			while (
				this.myIndex < this.myText.Length
				&& !this.IsCommandSeparator(
					this.myText[ this.myIndex ]
				)
				&& '}' != this.myText[ this.myIndex ]
			) {
				this.myIndex++;
			}

			var flags = this.myText.Substring(
				flagStart,
				this.myIndex - flagStart
			).Trim();

			this.ValidateSubstitutionFlags(
				flags
			);
			var parsedFlags = ParseSubstitutionFlags(
				flags
			);
			var regularExpression = this.CompileRegularExpression(
				pattern,
				SedRegularExpressionContext.Substitution,
				parsedFlags.IgnoreCase,
				parsedFlags.Multiline
			);

			return new Substitution(
				regularExpression,
				replacement,
				flags
			);
		}

		private Transliteration ParseTransliteration() {
			this.myIndex++;
			if ( this.myIndex >= this.myText.Length ) {
				throw this.Error(
					"transliteration is missing its delimiter"
				);
			}

			var delimiter = this.myText[ this.myIndex ];
			this.myIndex++;
			var source = this.ReadDelimited(
				delimiter
			);
			var destination = this.ReadDelimited(
				delimiter
			);
			this.RequireBoundary();
			if (
				ExpandCharacterSet(
					source
				).Length
				!= ExpandCharacterSet(
					destination
				).Length
			) {
				throw this.Error(
					"the y command source and destination must have equal lengths"
				);
			}

			return new Transliteration(
				source,
				destination
			);
		}

		private string ReadDelimited(
			char delimiter
		) {
			var output = new StringBuilder();
			var escaped = false;

			while ( this.myIndex < this.myText.Length ) {
				var character = this.myText[ this.myIndex ];
				this.myIndex++;

				if ( escaped ) {
					if ( delimiter == character ) {
						output.Append(
							character
						);
					} else {
						output.Append(
							'\\'
						);
						output.Append(
							character
						);
					}
					escaped = false;
				} else if ( '\\' == character ) {
					escaped = true;
				} else if ( delimiter == character ) {
					return output.ToString();
				} else {
					output.Append(
						character
					);
				}
			}

			throw this.Error(
				$"unterminated expression using delimiter '{delimiter}'"
			);
		}

		private string ReadTextArgument() {
			this.SkipHorizontalWhitespace();
			if (
				this.myIndex < this.myText.Length
				&& '\\' == this.myText[ this.myIndex ]
			) {
				this.myIndex++;
				if (
					this.myIndex < this.myText.Length
					&& '\r' == this.myText[ this.myIndex ]
				) {
					this.myIndex++;
				}
				if (
					this.myIndex < this.myText.Length
					&& '\n' == this.myText[ this.myIndex ]
				) {
					this.myIndex++;
				}
			}

			return UnescapeSedText(
				this.ReadUntilCommandSeparator()
			);
		}

		private string ReadFileArgument() {
			this.SkipHorizontalWhitespace();
			var output = this.ReadUntilCommandSeparator().Trim();
			if ( 0 == output.Length ) {
				throw this.Error(
					"missing file name"
				);
			}
			return output;
		}

		private string ReadSimpleArgument() {
			this.SkipHorizontalWhitespace();
			return this.ReadUntilCommandSeparator().Trim();
		}

		private string ReadUntilCommandSeparator() {
			var output = new StringBuilder();
			var escaped = false;

			while ( this.myIndex < this.myText.Length ) {
				var character = this.myText[ this.myIndex ];
				if (
					!escaped
					&& (
						this.IsCommandSeparator(
							character
						)
						|| '}' == character
					)
				) {
					break;
				}

				this.myIndex++;
				if ( escaped ) {
					output.Append(
						character
					);
					escaped = false;
				} else if ( '\\' == character ) {
					escaped = true;
					output.Append(
						character
					);
				} else {
					output.Append(
						character
					);
				}
			}

			return output.ToString();
		}

		private int? ReadOptionalInteger() {
			if (
				this.myIndex >= this.myText.Length
				|| !char.IsDigit(
					this.myText[ this.myIndex ]
				)
			) {
				return null;
			}
			return this.ReadInteger(
				allowZero: true
			);
		}

		private int ReadInteger(
			bool allowZero
		) {
			var start = this.myIndex;
			while (
				this.myIndex < this.myText.Length
				&& char.IsDigit(
					this.myText[ this.myIndex ]
				)
			) {
				this.myIndex++;
			}

			if (
				start == this.myIndex
				|| !int.TryParse(
					this.myText.Substring(
						start,
						this.myIndex - start
					),
					NumberStyles.None,
					CultureInfo.InvariantCulture,
					out var output
				)
				|| (
					!allowZero
					&& output <= 0
				)
			) {
				throw this.Error(
					"invalid numeric argument"
				);
			}

			return output;
		}

		private SedCompiledRegularExpression CompileRegularExpression(
			string pattern,
			SedRegularExpressionContext context,
			bool ignoreCase,
			bool multiline
		) {
			try {
				return this.myRegularExpressions.Compile(
					pattern,
					context,
					ignoreCase,
					multiline
				);
			} catch ( ScriptParseException ex ) {
				throw this.Error(
					ex.Message
				);
			}
		}

		private RegexAddress CreateRegexAddress(
			string pattern,
			bool ignoreCase,
			bool multiline
		) {
			return new RegexAddress(
				this.CompileRegularExpression(
					pattern,
					SedRegularExpressionContext.Address,
					ignoreCase,
					multiline
				)
			);
		}

		private void ReadAddressRegularExpressionModifiers(
			out bool ignoreCase,
			out bool multiline
		) {
			ignoreCase = false;
			multiline = false;
			while ( this.myIndex < this.myText.Length ) {
				var modifier = this.myText[ this.myIndex ];
				if ( 'I' == modifier ) {
					if ( this.myPosix ) {
						throw this.Error(
							"regular-expression address modifiers are not available in POSIX mode"
						);
					}
					ignoreCase = true;
					this.myIndex++;
				} else if ( 'M' == modifier ) {
					if ( this.myPosix ) {
						throw this.Error(
							"regular-expression address modifiers are not available in POSIX mode"
						);
					}
					multiline = true;
					this.myIndex++;
				} else {
					break;
				}
			}
		}

		private void RequireGnuExtension(
			char command
		) {
			if ( this.myPosix ) {
				throw this.Error(
					$"command '{command}' is not available in POSIX mode"
				);
			}
		}

		private void ValidateSubstitutionFlags(
			string flags
		) {
			var index = 0;
			var occurrenceSeen = false;
			while ( index < flags.Length ) {
				var character = flags[ index ];
				if ( char.IsWhiteSpace( character ) ) {
					index++;
					continue;
				}
				if ( char.IsDigit( character ) ) {
					if ( occurrenceSeen ) {
						throw this.Error(
							"multiple substitution occurrence numbers"
						);
					}
					occurrenceSeen = true;
					var occurrenceStart = index;
					while (
						index < flags.Length
						&& char.IsDigit( flags[ index ] )
					) {
						index++;
					}
					if (
						!int.TryParse(
							flags.Substring(
								occurrenceStart,
								index - occurrenceStart
							),
							NumberStyles.None,
							CultureInfo.InvariantCulture,
							out var occurrence
						)
						|| occurrence <= 0
					) {
						throw this.Error(
							"substitution occurrence must be a positive integer"
						);
					}
					continue;
				}
				if (
					'g' == character
					|| 'p' == character
				) {
					index++;
					continue;
				}
				if ( 'w' == character ) {
					if ( this.mySandbox ) {
						throw this.Error(
							"the substitution w flag is disabled in sandbox mode"
						);
					}
					index++;
					while (
						index < flags.Length
						&& char.IsWhiteSpace( flags[ index ] )
					) {
						index++;
					}
					if ( index >= flags.Length ) {
						throw this.Error(
							"the substitution w flag requires a file name"
						);
					}
					return;
				}
				if (
					'i' == character
					|| 'I' == character
					|| 'm' == character
					|| 'M' == character
					|| 'e' == character
				) {
					if ( this.myPosix ) {
						throw this.Error(
							$"substitution flag '{character}' is not available in POSIX mode"
						);
					}
					if (
						'e' == character
						&& this.mySandbox
					) {
						throw this.Error(
							"the substitution e flag is disabled in sandbox mode"
						);
					}
					index++;
					continue;
				}
				throw this.Error(
					$"unknown substitution flag '{character}'"
				);
			}
		}

		private void RequireAtMostOneAddress(
			AddressSelector? selector,
			char command
		) {
			if (
				null != selector
				&& selector.HasRange
			) {
				throw this.Error(
					$"command '{command}' accepts at most one address"
				);
			}
		}

		private void RequireFileAccess() {
			if ( this.mySandbox ) {
				throw this.Error(
					"file access commands are disabled in sandbox mode"
				);
			}
		}

		private void RequireBoundary() {
			if (
				this.myIndex < this.myText.Length
				&& !this.IsCommandSeparator(
					this.myText[ this.myIndex ]
				)
				&& '}' != this.myText[ this.myIndex ]
				&& !char.IsWhiteSpace(
					this.myText[ this.myIndex ]
				)
			) {
				throw this.Error(
					"unexpected text after command"
				);
			}
		}

		private void SkipComment() {
			while (
				this.myIndex < this.myText.Length
				&& '\n' != this.myText[ this.myIndex ]
			) {
				this.myIndex++;
			}
		}

		private void SkipHorizontalWhitespace() {
			while (
				this.myIndex < this.myText.Length
				&& (
					' ' == this.myText[ this.myIndex ]
					|| '\t' == this.myText[ this.myIndex ]
				)
			) {
				this.myIndex++;
			}
		}

		private void SkipSeparators() {
			while ( this.myIndex < this.myText.Length ) {
				var character = this.myText[ this.myIndex ];
				if (
					';' == character
					|| '\r' == character
					|| '\n' == character
					|| ' ' == character
					|| '\t' == character
				) {
					this.myIndex++;
				} else {
					break;
				}
			}
		}

		private bool IsCommandSeparator(
			char character
		) {
			return (
				';' == character
				|| '\r' == character
				|| '\n' == character
			);
		}

		private ScriptParseException Error(
			string message
		) {
			var location = this.myDocument.GetLocation( this.myIndex );
			return new ScriptParseException(
				$"{message} at {location.SourceName}:{location.Line}:{location.Column}"
			);
		}

	}


	private sealed class InsertArgument {

		public string Text {
			get;
		}

		public InsertArgument(
			string text
		) {
			this.Text = text;
		}

	}


	private static string UnescapeSedText(
		string value
	) {
		var output = new StringBuilder(
			value.Length
		);
		for (
			var index = 0;
			index < value.Length;
			index++
		) {
			var character = value[ index ];
			if (
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

	private static char UnescapeCharacter(
		char character
	) {
		return character switch {
			'a' => '\a',
			'b' => '\b',
			'f' => '\f',
			'n' => '\n',
			'r' => '\r',
			't' => '\t',
			'v' => '\v',
			_ => character
		};
	}


	private static async Task<string> ReadScriptFileAsync(
		string path,
		CancellationToken cancellationToken
	) {
		using ( var reader = new StreamReader(
			new FileStream(
				path,
				FileMode.Open,
				FileAccess.Read,
				FileShare.Read,
				8192,
				useAsync: true
			),
			Encoding.UTF8,
			detectEncodingFromByteOrderMarks: true,
			bufferSize: 8192,
			leaveOpen: false
		) ) {
			cancellationToken.ThrowIfCancellationRequested();
			return await reader.ReadToEndAsync(
				cancellationToken
			).ConfigureAwait( false );
		}
	}


}
