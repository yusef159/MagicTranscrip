namespace VoiceTyper.Models;

public class PromptHistoryEntry
{
    public DateTime Timestamp { get; init; }
    public string Prompt { get; init; } = string.Empty;
}
