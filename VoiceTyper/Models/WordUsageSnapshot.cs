namespace VoiceTyper.Models;

public class WordUsageSnapshot
{
    public int TodayWords { get; init; }
    public int CurrentMonthWords { get; init; }
    public int TotalWords { get; init; }
    public int AverageWordsPerDay { get; init; }
}
