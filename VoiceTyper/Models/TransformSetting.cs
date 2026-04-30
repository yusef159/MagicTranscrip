namespace VoiceTyper.Models;

public class TransformSetting
{
    public string Name { get; set; } = "";
    public string HotkeyModifiers { get; set; } = "";
    public string HotkeyKey { get; set; } = "";
    public string Prompt { get; set; } = "";
    public bool Enabled { get; set; } = true;
}
