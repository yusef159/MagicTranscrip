using System.Windows.Input;

namespace VoiceTyper.Models;

public class AppSettings
{
    public string HotkeyModifiers { get; set; } = "Ctrl+Alt";
    public string HotkeyKey { get; set; } = "Space";
    public string ProfessionalHotkeyModifiers { get; set; } = "Ctrl+Alt";
    public string ProfessionalHotkeyKey { get; set; } = "P";
    public bool EnableCleanup { get; set; } = false;
    public bool AutoPaste { get; set; } = true;
    public int MicrophoneDeviceIndex { get; set; } = 0;
    public string LanguageHint { get; set; } = "";
    public bool DictationEnabled { get; set; } = true;
}
