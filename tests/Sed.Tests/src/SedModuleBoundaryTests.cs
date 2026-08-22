namespace Icod.LineEditor.Sed.Tests;

using System.Reflection;
using Xunit;

/// <summary>
/// Verifies the public and private boundaries retained by the LE1 source decomposition.
/// </summary>
public sealed class SedModuleBoundaryTests {

	/// <summary>
	/// Verifies that the established synchronous and asynchronous command signatures remain available.
	/// </summary>
	[Fact]
	public void PublicCommandSignaturesRemainStable() {
		var commandType = typeof( Icod.LineEditor.Sed.Command );

		var run = commandType.GetMethod(
			"Run",
			BindingFlags.Public | BindingFlags.Static,
			binder: null,
			types: new Type[] {
				typeof( string[] ),
				typeof( TextReader ),
				typeof( TextWriter ),
				typeof( TextWriter )
			},
			modifiers: null
		);
		var runAsync = commandType.GetMethod(
			"RunAsync",
			BindingFlags.Public | BindingFlags.Static,
			binder: null,
			types: new Type[] {
				typeof( string[] ),
				typeof( TextReader ),
				typeof( TextWriter ),
				typeof( TextWriter ),
				typeof( CancellationToken )
			},
			modifiers: null
		);

		Assert.NotNull( run );
		Assert.Equal( typeof( int ), run!.ReturnType );
		Assert.NotNull( runAsync );
		Assert.Equal( typeof( Task<int> ), runAsync!.ReturnType );
	}

	/// <summary>
	/// Verifies that implementation types remain non-public details behind the command facade.
	/// </summary>
	[Theory]
	[InlineData( "Options" )]
	[InlineData( "ScriptParser" )]
	[InlineData( "SedProgram" )]
	[InlineData( "AddressSelector" )]
	[InlineData( "InputSequence" )]
	[InlineData( "ExecutionEnvironment" )]
	[InlineData( "SubstitutionFlags" )]
	[InlineData( "SedRegularExpressionCompiler" )]
	[InlineData( "SedCompiledRegularExpression" )]
	[InlineData( "TextWriterStream" )]
	public void DecomposedImplementationTypesRemainPrivate(
		string nestedTypeName
	) {
		var nestedType = typeof( Icod.LineEditor.Sed.Command ).GetNestedType(
			nestedTypeName,
			BindingFlags.NonPublic
		);

		Assert.NotNull( nestedType );
		Assert.True( nestedType!.IsNestedPrivate );
	}

}
