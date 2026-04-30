using System.Net.Http;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using System.Media;
using NAudio.Wave;
using VoiceTyper.Models;
using VoiceTyper.Services;
using Application = System.Windows.Application;

namespace VoiceTyper;

public partial class App : Application
{
    private static readonly object _soundLock = new();
    private static IWavePlayer? _activeSoundOutput;
    private static AudioFileReader? _activeSoundReader;
    private static bool _transformSoundUnavailable;
    private static bool _defaultSoundUnavailable;
    private static readonly string _downloadsDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    private static readonly string _transformSoundPath = Path.Combine(_downloadsDirectory, "transform.mp3");
    private static readonly string _defaultSoundPath = Path.Combine(_downloadsDirectory, "ping.mp3");

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
        _hotkeyService.UpdateTransforms(_settings.Transforms);
        _hotkeyService.Enabled = _settings.DictationEnabled;
        _hotkeyService.RecordingStarted += OnBuiltInRecordingStarted;
        _hotkeyService.RecordingStopped += OnBuiltInRecordingStopped;
        _hotkeyService.CustomRecordingStarted += OnCustomRecordingStarted;
        _hotkeyService.CustomRecordingStopped += OnCustomRecordingStopped;
        _hotkeyService.TransformTriggered += OnTransformTriggered;

