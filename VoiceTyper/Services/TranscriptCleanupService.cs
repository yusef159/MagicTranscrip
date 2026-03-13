using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace VoiceTyper.Services;

public class TranscriptCleanupService
{
    private const string ChatUrl = "https://api.openai.com/v1/chat/completions";

    private const string SystemPrompt =
        "You are cleaning up dictated text. " +
        "Fix punctuation, capitalization, and obvious speech recognition mistakes. " +
        "Do not change the meaning. " +
        "Return only the corrected text.";

    private const string ProfessionalRewriteSystemPrompt =
        "You rewrite dictated text into a professional, polished version. " +
        "Keep the original intent and key facts. " +
        "Improve clarity, tone, grammar, and structure without adding new information. " +
        "Return only the rewritten text.";

    private readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    public async Task<string> CleanupAsync(string rawText)
    {
        return await RewriteWithPromptAsync(rawText, SystemPrompt);
    }

    public async Task<string> RewriteProfessionalAsync(string rawText)
    {
        return await RewriteWithPromptAsync(rawText, ProfessionalRewriteSystemPrompt);
    }

    private async Task<string> RewriteWithPromptAsync(string rawText, string systemPrompt)
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? throw new InvalidOperationException("OPENAI_API_KEY environment variable is not set.");

        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var payload = new
        {
            model = "gpt-4o-mini",
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = rawText }
            },
            temperature = 0.3
        };

        var json = JsonSerializer.Serialize(payload);
        var requestContent = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _http.PostAsync(ChatUrl, requestContent);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Rewrite API returned {(int)response.StatusCode}: {body}");

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString()?.Trim()
            ?? rawText;
    }
}
