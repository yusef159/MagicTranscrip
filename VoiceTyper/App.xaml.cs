using System.Net.Http;
using System.Windows;
using System.Windows.Forms;
using System.Media;
using VoiceTyper.Models;
using VoiceTyper.Services;
using Application = System.Windows.Application;

namespace VoiceTyper;

public partial class App : Application
{
    private SettingsService _settingsService = null!;
    private AppSettings _settings = null!;
    private HotkeyService _hotkeyService = null!;
    private AudioRecorderService _audioRecorder = null!;
    private OpenAiTranscriptionService _transcriptionService = null!;
    private TranscriptCleanupService _cleanupService = null!;
    private TextInsertionService _textInsertion = null!;
    private WordUsageService _wordUsageService = null!;
    private TrayIconService _trayIcon = null!;
    private MainWindow _settingsWindow = null!;
    private bool _processing;
    private TranscriptMode _currentTranscriptMode = TranscriptMode.Normal;
    private string _currentCustomInstruction = "";
    private readonly SemaphoreSlim _realtimeStartLock = new(1, 1);
    private Task _realtimeWarmupTask = Task.CompletedTask;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _settingsService = new SettingsService();
        _settings = _settingsService.Load();

        _audioRecorder = new AudioRecorderService { DeviceNumber = _settings.MicrophoneDeviceIndex };
        _audioRecorder.AudioChunkAvailable += OnAudioChunkAvailable;
        _transcriptionService = new OpenAiTranscriptionService { LanguageHint = _settings.LanguageHint };
        _cleanupService = new TranscriptCleanupService();
        _textInsertion = new TextInsertionService();
        _wordUsageService = new WordUsageService();

        _trayIcon = new TrayIconService();
        _trayIcon.SettingsRequested += ShowSettings;
        _trayIcon.ExitRequested += ExitApp;
        _trayIcon.DictationToggled += OnDictationToggled;

        _settingsWindow = new MainWindow(_settingsService, _wordUsageService, _settings);
        _settingsWindow.SettingsSaved += OnSettingsSaved;

