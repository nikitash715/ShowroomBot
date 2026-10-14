using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using ShowroomBot.Configuration;
using ShowroomBot.Rdp;
using ShowroomBot.Windows;

internal static class RdpWindowChecks
{
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr window);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Bounds bounds);
    [StructLayout(LayoutKind.Sequential)] private struct Bounds { public int Left, Top, Right, Bottom; }

    public static async Task RunAsync(string config)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        var settings = new SettingsService(Path.GetFullPath(config)).Load();
        var rdp = new RdpController(settings.Rdp.Host);
        var keyboard = new KeyboardInputSender();
        var window = await rdp.OpenOrActivateAsync();
        var fullScreen = typeof(RdpController).GetMethod("IsFullScreen", BindingFlags.NonPublic | BindingFlags.Static)!;
        if ((bool)fullScreen.Invoke(null, [window])!)
        {
            RdpController.EnsureSessionForeground(window);
            keyboard.ToggleRdpFullScreen();
            await Task.Delay(1000);
        }
        var work = Screen.FromHandle(window).WorkingArea;
        foreach (var (name, x, y, width, height, minimized) in new[] {
            ("small", work.Left + 50, work.Top + 30, work.Width / 2, work.Height / 2, false),
            ("95 percent", work.Left, work.Top, work.Width * 95 / 100, work.Height * 95 / 100, false),
            ("partly offscreen", work.Left + work.Width / 3, work.Top + work.Height / 3, work.Width * 9 / 10, work.Height * 9 / 10, false),
            ("minimized", work.Left + 50, work.Top + 30, work.Width / 2, work.Height / 2, true) })
        {
            ShowWindow(window, 9);
            if (!SetWindowPos(window, IntPtr.Zero, x, y, width, height, 0x0004))
                throw new InvalidOperationException("Cannot set RDP test geometry");
            if (minimized) ShowWindow(window, 6);
            await Task.Delay(300);
            GetWindowRect(window, out var initial);
            Console.WriteLine($"START: {name}; actual window [{initial.Left},{initial.Top},{initial.Right},{initial.Bottom}], maximized={IsZoomed(window)}");
            var active = await rdp.OpenOrActivateAsync();
            if (active != window || !IsZoomed(window) || !GetWindowRect(window, out var bounds) ||
                bounds.Left > work.Left || bounds.Top > work.Top || bounds.Right < work.Right || bounds.Bottom < work.Bottom)
                throw new InvalidOperationException("RDP did not cover working area: " + name);
            var before = WindowScreenshotService.GetClientScreenBounds(window);
            await Task.Delay(250);
            if (before != WindowScreenshotService.GetClientScreenBounds(window))
                throw new InvalidOperationException("Unstable RDP geometry: " + name);
            Console.WriteLine($"PASS: {name} -> maximized stable client {before}; monitor work area {work}");
        }
        await RdpController.EnsureFullScreenAsync(window, keyboard, CancellationToken.None);
        if (!(bool)fullScreen.Invoke(null, [window])!) throw new InvalidOperationException("Fullscreen not confirmed");
        var final = WindowScreenshotService.GetClientScreenBounds(window);
        var directory = Path.GetFullPath(Path.Combine("artifacts", "rdp-window-check-" + DateTime.Now.ToString("yyyyMMdd-HHmmss")));
        var path = new WindowScreenshotService().CaptureClientArea(window, directory);
        using var screenshot = new Bitmap(path);
        if (screenshot.Size != final.Size) throw new InvalidOperationException("Screenshot has stale dimensions");
        Console.WriteLine($"PASS: fullscreen client {final}, screenshot size {screenshot.Size}; {path}");
    }
}
