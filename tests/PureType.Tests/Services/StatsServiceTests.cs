using PureType.Services;

namespace PureType.Tests.Services;

public class StatsServiceTests : IDisposable
{
    private readonly string _tempFile;

    public StatsServiceTests()
    {
        _tempFile = Path.Combine(Path.GetTempPath(), $"puretype-stats-test-{Guid.NewGuid()}.json");
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile))
            File.Delete(_tempFile);
    }

    [Fact]
    public void New_service_has_zero_totals()
    {
        var svc = new StatsService(_tempFile);
        var snap = svc.GetStats();

        Assert.Equal(0, snap.TotalWords);
        Assert.Equal(0, snap.TotalSessions);
        Assert.Equal(0, snap.TotalSeconds);
        Assert.Equal(0, snap.TodayWords);
        Assert.Equal(0, snap.TodaySessions);
        Assert.Equal(0, snap.TodaySeconds);
        Assert.Empty(snap.DayHistory);
    }

    [Fact]
    public void RecordSession_increments_today_and_totals()
    {
        var svc = new StatsService(_tempFile);

        svc.RecordSession(42, 120);

        var snap = svc.GetStats();
        Assert.Equal(42, snap.TotalWords);
        Assert.Equal(1, snap.TotalSessions);
        Assert.Equal(120, snap.TotalSeconds);
        Assert.Equal(42, snap.TodayWords);
        Assert.Equal(1, snap.TodaySessions);
        Assert.Equal(120, snap.TodaySeconds);
    }

    [Fact]
    public void RecordSession_accumulates_multiple_sessions()
    {
        var svc = new StatsService(_tempFile);

        svc.RecordSession(10, 30);
        svc.RecordSession(20, 60);
        svc.RecordSession(5, 15);

        var snap = svc.GetStats();
        Assert.Equal(35, snap.TotalWords);
        Assert.Equal(3, snap.TotalSessions);
        Assert.Equal(105, snap.TotalSeconds);
        Assert.Equal(35, snap.TodayWords);
        Assert.Equal(3, snap.TodaySessions);
        Assert.Equal(105, snap.TodaySeconds);
    }

    [Fact]
    public void Stats_persist_across_instances()
    {
        var svc1 = new StatsService(_tempFile);
        svc1.RecordSession(42, 120);

        var svc2 = new StatsService(_tempFile);
        var snap = svc2.GetStats();

        Assert.Equal(42, snap.TotalWords);
        Assert.Equal(1, snap.TotalSessions);
        Assert.Equal(120, snap.TotalSeconds);
    }

    [Fact]
    public void DayHistory_contains_today_entry()
    {
        var svc = new StatsService(_tempFile);
        svc.RecordSession(10, 30);

        var snap = svc.GetStats();
        Assert.Single(snap.DayHistory);

        var entry = snap.DayHistory[0];
        Assert.Equal(DateTime.Today.ToString("yyyy-MM-dd"), entry.Date);
        Assert.Equal(10, entry.Words);
        Assert.Equal(1, entry.Sessions);
        Assert.Equal(30, entry.Seconds);
    }

    [Fact]
    public void DayHistory_limited_to_30_entries()
    {
        // Pre-populate with 35 days of data
        var svc = new StatsService(_tempFile);

        // Write raw JSON with 35 days
        var days = new Dictionary<string, object>();
        for (int i = 0; i < 35; i++)
        {
            var date = DateTime.Today.AddDays(-i).ToString("yyyy-MM-dd");
            days[date] = new { Words = 10, Sessions = 1, Seconds = 60 };
        }
        var json = System.Text.Json.JsonSerializer.Serialize(new { Days = days });
        File.WriteAllText(_tempFile, json);

        // Re-load and record a session (triggers prune)
        var svc2 = new StatsService(_tempFile);
        svc2.RecordSession(1, 1);

        var snap = svc2.GetStats();
        Assert.True(snap.DayHistory.Count <= 30,
            $"Expected at most 30 day-history entries but got {snap.DayHistory.Count}");
    }
}
