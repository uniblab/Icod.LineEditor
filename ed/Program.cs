namespace Icod.LineEditor.Ed;

using Icod.CommandFramework.Diagnostics;

/// <summary>Hosts the asynchronous <c>ed</c> command entry point.</summary>
public static class Program {
	/// <summary>Runs the command with process console streams and cooperative Ctrl+C cancellation.</summary>
	public static async Task<int> Main(
		string[] args
	) {
		using var cancellationSource = new CancellationTokenSource();
		ConsoleCancelEventHandler handler = (
			_,
			eventArgs
		) => {
			eventArgs.Cancel = true;
			cancellationSource.Cancel();
		};
		Console.CancelKeyPress += handler;
		try {
			return await Command.RunAsync(
				args,
				CommandContext.CreateConsole(
					"ed",
					cancellationSource.Token
				)
			).ConfigureAwait( false );
		} finally {
			Console.CancelKeyPress -= handler;
		}
	}
}
