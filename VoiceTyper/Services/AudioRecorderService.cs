using NAudio.Wave;

namespace VoiceTyper.Services;

public class AudioRecorderService : IDisposable
{
    private WaveInEvent? _waveIn;
    private long _recordedBytes;
    private bool _isRecording;
    private int _deviceNumber;
    private readonly object _sync = new();

    public int DeviceNumber
    {
        get => _deviceNumber;
        set
        {
            lock (_sync)
            {
                if (_deviceNumber == value)
                    return;

                _deviceNumber = value;
                RecreateWaveIn();
            }
        }
    }
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
        lock (_sync)
        {
            if (_isRecording)
                return;

            EnsureWaveInCreated();
            _recordedBytes = 0;
            _waveIn!.StartRecording();
            _isRecording = true;
        }
    }

    public bool StopRecording()
    {
        lock (_sync)
        {
            if (_isRecording)
            {
                _waveIn?.StopRecording();
                _isRecording = false;
            }

            // ~100ms of audio at 24kHz mono PCM16 is 4,800 bytes.
            var hasAudio = _recordedBytes >= 4800;
            _recordedBytes = 0;
            return hasAudio;
        }
    }

    public void Warmup()
    {
        lock (_sync)
        {
            EnsureWaveInCreated();
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            DisposeWaveIn();
        }
    }

    private void EnsureWaveInCreated()
    {
        if (_waveIn != null)
            return;

        _waveIn = new WaveInEvent
        {
            // Realtime API audio/pcm expects 24kHz mono PCM16.
            WaveFormat = new WaveFormat(24000, 16, 1),
            DeviceNumber = _deviceNumber,
            // Smaller buffers reduce perceived key-up/key-down latency.
            BufferMilliseconds = 20
        };

        _waveIn.DataAvailable += (_, e) =>
        {
            _recordedBytes += e.BytesRecorded;
            var chunk = new byte[e.BytesRecorded];
            Buffer.BlockCopy(e.Buffer, 0, chunk, 0, e.BytesRecorded);
            AudioChunkAvailable?.Invoke(chunk);
        };
    }

    private void RecreateWaveIn()
    {
        DisposeWaveIn();
        EnsureWaveInCreated();
    }

    private void DisposeWaveIn()
    {
        if (_waveIn == null)
            return;

        if (_isRecording)
        {
            _waveIn.StopRecording();
            _isRecording = false;
        }

        _waveIn.Dispose();
        _waveIn = null;
    }
}
