using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using VoiceTyper.Models;

namespace VoiceTyper.Services;

public class WordUsageService
{
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
        lock (_sync)
        {
            var store = LoadStore();
            if (words > 0)
            {
                var today = DateTime.Now.Date;
                var key = today.ToString("yyyy-MM-dd");
                store.DailyWordCounts.TryGetValue(key, out var current);
                store.DailyWordCounts[key] = current + words;

                if (store.FirstUsageDate is null || today < store.FirstUsageDate.Value)
                    store.FirstUsageDate = today;

                SaveStore(store);
            }

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
            AverageWordsPerDay = averagePerDay
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
    }
}
