namespace Icod.LineEditor.Sed.Tests;

using Xunit;

/// <summary>
/// Locks the dependency direction established by the Phase LE9 sharing audit.
/// </summary>
public sealed class ArchitectureBoundaryTests {
	/// <summary>
	/// Verifies that Sed consumes the neutral command framework without
	/// taking a dependency on the Ed/Red engine, an executable, or a speculative
	/// LineEditor-family wrapper.
	/// </summary>
	[Fact]
	public void SedReferencesOnlyTheNeutralFoundationWithinTheFamily() {
		var references = typeof( Icod.LineEditor.Sed.Command )
			.Assembly
			.GetReferencedAssemblies()
			.Select( reference => reference.Name ?? string.Empty )
			.ToArray();

		Assert.Contains( "Icod.CommandFramework", references );
		Assert.DoesNotContain( "Icod.CoreUtils.Shared", references );
		Assert.DoesNotContain( "Icod.LineEditor.Ed.Shared", references );
		Assert.DoesNotContain( "Icod.LineEditor.Shared", references );
		Assert.DoesNotContain( "Icod.LineEditor.Ed", references );
		Assert.DoesNotContain( "Icod.LineEditor.Red", references );
		Assert.DoesNotContain( "ed", references );
		Assert.DoesNotContain( "red", references );
	}
}
