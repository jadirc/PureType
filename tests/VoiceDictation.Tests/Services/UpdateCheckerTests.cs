using VoiceDictation.Services;

namespace VoiceDictation.Tests.Services;

public class UpdateCheckerTests
{
    [Fact]
    public void ReleaseInfo_stores_values()
    {
        var info = new UpdateChecker.ReleaseInfo("v1.2.3", "https://example.com");
        Assert.Equal("v1.2.3", info.TagName);
        Assert.Equal("https://example.com", info.HtmlUrl);
    }
}
