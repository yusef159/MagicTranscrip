namespace VoiceTyper.Services;

public static class OpenAiConfiguration
{
    public static bool TryGetApiKey(out string apiKey)
    {
        apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")?.Trim() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(apiKey);
    }

    public static string GetApiKeyOrThrow()
    {
        if (TryGetApiKey(out var apiKey))
            return apiKey;

        throw new InvalidOperationException(
            "OPENAI_API_KEY environment variable is not set.");
    }
}