        // Prewarm local resources at startup to reduce first hotkey latency.
        try
        {
            _audioRecorder.Warmup();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[VoiceTyper] Audio warmup failed: {ex.Message}");
        }

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
        PlayDefaultSound();
    }

    private static void PlayStopBeep()
    {
        PlayDefaultSound();
    }

    private static void PlayTransformBeep()
    {
        PlayConfiguredSound(_transformSoundPath, ref _transformSoundUnavailable);
    }

    private static void PlayDefaultSound()
    {
        PlayConfiguredSound(_defaultSoundPath, ref _defaultSoundUnavailable);
    }

    private static void PlayConfiguredSound(string path, ref bool unavailableFlag)
    {
        try
        {
            lock (_soundLock)
            {
                if (unavailableFlag)
                {
                    SystemSounds.Beep.Play();
                    return;
                }

                if (!File.Exists(path))
                {
                    unavailableFlag = true;
                    SystemSounds.Beep.Play();
                    return;
                }

                _activeSoundOutput?.Stop();
                _activeSoundOutput?.Dispose();
                _activeSoundOutput = null;
                _activeSoundReader?.Dispose();
                _activeSoundReader = null;

                var output = new WaveOutEvent();
                var reader = new AudioFileReader(path);
                output.Init(reader);
                output.PlaybackStopped += (_, _) =>
                {
                    lock (_soundLock)
                    {
                        if (ReferenceEquals(_activeSoundOutput, output))
                        {
                            _activeSoundOutput.Dispose();
                            _activeSoundOutput = null;
                        }

                        if (ReferenceEquals(_activeSoundReader, reader))
                        {
                            _activeSoundReader.Dispose();
                            _activeSoundReader = null;
                        }
                    }
                };

                _activeSoundOutput = output;
                _activeSoundReader = reader;
                output.Play();
            }
        }
        catch
        {
            unavailableFlag = true;
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
            _audioRecorder.StartRecording();
            PlayStartBeep();
            Console.WriteLine("[VoiceTyper] Recording started");
            Dispatcher.BeginInvoke(() =>
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

            var snippetApplied = TryResolveSnippetReplacement(transcript, out var snippetText);
            if (snippetApplied)
            {
                transcript = snippetText;
                Console.WriteLine($"[VoiceTyper] Snippet matched. Expanded transcript to: \"{transcript}\"");
            }

            if (_settings.EnableCleanup && !snippetApplied)
            {
                Console.WriteLine("[VoiceTyper] Running cleanup...");
                Dispatcher.Invoke(() => _trayIcon.SetStatus("VoiceTyper - Cleaning up..."));
                transcript = await _cleanupService.CleanupAsync(transcript);
                Console.WriteLine($"[VoiceTyper] Cleaned up: \"{transcript}\"");
            }

            if (_currentTranscriptMode == TranscriptMode.Professional && !snippetApplied)
            {
                Console.WriteLine("[VoiceTyper] Rewriting transcript professionally...");
                Dispatcher.Invoke(() => _trayIcon.SetStatus("VoiceTyper - Rewriting..."));
                transcript = await _cleanupService.RewriteProfessionalAsync(transcript);
                Console.WriteLine($"[VoiceTyper] Professionally rewritten: \"{transcript}\"");
            }

            if (!string.IsNullOrWhiteSpace(_currentCustomInstruction) && !snippetApplied)
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
        _hotkeyService.UpdateTransforms(settings.Transforms);
        _hotkeyService.Enabled = settings.DictationEnabled;
        _audioRecorder.DeviceNumber = settings.MicrophoneDeviceIndex;
        _transcriptionService.LanguageHint = settings.LanguageHint;
        _trayIcon.SetDictationEnabled(settings.DictationEnabled);
    }

    private async void OnTransformTriggered(TransformHotkeyBinding transform)
    {
        Console.WriteLine($"[VoiceTyper] Transform triggered: {transform.Name}");
        if (_processing)
        {
            Console.WriteLine("[VoiceTyper] Transform skipped - already processing");
            return;
        }

        _processing = true;
        try
        {
            PlayTransformBeep();
            Dispatcher.Invoke(() => _trayIcon.SetStatus("VoiceTyper - Reading selection..."));
            var selectedText = await _textInsertion.GetSelectedTextAsync();
            if (string.IsNullOrWhiteSpace(selectedText))
            {
                Dispatcher.Invoke(() =>
                    _trayIcon.ShowNotification("VoiceTyper", "No selected text found for transform.", ToolTipIcon.Warning));
                return;
            }

            Dispatcher.Invoke(() => _trayIcon.SetStatus("VoiceTyper - Applying transform..."));
            var rewritten = await _cleanupService.RewriteWithInstructionAsync(selectedText, transform.Prompt);
            if (string.IsNullOrWhiteSpace(rewritten))
                rewritten = selectedText;

            await _textInsertion.InsertTextAsync(rewritten);
            Dispatcher.Invoke(() => _trayIcon.SetStatus("VoiceTyper - Ready"));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[VoiceTyper] Transform error: {ex}");
            Dispatcher.Invoke(() =>
            {
                var message = ex is HttpRequestException
                    ? $"Transform network error: {ex.Message}"
                    : $"Transform error: {ex.Message}";
                _trayIcon.ShowNotification("VoiceTyper", message, ToolTipIcon.Error);
                _trayIcon.SetStatus("VoiceTyper - Ready");
            });
        }
        finally
        {
            Dispatcher.Invoke(() => _trayIcon.SetStatus("VoiceTyper - Ready"));
            _processing = false;
        }
    }

    private bool TryResolveSnippetReplacement(string transcript, out string replacement)
    {
        replacement = transcript;
        if (_settings.Snippets == null || _settings.Snippets.Count == 0)
            return false;

        var normalizedTranscript = NormalizeSnippetKey(transcript);
        if (string.IsNullOrWhiteSpace(normalizedTranscript))
            return false;

        foreach (var snippet in _settings.Snippets)
        {
            if (!snippet.Enabled || string.IsNullOrWhiteSpace(snippet.Trigger) || string.IsNullOrWhiteSpace(snippet.Replacement))
                continue;

            var normalizedTrigger = NormalizeSnippetKey(snippet.Trigger);
            if (!string.Equals(normalizedTrigger, normalizedTranscript, StringComparison.OrdinalIgnoreCase))
                continue;

            replacement = snippet.Replacement.Trim();
            return true;
        }

        return false;
    }

    private static string NormalizeSnippetKey(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var trimmed = input.Trim();
        trimmed = trimmed.TrimEnd('.', ',', '!', '?', ';', ':', '"', '\'');

        var normalized = new System.Text.StringBuilder(trimmed.Length);
        var sawWhitespace = false;
        foreach (var ch in trimmed)
        {
            if (char.IsWhiteSpace(ch))
            {
                sawWhitespace = true;
                continue;
            }

            if (sawWhitespace && normalized.Length > 0)
                normalized.Append(' ');

            normalized.Append(char.ToLowerInvariant(ch));
            sawWhitespace = false;
        }

        return normalized.ToString();
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
