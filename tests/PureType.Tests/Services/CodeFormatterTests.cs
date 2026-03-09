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

    // ── PascalCase ────────────────────────────────────────────────────

    [Theory]
    [InlineData("pascal case foo bar", "FooBar")]
    [InlineData("pascal case hello world test", "HelloWorldTest")]
    public void PascalCase_transforms_words(string input, string expected)
    {
        Assert.Equal(expected, CodeFormatter.Apply(input));
    }

    // ── snake_case ────────────────────────────────────────────────────

    [Theory]
    [InlineData("snake case foo bar", "foo_bar")]
    [InlineData("snake case Hello World", "hello_world")]
    public void SnakeCase_transforms_words(string input, string expected)
    {
        Assert.Equal(expected, CodeFormatter.Apply(input));
    }

    // ── kebab-case ────────────────────────────────────────────────────

    [Theory]
    [InlineData("kebab case foo bar", "foo-bar")]
    [InlineData("kebab case Hello World", "hello-world")]
    public void KebabCase_transforms_words(string input, string expected)
    {
        Assert.Equal(expected, CodeFormatter.Apply(input));
    }

    // ── UPPER CASE ────────────────────────────────────────────────────

    [Theory]
    [InlineData("upper case foo bar", "FOO BAR")]
    [InlineData("upper case hello", "HELLO")]
    public void UpperCase_transforms_words(string input, string expected)
    {
        Assert.Equal(expected, CodeFormatter.Apply(input));
    }

    // ── lower case ────────────────────────────────────────────────────

    [Theory]
    [InlineData("lower case FOO BAR", "foo bar")]
    [InlineData("lower case Hello World", "hello world")]
    public void LowerCase_transforms_words(string input, string expected)
    {
        Assert.Equal(expected, CodeFormatter.Apply(input));
    }

    // ── nospace ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("no space foo bar", "foobar")]
    [InlineData("no space Hello World", "helloworld")]
    public void NoSpace_transforms_words(string input, string expected)
    {
        Assert.Equal(expected, CodeFormatter.Apply(input));
    }

    // ── Stop word ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("camel case my var stop rest of text", "myVar rest of text")]
    [InlineData("snake case foo bar stop baz", "foo_bar baz")]
    [InlineData("pascal case hello world stop and continue", "HelloWorld and continue")]
    public void Stop_word_ends_formatting(string input, string expected)
    {
        Assert.Equal(expected, CodeFormatter.Apply(input));
    }

    // ── Prefix text before command ────────────────────────────────────

    [Theory]
    [InlineData("set the variable camel case my value", "set the variable myValue")]
    [InlineData("type snake case some name here", "type some_name_here")]
    public void Prefix_text_preserved(string input, string expected)
    {
        Assert.Equal(expected, CodeFormatter.Apply(input));
    }

    // ── Case insensitivity ────────────────────────────────────────────

    [Theory]
    [InlineData("Camel Case foo bar", "fooBar")]
    [InlineData("SNAKE CASE foo bar", "foo_bar")]
    public void Commands_are_case_insensitive(string input, string expected)
    {
        Assert.Equal(expected, CodeFormatter.Apply(input));
    }
}
