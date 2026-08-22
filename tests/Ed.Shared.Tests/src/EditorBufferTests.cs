namespace Icod.LineEditor.Ed.Shared.Tests;

using System.Text;
using Icod.LineEditor.Ed;

public sealed class EditorBufferTests {
	[Fact]
	public void MovePreservesStableIdentityAndCopyAllocatesNewIdentity() {
		var buffer = new EditorBuffer();
		buffer.Append( Lines( "one", "two", "three", "four" ) );
		var identity = buffer.GetLine( 2 ).Id;

		var moved = buffer.Move( new EditorAddressRange( 2, 2 ), 4 );

		Assert.Equal( 4, moved.Start );
		Assert.Equal( identity, buffer.GetLine( moved.Start ).Id );

		var copied = buffer.Copy( new EditorAddressRange( moved.Start, moved.End ), 0 );

		Assert.NotEqual( identity, buffer.GetLine( copied.Start ).Id );
		Assert.Equal( 5, buffer.FindAddress( identity ) );
		Assert.Equal( new[] { "two", "one", "three", "four", "two" }, Text( buffer ) );
	}

	[Fact]
	public void JoinRetainsTheFirstLineIdentity() {
		var buffer = new EditorBuffer();
		buffer.Append( Lines( "ab", "cd", "ef" ) );
		var identity = buffer.GetLine( 1 ).Id;

		buffer.Join( new EditorAddressRange( 1, 3 ) );

		Assert.Equal( identity, buffer.GetLine( 1 ).Id );
		Assert.Equal( new[] { "abcdef" }, Text( buffer ) );
	}

	[Fact]
	public void LargeInsertDeleteMaintainsAddressOrderAcrossSegments() {
		var buffer = new EditorBuffer();
		var lines = Enumerable.Range( 1, 5000 )
			.Select( value => new ReadOnlyMemory<byte>( Encoding.UTF8.GetBytes( value.ToString() ) ) )
			.ToArray();
		buffer.Append( lines );

		buffer.Delete( new EditorAddressRange( 1001, 4000 ) );

		Assert.Equal( 2000, buffer.Count );
		Assert.Equal( "1000", buffer.GetLine( 1000 ).GetText() );
		Assert.Equal( "4001", buffer.GetLine( 1001 ).GetText() );
		Assert.Equal( "5000", buffer.GetLine( 2000 ).GetText() );
	}

	private static IReadOnlyList<ReadOnlyMemory<byte>> Lines(
		params string[] values
	) => values.Select( value => new ReadOnlyMemory<byte>( Encoding.UTF8.GetBytes( value ) ) ).ToArray();

	private static string[] Text(
		EditorBuffer buffer
	) => buffer.GetLines().Select( line => line.GetText() ).ToArray();
}
