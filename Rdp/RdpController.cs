using System.Diagnostics;
using ShowroomBot.Windows;
using ShowroomBot.Core.Scenarios;

namespace ShowroomBot.Rdp;

public sealed class RdpController(string defaultHost = "")
{
    public async Task<IntPtr> OpenOrActivateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryActivateExistingWindow(out _))
        {
            if (string.IsNullOrWhiteSpace(defaultHost))
                throw new InvalidOperationException("Не задан адрес RDP в настройках подключения.");
            ScenarioExecution.Log("RDP: открываем подключение из настроек");
            cancellationToken.ThrowIfCancellationRequested();
            OpenOrActivate(defaultHost);
        }
        ScenarioExecution.Log("RDP: ожидаем окно сеанса и стабилизацию фокуса");
        var timer = Stopwatch.StartNew();
        var stable = Stopwatch.StartNew();
        var lastHandle = IntPtr.Zero;
        while (timer.Elapsed < TimeSpan.FromSeconds(60))
        {
            await Task.Delay(500, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (TryActivateExistingWindow(out var handle) && IsSessionWindowReady(handle))
            {
                if (handle != lastHandle) stable.Restart();
                lastHandle = handle;
                if (stable.Elapsed >= TimeSpan.FromSeconds(5))
                {
                    ScenarioExecution.Log("RDP: окно сеанса активно и стабильно");
                    return handle;
                }
            }
            else
            {
                lastHandle = IntPtr.Zero;
                stable.Restart();
            }
        }
        throw new TimeoutException("RDP не готов за 60 секунд. Завершите вход и подтвердите диалоги подключения, затем повторите сценарий.");
    }

    private static bool IsSessionWindowReady(IntPtr handle)
    {
        var className = new System.Text.StringBuilder(256);
        NativeMethods.GetClassName(handle, className, className.Capacity);
        if (className.ToString() != "TscShellContainerClass") return false;
        var popup = NativeMethods.GetLastActivePopup(handle);
        return (popup == handle || !NativeMethods.IsWindowVisible(popup)) &&
            NativeMethods.GetForegroundWindow() == handle &&
            NativeMethods.GetClientRect(handle, out var bounds) &&
            bounds.Right - bounds.Left >= 320 && bounds.Bottom - bounds.Top >= 200;
    }

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
