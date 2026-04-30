using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Collections.ObjectModel;
using VoiceTyper.Models;
using VoiceTyper.Services;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfButton = System.Windows.Controls.Button;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace VoiceTyper;

public partial class MainWindow : Window
{
    private const string LightThemeResourceKey = "VTThemeLight";
    private const string DarkThemeResourceKey = "VTThemeDark";

    private readonly SettingsService _settingsService;
    private readonly WordUsageService _wordUsageService;
    private AppSettings _settings;
    private string _capturedTranscriptModifiers = "";
    private string _capturedTranscriptKey = "";
    private string _capturedProfessionalModifiers = "";
    private string _capturedProfessionalKey = "";
    private readonly ObservableCollection<CustomHotkeySetting> _customHotkeys = new();
    private readonly ObservableCollection<SnippetSetting> _snippets = new();
    private readonly ObservableCollection<TransformSetting> _transforms = new();
    private ResourceDictionary? _activeThemeDictionary;

    public event Action<AppSettings>? SettingsSaved;

    public MainWindow(SettingsService settingsService, WordUsageService wordUsageService, AppSettings settings)
    {
        InitializeComponent();
        _settingsService = settingsService;
        _wordUsageService = wordUsageService;
        _settings = settings;
        InitializeModernShell();
        CustomHotkeysItems.ItemsSource = _customHotkeys;
        SnippetsItems.ItemsSource = _snippets;
        TransformsItems.ItemsSource = _transforms;
        LoadIntoUi();
        UpdateWordUsage(_wordUsageService.GetSnapshot());
    }

    private void InitializeModernShell()
    {
        ThemeToggle.IsChecked = true;
        ApplyThemeFromToggle();
        SetVisibleSection("General");
    }

    private void LoadIntoUi()
    {
        _capturedTranscriptModifiers = _settings.HotkeyModifiers;
        _capturedTranscriptKey = _settings.HotkeyKey;
        TranscriptHotkeyBox.Text = FormatHotkey(_capturedTranscriptModifiers, _capturedTranscriptKey);

        _capturedProfessionalModifiers = _settings.ProfessionalHotkeyModifiers;
        _capturedProfessionalKey = _settings.ProfessionalHotkeyKey;
        ProfessionalHotkeyBox.Text = FormatHotkey(_capturedProfessionalModifiers, _capturedProfessionalKey);

        _customHotkeys.Clear();
        foreach (var customHotkey in _settings.CustomHotkeys ?? Enumerable.Empty<CustomHotkeySetting>())
        {
            _customHotkeys.Add(new CustomHotkeySetting
            {
                Name = customHotkey.Name,
                HotkeyModifiers = customHotkey.HotkeyModifiers,
                HotkeyKey = customHotkey.HotkeyKey,
                Instruction = customHotkey.Instruction,
                Enabled = customHotkey.Enabled
            });
        }

        _snippets.Clear();
        foreach (var snippet in _settings.Snippets ?? Enumerable.Empty<SnippetSetting>())
        {
            _snippets.Add(new SnippetSetting
            {
                Trigger = snippet.Trigger,
                Replacement = snippet.Replacement,
                Enabled = snippet.Enabled
            });
        }

        _transforms.Clear();
        foreach (var transform in _settings.Transforms ?? Enumerable.Empty<TransformSetting>())
        {
            _transforms.Add(new TransformSetting
            {
                Name = transform.Name,
                HotkeyModifiers = transform.HotkeyModifiers,
                HotkeyKey = transform.HotkeyKey,
                Prompt = transform.Prompt,
                Enabled = transform.Enabled
            });
        }

        var devices = AudioRecorderService.GetMicrophoneDevices();
        MicrophoneCombo.Items.Clear();
        foreach (var d in devices)
            MicrophoneCombo.Items.Add(d);

        if (devices.Count > 0)
            MicrophoneCombo.SelectedIndex = Math.Min(_settings.MicrophoneDeviceIndex, devices.Count - 1);

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
        WpfTextBox targetBox)
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

