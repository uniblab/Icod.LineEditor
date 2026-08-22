namespace Icod.LineEditor.Sed;

using System.Threading;
using System.Threading.Tasks;
using Icod.CommandFramework.Diagnostics;

/// <summary>
/// Provides the executable entry point for the GNU-compatible <c>sed</c> command for transforming text with stream-editor commands.
/// </summary>
public static class Program {
	/// <summary>
	/// Runs the <c>sed</c> command using the process console and converts a console interrupt into a cancellation request.
	/// </summary>
	/// <param name="args">The command-line arguments supplied to <c>sed</c>.</param>
	/// <returns>A task whose result is the command exit status.</returns>
	public static async Task<int> Main(
		string[] args
	) {
		using var cancellation = new CancellationTokenSource();
		Console.CancelKeyPress += (
			sender,
			eventArgs
		) => {
			eventArgs.Cancel = true;
			cancellation.Cancel();
		};
		var context = CommandContext.CreateConsole(
			"sed",
			cancellation.Token
		);
		return await Command.RunAsync(
			args,
			context
		).ConfigureAwait( false );
	}

}
