using PureType.Services;

namespace PureType.Tests.Services;

public class SoundFeedbackTests
{
    [Theory]
    [InlineData("Sanft", "Gentle")]
    [InlineData("Klick", "Click")]
    [InlineData("Glocke", "Bell")]
    [InlineData("Tief", "Deep")]
    [InlineData("Doppel-Pip", "Double-Pip")]
    [InlineData("Kein", "None")]
    [InlineData("Gentle", "Gentle")]
    [InlineData("Unknown", "Unknown")]
    public void MigrateName_maps_legacy_german_names(string input, string expected)
    {
        Assert.Equal(expected, SoundFeedback.MigrateName(input));
    }
}
