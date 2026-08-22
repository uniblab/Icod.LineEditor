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

// Responsibility: command-cycle execution and deferred output.
public static partial class Command {

	private enum DeferredOutputKind {
		Text,
		File
	}

	private sealed class DeferredOutputItem {

		public DeferredOutputKind Kind {
			get;
		}

		public bool Terminate {
			get;
		}

		public string Value {
			get;
		}

		public DeferredOutputItem(
			DeferredOutputKind kind,
			string value,
			bool terminate
		) {
			this.Kind = kind;
			this.Value = value;
			this.Terminate = terminate;
		}

	}

	private sealed class OutputFile : IDisposable {

		public SedOutputWriter Writer {
			get;
		}

		private readonly Stream myStream;

		public OutputFile(
			Stream stream,
			SedOutputWriter writer
		) {
			this.myStream = stream;
			this.Writer = writer;
		}

		public void Dispose() {
			this.myStream.Dispose();
		}

	}

	private sealed class ExecutionEnvironment : IDisposable {

		private readonly List<DeferredOutputItem> myDeferredOutput;
		private readonly Dictionary<string, AsyncRecordReader> myReadLineFiles;
		private readonly Dictionary<string, OutputFile> myWriteFiles;

		public bool Debug {
			get;
		}

		public ISedAuxiliaryFileCapability AuxiliaryFiles {
			get;
		}

		public TextWriter Error {
			get;
		}

		public string HoldSpace {
			get;
			set;
		} = string.Empty;

		public bool HoldSpaceTerminated {
			get;
			set;
		} = true;

		public int ListWidth {
			get;
		}

		public bool NullData {
			get;
		}

		public char PatternSeparator => this.NullData ? '\0' : '\n';

		public SedOutputWriter Output {
			get;
		}

		public bool SuppressAutomaticPrint {
			get;
		}

		public ISedShellCapability Shell {
			get;
		}

		public SedTextCodec TextCodec {
			get;
		}

		public ExecutionEnvironment(
			Stream output,
			SedTextCodec textCodec,
			TextWriter error,
			bool suppressAutomaticPrint,
			bool nullData,
			int listWidth,
			bool debug,
			bool unbuffered,
			ISedShellCapability shell,
			ISedAuxiliaryFileCapability auxiliaryFiles
		) : this(
			new SedOutputWriter( output, textCodec, nullData ) {
				AutoFlush = unbuffered
			},
			textCodec,
			error,
			suppressAutomaticPrint,
			nullData,
			listWidth,
			debug,
			shell,
			auxiliaryFiles
		) {
		}

		public ExecutionEnvironment(
			SedOutputWriter output,
			SedTextCodec textCodec,
			TextWriter error,
			bool suppressAutomaticPrint,
			bool nullData,
			int listWidth,
			bool debug,
			ISedShellCapability shell,
			ISedAuxiliaryFileCapability auxiliaryFiles
		) {
			this.TextCodec = textCodec ?? throw new ArgumentNullException( nameof( textCodec ) );
			this.Output = output ?? throw new ArgumentNullException( nameof( output ) );
			this.Error = error ?? throw new ArgumentNullException( nameof( error ) );
			this.Shell = shell ?? throw new ArgumentNullException( nameof( shell ) );
			this.AuxiliaryFiles = auxiliaryFiles ?? throw new ArgumentNullException( nameof( auxiliaryFiles ) );
			this.SuppressAutomaticPrint = suppressAutomaticPrint;
			this.Debug = debug;
			this.NullData = nullData;
			this.ListWidth = listWidth;
			this.myDeferredOutput = new List<DeferredOutputItem>();
			this.myReadLineFiles = new Dictionary<string, AsyncRecordReader>( StringComparer.Ordinal );
			this.myWriteFiles = new Dictionary<string, OutputFile>( StringComparer.Ordinal );
		}

		public void ClearDeferredOutput() {
			this.myDeferredOutput.Clear();
		}

