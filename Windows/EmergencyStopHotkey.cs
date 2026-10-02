using System.ComponentModel;
using System.Runtime.InteropServices;
using ShowroomBot.Core.Scenarios;

namespace ShowroomBot.Windows;

/// <summary>Local low-level keyboard hook: catches the stop chord before mstsc forwards it.</summary>
public sealed class EmergencyStopHotkey : IDisposable
{
    private readonly Thread _thread;
    private readonly HookProc _callback;
    private readonly Action _onStopped;
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private uint _threadId;
    private bool _pressed;
    private bool _disposed;

    public EmergencyStopHotkey(Action onStopped)
    {
        _onStopped = onStopped;
        _callback = OnKeyboard;
        _thread = new Thread(Listen) { IsBackground = true, Name = "ShowroomBot emergency stop" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _started.Task.GetAwaiter().GetResult();
    }

    private void Listen()
    {
        IntPtr hook = IntPtr.Zero;
        try
        {
            _threadId = GetCurrentThreadId();
            // Initialize a native message queue without creating any WinForms handles.
            // MainForm waits for startup inside OnHandleCreated; NativeWindow.CreateHandle
            // on this thread would wait for the WinForms lock held by the UI thread.
            PeekMessage(out _, IntPtr.Zero, 0, 0, 0);
            hook = SetWindowsHookEx(13 /* WH_KEYBOARD_LL */, _callback, GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "Не удалось включить аварийную остановку Ctrl+Alt+F12.");
            _started.TrySetResult();
            while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }
        }
        catch (Exception exception)
        {
            _started.TrySetException(exception);
        }
        finally
        {
            if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook);
        }
    }

    private IntPtr OnKeyboard(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && Marshal.ReadInt32(data) == 0x7B /* F12 */)
        {
            var kind = message.ToInt64();
            if (kind is 0x101 or 0x105) _pressed = false; // KEYUP / SYSKEYUP
            if (kind is 0x100 or 0x104 &&
                (GetAsyncKeyState(0x11) & 0x8000) != 0 && (GetAsyncKeyState(0x12) & 0x8000) != 0)
            {
                if (!_pressed)
                {
                    _pressed = true;
                    // Cancellation is issued on this dedicated thread, even if the UI is busy with OCR.
                    ScenarioExecution.CancelCurrent();
                    _onStopped();
                }
                return new IntPtr(1);
            }
        }
        return CallNextHookEx(IntPtr.Zero, code, message, data);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        PostThreadMessage(_threadId, 0x12 /* WM_QUIT */, IntPtr.Zero, IntPtr.Zero);
        _thread.Join();
        GC.KeepAlive(_callback);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public IntPtr Window;
        public uint Id;
        public UIntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Private;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(out Message message, IntPtr window, uint min, uint max, uint remove);
    [DllImport("user32.dll")]
    private static extern int GetMessage(out Message message, IntPtr window, uint min, uint max);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref Message message);
    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int id, HookProc callback, IntPtr module, uint threadId);
    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessage(uint threadId, uint message, IntPtr wParam, IntPtr lParam);
}
