using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using ShowroomBot.Configuration;
using ShowroomBot.Core.Scenarios;
using ShowroomBot.Mail;
using ShowroomBot.Telegram;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

internal static class CheckMailChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        // Unique, absolute directory owned by this test. No real mailbox/history/Telegram is used.
        var root = Path.Combine(Path.GetTempPath(), "showroombot-mail-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var clock = new Clock();
            var path = Path.Combine(root, "history.json");
            var history = new MailDeliveryHistory(path, clock);
            var first = new OutlookMail("store1", "id1", "Отправитель <test@example.com>", "Тема", "Текст");
            var second = first with { EntryId = "id2" };
            var otherStore = first with { StoreId = "store2" };
            var reader = new Reader([first, second, otherStore]);
            var sent = new List<OutlookMail>();
            var failSecond = true;
            Task Send(OutlookMail mail, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                if (mail == second && failSecond) throw new InvalidOperationException("Fake delivery failure");
                sent.Add(mail);
                return Task.CompletedTask;
            }
            var step = new CheckMailStep(reader, history, Send);
            try { await step.ExecuteAsync(); throw new Exception("Delivery failure ignored"); }
            catch (InvalidOperationException e) when (e.Message == "Fake delivery failure") { }
            using (var saved = history.Open())
                check(saved.Contains(first) && !saved.Contains(second) && !saved.Contains(otherStore),
                    "CheckMail: only confirmed delivery is recorded; same EntryID in another store is distinct");
            failSecond = false;
            // A fresh history instance represents a process restart.
            await new CheckMailStep(reader, new MailDeliveryHistory(path, clock), Send).ExecuteAsync();
            await step.ExecuteAsync();
            check(sent.SequenceEqual(new[] { first, second, otherStore }),
                "CheckMail: restart retries failed mail and never resends confirmed unread mail");
            check(!File.ReadAllText(path).Contains(first.Subject) && !File.ReadAllText(path).Contains(first.Body),
                "CheckMail: history contains IDs and timestamps, not message content");
            using (var lease = history.Open())
            {
                check(lease.Contains(first with { StoreId = "STORE1", EntryId = "ID1" }), "CheckMail: Outlook IDs compare case-insensitively");
                try { using var duplicate = history.Open(); throw new Exception("History lease ignored"); }
                catch (IOException) { check(true, "CheckMail: concurrent history access is blocked"); }
            }
            clock.Advance(TimeSpan.FromDays(5));
            using (var boundary = history.Open()) check(boundary.Contains(first), "CheckMail: retain exactly 5 days");
            clock.Advance(TimeSpan.FromSeconds(1));
            reader.Messages = [];
            await step.ExecuteAsync();
            using (var pruned = history.Open()) check(!pruned.Contains(first), "CheckMail: empty inbox still prunes expired history");
            reader.Messages = [first];
            await step.ExecuteAsync();
            check(sent.Count == 4, "CheckMail: expired unread mail becomes eligible again");

            using (var cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel();
                var reads = reader.Reads;
                try { await step.ExecuteAsync(cancelled.Token); throw new Exception("Cancellation ignored"); }
                catch (OperationCanceledException) { }
                check(reader.Reads == reads, "CheckMail: prior cancellation prevents Outlook access");
            }
            var final = first with { EntryId = "cancel-after-send" };
            using (var cancelled = new CancellationTokenSource())
            {
                var cancelAfterSend = new CheckMailStep(new Reader([final]), history, (_, _) =>
                {
                    cancelled.Cancel();
                    return Task.CompletedTask;
                });
                await cancelAfterSend.ExecuteAsync(cancelled.Token);
                using var saved = history.Open();
                check(saved.Contains(final), "CheckMail: confirmed send is persisted even if cancellation arrives immediately after it");
            }

            var malformed = Path.Combine(root, "broken.json");
            File.WriteAllText(malformed, "{invalid");
            var unread = new Reader([first]);
            try
            {
                await new CheckMailStep(unread, new MailDeliveryHistory(malformed), Send).ExecuteAsync();
                throw new Exception("Corrupt history ignored");
            }
            catch (InvalidDataException) { check(unread.Reads == 0, "CheckMail: corrupt history stops before accessing Outlook or sending mail"); }

            await CheckTelegramAsync(root, first, check);
            await CheckContextAsync(root, check);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static async Task CheckTelegramAsync(string root, OutlookMail mail, Action<bool, string> check)
    {
        var settings = new TelegramSettings
        { Enabled = true, BotToken = "123:fake_mail_token", AllowedUserId = 5000000001, NotifyDemoEvents = false };
        var parts = new List<string>();
        var failAt = 2;
        using var handler = new Handler(async (request, token) =>
        {
            check(request.RequestUri!.Segments.Last() == "sendMessage", "CheckMail Telegram: sends plain text through existing service");
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            check(json.RootElement.GetProperty("chat_id").GetInt64() == settings.AllowedUserId,
                "CheckMail Telegram: sends only to configured owner");
            check(!json.RootElement.TryGetProperty("parse_mode", out _), "CheckMail Telegram: mail text is not interpreted as markup");
            parts.Add(json.RootElement.GetProperty("text").GetString()!);
            return new HttpResponseMessage(parts.Count == failAt ? HttpStatusCode.BadGateway : HttpStatusCode.OK)
            { Content = new StringContent("{\"ok\":true,\"result\":{}}") };
        });
        using var client = new HttpClient(handler);
        var telegram = new TelegramBotService(client, settings, _ => Task.FromResult(""), _ => Task.FromResult(""), _ => "");
        // Place a surrogate pair exactly at the first chunk boundary.
        var prefix = $"Новое письмо Outlook\nОт: {mail.Sender}\nТема: {mail.Subject}\n\n";
        var longMail = mail with { Body = new string('я', 3999 - prefix.Length) + "😀" + new string('z', 4500) };
        var history = new MailDeliveryHistory(Path.Combine(root, "telegram-history.json"));
        var step = new CheckMailStep(new Reader([longMail]), history, telegram.SendMailAsync);
        try { await step.ExecuteAsync(); throw new Exception("Partial failure ignored"); }
        catch (InvalidOperationException e) when (e.Message == "Telegram request failed.") { }
        using (var saved = history.Open()) check(!saved.Contains(longMail), "CheckMail Telegram: a failed later chunk does not mark the mail delivered");
        failAt = -1;
        parts.Clear();
        await step.ExecuteAsync();
        check(string.Concat(parts) == prefix + longMail.Body && parts.All(p => p.Length <= 4000 &&
            !char.IsHighSurrogate(p[^1]) && !char.IsLowSurrogate(p[0])),
            "CheckMail Telegram: complete long Unicode body survives splitting; lifecycle notifications may be disabled");
        var count = parts.Count;
        await step.ExecuteAsync();
        check(parts.Count == count, "CheckMail Telegram: no network call for an already delivered unread mail");
        settings.Enabled = false;
        var fresh = mail with { EntryId = "disabled" };
        try
        {
            await new CheckMailStep(new Reader([fresh]), history, telegram.SendMailAsync).ExecuteAsync();
            throw new Exception("Disabled Telegram ignored");
        }
        catch (InvalidOperationException e) when (e.Message.StartsWith("CheckMail:")) { }
        using (var saved = history.Open()) check(!saved.Contains(fresh), "CheckMail Telegram: disabled transport does not consume unread mail");
        await new CheckMailStep(new Reader([]), history, telegram.SendMailAsync).ExecuteAsync();
        check(parts.Count == count, "CheckMail Telegram: empty inbox succeeds with disabled transport");
    }

    private static async Task CheckContextAsync(string root, Action<bool, string> check)
    {
        var yaml = new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).Build();
        var definition = yaml.Deserialize<ScenarioDefinition>("name: Mail\nsteps:\n  - type: cHeCkMaIl\n  - type: Wait\n    seconds: 1\n");
        check(!definition.RequiresRdp && definition.Steps[0].ExecutionContext == ScenarioExecutionContext.Local &&
            definition.Steps[1].ExecutionContext == ScenarioExecutionContext.None, "CheckMail: minimal YAML determines local/neutral context without RDP");
        foreach (var type in new[] { "Open1C", "OpenConfig", "Open1CSection", "Open1CCommand", "ExecuteToolkitQuery" })
            check(new ScenarioStepDefinition { Type = type }.ExecutionContext == ScenarioExecutionContext.Rdp,
                $"CheckMail: existing {type} remains RDP");
        var minimized = false;
        var reader = new Reader([])
        {
            OnRead = () => check(minimized && ScenarioExecution.Current?.ExecutionContext == ScenarioExecutionContext.Local,
                "CheckMail: runner prepares local desktop before any Outlook access")
        };
        var factory = new ScenarioStepFactory(null!, null!, null!, null!, null!,
            new CheckMailStep(reader, new MailDeliveryHistory(Path.Combine(root, "context.json")), (_, _) => Task.CompletedTask));
        var runner = new ScenarioRunner(factory, _ => throw new Exception("Unexpected screenshot"), () =>
        {
            minimized = true;
            var execution = ScenarioExecution.Current!;
            // A stale window from a previous RDP step must not gate local input.
            typeof(ScenarioExecution).GetProperty("RdpWindow", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(execution, new IntPtr(123));
            var guard = typeof(ScenarioExecution).Assembly.GetType("ShowroomBot.Rdp.RdpInputGuard")!;
            guard.GetMethod("Check")!.Invoke(null, null);
        });
        await runner.RunAsync(definition);
        check(reader.Reads == 1, "CheckMail: local runner works with no RDP dependencies and no screenshots");
    }

    private sealed class Reader(IReadOnlyList<OutlookMail> messages) : IOutlookInboxReader
    {
        public IReadOnlyList<OutlookMail> Messages { get; set; } = messages;
        public int Reads { get; private set; }
        public Action? OnRead { get; init; }
        public Task<IReadOnlyList<OutlookMail>> ReadUnreadInboxAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            OnRead?.Invoke();
            Reads++;
            return Task.FromResult(Messages);
        }
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan time) => _now += time;
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token);
    }
}
