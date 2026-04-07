namespace VoiceTyper.Models;

public class CustomHotkeySetting
{
    public string Name { get; set; } = "Custom hotkey";
    public string HotkeyModifiers { get; set; } = "";
    public string HotkeyKey { get; set; } = "";
    public string Instruction { get; set; } = "";
    public bool Enabled { get; set; } = true;
}
