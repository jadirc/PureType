using System.Windows;
using PureType.Services;

namespace PureType;

public partial class StatsWindow : Window
{
    public StatsWindow(StatsService stats)
    {
        InitializeComponent();
        PopulateStats(stats.GetStats());
    }

    private void PopulateStats(StatsSnapshot s)
    {
        TodayWords.Text = s.TodayWords.ToString("N0");
        TodaySessions.Text = $"{s.TodaySessions} sessions";
        TodayDuration.Text = FormatDuration(s.TodaySeconds);
        TodayTiming.Text = FormatAvgTiming(s.TodaySttMs, s.TodayAiMs, s.TodaySessions);

        TotalWords.Text = s.TotalWords.ToString("N0");
        TotalSessions.Text = $"{s.TotalSessions} sessions";
        TotalDuration.Text = FormatDuration(s.TotalSeconds);
        TotalTiming.Text = FormatAvgTiming(s.TotalSttMs, s.TotalAiMs, s.TotalSessions);

        HistoryGrid.ItemsSource = s.DayHistory.Select(d => new
        {
            d.Date,
            d.Words,
            d.Sessions,
            DurationDisplay = FormatDuration(d.Seconds),
            AvgStt = FormatAvgMs(d.SttMs, d.Sessions),
            AvgAi = FormatAvgMs(d.AiMs, d.Sessions),
        }).ToList();
    }

    private static string FormatAvgTiming(int sttMs, int aiMs, int sessions)
    {
        if (sessions == 0) return "";
        var sttAvg = FormatAvgMs(sttMs, sessions);
        var aiAvg = FormatAvgMs(aiMs, sessions);
        if (sttAvg == "\u2014" && aiAvg == "\u2014") return "";
        return $"\u00D8 STT: {sttAvg} \u00B7 \u00D8 AI: {aiAvg}";
    }

    private static string FormatAvgMs(int totalMs, int sessions)
    {
        if (sessions == 0 || totalMs == 0) return "\u2014";
        var avgSec = totalMs / 1000.0 / sessions;
        return $"{avgSec:F1}s";
    }

    private static string FormatDuration(int seconds)
    {
        if (seconds < 60) return $"{seconds}s";
        if (seconds < 3600) return $"{seconds / 60} min";
        return $"{seconds / 3600}h {seconds % 3600 / 60}m";
    }
}
