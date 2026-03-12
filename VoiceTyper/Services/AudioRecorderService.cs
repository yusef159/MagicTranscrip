using System.IO;
using NAudio.Wave;

namespace VoiceTyper.Services;

public class AudioRecorderService : IDisposable
{
    private WaveInEvent? _waveIn;
    private WaveFileWriter? _writer;
    private string? _tempFilePath;

    public int DeviceNumber { get; set; }

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
        _tempFilePath = Path.Combine(Path.GetTempPath(), $"voicetyper_{Guid.NewGuid():N}.wav");

        _waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(16000, 16, 1),
            DeviceNumber = DeviceNumber,
            BufferMilliseconds = 50
        };

        _writer = new WaveFileWriter(_tempFilePath, _waveIn.WaveFormat);

        _waveIn.DataAvailable += (_, e) =>
        {
            _writer?.Write(e.Buffer, 0, e.BytesRecorded);
        };

        _waveIn.StartRecording();
    }

    /// <returns>Path to the recorded WAV file, or null if nothing was recorded.</returns>
    public string? StopRecording()
    {
        _waveIn?.StopRecording();
        _waveIn?.Dispose();
        _waveIn = null;

        _writer?.Flush();
        var length = _writer?.Length ?? 0;
        _writer?.Dispose();
        _writer = null;

        if (length <= 44) // WAV header only, no audio data
        {
            TryDeleteFile(_tempFilePath);
            return null;
        }

        return _tempFilePath;
    }

    public static void CleanupFile(string? path)
    {
        TryDeleteFile(path);
    }

    private static void TryDeleteFile(string? path)
    {
        try
        {
            if (path != null && File.Exists(path))
                File.Delete(path);
        }
        catch { }
    }

    public void Dispose()
    {
        _waveIn?.StopRecording();
        _waveIn?.Dispose();
        _writer?.Dispose();
    }
}
