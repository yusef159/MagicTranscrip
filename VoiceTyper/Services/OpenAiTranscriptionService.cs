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

        await using var audioStream = File.OpenRead(audioFilePath);
        using var content = new MultipartFormDataContent();
        using var fileContent = new StreamContent(audioStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(fileContent, "file", "audio.wav");
        content.Add(new StringContent("gpt-4o-mini-transcribe"), "model");

        if (!string.IsNullOrWhiteSpace(LanguageHint))
            content.Add(new StringContent(LanguageHint), "language");

        using var request = new HttpRequestMessage(HttpMethod.Post, TranscriptionUrl)
        {
            Content = content
        };
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Transcription API returned {(int)response.StatusCode}: {body}");
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(responseStream);
        return doc.RootElement.GetProperty("text").GetString()
            ?? throw new InvalidOperationException("Transcription returned empty text.");
    }
}
