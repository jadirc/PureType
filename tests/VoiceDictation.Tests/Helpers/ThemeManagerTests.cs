using VoiceDictation.Helpers;

namespace VoiceDictation.Tests.Helpers;

public class ThemeManagerTests
{
    [Fact]
    public void DetectSystemTheme_returns_dark_or_light()
    {
        var result = ThemeManager.DetectSystemTheme();
        Assert.True(result is "Dark" or "Light", $"Expected Dark or Light but got: {result}");
    }
}
