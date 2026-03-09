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

        TotalWords.Text = s.TotalWords.ToString("N0");
        TotalSessions.Text = $"{s.TotalSessions} sessions";
        TotalDuration.Text = FormatDuration(s.TotalSeconds);

        HistoryGrid.ItemsSource = s.DayHistory.Select(d => new
        {
            d.Date,
            d.Words,
            d.Sessions,
            DurationDisplay = FormatDuration(d.Seconds),
        }).ToList();
    }

    private static string FormatDuration(int seconds)
    {
        if (seconds < 60) return $"{seconds}s";
        if (seconds < 3600) return $"{seconds / 60} min";
        return $"{seconds / 3600}h {seconds % 3600 / 60}m";
    }
}