		public void Defer(
			string value,
			bool terminate = true
		) {
			this.myDeferredOutput.Add(
				new DeferredOutputItem( DeferredOutputKind.Text, value, terminate )
			);
		}

		public void DeferFile(
			string fileName
		) {
			this.myDeferredOutput.Add(
				new DeferredOutputItem( DeferredOutputKind.File, fileName, terminate: false )
			);
		}

		public async Task DeferFileLineAsync(
			string fileName,
			CancellationToken cancellationToken
		) {
			try {
				if ( !this.myReadLineFiles.TryGetValue( fileName, out var reader ) ) {
					var stream = await this.AuxiliaryFiles.OpenReadAsync(
						fileName,
						cancellationToken
					).ConfigureAwait( false );
					reader = new AsyncRecordReader(
						stream,
						this.NullData,
						ownsStream: true,
						this.TextCodec,
						new SedInputSourceIdentity( 0, fileName, isStandardInput: false )
					);
					this.myReadLineFiles.Add( fileName, reader );
				}
				var line = await reader.ReadAsync( 1, cancellationToken ).ConfigureAwait( false );
				if ( null != line ) {
					this.Defer( line.Text, line.IsTerminated );
				}
			} catch ( OperationCanceledException ) {
				throw;
			} catch ( SedCapabilityDeniedException ) {
				throw;
			} catch ( Exception ex ) {
				await this.Error.WriteLineAsync( $"sed: {fileName}: {ex.Message}" ).ConfigureAwait( false );
			}
		}

		public async Task FlushDeferredOutputAsync(
			CancellationToken cancellationToken
		) {
			foreach ( var item in this.myDeferredOutput ) {
				cancellationToken.ThrowIfCancellationRequested();
				if ( DeferredOutputKind.Text == item.Kind ) {
					await this.Output.WriteRecordAsync( item.Value, item.Terminate, cancellationToken ).ConfigureAwait( false );
					continue;
				}
				try {
					await this.Output.BeginOutputAsync( cancellationToken ).ConfigureAwait( false );
					using var stream = await this.AuxiliaryFiles.OpenReadAsync(
						item.Value,
						cancellationToken
					).ConfigureAwait( false );
					using var reader = new AsyncRecordReader(
						stream,
						this.NullData,
						ownsStream: false,
						this.TextCodec,
						new SedInputSourceIdentity( 0, item.Value, isStandardInput: false )
					);
					long recordNumber = 0;
					SedInputRecord? record;
					while ( null != ( record = await reader.ReadAsync( ++recordNumber, cancellationToken ).ConfigureAwait( false ) ) ) {
						await this.Output.WriteRecordAsync( record.Text, record.IsTerminated, cancellationToken ).ConfigureAwait( false );
					}
				} catch ( OperationCanceledException ) {
					throw;
				} catch ( SedCapabilityDeniedException ) {
					throw;
				} catch ( Exception ex ) {
					await this.Error.WriteLineAsync( $"sed: {item.Value}: {ex.Message}" ).ConfigureAwait( false );
				}
			}
			this.myDeferredOutput.Clear();
		}

		public async Task WriteFileAsync(
			string fileName,
			string value,
			bool terminate,
			CancellationToken cancellationToken
		) {
			if ( !this.myWriteFiles.TryGetValue( fileName, out var outputFile ) ) {
				var stream = await this.AuxiliaryFiles.OpenWriteAsync(
					fileName,
					cancellationToken
				).ConfigureAwait( false );
				outputFile = new OutputFile(
					stream,
					new SedOutputWriter( stream, this.TextCodec, this.NullData )
				);
				this.myWriteFiles.Add( fileName, outputFile );
			}
			await outputFile.Writer.WriteRecordAsync( value, terminate, cancellationToken ).ConfigureAwait( false );
			await outputFile.Writer.FlushAsync( cancellationToken ).ConfigureAwait( false );
		}

