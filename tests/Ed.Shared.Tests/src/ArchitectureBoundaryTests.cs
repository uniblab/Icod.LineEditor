namespace Icod.LineEditor.Ed.Shared.Tests;

using Icod.LineEditor.Ed;

/// <summary>
/// Locks the dependency direction established by the Phase LE9 sharing audit.
/// </summary>
public sealed class ArchitectureBoundaryTests {
	/// <summary>
	/// Verifies that the Ed/Red engine consumes the neutral command framework
	/// without taking a dependency on Sed, an executable, or a
	/// speculative LineEditor-family wrapper.
	/// </summary>
	[Fact]
	public void EdSharedReferencesOnlyTheNeutralFoundationWithinTheFamily() {
		var references = typeof( EditorEngine )
			.Assembly
			.GetReferencedAssemblies()
			.Select( reference => reference.Name ?? string.Empty )
			.ToArray();

		Assert.Contains( "Icod.CommandFramework", references );
		Assert.DoesNotContain( "Icod.CoreUtils.Shared", references );
		Assert.DoesNotContain( "Icod.LineEditor.Sed", references );
		Assert.DoesNotContain( "Icod.LineEditor.Shared", references );
		Assert.DoesNotContain( "Icod.LineEditor.Ed", references );
		Assert.DoesNotContain( "Icod.LineEditor.Red", references );
		Assert.DoesNotContain( "ed", references );
		Assert.DoesNotContain( "red", references );
		Assert.DoesNotContain( "sed", references );
	}
}
