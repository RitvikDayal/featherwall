using FeatherWall.Widgets.Themes;

namespace FeatherWall.Tests;

/// <summary>Runs alone: it swaps the process-wide "is this font installed" hook, and any theme test
/// measuring text at the same moment would get fallback fonts — the Glass dock width test failed
/// one run in four that way.</summary>
[CollectionDefinition(nameof(GlobalFontHook), DisableParallelization = true)]
public sealed class GlobalFontHook;

[Collection(nameof(GlobalFontHook))]
public class ThemeFontsHookTests
{
    [Fact]
    public void Fonts_FallBackWithoutVariable()
    {
        var real = ThemeFonts.IsInstalled;
        try
        {
            ThemeFonts.IsInstalled = n => !n.StartsWith("Segoe UI Variable", StringComparison.Ordinal);
            using var font = ThemeFonts.Create(ThemeFonts.DisplayLight, 20);
            Assert.Equal("Segoe UI Light", font.Name);
        }
        finally
        {
            ThemeFonts.IsInstalled = real;
        }
    }
}
