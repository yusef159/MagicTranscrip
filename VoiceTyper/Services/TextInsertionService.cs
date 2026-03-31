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
        var dispatcher = System.Windows.Application.Current.Dispatcher;
        System.Windows.IDataObject? previousClipboardData = null;
        var hadPreviousClipboardData = false;

        dispatcher.Invoke(() =>
        {
            try
            {
                previousClipboardData = System.Windows.Clipboard.GetDataObject();
                hadPreviousClipboardData = previousClipboardData is not null;
            }
            catch (COMException)
            {
                hadPreviousClipboardData = false;
            }
        });

        var clipboardPrepared = await TrySetClipboardTextAsync(dispatcher, text);
        if (!clipboardPrepared)
            throw new InvalidOperationException("Unable to update clipboard for text insertion.");

        await Task.Delay(70);

        var hwnd = GetForegroundWindow();
        var sb = new StringBuilder(256);
        GetWindowText(hwnd, sb, sb.Capacity);
        Console.WriteLine($"[VoiceTyper] Foreground window: \"{sb}\" (hwnd=0x{hwnd:X})");

        // Release any modifier keys that may still be physically held
        keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VK_ALT, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VK_SHIFT, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        await Task.Delay(30);

        // Simulate Ctrl+V
        keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
        keybd_event(VK_V, 0, 0, UIntPtr.Zero);
        keybd_event(VK_V, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

        Console.WriteLine("[VoiceTyper] keybd_event Ctrl+V sent");

        _ = RestoreClipboardAsync(dispatcher, hadPreviousClipboardData, previousClipboardData, text);
    }

    private static async Task<bool> TrySetClipboardTextAsync(System.Windows.Threading.Dispatcher dispatcher, string text)
    {
        const int maxAttempts = 8;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var success = dispatcher.Invoke(() =>
            {
                try
                {
                    System.Windows.Clipboard.SetText(text);
                    return System.Windows.Clipboard.ContainsText() &&
                           string.Equals(System.Windows.Clipboard.GetText(), text, StringComparison.Ordinal);
                }
                catch (COMException)
                {
                    return false;
                }
            });

            if (success)
                return true;

            await Task.Delay(35);
        }

        return false;
    }

    private static async Task RestoreClipboardAsync(
        System.Windows.Threading.Dispatcher dispatcher,
        bool hadData,
        System.Windows.IDataObject? previousData,
        string insertedText)
    {
        await Task.Delay(1200);
        dispatcher.Invoke(() =>
        {
            try
            {
                if (!hadData || previousData is null)
                    return;

                // Restore only if clipboard still contains our inserted transcript.
                if (!System.Windows.Clipboard.ContainsText() ||
                    !string.Equals(System.Windows.Clipboard.GetText(), insertedText, StringComparison.Ordinal))
                {
                    return;
                }

                System.Windows.Clipboard.SetDataObject(previousData, true);
            }
            catch (COMException)
            {
                // Best effort only; do not fail dictation insertion.
            }
        });
    }

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
}
