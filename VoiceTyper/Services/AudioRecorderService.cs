using NAudio.Wave;

namespace VoiceTyper.Services;

public class AudioRecorderService : IDisposable
{
    private WaveInEvent? _waveIn;
    private long _recordedBytes;

    public int DeviceNumber { get; set; }
    public event Action<byte[]>? AudioChunkAvailable;

    public static List<string> GetMicrophoneDevices()
    {
        var devices = new List<string>();
        for (int i = 0; i < WaveInEvent.DeviceCount; i++)
        {
            var caps = WaveInEvent.GetCapabilities(i);
            devices.Add(caps.ProductName);
        }
        return devices;
    }

    public void StartRecording()
    {
        _waveIn = new WaveInEvent
        {
            // Realtime API audio/pcm expects 24kHz mono PCM16.
            WaveFormat = new WaveFormat(24000, 16, 1),
            DeviceNumber = DeviceNumber,
            BufferMilliseconds = 50
        };
        _recordedBytes = 0;

        _waveIn.DataAvailable += (_, e) =>
        {
            _recordedBytes += e.BytesRecorded;
            var chunk = new byte[e.BytesRecorded];
            Buffer.BlockCopy(e.Buffer, 0, chunk, 0, e.BytesRecorded);
            AudioChunkAvailable?.Invoke(chunk);
        };

        _waveIn.StartRecording();
    }

    public bool StopRecording()
    {
        _waveIn?.StopRecording();
        _waveIn?.Dispose();
        _waveIn = null;

        // ~100ms of audio at 24kHz mono PCM16 is 4,800 bytes.
        var hasAudio = _recordedBytes >= 4800;
        _recordedBytes = 0;
        return hasAudio;
    }

    public void Dispose()
    {
        _waveIn?.StopRecording();
        _waveIn?.Dispose();
    }
}
