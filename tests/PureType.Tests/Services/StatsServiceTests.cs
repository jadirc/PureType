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
        Assert.Equal(0, snap.TodaySttMs);
        Assert.Equal(0, snap.TodayAiMs);
        Assert.Equal(0, snap.TotalSttMs);
        Assert.Equal(0, snap.TotalAiMs);
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
    public void Corrupted_file_starts_fresh()
    {
        File.WriteAllText(_tempFile, "NOT VALID JSON {{{");

        var svc = new StatsService(_tempFile);
        var snap = svc.GetStats();

        Assert.Equal(0, snap.TotalWords);
        Assert.Equal(0, snap.TotalSessions);
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

    [Fact]
    public void RecordSession_tracks_stt_milliseconds()
    {
        var svc = new StatsService(_tempFile);
        svc.RecordSession(10, 30, sttMs: 1200);
        var snap = svc.GetStats();
        Assert.Equal(1200, snap.TodaySttMs);
    }

    [Fact]
    public void RecordAiTime_tracks_ai_milliseconds()
    {
        var svc = new StatsService(_tempFile);
        svc.RecordSession(10, 30, sttMs: 1200);
        svc.RecordAiTime(700);
        var snap = svc.GetStats();
        Assert.Equal(1200, snap.TodaySttMs);
        Assert.Equal(700, snap.TodayAiMs);
        Assert.Equal(1, snap.TodaySessions); // RecordAiTime must NOT increment sessions
    }

    [Fact]
    public void RecordSession_accumulates_timing()
    {
        var svc = new StatsService(_tempFile);
        svc.RecordSession(10, 30, sttMs: 1000);
        svc.RecordAiTime(500);
        svc.RecordSession(20, 60, sttMs: 1500);
        svc.RecordAiTime(800);
        var snap = svc.GetStats();
        Assert.Equal(2500, snap.TodaySttMs);
        Assert.Equal(1300, snap.TodayAiMs);
    }

    [Fact]
    public void GetStats_returns_total_timing()
    {
        var svc = new StatsService(_tempFile);
        svc.RecordSession(10, 30, sttMs: 1000);
        svc.RecordAiTime(500);
        svc.RecordSession(20, 60, sttMs: 1500);
        svc.RecordAiTime(800);

        var snap = svc.GetStats();
        Assert.Equal(2500, snap.TotalSttMs);
        Assert.Equal(1300, snap.TotalAiMs);
    }

    [Fact]
    public void RecordSession_without_timing_defaults_to_zero()
    {
        var svc = new StatsService(_tempFile);
        svc.RecordSession(10, 30);
        var snap = svc.GetStats();
        Assert.Equal(0, snap.TodaySttMs);
        Assert.Equal(0, snap.TodayAiMs);
    }

    [Fact]
    public void Timing_persists_across_instances()
    {
        var svc1 = new StatsService(_tempFile);
        svc1.RecordSession(10, 30, sttMs: 1200);
        svc1.RecordAiTime(700);
        var svc2 = new StatsService(_tempFile);
        var snap = svc2.GetStats();
        Assert.Equal(1200, snap.TodaySttMs);
        Assert.Equal(700, snap.TodayAiMs);
    }

    [Fact]
    public void DayHistory_includes_timing()
    {
        var svc = new StatsService(_tempFile);
        svc.RecordSession(10, 30, sttMs: 1200);
        svc.RecordAiTime(700);
        var snap = svc.GetStats();
        var entry = snap.DayHistory[0];
        Assert.Equal(1200, entry.SttMs);
        Assert.Equal(700, entry.AiMs);
    }
}
