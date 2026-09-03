namespace Icod.LineEditor.Router.Tests;

using Xunit;

public sealed class CommandTests {
	[Fact]
	public async Task HelpListsAllCommands() {
		var output = new StringWriter();
		var status = await Command.RunAsync(
			[ "--help" ],
			new StringReader( string.Empty ),
			output,
			new StringWriter()
		);

		Assert.Equal( 0, status );
		Assert.Contains( " ed ", output.ToString() );
		Assert.Contains( " red ", output.ToString() );
		Assert.Contains( " sed ", output.ToString() );
	}

	[Fact]
	public async Task UnknownCommandIsUsageError() {
		var error = new StringWriter();
		var status = await Command.RunAsync(
			[ "nope" ],
			new StringReader( string.Empty ),
			new StringWriter(),
			error
		);

		Assert.Equal( 2, status );
		Assert.Contains( "unknown command", error.ToString() );
	}

	[Theory]
	[InlineData( "ed" )]
	[InlineData( "red" )]
	[InlineData( "sed" )]
	public async Task DispatchesVersionToManagedCommand( string commandName ) {
		var output = new StringWriter();
		var status = await Command.RunAsync(
			[ commandName, "--version" ],
			new StringReader( string.Empty ),
			output,
			new StringWriter()
		);

		Assert.Equal( 0, status );
		Assert.Contains( "1.0", output.ToString() );
	}
}
