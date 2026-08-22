namespace Icod.LineEditor.Red;

using Icod.CommandFramework.Diagnostics;

/// <summary>Hosts the asynchronous <c>red</c> command entry point.</summary>
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
					"red",
					cancellationSource.Token
				)
			).ConfigureAwait( false );
		} finally {
			Console.CancelKeyPress -= handler;
		}
	}
}
