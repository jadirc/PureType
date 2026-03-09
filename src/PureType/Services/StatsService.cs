using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Serilog;

namespace PureType.Services;

// ── Data model ──────────────────────────────────────────────────────────

public class DayStats
{
    public int Words { get; set; }
    public int Sessions { get; set; }
    public int Seconds { get; set; }
}

public class StatsData
{
    public Dictionary<string, DayStats> Days { get; set; } = new();
}

public record DayHistoryEntry(string Date, int Words, int Sessions, int Seconds);

public record StatsSnapshot(
    int TotalWords,
    int TotalSessions,
    int TotalSeconds,
    int TodayWords,
    int TodaySessions,
    int TodaySeconds,
    IReadOnlyList<DayHistoryEntry> DayHistory);

// ── Service ─────────────────────────────────────────────────────────────

public class StatsService
{
    private const int MaxDayHistory = 30;
    private readonly string _filePath;
    private StatsData _data;

    public StatsService()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PureType", "stats.json"))
    {
    }

    public StatsService(string filePath)
    {
        _filePath = filePath;
        _data = Load();
        Prune();
    }

    /// <summary>
    /// Records a completed dictation session.
    /// </summary>
    public void RecordSession(int wordCount, int durationSeconds)
    {
        var key = DateTime.Today.ToString("yyyy-MM-dd");

        if (!_data.Days.TryGetValue(key, out var day))
        {
            day = new DayStats();
            _data.Days[key] = day;
        }

        day.Words += wordCount;
        day.Sessions += 1;
        day.Seconds += durationSeconds;

        Prune();
        Save();
    }

    /// <summary>
    /// Returns an immutable snapshot of current statistics.
    /// </summary>
    public StatsSnapshot GetStats()
    {
        var todayKey = DateTime.Today.ToString("yyyy-MM-dd");
        _data.Days.TryGetValue(todayKey, out var today);

        var totalWords = _data.Days.Values.Sum(d => d.Words);
        var totalSessions = _data.Days.Values.Sum(d => d.Sessions);
        var totalSeconds = _data.Days.Values.Sum(d => d.Seconds);

        var history = _data.Days
            .OrderByDescending(kv => kv.Key)
            .Select(kv => new DayHistoryEntry(kv.Key, kv.Value.Words, kv.Value.Sessions, kv.Value.Seconds))
            .ToList();

        return new StatsSnapshot(
            totalWords, totalSessions, totalSeconds,
            today?.Words ?? 0, today?.Sessions ?? 0, today?.Seconds ?? 0,
            history);
    }

    // ── Private helpers ─────────────────────────────────────────────────

    private StatsData Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                return JsonSerializer.Deserialize<StatsData>(json) ?? new StatsData();
            }
        }
        catch
        {
            // Corrupted file — start fresh
        }
        return new StatsData();
    }

    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to save stats");
        }
    }

    private void Prune()
    {
        if (_data.Days.Count <= MaxDayHistory)
            return;

        var keysToRemove = _data.Days
            .OrderByDescending(kv => kv.Key)
            .Skip(MaxDayHistory)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var key in keysToRemove)
            _data.Days.Remove(key);
    }
}
