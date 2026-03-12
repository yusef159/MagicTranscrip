using System.Net.Http;
using System.Windows;
using System.Windows.Forms;
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
    private TrayIconService _trayIcon = null!;
    private MainWindow _settingsWindow = null!;
    private bool _processing;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _settingsService = new SettingsService();
        _settings = _settingsService.Load();

        _audioRecorder = new AudioRecorderService { DeviceNumber = _settings.MicrophoneDeviceIndex };
        _transcriptionService = new OpenAiTranscriptionService { LanguageHint = _settings.LanguageHint };
        _cleanupService = new TranscriptCleanupService();
        _textInsertion = new TextInsertionService();

        _trayIcon = new TrayIconService();
        _trayIcon.SettingsRequested += ShowSettings;
        _trayIcon.ExitRequested += ExitApp;
        _trayIcon.DictationToggled += OnDictationToggled;

        _settingsWindow = new MainWindow(_settingsService, _settings);
        _settingsWindow.SettingsSaved += OnSettingsSaved;

        _hotkeyService = new HotkeyService();
        _hotkeyService.UpdateHotkey(_settings.HotkeyModifiers, _settings.HotkeyKey);
        _hotkeyService.Enabled = _settings.DictationEnabled;
        _hotkeyService.RecordingStarted += OnRecordingStarted;
        _hotkeyService.RecordingStopped += OnRecordingStopped;

        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OPENAI_API_KEY")))
        {
            _trayIcon.ShowNotification("VoiceTyper",
                "OPENAI_API_KEY environment variable is not set. Dictation will not work.",
                ToolTipIcon.Warning);
        }
    }

    private void OnRecordingStarted()
    {
        Console.WriteLine("[VoiceTyper] OnRecordingStarted fired");
        if (_processing)
        {
            Console.WriteLine("[VoiceTyper] Skipped — still processing previous recording");
            return;
        }

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
            Console.WriteLine("[VoiceTyper] Recording started");
            Dispatcher.Invoke(() => _trayIcon.SetRecording(true));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[VoiceTyper] Recording start error: {ex}");
            Dispatcher.Invoke(() =>
                _trayIcon.ShowNotification("VoiceTyper", $"Recording error: {ex.Message}", ToolTipIcon.Error));
        }
    }

    private async void OnRecordingStopped()
    {
        Console.WriteLine("[VoiceTyper] OnRecordingStopped fired");
        if (_processing)
        {
            Console.WriteLine("[VoiceTyper] Skipped — still processing");
            return;
        }
        _processing = true;

        string? filePath = null;
        try
        {
            filePath = _audioRecorder.StopRecording();
            Console.WriteLine($"[VoiceTyper] Recording stopped. File: {filePath ?? "NULL (empty)"}");
            Dispatcher.Invoke(() =>
            {
                _trayIcon.SetRecording(false);
                _trayIcon.SetStatus("VoiceTyper - Transcribing...");
            });

            if (filePath == null)
            {
                Console.WriteLine("[VoiceTyper] Recording was empty, aborting");
                Dispatcher.Invoke(() =>
                    _trayIcon.ShowNotification("VoiceTyper", "Recording was empty.", ToolTipIcon.Warning));
                return;
            }

            var fileSize = new System.IO.FileInfo(filePath).Length;
            Console.WriteLine($"[VoiceTyper] WAV file size: {fileSize} bytes");

            Console.WriteLine("[VoiceTyper] Calling OpenAI transcription...");
            var transcript = await _transcriptionService.TranscribeAsync(filePath);
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
            AudioRecorderService.CleanupFile(filePath);
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
        _hotkeyService.UpdateHotkey(settings.HotkeyModifiers, settings.HotkeyKey);
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
}
