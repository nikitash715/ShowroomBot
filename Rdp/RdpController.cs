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

        var existingProcess = Process.GetProcessesByName("mstsc")
            .FirstOrDefault(process => !process.HasExited);

        if (existingProcess is not null)
        {
            ActivateProcessWindow(existingProcess);
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "mstsc.exe",
            Arguments = $"/v:{host}",
            UseShellExecute = true
        });
    }

    private static void ActivateProcessWindow(Process process)
    {
        if (process.MainWindowHandle == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.ShowWindow(process.MainWindowHandle, NativeMethods.SW_RESTORE);
        NativeMethods.SetForegroundWindow(process.MainWindowHandle);
    }
}
