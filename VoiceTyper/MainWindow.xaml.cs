using System.Windows;
using System.Windows.Input;
using VoiceTyper.Models;
using VoiceTyper.Services;

namespace VoiceTyper;

public partial class MainWindow : Window
{
    private readonly SettingsService _settingsService;
    private AppSettings _settings;
    private string _capturedModifiers = "";
    private string _capturedKey = "";

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
        _capturedModifiers = _settings.HotkeyModifiers;
        _capturedKey = _settings.HotkeyKey;
        HotkeyBox.Text = FormatHotkey(_capturedModifiers, _capturedKey);

        var devices = AudioRecorderService.GetMicrophoneDevices();
        MicrophoneCombo.Items.Clear();
        foreach (var d in devices)
            MicrophoneCombo.Items.Add(d);

        if (devices.Count > 0)
            MicrophoneCombo.SelectedIndex = Math.Min(_settings.MicrophoneDeviceIndex, devices.Count - 1);

        LanguageBox.Text = _settings.LanguageHint;
        CleanupCheck.IsChecked = _settings.EnableCleanup;
        AutoPasteCheck.IsChecked = _settings.AutoPaste;
        ProfessionalRewriteCheck.IsChecked = _settings.EnableProfessionalRewrite;
    }

    private void HotkeyBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
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

        _capturedModifiers = string.Join("+", mods);
        _capturedKey = key.ToString();
        HotkeyBox.Text = FormatHotkey(_capturedModifiers, _capturedKey);
    }

    private void HotkeyBox_GotFocus(object sender, RoutedEventArgs e)
    {
        HotkeyBox.Text = "Press a key combination...";
    }

    private void HotkeyBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (HotkeyBox.Text == "Press a key combination...")
            HotkeyBox.Text = FormatHotkey(_capturedModifiers, _capturedKey);
    }

    private static string FormatHotkey(string modifiers, string key)
    {
        return string.IsNullOrEmpty(modifiers) ? key : $"{modifiers}+{key}";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _settings.HotkeyModifiers = _capturedModifiers;
        _settings.HotkeyKey = _capturedKey;
        _settings.MicrophoneDeviceIndex = MicrophoneCombo.SelectedIndex >= 0 ? MicrophoneCombo.SelectedIndex : 0;
        _settings.LanguageHint = LanguageBox.Text.Trim();
        _settings.EnableCleanup = CleanupCheck.IsChecked == true;
        _settings.AutoPaste = AutoPasteCheck.IsChecked == true;
        _settings.EnableProfessionalRewrite = ProfessionalRewriteCheck.IsChecked == true;

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