        _hotkeyService = new HotkeyService();
        _hotkeyService.UpdateHotkeys(
            _settings.HotkeyModifiers,
            _settings.HotkeyKey,
            _settings.ProfessionalHotkeyModifiers,
            _settings.ProfessionalHotkeyKey);
        _hotkeyService.UpdateCustomHotkeys(_settings.CustomHotkeys);
        _hotkeyService.Enabled = _settings.DictationEnabled;
        _hotkeyService.RecordingStarted += OnBuiltInRecordingStarted;
        _hotkeyService.RecordingStopped += OnBuiltInRecordingStopped;
        _hotkeyService.CustomRecordingStarted += OnCustomRecordingStarted;
        _hotkeyService.CustomRecordingStopped += OnCustomRecordingStopped;

        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OPENAI_API_KEY")))
        {
            _trayIcon.ShowNotification("VoiceTyper",
                "OPENAI_API_KEY environment variable is not set. Dictation will not work.",
                ToolTipIcon.Warning);
        }
        else
        {
            // Warm a realtime session in the background so hotkey start is instant.
            _ = EnsureRealtimeSessionStartedAsync();
        }
    }

    private static void PlayStartBeep()
    {
        PlayClickSound();
    }

    private static void PlayStopBeep()
    {
        PlayClickSound();
    }

    private static void PlayClickSound()
    {
        try
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var path = System.IO.Path.Combine(baseDir, "click.wav");
            using var player = new SoundPlayer(path);
            player.Play();
        }
        catch
        {
            SystemSounds.Beep.Play();
        }
    }

    private void OnBuiltInRecordingStarted(TranscriptMode mode)
    {
        _currentCustomInstruction = "";
        OnRecordingStarted(mode);
    }

    private void OnCustomRecordingStarted(CustomHotkeyBinding customHotkey)
    {
        _currentCustomInstruction = customHotkey.Instruction;
        OnRecordingStarted(TranscriptMode.Normal);
    }

    private void OnRecordingStarted(TranscriptMode mode)
    {
        Console.WriteLine($"[VoiceTyper] OnRecordingStarted fired ({mode})");
        if (_processing)
        {
            Console.WriteLine("[VoiceTyper] Skipped — still processing previous recording");
            return;
        }

        _currentTranscriptMode = mode;

        try
        {
            var deviceCount = NAudio.Wave.WaveInEvent.DeviceCount;
            Console.WriteLine($"[VoiceTyper] Microphone devices found: {deviceCount}");
            if (deviceCount == 0)
            {
                Dispatcher.Invoke(() =>
                    _trayIcon.ShowNotification("VoiceTyper", "No microphone detected.", ToolTipIcon.Error));
                return;
            }

            _audioRecorder.StartRecording();
            PlayStartBeep();
            Console.WriteLine("[VoiceTyper] Recording started");
            Dispatcher.Invoke(() =>
            {
                _trayIcon.SetRecording(true);
                _trayIcon.SetStatus("VoiceTyper - Recording...");
            });

            // Ensure session exists, but do not block recording start UX.
            _ = EnsureRealtimeSessionStartedAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[VoiceTyper] Recording start error: {ex}");
            Dispatcher.Invoke(() =>
                _trayIcon.ShowNotification("VoiceTyper", $"Recording error: {ex.Message}", ToolTipIcon.Error));
        }
    }

    private void OnBuiltInRecordingStopped(TranscriptMode mode)
    {
        OnRecordingStopped(mode);
    }

    private void OnCustomRecordingStopped(CustomHotkeyBinding customHotkey)
    {
        _currentCustomInstruction = customHotkey.Instruction;
        OnRecordingStopped(TranscriptMode.Normal);
    }

    private async void OnRecordingStopped(TranscriptMode mode)
    {
        Console.WriteLine($"[VoiceTyper] OnRecordingStopped fired ({mode})");
        if (_processing)
        {
            Console.WriteLine("[VoiceTyper] Skipped — still processing");
            return;
        }
        _processing = true;
        _currentTranscriptMode = mode;

        PlayStopBeep();

        try
        {
            var hasAudio = _audioRecorder.StopRecording();
            Console.WriteLine($"[VoiceTyper] Recording stopped. Has audio: {hasAudio}");
            Dispatcher.Invoke(() =>
            {
                _trayIcon.SetRecording(false);
                _trayIcon.SetStatus("VoiceTyper - Transcribing...");
            });

            if (!hasAudio)
            {
                Console.WriteLine("[VoiceTyper] Recording was empty, aborting");
                await EnsureRealtimeSessionStartedAsync();
                await _transcriptionService.AbortSessionAsync();
                Dispatcher.Invoke(() =>
                    _trayIcon.ShowNotification("VoiceTyper", "Recording was empty.", ToolTipIcon.Warning));
                return;
            }

            await EnsureRealtimeSessionStartedAsync();
            Console.WriteLine("[VoiceTyper] Finalizing realtime transcription...");
            var transcript = await _transcriptionService.CompleteSessionAsync();
            Console.WriteLine($"[VoiceTyper] Transcript received: \"{transcript}\"");

            if (string.IsNullOrWhiteSpace(transcript))
            {
                Console.WriteLine("[VoiceTyper] Transcript was empty");
                Dispatcher.Invoke(() =>
                    _trayIcon.ShowNotification("VoiceTyper", "Transcription returned empty text.", ToolTipIcon.Warning));
                return;
            }

            if (_settings.EnableCleanup)
            {
                Console.WriteLine("[VoiceTyper] Running cleanup...");
                Dispatcher.Invoke(() => _trayIcon.SetStatus("VoiceTyper - Cleaning up..."));
                transcript = await _cleanupService.CleanupAsync(transcript);
                Console.WriteLine($"[VoiceTyper] Cleaned up: \"{transcript}\"");
            }

            if (_currentTranscriptMode == TranscriptMode.Professional)
            {
                Console.WriteLine("[VoiceTyper] Rewriting transcript professionally...");
                Dispatcher.Invoke(() => _trayIcon.SetStatus("VoiceTyper - Rewriting..."));
                transcript = await _cleanupService.RewriteProfessionalAsync(transcript);
                Console.WriteLine($"[VoiceTyper] Professionally rewritten: \"{transcript}\"");
            }

            if (!string.IsNullOrWhiteSpace(_currentCustomInstruction))
            {
                Console.WriteLine("[VoiceTyper] Applying custom hotkey instruction...");
                Dispatcher.Invoke(() => _trayIcon.SetStatus("VoiceTyper - Applying custom instruction..."));
                transcript = await _cleanupService.RewriteWithInstructionAsync(transcript, _currentCustomInstruction);
                Console.WriteLine($"[VoiceTyper] Custom-instruction rewrite complete: \"{transcript}\"");
            }

            var usageSnapshot = _wordUsageService.TrackTranscript(transcript);
            Dispatcher.Invoke(() => _settingsWindow.UpdateWordUsage(usageSnapshot));

            if (_settings.AutoPaste)
            {
                Console.WriteLine("[VoiceTyper] Inserting text via clipboard + Ctrl+V...");
                await _textInsertion.InsertTextAsync(transcript);
                Console.WriteLine("[VoiceTyper] Text inserted");
            }
            else
            {
                Console.WriteLine("[VoiceTyper] AutoPaste is off, skipping insertion");
            }

            Dispatcher.Invoke(() => _trayIcon.SetStatus("VoiceTyper - Ready"));
            Console.WriteLine("[VoiceTyper] Done — ready for next recording");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[VoiceTyper] ERROR: {ex}");
            Dispatcher.Invoke(() =>
            {
                var message = ex is HttpRequestException
                    ? $"Network error: {ex.Message}"
                    : $"Error: {ex.Message}";
                _trayIcon.ShowNotification("VoiceTyper", message, ToolTipIcon.Error);
                _trayIcon.SetStatus("VoiceTyper - Ready");
            });
        }
        finally
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OPENAI_API_KEY")))
            {
                // Re-warm next session immediately for near-instant next hotkey press.
                _ = EnsureRealtimeSessionStartedAsync();
            }
            _currentCustomInstruction = "";
            _processing = false;
        }
    }

    private void OnDictationToggled(bool enabled)
    {
        _settings.DictationEnabled = enabled;
        _hotkeyService.Enabled = enabled;
        _settingsService.Save(_settings);
    }

    private void OnSettingsSaved(AppSettings settings)
    {
        _settings = settings;
        _hotkeyService.UpdateHotkeys(
            settings.HotkeyModifiers,
            settings.HotkeyKey,
            settings.ProfessionalHotkeyModifiers,
            settings.ProfessionalHotkeyKey);
        _hotkeyService.UpdateCustomHotkeys(settings.CustomHotkeys);
        _hotkeyService.Enabled = settings.DictationEnabled;
        _audioRecorder.DeviceNumber = settings.MicrophoneDeviceIndex;
        _transcriptionService.LanguageHint = settings.LanguageHint;
        _trayIcon.SetDictationEnabled(settings.DictationEnabled);
    }

    private void ShowSettings()
    {
        Dispatcher.Invoke(() =>
        {
            _settingsWindow.Show();
            _settingsWindow.Activate();
        });
    }

    private void ExitApp()
    {
        _hotkeyService.Dispose();
        _audioRecorder.Dispose();
        _trayIcon.Dispose();
        Shutdown();
    }

    private async void OnAudioChunkAvailable(byte[] chunk)
    {
        try
        {
            await _transcriptionService.AppendAudioChunkAsync(chunk);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[VoiceTyper] Failed to stream audio chunk: {ex.Message}");
        }
    }

    private async Task EnsureRealtimeSessionStartedAsync()
    {
        await _realtimeStartLock.WaitAsync();
        try
        {
            if (!_transcriptionService.IsSessionActive && _realtimeWarmupTask.IsCompleted)
            {
                _realtimeWarmupTask = _transcriptionService.StartSessionAsync();
            }
        }
        finally
        {
            _realtimeStartLock.Release();
        }

        await _realtimeWarmupTask;
    }
}
