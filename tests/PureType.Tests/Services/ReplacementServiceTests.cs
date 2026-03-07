using PureType.Services;

namespace PureType.Tests.Services;

public class ReplacementServiceTests : IDisposable
{
    private readonly string _tempFile;
    private ReplacementService? _service;

    public ReplacementServiceTests()
    {
        _tempFile = Path.GetTempFileName();
    }

    public void Dispose()
    {
        _service?.Dispose();
        if (File.Exists(_tempFile)) File.Delete(_tempFile);
    }

    private ReplacementService CreateService(params string[] lines)
    {
        File.WriteAllLines(_tempFile, lines);
        _service = new ReplacementService(_tempFile);
        return _service;
    }

    [Fact]
    public void Apply_replaces_case_insensitive()
    {
        var svc = CreateService("Punkt -> .");
        Assert.Equal("ein . hier", svc.Apply("ein Punkt hier"));
        Assert.Equal("ein . hier", svc.Apply("ein punkt hier"));
        Assert.Equal("ein . hier", svc.Apply("ein PUNKT hier"));
    }

    [Fact]
    public void Apply_handles_arrow_delimiter()
    {
        var svc = CreateService("mfg -> Mit freundlichen Grüßen");
        Assert.Equal("Mit freundlichen Grüßen", svc.Apply("mfg"));
    }

    [Fact]
    public void Apply_handles_unicode_arrow()
    {
        var svc = CreateService("mfg → Mit freundlichen Grüßen");
        Assert.Equal("Mit freundlichen Grüßen", svc.Apply("mfg"));
    }

    [Fact]
    public void Apply_converts_backslash_n_to_newline()
    {
        var svc = CreateService("neue Zeile -> \\n");
        Assert.Equal("text \n more", svc.Apply("text neue Zeile more"));
    }

    [Fact]
    public void Apply_returns_original_when_no_rules()
    {
        var svc = CreateService();
        Assert.Equal("hello world", svc.Apply("hello world"));
    }

    [Fact]
    public void Apply_returns_original_when_empty_text()
    {
        var svc = CreateService("foo -> bar");
        Assert.Equal("", svc.Apply(""));
    }

    [Fact]
    public void Apply_applies_rules_in_order()
    {
        var svc = CreateService("aa -> bb", "bb -> cc");
        Assert.Equal("cc", svc.Apply("aa"));
    }
}
