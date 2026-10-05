using System.Reflection;
using ShowroomBot.Core.Scenarios;
using ShowroomBot.Rdp;
using ShowroomBot.Windows;

internal static class RdpChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var matches = typeof(RdpController).GetMethod("MatchesSession", BindingFlags.Static | BindingFlags.NonPublic)!
            .CreateDelegate<Func<string, string, string, bool>>();
        check(matches("TscShellContainerClass", "192.168.174.132 — Подключение к удаленному рабочему столу", "192.168.174.132"),
            "RDP: actual minimized session title from failure log matches configured host");
        foreach (var separator in new[] { " - ", " – ", " — ", "\u00a0—\u00a0" })
            check(matches("TscShellContainerClass", "server" + separator + "Remote Desktop Connection", "server"),
                "RDP: localized title separator supported");
        check(matches("TscShellContainerClass", "server", " server "), "RDP: exact host title supported");
        check(!matches("TscShellContainerClass", "192.168.174.1320 — Подключение к удаленному рабочему столу", "192.168.174.132"),
            "RDP: localized separator does not allow partial host match");
        check(!matches("#32770", "192.168.174.132 — Подключение к удаленному рабочему столу", "192.168.174.132"),
            "RDP: matching host in a dialog is still rejected");
        check(matches("TscShellContainerClass", "SERVER:3390 - Подключение к удаленному рабочему столу", "server:3390"),
            "RDP: matching host and port, case insensitive");
        check(!matches("TscShellContainerClass", "server-other - Remote Desktop Connection", "server"),
            "RDP: host prefix does not select another machine");
        check(!matches("#32770", "Подключение к удаленному рабочему столу", ""),
            "RDP: disconnected dialog is not a session even without configured host");
        check(!matches("TscShellContainerClass", "other - Remote Desktop Connection", "server"),
            "RDP: wrong host rejected");

        // Zero is deliberately invalid: each action must fail before reaching native SendInput.
        var guardType = typeof(RdpController).Assembly.GetType("ShowroomBot.Rdp.RdpInputGuard")!;
        using (var guard = (IDisposable)guardType.GetMethod("Require")!.Invoke(null, [IntPtr.Zero])!)
        {
            await Task.Delay(1); // The requirement must survive asynchronous pauses.
            var keyboard = new KeyboardInputSender();
            foreach (var action in new Action[] { keyboard.SendWindowsRun, keyboard.SendEnter,
                         new MouseInputSender().ClickLeft, () => new MouseInputSender().Scroll(1) })
                ExpectBlocked(action, check);
            try
            {
                await keyboard.SendTextAsync("powershell", CancellationToken.None);
                throw new Exception("RDP text input was not blocked");
            }
            catch (InvalidOperationException error) when (error.Message.Contains("RDP-сеанс"))
            { check(true, "RDP: text blocked after asynchronous pause"); }
        }
        using (var execution = new ScenarioExecution(CancellationToken.None))
            ExpectBlocked(new KeyboardInputSender().SendWindowsRun, check);
    }

    private static void ExpectBlocked(Action action, Action<bool, string> check)
    {
        try { action(); throw new Exception("Unsafe input was not blocked"); }
        catch (InvalidOperationException error) when (error.Message.Contains("RDP-сеанс"))
        { check(true, "RDP: missing session blocks input before SendInput"); }
    }
}
