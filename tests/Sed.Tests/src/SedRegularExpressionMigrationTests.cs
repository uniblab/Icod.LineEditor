namespace Icod.LineEditor.Sed.Tests;

using System.Globalization;
using SedCommand = Icod.LineEditor.Sed.Command;
using Xunit;

/// <summary>
/// Verifies the Phase LE3 migration from the private .NET translator to the Shared GNU regular-expression provider.
/// </summary>
[Collection( "Sed environment" )]
public sealed class SedRegularExpressionMigrationTests {

	/// <summary>
	/// Exercises a GNU Sed differential corpus whose expected outputs were established with GNU sed 4.10.
	/// </summary>
	/// <param name="option">An optional syntax-selection option.</param>
	/// <param name="script">The Sed program.</param>
	/// <param name="input">The source text.</param>
	/// <param name="expected">The expected edited text.</param>
	[Theory]
	[InlineData( "", "s/^\\(a*\\)\\(b*\\)$/\\2-\\1/", "aaabb\n", "bb-aaa\n" )]
	[InlineData( "-E", "s/(a|ab)/X/", "ab\n", "X\n" )]
	[InlineData( "", "s/x*/X/g", "abc\n", "XaXbXcX\n" )]
	[InlineData( "", "s/a*/X/g", "ab\n", "XbX\n" )]
	[InlineData( "", "s/b*/X/g", "ab\n", "XaX\n" )]
	[InlineData( "", "s/[a-z]*/X/g", "abc\n", "X\n" )]
	public async Task GnuSedDifferentialCorpusMatches(
		string option,
		string script,
		string input,
		string expected
	) {
		var args = string.IsNullOrEmpty( option )
			? new string[] { script }
			: new string[] { option, script }
		;
		var result = await RunAsync(
			args,
			input
		);

		Assert.Equal( 0, result.ExitCode );
		Assert.Equal( expected, result.Output );
		Assert.Equal( string.Empty, result.Error );
	}

	/// <summary>
	/// Verifies that address modifiers are compiled once and retained when an empty substitution reuses the expression.
	/// </summary>
	[Fact]
	public async Task EmptyExpressionReusesCompiledAddressIncludingModifiers() {
		var result = await RunAsync(
			new string[] { "-n", "/ALPHA/I{s//X/;p;}" },
			"alpha\nbeta\n"
		);

		Assert.Equal( 0, result.ExitCode );
		Assert.Equal( "X\n", result.Output );
	}

	/// <summary>
	/// Verifies GNU Sed escape processing before BRE/ERE parsing.
	/// </summary>
	/// <param name="script">The Sed substitution program.</param>
	/// <param name="input">The source text.</param>
	/// <param name="expected">The expected edited text.</param>
	[Theory]
	[InlineData( @"s/\t/X/", "\t\n", "X\n" )]
	[InlineData( @"s/\d065/X/", "A\n", "X\n" )]
	[InlineData( @"s/\o101/X/", "A\n", "X\n" )]
	[InlineData( @"s/\x41/X/", "A\n", "X\n" )]
	[InlineData( @"s/\cA/X/", "\u0001\n", "X\n" )]
	[InlineData( @"s/\x5ba\x5d/X/", "a\n", "X\n" )]
	public async Task GnuSedEscapesAreExpandedBeforeRegularExpressionParsing(
		string script,
		string input,
		string expected
	) {
		var result = await RunAsync(
			new string[] { script },
			input
		);

		Assert.Equal( 0, result.ExitCode );
		Assert.Equal( expected, result.Output );
		Assert.Equal( string.Empty, result.Error );
	}

	/// <summary>
	/// Verifies that the GNU newline escape can match embedded pattern-space separators.
	/// </summary>
	[Fact]
	public async Task NewlineEscapeMatchesEmbeddedPatternSpaceSeparator() {
		var result = await RunAsync(
			new string[] { "-n", @"N;s/^\(.*\)\n\1$/same/p" },
			"repeat\nrepeat\n"
		);

		Assert.Equal( 0, result.ExitCode );
		Assert.Equal( "same\n", result.Output );
		Assert.Equal( string.Empty, result.Error );
	}

	/// <summary>
	/// Verifies that strict POSIX mode disables GNU escape processing only inside raw bracket expressions.
	/// </summary>
	[Fact]
	public async Task PosixModeDisablesGnuEscapesInsideRawBracketExpressions() {
		var defaultResult = await RunAsync(
			new string[] { @"s/[\t]/X/" },
			"\t\n"
		);
		var posixBracketResult = await RunAsync(
			new string[] { "--posix", @"s/[\t]/X/" },
			"\t\n"
		);
		var posixOutsideResult = await RunAsync(
			new string[] { "--posix", @"s/\t/X/" },
			"\t\n"
		);
		var generatedBracketResult = await RunAsync(
			new string[] { "--posix", @"s/\x5b\t\x5d/X/" },
			"\t\n"
		);

		Assert.Equal( 0, defaultResult.ExitCode );
		Assert.Equal( "X\n", defaultResult.Output );
		Assert.Equal( 0, posixBracketResult.ExitCode );
		Assert.Equal( "\t\n", posixBracketResult.Output );
		Assert.Equal( 0, posixOutsideResult.ExitCode );
		Assert.Equal( "X\n", posixOutsideResult.Output );
		Assert.Equal( 0, generatedBracketResult.ExitCode );
		Assert.Equal( "X\n", generatedBracketResult.Output );
	}

