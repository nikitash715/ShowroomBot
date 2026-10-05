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
                    // SW_RESTORE would undo our maximize on the next activation poll.
                    if (!NativeMethods.IsZoomed(handle))
                        NativeMethods.ShowWindow(handle, NativeMethods.SW_RESTORE);
                    activated = NativeMethods.SetForegroundWindow(handle);
                });
                if (activated) EnsureWindowSize(handle);
                return activated;
            }
        }

        return false;
    }

    private static void EnsureWindowSize(IntPtr handle)
    {
        var monitor = NativeMethods.MonitorFromWindow(handle, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var info = new NativeMethods.MONITORINFO
        {
            Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>()
        };
        if (monitor == IntPtr.Zero || !NativeMethods.GetMonitorInfo(monitor, ref info) ||
            !NativeMethods.GetWindowRect(handle, out var bounds))
            throw new InvalidOperationException("Не удалось определить размер окна RDP и рабочую область монитора.");

        ScenarioExecution.Log($"RDP: окно [{bounds.Left},{bounds.Top},{bounds.Right},{bounds.Bottom}], рабочая область [{info.WorkArea.Left},{info.WorkArea.Top},{info.WorkArea.Right},{info.WorkArea.Bottom}], maximized={NativeMethods.IsZoomed(handle)}");
        if (!NeedsMaximize(bounds, info.WorkArea) || NativeMethods.IsZoomed(handle)) return;

        ScenarioExecution.Log("RDP: видимая часть окна меньше 90% рабочей области, разворачиваем Windows Maximize");
        ScenarioExecution.Perform(() => NativeMethods.ShowWindow(handle, NativeMethods.SW_MAXIMIZE));
        // Synchronous activation callers also need the resized interface to settle.
        for (var i = 0; i < 20; i++)
        {
            ScenarioExecution.CheckCancellation();
            Thread.Sleep(50);
        }
        ScenarioExecution.CheckCancellation();
    }

    private static bool NeedsMaximize(NativeMethods.RECT window, NativeMethods.RECT workArea)
    {
        var width = (long)workArea.Right - workArea.Left;
        var height = (long)workArea.Bottom - workArea.Top;
        return width > 0 && height > 0 &&
            (((long)Math.Min(window.Right, workArea.Right) - Math.Max(window.Left, workArea.Left)) < width * 0.9 ||
             ((long)Math.Min(window.Bottom, workArea.Bottom) - Math.Max(window.Top, workArea.Top)) < height * 0.9);
    }
}
