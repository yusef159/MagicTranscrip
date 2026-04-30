namespace VoiceTyper.Models;

public class SnippetSetting
{
    public string Trigger { get; set; } = "";
    public string Replacement { get; set; } = "";
    public bool Enabled { get; set; } = true;
}
