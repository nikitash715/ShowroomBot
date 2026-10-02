using System.Diagnostics;
using ShowroomBot.Windows;
using ShowroomBot.Core.Scenarios;

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

        ScenarioExecution.Perform(() => Process.Start(new ProcessStartInfo
        {
            FileName = "mstsc.exe",
            Arguments = $"/v:{host}",
            UseShellExecute = true
        }));
    }

    public bool TryActivateExistingWindow(out IntPtr windowHandle)
    {
        ScenarioExecution.CheckCancellation();
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
                var handle = windowHandle;
                var activated = false;
                ScenarioExecution.Perform(() =>
                {
                    NativeMethods.ShowWindowAsync(handle, NativeMethods.SW_RESTORE);
                    activated = NativeMethods.SetForegroundWindow(handle);
                });
                return activated;
            }
        }

        return false;
    }
}
