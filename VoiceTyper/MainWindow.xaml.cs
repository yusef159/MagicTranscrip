using System.Windows;
using System.Windows.Input;
using VoiceTyper.Models;
using VoiceTyper.Services;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace VoiceTyper;

public partial class MainWindow : Window
{
    private readonly SettingsService _settingsService;
    private AppSettings _settings;
    private string _capturedTranscriptModifiers = "";
    private string _capturedTranscriptKey = "";
    private string _capturedProfessionalModifiers = "";
    private string _capturedProfessionalKey = "";

    public event Action<AppSettings>? SettingsSaved;

    public MainWindow(SettingsService settingsService, AppSettings settings)
    {
        InitializeComponent();
        _settingsService = settingsService;
        _settings = settings;
        LoadIntoUi();
    }

    private void LoadIntoUi()
    {
        _capturedTranscriptModifiers = _settings.HotkeyModifiers;
        _capturedTranscriptKey = _settings.HotkeyKey;
        TranscriptHotkeyBox.Text = FormatHotkey(_capturedTranscriptModifiers, _capturedTranscriptKey);

        _capturedProfessionalModifiers = _settings.ProfessionalHotkeyModifiers;
        _capturedProfessionalKey = _settings.ProfessionalHotkeyKey;
        ProfessionalHotkeyBox.Text = FormatHotkey(_capturedProfessionalModifiers, _capturedProfessionalKey);

        var devices = AudioRecorderService.GetMicrophoneDevices();
        MicrophoneCombo.Items.Clear();
        foreach (var d in devices)
            MicrophoneCombo.Items.Add(d);

        if (devices.Count > 0)
            MicrophoneCombo.SelectedIndex = Math.Min(_settings.MicrophoneDeviceIndex, devices.Count - 1);

        LanguageBox.Text = _settings.LanguageHint;
        AutoPasteCheck.IsChecked = _settings.AutoPaste;
    }

    private void TranscriptHotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        CaptureHotkey(
            e,
            value => _capturedTranscriptModifiers = value,
            value => _capturedTranscriptKey = value,
            TranscriptHotkeyBox);
    }

    private void ProfessionalHotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        CaptureHotkey(
            e,
            value => _capturedProfessionalModifiers = value,
            value => _capturedProfessionalKey = value,
            ProfessionalHotkeyBox);
    }

    private static void CaptureHotkey(
        KeyEventArgs e,
        Action<string> setModifiers,
        Action<string> setKey,
        System.Windows.Controls.TextBox targetBox)
    {
        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return;

        var mods = new List<string>();
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) mods.Add("Ctrl");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) mods.Add("Alt");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) mods.Add("Shift");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) mods.Add("Windows");

        var modifiers = string.Join("+", mods);
        var keyText = key.ToString();
        setModifiers(modifiers);
        setKey(keyText);
        targetBox.Text = FormatHotkey(modifiers, keyText);
    }

    private void TranscriptHotkeyBox_GotFocus(object sender, RoutedEventArgs e)
    {
        TranscriptHotkeyBox.Text = "Press a key combination...";
    }

    private void TranscriptHotkeyBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (TranscriptHotkeyBox.Text == "Press a key combination...")
            TranscriptHotkeyBox.Text = FormatHotkey(_capturedTranscriptModifiers, _capturedTranscriptKey);
    }

    private void ProfessionalHotkeyBox_GotFocus(object sender, RoutedEventArgs e)
    {
        ProfessionalHotkeyBox.Text = "Press a key combination...";
    }

    private void ProfessionalHotkeyBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (ProfessionalHotkeyBox.Text == "Press a key combination...")
            ProfessionalHotkeyBox.Text = FormatHotkey(_capturedProfessionalModifiers, _capturedProfessionalKey);
    }

    private static string FormatHotkey(string modifiers, string key)
    {
        return string.IsNullOrEmpty(modifiers) ? key : $"{modifiers}+{key}";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _settings.HotkeyModifiers = _capturedTranscriptModifiers;
        _settings.HotkeyKey = _capturedTranscriptKey;
        _settings.ProfessionalHotkeyModifiers = _capturedProfessionalModifiers;
        _settings.ProfessionalHotkeyKey = _capturedProfessionalKey;
        _settings.MicrophoneDeviceIndex = MicrophoneCombo.SelectedIndex >= 0 ? MicrophoneCombo.SelectedIndex : 0;
        _settings.LanguageHint = LanguageBox.Text.Trim();
        _settings.AutoPaste = AutoPasteCheck.IsChecked == true;

        _settingsService.Save(_settings);
        SettingsSaved?.Invoke(_settings);
        Hide();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        LoadIntoUi();
        Hide();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        LoadIntoUi();
        Hide();
    }
}