    private void AddCustomHotkey_Click(object sender, RoutedEventArgs e)
    {
        _customHotkeys.Add(new CustomHotkeySetting
        {
            Name = $"Custom hotkey {_customHotkeys.Count + 1}",
            HotkeyModifiers = "",
            HotkeyKey = "",
            Instruction = "",
            Enabled = true
        });
    }

    private void RemoveCustomHotkey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton { DataContext: CustomHotkeySetting hotkey })
            _customHotkeys.Remove(hotkey);
    }

    private void AddSnippet_Click(object sender, RoutedEventArgs e)
    {
        _snippets.Add(new SnippetSetting
        {
            Trigger = "",
            Replacement = "",
            Enabled = true
        });
    }

    private void RemoveSnippet_Click(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton { DataContext: SnippetSetting snippet })
            _snippets.Remove(snippet);
    }

    private void AddTransform_Click(object sender, RoutedEventArgs e)
    {
        _transforms.Add(new TransformSetting
        {
            Name = $"Transform {_transforms.Count + 1}",
            HotkeyModifiers = "",
            HotkeyKey = "",
            Prompt = "",
            Enabled = true
        });
    }

    private void RemoveTransform_Click(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton { DataContext: TransformSetting transform })
            _transforms.Remove(transform);
    }

    private void CustomHotkeyBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is WpfTextBox { DataContext: CustomHotkeySetting hotkey } box)
            box.Text = FormatHotkey(hotkey.HotkeyModifiers, hotkey.HotkeyKey);
    }

    private void CustomHotkeyBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is WpfTextBox box)
            box.Text = "Press a key combination...";
    }

    private void CustomHotkeyBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is WpfTextBox { DataContext: CustomHotkeySetting hotkey } box &&
            box.Text == "Press a key combination...")
        {
            box.Text = FormatHotkey(hotkey.HotkeyModifiers, hotkey.HotkeyKey);
        }
    }

    private void CustomHotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not WpfTextBox { DataContext: CustomHotkeySetting hotkey } box)
            return;

        CaptureHotkey(
            e,
            value => hotkey.HotkeyModifiers = value,
            value => hotkey.HotkeyKey = value,
            box);
    }

    private void TransformHotkeyBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is WpfTextBox { DataContext: TransformSetting transform } box)
            box.Text = FormatHotkey(transform.HotkeyModifiers, transform.HotkeyKey);
    }

    private void TransformHotkeyBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is WpfTextBox box)
            box.Text = "Press a key combination...";
    }

    private void TransformHotkeyBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is WpfTextBox { DataContext: TransformSetting transform } box &&
            box.Text == "Press a key combination...")
        {
            box.Text = FormatHotkey(transform.HotkeyModifiers, transform.HotkeyKey);
        }
    }

    private void TransformHotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not WpfTextBox { DataContext: TransformSetting transform } box)
            return;

        CaptureHotkey(
            e,
            value => transform.HotkeyModifiers = value,
            value => transform.HotkeyKey = value,
            box);
    }

    private static string FormatHotkey(string modifiers, string key)
    {
        return string.IsNullOrEmpty(modifiers) ? key : $"{modifiers}+{key}";
    }

    private void ThemeToggle_Checked(object sender, RoutedEventArgs e)
    {
        ApplyThemeFromToggle();
    }

    private void ApplyThemeFromToggle()
    {
        var selectedThemeKey = ThemeToggle.IsChecked == true
            ? DarkThemeResourceKey
            : LightThemeResourceKey;
        ApplyTheme(selectedThemeKey);
    }

    private void ApplyTheme(string themeResourceKey)
    {
        if (System.Windows.Application.Current.Resources[themeResourceKey] is not ResourceDictionary sourceTheme)
            return;

        if (_activeThemeDictionary != null)
            Resources.MergedDictionaries.Remove(_activeThemeDictionary);

        var clonedTheme = new ResourceDictionary();
        foreach (var key in sourceTheme.Keys)
            clonedTheme[key] = sourceTheme[key];

        Resources.MergedDictionaries.Add(clonedTheme);
        _activeThemeDictionary = clonedTheme;
    }

    private void NavSections_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NavSections.SelectedItem is not ListBoxItem selectedItem)
            return;

        var sectionKey = selectedItem.Tag as string ?? "General";
        SetVisibleSection(sectionKey);
    }

    private void SetVisibleSection(string sectionKey)
    {
        if (GeneralSectionPanel == null || CustomCommandsPanel == null || UsagePanel == null ||
            SnippetsPanel == null || TransformsPanel == null)
            return;

        GeneralSectionPanel.Visibility = sectionKey == "General" ? Visibility.Visible : Visibility.Collapsed;
        CustomCommandsPanel.Visibility = sectionKey == "Custom" ? Visibility.Visible : Visibility.Collapsed;
        UsagePanel.Visibility = sectionKey == "Usage" ? Visibility.Visible : Visibility.Collapsed;
        SnippetsPanel.Visibility = sectionKey == "Snippets" ? Visibility.Visible : Visibility.Collapsed;
        TransformsPanel.Visibility = sectionKey == "Transforms" ? Visibility.Visible : Visibility.Collapsed;
    }

    public void UpdateWordUsage(WordUsageSnapshot snapshot)
    {
        TodayWordsText.Text = FormatWordLabel(snapshot.TodayWords);
        MonthWordsText.Text = FormatWordLabel(snapshot.CurrentMonthWords);
        TotalWordsText.Text = FormatWordLabel(snapshot.OneYearWords);
        AverageWordsText.Text = FormatWordLabel(snapshot.AverageWordsPerDay);
    }

    private static string FormatWordLabel(int words)
    {
        var suffix = words == 1 ? "word" : "words";
        return $"{words:N0} {suffix}";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _settings.HotkeyModifiers = _capturedTranscriptModifiers;
        _settings.HotkeyKey = _capturedTranscriptKey;
        _settings.ProfessionalHotkeyModifiers = _capturedProfessionalModifiers;
        _settings.ProfessionalHotkeyKey = _capturedProfessionalKey;
        _settings.MicrophoneDeviceIndex = MicrophoneCombo.SelectedIndex >= 0 ? MicrophoneCombo.SelectedIndex : 0;
        _settings.AutoPaste = AutoPasteCheck.IsChecked == true;
        _settings.CustomHotkeys = _customHotkeys
            .Where(hotkey => !string.IsNullOrWhiteSpace(hotkey.HotkeyKey))
            .Select(hotkey => new CustomHotkeySetting
            {
                Name = hotkey.Name.Trim(),
                HotkeyModifiers = hotkey.HotkeyModifiers,
                HotkeyKey = hotkey.HotkeyKey,
                Instruction = hotkey.Instruction.Trim(),
                Enabled = hotkey.Enabled
            })
            .ToList();
        _settings.Snippets = _snippets
            .Where(snippet =>
                !string.IsNullOrWhiteSpace(snippet.Trigger) &&
                !string.IsNullOrWhiteSpace(snippet.Replacement))
            .Select(snippet => new SnippetSetting
            {
                Trigger = snippet.Trigger.Trim(),
                Replacement = snippet.Replacement.Trim(),
                Enabled = snippet.Enabled
            })
            .ToList();
        _settings.Transforms = _transforms
            .Where(transform =>
                !string.IsNullOrWhiteSpace(transform.HotkeyKey) &&
                !string.IsNullOrWhiteSpace(transform.Prompt))
            .Select(transform => new TransformSetting
            {
                Name = transform.Name.Trim(),
                HotkeyModifiers = transform.HotkeyModifiers,
                HotkeyKey = transform.HotkeyKey,
                Prompt = transform.Prompt.Trim(),
                Enabled = transform.Enabled
            })
            .ToList();

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
