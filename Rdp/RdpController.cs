using System.Diagnostics;
using ShowroomBot.Windows;
using ShowroomBot.Core.Scenarios;

namespace ShowroomBot.Rdp;

public sealed class RdpController(string defaultHost = "")
{
    public static void MinimizeForLocalStep()
    {
        ScenarioExecution.Perform(() =>
        {
            // Enumerate actual local RDP windows, independent of host/title and connection state.
            if (!NativeMethods.EnumWindows((handle, _) =>
            {
                var className = new System.Text.StringBuilder(256);
                NativeMethods.GetClassName(handle, className, className.Capacity);
                if (className.ToString() == "TscShellContainerClass" && NativeMethods.IsWindowVisible(handle))
                    NativeMethods.ShowWindow(handle, 6); // SW_MINIMIZE; does not disconnect the session.
                return true;
            }, IntPtr.Zero))
                throw new InvalidOperationException("Не удалось свернуть окна RDP перед локальным шагом.");
            var foregroundClass = new System.Text.StringBuilder(256);
            NativeMethods.GetClassName(NativeMethods.GetForegroundWindow(), foregroundClass, foregroundClass.Capacity);
            if (foregroundClass.ToString() == "TscShellContainerClass")
                throw new InvalidOperationException("RDP остался на переднем плане перед локальным шагом.");
        });
    }

    public async Task<IntPtr> OpenOrActivateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var session = FindExistingWindow(defaultHost);
        if (session == IntPtr.Zero)
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
            if (session == IntPtr.Zero) session = FindExistingWindow(defaultHost, allowPending: true);
            var handle = session;
            if (handle != IntPtr.Zero && Activate(handle) && IsSessionWindowReady(handle))
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

    internal static bool IsSessionWindowReady(IntPtr handle)
    {
        var className = new System.Text.StringBuilder(256);
        NativeMethods.GetClassName(handle, className, className.Capacity);
        if (className.ToString() != "TscShellContainerClass" || !NativeMethods.IsWindowVisible(handle) ||
            !NativeMethods.IsWindowEnabled(handle) || NativeMethods.IsIconic(handle)) return false;
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

        var existing = FindExistingWindow(host);
        if (existing != IntPtr.Zero)
        {
            if (!Activate(existing))
                throw new InvalidOperationException("RDP найден, но не активирован. Новый mstsc не запущен.");
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
        windowHandle = FindExistingWindow(defaultHost);
        return windowHandle != IntPtr.Zero && Activate(windowHandle);
    }

    private static IntPtr FindExistingWindow(string host, bool allowPending = false)
    {
        var processes = Process.GetProcessesByName("mstsc");
        try
        {
            var ids = processes.Select(p => (uint)p.Id).ToHashSet();
            var candidates = new List<IntPtr>();
            if (!NativeMethods.EnumWindows((handle, _) =>
            {
                NativeMethods.GetWindowThreadProcessId(handle, out var pid);
                if (!ids.Contains(pid) || !NativeMethods.IsWindowVisible(handle)) return true;
                var name = new System.Text.StringBuilder(256);
                var title = new System.Text.StringBuilder(1024);
                NativeMethods.GetClassName(handle, name, name.Capacity);
                NativeMethods.GetWindowText(handle, title, title.Capacity);
                var match = MatchesSession(name.ToString(), title.ToString(), host);
                ScenarioExecution.Log($"RDP: hwnd=0x{handle:X}, pid={pid}, class={name}, title={title}, minimized={NativeMethods.IsIconic(handle)}, match={match}");
                if (match) candidates.Add(handle);
                return true;
            }, IntPtr.Zero))
                throw new InvalidOperationException("Не удалось перечислить окна RDP. Новый mstsc не запущен.");
            if (candidates.Count == 1) return candidates[0];
            if (candidates.Count > 1)
                throw new InvalidOperationException("Найдено несколько подходящих RDP-сеансов. Закройте лишние подключения.");
            // Unknown windows may belong to a connecting/disconnected session or use a custom title.
            // Do not risk creating a duplicate when the destination cannot be established.
            if (ids.Count > 0 && !allowPending)
                throw new InvalidOperationException("mstsc уже запущен, но подходящее окно сеанса не найдено. Проверьте адрес и диалоги подключения. Новый mstsc не запущен.");
            return IntPtr.Zero;
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    internal static bool MatchesSession(string className, string title, string host)
    {
        if (className != "TscShellContainerClass") return false;
        if (string.IsNullOrWhiteSpace(host)) return true;
        host = host.Trim();
        title = title.Trim();
        if (title.Equals(host, StringComparison.OrdinalIgnoreCase)) return true;
        if (!title.StartsWith(host, StringComparison.OrdinalIgnoreCase)) return false;
        // mstsc uses a localized separator (including an em dash on Russian Windows).
        // Require whitespace around it so host prefixes cannot select another machine.
        var suffix = title.AsSpan(host.Length);
        if (suffix.IsEmpty || !char.IsWhiteSpace(suffix[0])) return false;
        suffix = suffix.TrimStart();
        return suffix.Length >= 2 && suffix[0] is '-' or '–' or '—' && char.IsWhiteSpace(suffix[1]);
    }

    private static bool Activate(IntPtr handle)
    {
        var requested = false;
        ScenarioExecution.Perform(() =>
        {
            if (NativeMethods.IsIconic(handle)) NativeMethods.ShowWindow(handle, NativeMethods.SW_RESTORE);
            requested = NativeMethods.SetForegroundWindow(handle);
        });
        if (NativeMethods.GetForegroundWindow() == handle) EnsureWindowSize(handle);
        var ready = IsSessionWindowReady(handle);
        ScenarioExecution.Log($"RDP: activation hwnd=0x{handle:X}, SetForegroundWindow={requested}, foreground=0x{NativeMethods.GetForegroundWindow():X}, ready={ready}");
        if (ready && ScenarioExecution.Current is { } execution) execution.RdpWindow = handle;
        return ready;
    }

    public static void EnsureSessionForeground(IntPtr handle)
    {
        ScenarioExecution.CheckCancellation();
        if (handle != IntPtr.Zero && IsSessionWindowReady(handle)) return;
        ScenarioExecution.Log($"RDP: ввод заблокирован; expected=0x{handle:X}, foreground=0x{NativeMethods.GetForegroundWindow():X}");
        if (ScenarioExecution.Current is not null) ScenarioExecution.CancelCurrent();
        throw new InvalidOperationException("RDP-сеанс не готов или потерял фокус. Сценарий остановлен до отправки ввода.");
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