	/// <summary>
	/// Verifies GNU Sed's rule that modifiers cannot be attached to an empty expression.
	/// </summary>
	[Fact]
	public async Task EmptyExpressionRejectsNewModifiers() {
		var result = await RunAsync(
			new string[] { "s/a/A/;s//X/I" },
			"a\n"
		);

		Assert.Equal( 2, result.ExitCode );
		Assert.Contains( "cannot specify modifiers on an empty regular expression", result.Error );
	}

	/// <summary>
	/// Verifies that GNU multiline mode affects anchors inside a multiline pattern space.
	/// </summary>
	[Fact]
	public async Task MultilineModifierUsesSharedLineSensitiveMatching() {
		var result = await RunAsync(
			new string[] { "-n", "N;s/^two$/X/M;p" },
			"one\ntwo\n"
		);

		Assert.Equal( 0, result.ExitCode );
		Assert.Equal( "one\nX\n", result.Output );
	}

	/// <summary>
	/// Verifies that invariant process culture does not override an explicit UTF-8 locale profile.
	/// </summary>
	[Fact]
	public async Task InvariantCultureRetainsUtf8LocaleCharacterClasses() {
		var originalLocale = CaptureLocale();
		var originalCulture = CultureInfo.CurrentCulture;
		var originalUiCulture = CultureInfo.CurrentUICulture;
		try {
			SetLocale( "C.UTF-8" );
			CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
			CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
			var result = await RunAsync(
				new string[] { "s/[[:alpha:]]/X/g" },
				"éA\n"
			);

			Assert.Equal( 0, result.ExitCode );
			Assert.Equal( "XX\n", result.Output );
		} finally {
			CultureInfo.CurrentCulture = originalCulture;
			CultureInfo.CurrentUICulture = originalUiCulture;
			RestoreLocale( originalLocale );
		}
	}

	/// <summary>
	/// Verifies that POSIX mode treats GNU-only escaped BRE operators as literals.
	/// </summary>
	[Fact]
	public async Task PosixModeDisablesGnuBasicOperators() {
		var result = await RunAsync(
			new string[] { "--posix", @"s/a\+/X/" },
			"a+\naaa\n"
		);

		Assert.Equal( 0, result.ExitCode );
		Assert.Equal( "X\naaa\n", result.Output );
	}

	/// <summary>
	/// Verifies that Shared compile diagnostics are translated into Sed usage diagnostics.
	/// </summary>
	[Fact]
	public async Task SharedCompileDiagnosticUsesSedPresentation() {
		var result = await RunAsync(
			new string[] { "s/[a-/X/" },
			"a\n"
		);

		Assert.Equal( 2, result.ExitCode );
		Assert.Contains( "invalid regular expression in substitution", result.Error );
	}

	private static LocaleValues CaptureLocale() {
		return new LocaleValues(
			Environment.GetEnvironmentVariable( "LC_ALL" ),
			Environment.GetEnvironmentVariable( "LC_CTYPE" ),
			Environment.GetEnvironmentVariable( "LANG" )
		);
	}
	private static void SetLocale(
		string name
	) {
		Environment.SetEnvironmentVariable( "LC_ALL", name );
		Environment.SetEnvironmentVariable( "LC_CTYPE", null );
		Environment.SetEnvironmentVariable( "LANG", null );
	}
	private static void RestoreLocale(
		LocaleValues values
	) {
		Environment.SetEnvironmentVariable( "LC_ALL", values.LcAll );
		Environment.SetEnvironmentVariable( "LC_CTYPE", values.LcCtype );
		Environment.SetEnvironmentVariable( "LANG", values.Lang );
	}

	private static async Task<CommandResult> RunAsync(
		string[] args,
		string input,
		CancellationToken cancellationToken = default
	) {
		using var output = new StringWriter { NewLine = "\n" };
		using var error = new StringWriter { NewLine = "\n" };
		var exitCode = await SedCommand.RunAsync(
			args,
			new StringReader( input ),
			output,
			error,
			cancellationToken
		);
		return new CommandResult(
			exitCode,
			output.ToString(),
			error.ToString()
		);
	}

	private sealed record CommandResult(
		int ExitCode,
		string Output,
		string Error
	);
	private readonly record struct LocaleValues(
		string? LcAll,
		string? LcCtype,
		string? Lang
	);

}
