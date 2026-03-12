using System.Runtime.InteropServices;
using System.Text;

namespace VoiceTyper.Services;

public class TextInsertionService
{
    private const byte VK_CONTROL = 0x11;
    private const byte VK_ALT = 0x12;
    private const byte VK_SHIFT = 0x10;
    private const byte VK_V = 0x56;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    public async Task InsertTextAsync(string text)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
            System.Windows.Clipboard.SetText(text));

        await Task.Delay(150);

        var hwnd = GetForegroundWindow();
        var sb = new StringBuilder(256);
        GetWindowText(hwnd, sb, sb.Capacity);
        Console.WriteLine($"[VoiceTyper] Foreground window: \"{sb}\" (hwnd=0x{hwnd:X})");

        // Release any modifier keys that may still be physically held
        keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VK_ALT, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VK_SHIFT, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        await Task.Delay(50);

        // Simulate Ctrl+V
        keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
        keybd_event(VK_V, 0, 0, UIntPtr.Zero);
        keybd_event(VK_V, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

        Console.WriteLine("[VoiceTyper] keybd_event Ctrl+V sent");
    }

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
}
