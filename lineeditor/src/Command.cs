namespace Icod.LineEditor.Router;

using System.Reflection;
using EdCommand = Icod.LineEditor.Ed.Command;
using RedCommand = Icod.LineEditor.Red.Command;
using SedCommand = Icod.LineEditor.Sed.Command;

/// <summary>Routes <c>lineeditor COMMAND [args...]</c> to managed line-editor commands.</summary>
public static class Command {
	private const string CommandName = "lineeditor";
	private const int UsageError = 2;
	private const int Canceled = 130;

	/// <summary>Runs the router with caller-owned text streams.</summary>
	public static async Task<int> RunAsync(
		string[] args,
		TextReader stdin,
		TextWriter stdout,
		TextWriter stderr,
		CancellationToken cancellationToken = default
	) {
		ArgumentNullException.ThrowIfNull( args );
		ArgumentNullException.ThrowIfNull( stdin );
		ArgumentNullException.ThrowIfNull( stdout );
		ArgumentNullException.ThrowIfNull( stderr );

		if ( cancellationToken.IsCancellationRequested ) {
			return Canceled;
		}
		if ( 0 == args.Length ) {
			await stderr.WriteLineAsync(
				$"{CommandName}: missing command; use --help to list supported commands"
			).ConfigureAwait( false );
			return UsageError;
		}

		var commandName = args[ 0 ];
		if ( commandName is "--help" or "-h" ) {
			await stdout.WriteAsync( GetHelpText() ).ConfigureAwait( false );
			return 0;
		}
		if ( commandName is "--version" or "-V" ) {
			await stdout.WriteLineAsync(
				$"{CommandName} (Icod.LineEditor) {GetSemanticVersion()}"
			).ConfigureAwait( false );
			return 0;
		}
		if ( commandName is not ( "ed" or "red" or "sed" ) ) {
			await stderr.WriteLineAsync(
				$"{CommandName}: unknown command '{commandName}'; use --help to list supported commands"
			).ConfigureAwait( false );
			return UsageError;
		}

		var commandArguments = args[ 1.. ];
		try {
			return commandName switch {
				"ed" => await EdCommand.RunAsync(
					commandArguments,
					stdin,
					stdout,
					stderr,
					cancellationToken
				).ConfigureAwait( false ),
				"red" => await RedCommand.RunAsync(
					commandArguments,
					stdin,
					stdout,
					stderr,
					cancellationToken
				).ConfigureAwait( false ),
				"sed" => await SedCommand.RunAsync(
					commandArguments,
					stdin,
					stdout,
					stderr,
					cancellationToken
				).ConfigureAwait( false ),
				_ => throw new InvalidOperationException( "Known command dispatch was incomplete." )
			};
		} catch ( OperationCanceledException ) {
			return Canceled;
		}
	}

	private static string GetHelpText() =>
		$"Usage: {CommandName} COMMAND [OPTION]... [ARG]...{Environment.NewLine}"
		+ Environment.NewLine
		+ $"Commands:{Environment.NewLine}"
		+ $" ed    line-oriented text editor{Environment.NewLine}"
		+ $" red   restricted line-oriented text editor{Environment.NewLine}"
		+ $" sed   stream editor{Environment.NewLine}"
		+ Environment.NewLine
		+ $"Router options:{Environment.NewLine}"
		+ $" -h, --help       display this help and exit{Environment.NewLine}"
		+ $" -V, --version    output the router version and exit{Environment.NewLine}"
		+ Environment.NewLine
		+ $"Run '{CommandName} COMMAND --help' for command-specific help.{Environment.NewLine}";

	private static string GetSemanticVersion() {
		var version = typeof( Command )
			.Assembly
			.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
			?.InformationalVersion
			?? "0.0.0";
		var separator = version.IndexOf( '+', StringComparison.Ordinal );
		return 0 <= separator ? version[ ..separator ] : version;
	}
}