		public async Task DisposeAsync(
			CancellationToken cancellationToken
		) {
			foreach ( var outputFile in this.myWriteFiles.Values ) {
				await outputFile.Writer.FlushAsync( cancellationToken ).ConfigureAwait( false );
			}
			await this.Output.FlushAsync( cancellationToken ).ConfigureAwait( false );
			this.Dispose();
		}

		public void Dispose() {
			foreach ( var reader in this.myReadLineFiles.Values ) {
				reader.Dispose();
			}
			this.myReadLineFiles.Clear();
			foreach ( var writer in this.myWriteFiles.Values ) {
				writer.Dispose();
			}
			this.myWriteFiles.Clear();
		}

	}

	/// <summary>Reports whether execution requested termination and its exit status.</summary>
	internal sealed class ExecutionResult {

		/// <summary>Gets the requested process exit status.</summary>
		public int ExitCode {
			get;
		}

		/// <summary>Gets whether execution requested command termination.</summary>
		public bool Quit {
			get;
		}

		/// <summary>Initializes one execution result.</summary>
		public ExecutionResult(
			bool quit,
			int exitCode
		) {
			this.Quit = quit;
			this.ExitCode = exitCode;
		}

	}

	private static async Task<ExecutionResult> ExecuteAsync(
		SedProgram program,
		InputSequence input,
		ExecutionEnvironment environment,
		CancellationToken cancellationToken
	) {
		program.ResetAddresses();

		while (
			await input.MoveNextAsync(
				cancellationToken
			).ConfigureAwait( false )
		) {
			var patternSpace = input.Current.Text;
			var patternTerminated = input.Current.IsTerminated;
			if ( environment.Debug ) {
				await environment.Error.WriteLineAsync(
					$"INPUT:   {input.LineNumber}"
				).ConfigureAwait( false );
				await environment.Error.WriteLineAsync(
					$"PATTERN: {EscapeDebugText( patternSpace )}"
				).ConfigureAwait( false );
			}
			var substitutionSucceeded = false;
			var automaticPrint = true;
			var programCounter = 0;
			environment.ClearDeferredOutput();

			while ( programCounter < program.Instructions.Count ) {
				cancellationToken.ThrowIfCancellationRequested();

				var instruction = program.Instructions[ programCounter ];
				if ( InstructionKind.Label == instruction.Kind ) {
					programCounter++;
					continue;
				} else if ( InstructionKind.EndGroup == instruction.Kind ) {
					programCounter++;
					continue;
				}

				var context = new AddressContext(
					input.LineNumber,
					input.IsLast,
					patternSpace,
					cancellationToken
				);
				var selection = instruction.Address?.Evaluate(
					context
				) ?? new Selection(
					isSelected: true,
					rangeStarted: false,
					rangeEnded: false
				);

				if ( InstructionKind.BeginGroup == instruction.Kind ) {
					programCounter = selection.IsSelected
						? programCounter + 1
						: instruction.JumpIndex
					;
					continue;
				}

				if ( !selection.IsSelected ) {
					programCounter++;
					continue;
				}

				switch ( instruction.Kind ) {
					case InstructionKind.AppendText: {
							environment.Defer(
								instruction.Argument as string
									?? string.Empty
							);
							programCounter++;
							break;
						}

					case InstructionKind.AppendHold: {
							if ( instruction.Argument is bool ) {
								environment.HoldSpace = string.Concat(
									environment.HoldSpace,
									environment.PatternSeparator.ToString(),
									patternSpace
								);
								environment.HoldSpaceTerminated = patternTerminated;
							} else {
								patternSpace = string.Concat(
									patternSpace,
									environment.PatternSeparator.ToString(),
									environment.HoldSpace
								);
								patternTerminated = environment.HoldSpaceTerminated;
							}
							programCounter++;
							break;
						}

					case InstructionKind.AppendNext: {
							if (
								!await input.MoveNextAsync(
									cancellationToken
								).ConfigureAwait( false )
							) {
								if (
									automaticPrint
									&& !environment.SuppressAutomaticPrint
								) {
									await WriteRecordAsync(
										environment.Output,
										patternSpace,
										patternTerminated,
										cancellationToken
									).ConfigureAwait( false );
								}
								await environment.FlushDeferredOutputAsync(
									cancellationToken
								).ConfigureAwait( false );
								return new ExecutionResult(
									quit: false,
									exitCode: 0
								);
							}
							patternSpace = string.Concat(
								patternSpace,
								environment.PatternSeparator.ToString(),
								input.Current.Text
							);
							patternTerminated = input.Current.IsTerminated;
							programCounter++;
							break;
						}

					case InstructionKind.Branch: {
							programCounter = program.ResolveLabel(
								instruction.Argument as string
							);
							break;
						}

					case InstructionKind.ChangeText: {
							if (
								null == instruction.Address
								|| !instruction.Address.HasRange
								|| instruction.Address.Negated
								|| selection.RangeStarted
							) {
								await WriteRecordAsync(
									environment.Output,
									instruction.Argument as string
										?? string.Empty,
									terminate: true,
									cancellationToken
								).ConfigureAwait( false );
							}
							automaticPrint = false;
							programCounter = program.Instructions.Count;
							break;
						}

					case InstructionKind.Delete: {
							automaticPrint = false;
							programCounter = program.Instructions.Count;
							break;
						}

					case InstructionKind.DeleteFirst: {
							var newline = patternSpace.IndexOf(
								environment.PatternSeparator
							);
							if ( newline < 0 ) {
								automaticPrint = false;
								programCounter = program.Instructions.Count;
							} else {
								patternSpace = patternSpace.Substring(
									newline + 1
								);
								substitutionSucceeded = false;
								programCounter = 0;
							}
							break;
						}

					case InstructionKind.Execute: {
							var commandText = instruction.Argument as string;
							if ( string.IsNullOrWhiteSpace( commandText ) ) {
								commandText = patternSpace;
							}
							var shellResult = await ExecuteShellAsync(
								commandText,
								environment,
								captureStandardOutput: false,
								cancellationToken
							).ConfigureAwait( false );
							if ( shellResult.ExitCode != 0 ) {
								await environment.Error.WriteLineAsync(
									$"sed: command exited with status {shellResult.ExitCode}"
								).ConfigureAwait( false );
							}
							programCounter++;
							break;
						}

					case InstructionKind.Exchange: {
							var value = patternSpace;
							var valueTerminated = patternTerminated;
							patternSpace = environment.HoldSpace;
							patternTerminated = environment.HoldSpaceTerminated;
							environment.HoldSpace = value;
							environment.HoldSpaceTerminated = valueTerminated;
							programCounter++;
							break;
						}

					case InstructionKind.GetHold: {
							patternSpace = environment.HoldSpace;
							patternTerminated = environment.HoldSpaceTerminated;
							programCounter++;
							break;
						}

					case InstructionKind.LineNumber: {
							await WriteRecordAsync(
								environment.Output,
								input.LineNumber.ToString(
									CultureInfo.InvariantCulture
								),
								terminate: true,
								cancellationToken
							).ConfigureAwait( false );
							programCounter++;
							break;
						}

					case InstructionKind.List: {
							var width = instruction.Argument is int configuredWidth
								? configuredWidth
								: environment.ListWidth
							;
							await WriteRecordAsync(
								environment.Output,
								FormatList(
									patternSpace,
									width
								),
								terminate: true,
								cancellationToken
							).ConfigureAwait( false );
							programCounter++;
							break;
						}

					case InstructionKind.Next: {
							if ( !environment.SuppressAutomaticPrint ) {
								await WriteRecordAsync(
									environment.Output,
									patternSpace,
									patternTerminated,
									cancellationToken
								).ConfigureAwait( false );
							}
							await environment.FlushDeferredOutputAsync(
								cancellationToken
							).ConfigureAwait( false );
							if (
								!await input.MoveNextAsync(
									cancellationToken
								).ConfigureAwait( false )
							) {
								return new ExecutionResult(
									quit: false,
									exitCode: 0
								);
							}
							patternSpace = input.Current.Text;
							patternTerminated = input.Current.IsTerminated;
							substitutionSucceeded = false;
							programCounter++;
							break;
						}

					case InstructionKind.Print: {
							if ( instruction.Argument is InsertArgument insert ) {
								await WriteRecordAsync(
									environment.Output,
									insert.Text,
									terminate: true,
									cancellationToken
								).ConfigureAwait( false );
							} else {
								await WriteRecordAsync(
									environment.Output,
									patternSpace,
									patternTerminated,
									cancellationToken
								).ConfigureAwait( false );
							}
							programCounter++;
							break;
						}

					case InstructionKind.PrintFirst: {
							await WriteRecordAsync(
								environment.Output,
								FirstPatternLine(
									patternSpace,
									environment.PatternSeparator
								),
								0 <= patternSpace.IndexOf( environment.PatternSeparator ) || patternTerminated,
								cancellationToken
							).ConfigureAwait( false );
							programCounter++;
							break;
						}

					case InstructionKind.Quit: {
							if ( !environment.SuppressAutomaticPrint ) {
								await WriteRecordAsync(
									environment.Output,
									patternSpace,
									patternTerminated,
									cancellationToken
								).ConfigureAwait( false );
							}
							await environment.FlushDeferredOutputAsync(
								cancellationToken
							).ConfigureAwait( false );
							return new ExecutionResult(
								quit: true,
								exitCode: instruction.Argument is int configuredExitCode
									? configuredExitCode
									: 0
							);
						}

					case InstructionKind.QuitSilent: {
							return new ExecutionResult(
								quit: true,
								exitCode: instruction.Argument is int configuredExitCode
									? configuredExitCode
									: 0
							);
						}

					case InstructionKind.ReadFile: {
							environment.DeferFile(
								instruction.Argument as string
									?? string.Empty
							);
							programCounter++;
							break;
						}

					case InstructionKind.ReadFileLine: {
							await environment.DeferFileLineAsync(
								instruction.Argument as string
									?? string.Empty,
								cancellationToken
							).ConfigureAwait( false );
							programCounter++;
							break;
						}

					case InstructionKind.SetHold: {
							environment.HoldSpace = patternSpace;
							environment.HoldSpaceTerminated = patternTerminated;
							programCounter++;
							break;
						}

					case InstructionKind.Substitute: {
							var substitution = instruction.Argument as Substitution
								?? throw new InvalidOperationException()
							;
							var result = ApplySubstitution(
								patternSpace,
								substitution,
								out var replaced,
								cancellationToken
							);
							if ( replaced ) {
								patternSpace = result;
								substitutionSucceeded = true;
								var flags = ParseSubstitutionFlags(
									substitution.Flags
								);
								if ( flags.Execute ) {
									var shellResult = await ExecuteShellAsync(
										patternSpace,
										environment,
										captureStandardOutput: true,
										cancellationToken
									).ConfigureAwait( false );
									patternSpace = shellResult.StandardOutput.TrimEnd(
										'\r',
										'\n'
									);
									if ( shellResult.ExitCode != 0 ) {
										await environment.Error.WriteLineAsync(
											$"sed: command exited with status {shellResult.ExitCode}"
										).ConfigureAwait( false );
									}
								}
								if ( flags.Print ) {
									await WriteRecordAsync(
										environment.Output,
										patternSpace,
										patternTerminated,
										cancellationToken
									).ConfigureAwait( false );
								}
								if ( !string.IsNullOrEmpty( flags.WriteFile ) ) {
									await environment.WriteFileAsync(
										flags.WriteFile,
										patternSpace,
										patternTerminated,
										cancellationToken
									).ConfigureAwait( false );
								}
							}
							programCounter++;
							break;
						}

					case InstructionKind.TestBranch: {
							var branch = substitutionSucceeded;
							substitutionSucceeded = false;
							programCounter = branch
								? program.ResolveLabel(
									instruction.Argument as string
								)
								: programCounter + 1
							;
							break;
						}

					case InstructionKind.TestNoBranch: {
							var branch = !substitutionSucceeded;
							substitutionSucceeded = false;
							programCounter = branch
								? program.ResolveLabel(
									instruction.Argument as string
								)
								: programCounter + 1
							;
							break;
						}

					case InstructionKind.Transliterate: {
							patternSpace = Transliterate(
								patternSpace,
								instruction.Argument as Transliteration
									?? throw new InvalidOperationException()
							);
							programCounter++;
							break;
						}

					case InstructionKind.WriteFile: {
							await environment.WriteFileAsync(
								instruction.Argument as string
									?? string.Empty,
								patternSpace,
								patternTerminated,
								cancellationToken
							).ConfigureAwait( false );
							programCounter++;
							break;
						}

					case InstructionKind.WriteFirst: {
							await environment.WriteFileAsync(
								instruction.Argument as string
									?? string.Empty,
								FirstPatternLine(
									patternSpace,
									environment.PatternSeparator
								),
								0 <= patternSpace.IndexOf( environment.PatternSeparator ) || patternTerminated,
								cancellationToken
							).ConfigureAwait( false );
							programCounter++;
							break;
						}

					default:
						throw new InvalidOperationException(
							$"Unhandled instruction {instruction.Kind}."
						);
				}
			}

			if (
				automaticPrint
				&& !environment.SuppressAutomaticPrint
			) {
				await WriteRecordAsync(
					environment.Output,
					patternSpace,
					patternTerminated,
					cancellationToken
				).ConfigureAwait( false );
			}
			await environment.FlushDeferredOutputAsync(
				cancellationToken
			).ConfigureAwait( false );
		}

		return new ExecutionResult(
			quit: false,
			exitCode: 0
		);
	}

