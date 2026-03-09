using PureType.Services;

namespace PureType.Tests.Services;

public class CodeFormatterTests
{
    // ── camelCase ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("camel case foo bar", "fooBar")]
    [InlineData("camel case hello world test", "helloWorldTest")]
    [InlineData("camel case single", "single")]
    [InlineData("prefix camel case foo bar", "prefix fooBar")]
    [InlineData("camel case FOO BAR", "fooBar")]
    public void CamelCase_transforms_words(string input, string expected)
    {
        Assert.Equal(expected, CodeFormatter.Apply(input));
    }

    // ── No match passthrough ──────────────────────────────────────────

    [Theory]
    [InlineData("hello world")]
    [InlineData("just normal text")]
    [InlineData("")]
    public void No_command_passes_through(string input)
    {
        Assert.Equal(input, CodeFormatter.Apply(input));
    }
}
