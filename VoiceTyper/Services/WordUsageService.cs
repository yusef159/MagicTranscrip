using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using VoiceTyper.Models;

namespace VoiceTyper.Services;

public class WordUsageService
{
    private const int MaxPromptHistoryEntries = 500;

    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VoiceTyper");

    private static readonly string UsagePath =
        Path.Combine(SettingsDir, "word-usage.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly object _sync = new();

    public WordUsageSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            var store = LoadStore();
            return BuildSnapshot(store);
        }
    }

    public WordUsageSnapshot TrackTranscript(string transcript)
    {
        var words = CountWords(transcript);
        var finalText = transcript?.Trim() ?? string.Empty;
        lock (_sync)
        {
            var store = LoadStore();
            store.PromptHistory ??= new List<PromptHistoryRecord>();

            if (words > 0)
            {
                var today = DateTime.Now.Date;
                var key = today.ToString("yyyy-MM-dd");
                store.DailyWordCounts.TryGetValue(key, out var current);
                store.DailyWordCounts[key] = current + words;

                if (store.FirstUsageDate is null || today < store.FirstUsageDate.Value)
                    store.FirstUsageDate = today;
            }

            if (!string.IsNullOrWhiteSpace(finalText))
            {
                store.PromptHistory.Insert(0, new PromptHistoryRecord
                {
                    TimestampUtc = DateTime.UtcNow,
                    Prompt = finalText
                });

                if (store.PromptHistory.Count > MaxPromptHistoryEntries)
                    store.PromptHistory = store.PromptHistory.Take(MaxPromptHistoryEntries).ToList();
            }

            if (words > 0 || !string.IsNullOrWhiteSpace(finalText))
                SaveStore(store);

            return BuildSnapshot(store);
        }
    }

    public WordUsageSnapshot TrackPrompt(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            return GetSnapshot();

        lock (_sync)
        {
            var store = LoadStore();
            store.PromptHistory ??= new List<PromptHistoryRecord>();
            store.PromptHistory.Insert(0, new PromptHistoryRecord
            {
                TimestampUtc = DateTime.UtcNow,
                Prompt = prompt.Trim()
            });

            if (store.PromptHistory.Count > MaxPromptHistoryEntries)
                store.PromptHistory = store.PromptHistory.Take(MaxPromptHistoryEntries).ToList();

            SaveStore(store);
            return BuildSnapshot(store);
        }
    }

    public WordUsageSnapshot ClearTranscriptHistory(HistoryClearRange range)
    {
        lock (_sync)
        {
            var store = LoadStore();
            store.PromptHistory ??= new List<PromptHistoryRecord>();

            if (store.PromptHistory.Count == 0)
                return BuildSnapshot(store);

            var now = DateTime.Now;
            var removedCount = range switch
            {
                HistoryClearRange.Today => store.PromptHistory.RemoveAll(entry =>
                    entry.TimestampUtc.ToLocalTime().Date == now.Date),
                HistoryClearRange.ThisWeek => RemoveCurrentWeek(store.PromptHistory, now.Date),
                HistoryClearRange.ThisMonth => RemoveCurrentMonth(store.PromptHistory, now),
                HistoryClearRange.AllTime => ClearAll(store.PromptHistory),
                _ => 0
            };

            if (removedCount > 0)
                SaveStore(store);

            return BuildSnapshot(store);
        }
    }

    private static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        return Regex.Matches(text, @"[\p{L}\p{N}']+").Count;
    }

    private static WordUsageSnapshot BuildSnapshot(WordUsageStore store)
    {
        store.PromptHistory ??= new List<PromptHistoryRecord>();

        var today = DateTime.Now.Date;
        var todayKey = today.ToString("yyyy-MM-dd");
        var monthPrefix = today.ToString("yyyy-MM");

        store.DailyWordCounts.TryGetValue(todayKey, out var todayWords);
        var monthWords = store.DailyWordCounts
            .Where(kvp => kvp.Key.StartsWith(monthPrefix, StringComparison.Ordinal))
            .Sum(kvp => kvp.Value);
        var oneYearCutoff = today.AddDays(-364);
        var oneYearWords = store.DailyWordCounts
            .Where(kvp => DateTime.TryParseExact(
                kvp.Key,
                "yyyy-MM-dd",
                null,
                System.Globalization.DateTimeStyles.None,
                out var date) && date >= oneYearCutoff && date <= today)
            .Sum(kvp => kvp.Value);
        var totalWords = store.DailyWordCounts.Sum(kvp => kvp.Value);

        var averagePerDay = 0;
        if (totalWords > 0)
        {
            var firstDay = store.FirstUsageDate ?? today;
            var activeDays = Math.Max(1, (today - firstDay).Days + 1);
            averagePerDay = (int)Math.Round((double)totalWords / activeDays, MidpointRounding.AwayFromZero);
        }

        return new WordUsageSnapshot
        {
            TodayWords = todayWords,
            CurrentMonthWords = monthWords,
            OneYearWords = oneYearWords,
            AverageWordsPerDay = averagePerDay,
            PromptHistory = store.PromptHistory
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Prompt))
                .OrderByDescending(entry => entry.TimestampUtc)
                .Select(entry => new PromptHistoryEntry
                {
                    Timestamp = entry.TimestampUtc.ToLocalTime(),
                    Prompt = entry.Prompt
                })
                .ToList()
        };
    }

    private static WordUsageStore LoadStore()
    {
        try
        {
            if (File.Exists(UsagePath))
            {
                var json = File.ReadAllText(UsagePath);
                var store = JsonSerializer.Deserialize<WordUsageStore>(json, JsonOptions);
                if (store is not null)
                    return store;
            }
        }
        catch
        {
            // Corrupted file - start fresh.
        }

        return new WordUsageStore();
    }

    private static void SaveStore(WordUsageStore store)
    {
        Directory.CreateDirectory(SettingsDir);
        var json = JsonSerializer.Serialize(store, JsonOptions);
        File.WriteAllText(UsagePath, json);
    }

    private sealed class WordUsageStore
    {
        public DateTime? FirstUsageDate { get; set; }
        public Dictionary<string, int> DailyWordCounts { get; set; } = new();
        public List<PromptHistoryRecord> PromptHistory { get; set; } = new();
    }

    private sealed class PromptHistoryRecord
    {
        public DateTime TimestampUtc { get; set; }
        public string Prompt { get; set; } = string.Empty;
    }

    private static int ClearAll(List<PromptHistoryRecord> promptHistory)
    {
        var count = promptHistory.Count;
        promptHistory.Clear();
        return count;
    }

    private static int RemoveCurrentWeek(List<PromptHistoryRecord> promptHistory, DateTime today)
    {
        var weekStart = StartOfWeek(today, DayOfWeek.Monday);
        var weekEndExclusive = weekStart.AddDays(7);
        return promptHistory.RemoveAll(entry =>
        {
            var local = entry.TimestampUtc.ToLocalTime();
            return local >= weekStart && local < weekEndExclusive;
        });
    }

    private static int RemoveCurrentMonth(List<PromptHistoryRecord> promptHistory, DateTime now)
    {
        var monthStart = new DateTime(now.Year, now.Month, 1);
        var monthEndExclusive = monthStart.AddMonths(1);
        return promptHistory.RemoveAll(entry =>
        {
            var local = entry.TimestampUtc.ToLocalTime();
            return local >= monthStart && local < monthEndExclusive;
        });
    }

    private static DateTime StartOfWeek(DateTime value, DayOfWeek firstDayOfWeek)
    {
        var diff = (7 + (value.DayOfWeek - firstDayOfWeek)) % 7;
        return value.AddDays(-diff).Date;
    }
}
