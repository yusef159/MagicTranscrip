using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;
using VoiceTyper.Models;

namespace VoiceTyper.Services;

public enum TranscriptMode
{
    Normal,
    Professional
}

public class CustomHotkeyBinding
{
    public string Name { get; init; } = "";
    public string Instruction { get; init; } = "";
    public Key TriggerKey { get; init; } = Key.None;
    public ModifierKeys Modifiers { get; init; } = ModifierKeys.None;
}

public class HotkeyService : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    private IntPtr _hookId = IntPtr.Zero;
    private readonly LowLevelKeyboardProc _hookProc;
    private bool _isRecording;
    private TranscriptMode _activeMode = TranscriptMode.Normal;
    private Key _activeTriggerKey = Key.None;
    private CustomHotkeyBinding? _activeCustomHotkey;
    private List<CustomHotkeyBinding> _customHotkeys = new();

    public Key TriggerKey { get; set; } = Key.Space;
    public ModifierKeys Modifiers { get; set; } = ModifierKeys.Control | ModifierKeys.Alt;
    public Key ProfessionalTriggerKey { get; set; } = Key.P;
    public ModifierKeys ProfessionalModifiers { get; set; } = ModifierKeys.Control | ModifierKeys.Alt;
    public bool Enabled { get; set; } = true;

    public event Action<TranscriptMode>? RecordingStarted;
    public event Action<TranscriptMode>? RecordingStopped;
    public event Action<CustomHotkeyBinding>? CustomRecordingStarted;
    public event Action<CustomHotkeyBinding>? CustomRecordingStopped;

    public HotkeyService()
    {
        _hookProc = HookCallback;
        _hookId = SetHook(_hookProc);
        Console.WriteLine($"[HotkeyService] Hook installed: 0x{_hookId:X}");
    }

    public void UpdateHotkeys(string modifiers, string key, string professionalModifiers, string professionalKey)
    {
        Modifiers = ParseModifiers(modifiers);
        TriggerKey = ParseKey(key, TriggerKey);
        ProfessionalModifiers = ParseModifiers(professionalModifiers);
        ProfessionalTriggerKey = ParseKey(professionalKey, ProfessionalTriggerKey);
    }

    public void UpdateCustomHotkeys(IEnumerable<CustomHotkeySetting>? customHotkeys)
    {
        _customHotkeys = (customHotkeys ?? Enumerable.Empty<CustomHotkeySetting>())
            .Where(hotkey => hotkey.Enabled && !string.IsNullOrWhiteSpace(hotkey.HotkeyKey))
            .Select(hotkey => new CustomHotkeyBinding
            {
                Name = hotkey.Name,
                Instruction = hotkey.Instruction,
                Modifiers = ParseModifiers(hotkey.HotkeyModifiers),
                TriggerKey = ParseKey(hotkey.HotkeyKey, Key.None)
            })
            .Where(binding => binding.TriggerKey != Key.None)
            .ToList();
    }

    private IntPtr SetHook(LowLevelKeyboardProc proc)
    {
        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule!;
        return SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(module.ModuleName!), 0);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && Enabled)
        {
            int msg = wParam.ToInt32();
            var vkCode = Marshal.ReadInt32(lParam);
            var key = KeyInterop.KeyFromVirtualKey(vkCode);
            bool isKeyDown = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
            bool isKeyUp = msg == WM_KEYUP || msg == WM_SYSKEYUP;

            if (isKeyDown && !_isRecording && TryResolveTrigger(key, out var mode, out var customHotkey))
            {
                _isRecording = true;
                _activeMode = mode;
                _activeTriggerKey = key;
                _activeCustomHotkey = customHotkey;
                Console.WriteLine($"[HotkeyService] >>> Recording STARTED ({mode})");
                if (customHotkey is not null)
                    Task.Run(() => CustomRecordingStarted?.Invoke(customHotkey));
                else
                    Task.Run(() => RecordingStarted?.Invoke(mode));
                return (IntPtr)1;
            }

            if (isKeyDown && _isRecording && (key == _activeTriggerKey || IsModifierKey(key)))
            {
                return (IntPtr)1;
            }

            // STOP: any key released while recording (trigger key OR any modifier)
            if (isKeyUp && _isRecording)
            {
                if (key == _activeTriggerKey || IsModifierKey(key))
                {
                    _isRecording = false;
                    var activeMode = _activeMode;
                    _activeMode = TranscriptMode.Normal;
                    var activeTriggerKey = _activeTriggerKey;
                    _activeTriggerKey = Key.None;
                    var activeCustomHotkey = _activeCustomHotkey;
                    _activeCustomHotkey = null;
                    Console.WriteLine($"[HotkeyService] <<< Recording STOPPED (released: {key}, mode: {activeMode})");
                    if (activeCustomHotkey is not null)
                        Task.Run(() => CustomRecordingStopped?.Invoke(activeCustomHotkey));
                    else
                        Task.Run(() => RecordingStopped?.Invoke(activeMode));
                    if (key == activeTriggerKey || IsModifierKey(key))
                        return (IntPtr)1;
                }
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private bool TryResolveTrigger(Key key, out TranscriptMode mode, out CustomHotkeyBinding? customHotkey)
    {
        if (key == TriggerKey && AreModifiersPressed(Modifiers))
        {
            mode = TranscriptMode.Normal;
            customHotkey = null;
            return true;
        }

        if (key == ProfessionalTriggerKey && AreModifiersPressed(ProfessionalModifiers))
        {
            mode = TranscriptMode.Professional;
            customHotkey = null;
            return true;
        }

        foreach (var binding in _customHotkeys)
        {
            if (key == binding.TriggerKey && AreModifiersPressed(binding.Modifiers))
            {
                mode = TranscriptMode.Normal;
                customHotkey = binding;
                return true;
            }
        }

        mode = TranscriptMode.Normal;
        customHotkey = null;
        return false;
    }

    private bool AreModifiersPressed(ModifierKeys requiredModifiers)
    {
        if (requiredModifiers.HasFlag(ModifierKeys.Control) &&
            !IsKeyDown(0x11))
            return false;

        if (requiredModifiers.HasFlag(ModifierKeys.Alt) &&
            !IsKeyDown(0x12))
            return false;

        if (requiredModifiers.HasFlag(ModifierKeys.Shift) &&
            !IsKeyDown(0x10))
            return false;

        if (requiredModifiers.HasFlag(ModifierKeys.Windows) &&
            !IsKeyDown(0x5B) && !IsKeyDown(0x5C))
            return false;

        return true;
    }

    private static ModifierKeys ParseModifiers(string modifiers)
    {
        var parsedModifiers = ModifierKeys.None;
        foreach (var mod in modifiers.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var normalized = mod.Trim().ToLowerInvariant();
            var parsed = normalized switch
            {
                "ctrl" or "control" => ModifierKeys.Control,
                "alt" => ModifierKeys.Alt,
                "shift" => ModifierKeys.Shift,
                "win" or "windows" => ModifierKeys.Windows,
                _ => ModifierKeys.None
            };

            if (parsed != ModifierKeys.None)
                parsedModifiers |= parsed;
        }

        return parsedModifiers;
    }

    private static Key ParseKey(string key, Key fallback)
    {
        if (Enum.TryParse<Key>(key, true, out var parsedKey))
            return parsedKey;

        return fallback;
    }

    private static bool IsModifierKey(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin;

    private static bool IsKeyDown(int vkCode) =>
        (GetAsyncKeyState(vkCode) & 0x8000) != 0;

    public void Dispose()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);
}
