using System.Diagnostics;
using ShowroomBot.Windows;

namespace ShowroomBot.Rdp;

public sealed class RdpController
{
    public void OpenOrActivate(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return;
        }

        if (TryActivateExistingWindow(out _))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "mstsc.exe",
            Arguments = $"/v:{host}",
            UseShellExecute = true
        });
    }

    public bool TryActivateExistingWindow(out IntPtr windowHandle)
    {
        windowHandle = IntPtr.Zero;

        foreach (var process in Process.GetProcessesByName("mstsc"))
        {
            using (process)
            {
                process.Refresh();
                if (process.HasExited || process.MainWindowHandle == IntPtr.Zero)
                {
                    continue;
                }

                windowHandle = process.MainWindowHandle;
                NativeMethods.ShowWindow(windowHandle, NativeMethods.SW_RESTORE);
                return NativeMethods.SetForegroundWindow(windowHandle);
            }
        }

        return false;
    }
}
