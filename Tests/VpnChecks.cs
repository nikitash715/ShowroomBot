using ShowroomBot.Configuration;
using ShowroomBot.Windows;

internal static class VpnChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var phonebookDirectory = Path.Combine(Path.GetTempPath(), $"showroombot-vpn-{Guid.NewGuid():N}");
        Directory.CreateDirectory(phonebookDirectory);
        try
        {
            var path = Path.Combine(phonebookDirectory, "rasphone.pbk");
            const string profile = "КЛЕВЕР VPN & (office)";
            File.WriteAllText(path,
                $"[{profile}]\r\nPreviewUserPw=1\r\nPreviewDomain=1\r\nCustomAuthKey=123\r\n\r\n[Other VPN]\r\nPreviewUserPw=1\r\nPreviewDomain=1\r\n",
                System.Text.Encoding.Unicode);
            var original = File.ReadAllText(path);
            check(VpnPhonebook.FindConnection(profile, [path]) == path,
                "VPN: selects exact Unicode phonebook profile");
            var text = File.ReadAllText(path);
            check(text == original, "VPN: profile lookup leaves authentication and preview settings unchanged");
            VpnPhonebook.FindConnection(profile, [path]);
            check(File.ReadAllText(path) == text, "VPN: preparation is idempotent");
            try
            {
                VpnPhonebook.FindConnection("Missing VPN", [path]);
                check(false, "VPN: missing profile must fail");
            }
            catch (InvalidOperationException)
            {
                check(File.ReadAllText(path) == text, "VPN: missing profile never creates or changes entries");
            }
        }
        finally
        {
            Directory.Delete(phonebookDirectory, true);
        }
        var settings = new VpnSettings { ConnectionNames = ["First VPN & (office)", "Second VPN"] };
        var launches = new List<string>();
        var connector = new VpnConnector(settings, launches.Add);
        var reply = await connector.ConnectAsync(CancellationToken.None);
        check(launches.Single() == settings.ConnectionNames[0],
            "VPN: passes only the first configured profile to the Windows credential dialer");
        check(reply.Contains("на телефоне") && reply.Contains("не подтверждение соединения"),
            "VPN: reports launch and manual MFA, not successful connection");
        settings.ConnectionNames = ["Changed VPN"];
        await connector.ConnectAsync(CancellationToken.None);
        check(launches.Last() == "Changed VPN", "VPN: reads shared settings on each request");
        foreach (var names in new[] { Array.Empty<string>(), new[] { "", "Second VPN" }, new[] { "/disconnect" } })
        {
            settings.ConnectionNames = names;
            var error = await connector.ConnectAsync(CancellationToken.None);
            check(error.Contains("Не удалось") && launches.Count == 2, "VPN: invalid first entry never launches or falls back to second");
        }
        settings.ConnectionNames = ["First VPN"];
        var failed = new VpnConnector(settings, _ => throw new System.ComponentModel.Win32Exception("private error"));
        var failedReply = await failed.ConnectAsync(CancellationToken.None);
        check(failedReply.Contains("Не удалось") && !failedReply.Contains("private error"), "VPN: launch failure returns safe error");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try
        {
            await connector.ConnectAsync(cancelled.Token);
            check(false, "VPN: cancelled request must not launch");
        }
        catch (OperationCanceledException)
        {
            check(launches.Count == 2, "VPN: cancelled request never launches");
        }
    }
}
