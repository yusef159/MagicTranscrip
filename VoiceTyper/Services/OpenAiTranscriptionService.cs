using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace VoiceTyper.Services;

public class OpenAiTranscriptionService
{
    private const string RealtimeUrl = "wss://api.openai.com/v1/realtime?intent=transcription";
    private const string RealtimeTranscriptionModel = "gpt-4o-mini-transcribe";
    private static readonly TimeSpan FinalTranscriptTimeout = TimeSpan.FromSeconds(12);

    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private Channel<string> _outboundEvents = Channel.CreateUnbounded<string>();
    private readonly StringBuilder _transcriptBuilder = new();
    private ClientWebSocket? _socket;
    private Task? _senderTask;
    private Task? _receiverTask;
    private TaskCompletionSource<string> _transcriptCompletion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CancellationTokenSource? _sessionCts;

    public string? LanguageHint { get; set; }
    public event Action<string>? TranscriptDeltaReceived;
    public bool IsSessionActive => _socket?.State == WebSocketState.Open;

    public async Task StartSessionAsync()
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? throw new InvalidOperationException("OPENAI_API_KEY environment variable is not set.");

        await ResetSessionStateAsync();

        _sessionCts = new CancellationTokenSource();
        _socket = new ClientWebSocket();
        _socket.Options.SetRequestHeader("Authorization", $"Bearer {apiKey}");
        _socket.Options.SetRequestHeader("OpenAI-Beta", "realtime=v1");
        await _socket.ConnectAsync(new Uri(RealtimeUrl), _sessionCts.Token);

        _senderTask = Task.Run(() => SenderLoopAsync(_sessionCts.Token));
        _receiverTask = Task.Run(() => ReceiverLoopAsync(_sessionCts.Token));

        var session = BuildSessionUpdateEvent();
        await SendRawAsync(session, _sessionCts.Token);
    }

    public Task AppendAudioChunkAsync(byte[] pcmChunk)
    {
        if (_socket == null)
            return Task.CompletedTask;

        var payload = JsonSerializer.Serialize(new
        {
            type = "input_audio_buffer.append",
            audio = Convert.ToBase64String(pcmChunk)
        });
        _outboundEvents.Writer.TryWrite(payload);
        return Task.CompletedTask;
    }

    public async Task<string> CompleteSessionAsync()
    {
        if (_socket == null)
            throw new InvalidOperationException("Realtime session is not active.");

        _outboundEvents.Writer.TryWrite("""{"type":"input_audio_buffer.commit"}""");
        try
        {
            return await WaitForTranscriptAsync(FinalTranscriptTimeout);
        }
        finally
        {
            await CloseSessionAsync();
        }
    }

    public async Task AbortSessionAsync()
    {
        await CloseSessionAsync();
    }

    private string BuildSessionUpdateEvent()
    {
        var session = new Dictionary<string, object?>
        {
            ["input_audio_format"] = "pcm16",
            ["input_audio_transcription"] = new Dictionary<string, object?>
            {
                ["model"] = RealtimeTranscriptionModel
            },
            // Manual commit at key-up; server-side VAD disabled.
            ["turn_detection"] = null
        };

        if (!string.IsNullOrWhiteSpace(LanguageHint))
        {
            var transcription = (Dictionary<string, object?>)session["input_audio_transcription"]!;
            transcription["language"] = LanguageHint;
        }

        return JsonSerializer.Serialize(new
        {
            type = "transcription_session.update",
            session
        });
    }

    private async Task<string> WaitForTranscriptAsync(TimeSpan timeout)
    {
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var registration = timeoutCts.Token.Register(() =>
            _transcriptCompletion.TrySetException(
                new TimeoutException("Timed out waiting for realtime transcription.")));
        return await _transcriptCompletion.Task;
    }

    private async Task SenderLoopAsync(CancellationToken ct)
    {
        while (await _outboundEvents.Reader.WaitToReadAsync(ct))
        {
            while (_outboundEvents.Reader.TryRead(out var payload))
            {
                await SendRawAsync(payload, ct);
            }
        }
    }

    private async Task SendRawAsync(string payload, CancellationToken ct)
    {
        if (_socket == null || _socket.State != WebSocketState.Open)
            return;

        var bytes = Encoding.UTF8.GetBytes(payload);
        await _sendLock.WaitAsync(ct);
        try
        {
            await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task ReceiverLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        var messageBuilder = new StringBuilder();

        while (!ct.IsCancellationRequested && _socket is { State: WebSocketState.Open })
        {
            var result = await _socket.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close)
                break;

            messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            if (!result.EndOfMessage)
                continue;

            var json = messageBuilder.ToString();
            messageBuilder.Clear();
            ProcessServerEvent(json);
        }
    }

    private void ProcessServerEvent(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("type", out var typeProp))
            return;

        var type = typeProp.GetString();
        switch (type)
        {
            case "conversation.item.input_audio_transcription.delta":
            {
                if (!root.TryGetProperty("delta", out var deltaProp))
                    return;
                var delta = deltaProp.GetString() ?? string.Empty;
                if (string.IsNullOrEmpty(delta))
                    return;
                _transcriptBuilder.Append(delta);
                TranscriptDeltaReceived?.Invoke(delta);
                return;
            }
            case "conversation.item.input_audio_transcription.completed":
            {
                var transcript = root.TryGetProperty("transcript", out var transcriptProp)
                    ? transcriptProp.GetString()
                    : null;
                var finalTranscript = string.IsNullOrWhiteSpace(transcript)
                    ? _transcriptBuilder.ToString()
                    : transcript!;
                _transcriptCompletion.TrySetResult(finalTranscript);
                return;
            }
            case "error":
            {
                var message = root.TryGetProperty("error", out var errObj)
                    && errObj.TryGetProperty("message", out var msg)
                        ? msg.GetString()
                        : "Unknown realtime API error.";
                _transcriptCompletion.TrySetException(new InvalidOperationException(message));
                return;
            }
        }
    }

    private async Task CloseSessionAsync()
    {
        try
        {
            _outboundEvents.Writer.TryComplete();
        }
        catch
        {
            // ignore double-complete races
        }

        _sessionCts?.Cancel();

        if (_socket is { State: WebSocketState.Open })
        {
            try
            {
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
            }
            catch
            {
                // ignore shutdown errors
            }
        }

        try
        {
            if (_senderTask != null)
                await _senderTask;
        }
        catch { }

        try
        {
            if (_receiverTask != null)
                await _receiverTask;
        }
        catch { }

        _socket?.Dispose();
        _socket = null;
        _sessionCts?.Dispose();
        _sessionCts = null;
    }

    private async Task ResetSessionStateAsync()
    {
        if (_socket != null)
            await CloseSessionAsync();

        while (_outboundEvents.Reader.TryRead(out _))
        {
            // drain stale events
        }

        _outboundEvents = Channel.CreateUnbounded<string>();
        _transcriptBuilder.Clear();
        _transcriptCompletion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
