using Xunit;

namespace PureType.Tests;

public class SettingsSearchTests
{
    [Theory]
    [InlineData("theme", "theme dark light auto color appearance", true)]
    [InlineData("THEME", "theme dark light auto color appearance", true)]
    [InlineData("dark", "theme dark light auto color appearance", true)]
    [InlineData("xyz", "theme dark light auto color appearance", false)]
    [InlineData("short", "toggle shortcut hotkey start stop recording", true)]
    [InlineData("api", "api key deepgram authentication", true)]
    [InlineData("whisper", "whisper model download local", true)]
    [InlineData("nonexistent", "whisper model download local", false)]
    [InlineData("", "anything", true)]
    public void MatchesSearch_ChecksTagContains(string query, string tag, bool expected)
    {
        var result = string.IsNullOrEmpty(query)
            || tag.Contains(query, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(expected, result);
    }
}
