using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace VoiceTyper.Services;

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

    public Key TriggerKey { get; set; } = Key.Space;
    public ModifierKeys Modifiers { get; set; } = ModifierKeys.Control | ModifierKeys.Alt;
    public bool Enabled { get; set; } = true;

    public event Action? RecordingStarted;
    public event Action? RecordingStopped;

    public HotkeyService()
    {
        _hookProc = HookCallback;
        _hookId = SetHook(_hookProc);
        Console.WriteLine($"[HotkeyService] Hook installed: 0x{_hookId:X}");
    }

    public void UpdateHotkey(string modifiers, string key)
    {
        Modifiers = ModifierKeys.None;
        foreach (var mod in modifiers.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Enum.TryParse<ModifierKeys>(mod, true, out var parsed))
                Modifiers |= parsed;
        }

        if (Enum.TryParse<Key>(key, true, out var parsedKey))
            TriggerKey = parsedKey;
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

            // START: trigger key pressed with correct modifiers held
            if (isKeyDown && !_isRecording && key == TriggerKey && AreModifiersPressed())
            {
                _isRecording = true;
                Console.WriteLine("[HotkeyService] >>> Recording STARTED");
                Task.Run(() => RecordingStarted?.Invoke());
                return (IntPtr)1;
            }

            // STOP: any key released while recording (trigger key OR any modifier)
            if (isKeyUp && _isRecording)
            {
                if (key == TriggerKey || IsModifierKey(key))
                {
                    _isRecording = false;
                    Console.WriteLine($"[HotkeyService] <<< Recording STOPPED (released: {key})");
                    Task.Run(() => RecordingStopped?.Invoke());
                    if (key == TriggerKey)
                        return (IntPtr)1;
                }
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private bool AreModifiersPressed()
    {
        if (Modifiers.HasFlag(ModifierKeys.Control) &&
            !IsKeyDown(0x11))
            return false;

        if (Modifiers.HasFlag(ModifierKeys.Alt) &&
            !IsKeyDown(0x12))
            return false;

        if (Modifiers.HasFlag(ModifierKeys.Shift) &&
            !IsKeyDown(0x10))
            return false;

        if (Modifiers.HasFlag(ModifierKeys.Windows) &&
            !IsKeyDown(0x5B) && !IsKeyDown(0x5C))
            return false;

        return true;
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
