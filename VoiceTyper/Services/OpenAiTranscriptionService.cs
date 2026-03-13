using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace VoiceTyper.Services;

public class OpenAiTranscriptionService
{
    private const string TranscriptionUrl = "https://api.openai.com/v1/audio/transcriptions";

    private readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(60)
    };

    public string? LanguageHint { get; set; }

    public async Task<string> TranscribeAsync(string audioFilePath)
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? throw new InvalidOperationException("OPENAI_API_KEY environment variable is not set.");

        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var content = new MultipartFormDataContent();
        var audioBytes = await File.ReadAllBytesAsync(audioFilePath);
        var fileContent = new ByteArrayContent(audioBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(fileContent, "file", "audio.wav");
        content.Add(new StringContent("gpt-4o-mini-transcribe"), "model");

        if (!string.IsNullOrWhiteSpace(LanguageHint))
            content.Add(new StringContent(LanguageHint), "language");

        var response = await _http.PostAsync(TranscriptionUrl, content);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Transcription API returned {(int)response.StatusCode}: {body}");

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("text").GetString()
            ?? throw new InvalidOperationException("Transcription returned empty text.");
    }
}