	private static string EscapeDebugText(
		string value
	) {
		return value
			.Replace( "\\", "\\\\", StringComparison.Ordinal )
			.Replace( "\r", "\\r", StringComparison.Ordinal )
			.Replace( "\n", "\\n", StringComparison.Ordinal )
			.Replace( "\0", "\\0", StringComparison.Ordinal )
		;
	}



	private static string FormatList(
		string value,
		int width
	) {
		var escaped = new StringBuilder();
		foreach ( var character in value ) {
			switch ( character ) {
				case '\\':
					escaped.Append(
						"\\\\"
					);
					break;
				case '\a':
					escaped.Append(
						"\\a"
					);
					break;
				case '\b':
					escaped.Append(
						"\\b"
					);
					break;
				case '\f':
					escaped.Append(
						"\\f"
					);
					break;
				case '\0':
					escaped.Append(
						"\\000"
					);
					break;
				case '\n':
					escaped.Append(
						"\\n"
					);
					break;
				case '\r':
					escaped.Append(
						"\\r"
					);
					break;
				case '\t':
					escaped.Append(
						"\\t"
					);
					break;
				default:
					if (
						char.IsControl(
							character
						)
					) {
						escaped.AppendFormat(
							CultureInfo.InvariantCulture,
							"\\x{0:X2}",
							(int)character
						);
					} else {
						escaped.Append(
							character
						);
					}
					break;
			}
		}
		escaped.Append(
			'$'
		);

		if (
			width <= 0
			|| escaped.Length <= width
		) {
			return escaped.ToString();
		}

		var output = new StringBuilder();
		var index = 0;
		while ( index < escaped.Length ) {
			var count = Math.Min(
				width,
				escaped.Length - index
			);
			output.Append(
				escaped,
				index,
				count
			);
			index += count;
			if ( index < escaped.Length ) {
				output.Append(
					"\\\n"
				);
			}
		}
		return output.ToString();
	}

	private static string FirstPatternLine(
		string patternSpace,
		char separator
	) {
		var index = patternSpace.IndexOf(
			separator
		);
		return index < 0
			? patternSpace
			: patternSpace.Substring(
				0,
				index
			)
		;
	}


}
