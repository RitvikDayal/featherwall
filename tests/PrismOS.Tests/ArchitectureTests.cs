namespace PrismOS.Tests;

/// <summary>Phase 1 of the engine/shell split: PrismOS.Core must never depend on the shell.
///
/// The extraction's whole value is the direction of the arrow. A Core type that calls back into
/// <c>Engine</c> (as <c>UninstallCleanup</c> did) would compile fine and quietly undo the
/// boundary, so it is guarded here rather than left to review.</summary>
public class ArchitectureTests
{
    [Fact]
    public void Core_DoesNotReferenceTheShell()
    {
        var referenced = typeof(PrismOS.Rendering.WakeEase).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToArray();

        Assert.DoesNotContain("prismos", referenced);
        Assert.DoesNotContain("PrismOS.Tests", referenced);
    }

    [Fact]
    public void Shell_ReferencesCore()
    {
        var referenced = typeof(PrismOS.Engine).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name);

        Assert.Contains("PrismOS.Core", referenced);
    }

    [Fact]
    public void CoreAndShell_AreDifferentAssemblies()
    {
        Assert.NotEqual(typeof(PrismOS.Engine).Assembly, typeof(PrismOS.Rendering.WakeEase).Assembly);
    }
}
