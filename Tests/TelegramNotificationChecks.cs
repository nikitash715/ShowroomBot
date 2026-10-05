using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using ShowroomBot.Configuration;
using ShowroomBot.Core.Scenarios;
using ShowroomBot.Telegram;

internal static class TelegramNotificationChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var events = new List<ScenarioNotification>();
        var captures = 0;
        var captureFails = false;
        var runner = new ScenarioRunner(new ScenarioStepFactory(null!, null!, null!, null!, null!), directory =>
        {
            captures++;
            if (captureFails) throw new IOException("capture unavailable");
            var path = Path.Combine(directory, "notification-test.png");
            File.WriteAllBytes(path, [137, 80, 78, 71]);
            return path;
        });
        runner.ScenarioChanged += events.Add;
        runner.ScenarioChanged += _ => throw new Exception("broken observer");
        await runner.RunAsync(new() { Name = "success", Steps = [] });
        check(events.Count == 2 && events[0].Outcome == ScenarioOutcome.Started &&
            events[1].Outcome == ScenarioOutcome.Completed && captures == 1 && File.Exists(events[1].ScreenshotPath),
            "Notifications: success captures once; observer failures do not change outcome");
        var longError = new string('x', 9000) + " tail";
        try { await runner.RunAsync(new() { Name = "failure", Steps = [new() { Type = longError }] }); }
        catch (InvalidOperationException) { }
        check(events.Count == 4 && events[3].Outcome == ScenarioOutcome.Failed &&
            events[3].Error!.Contains(longError) && captures == 2 && File.Exists(events[3].ScreenshotPath),
            "Notifications: original error and existing diagnostic screenshot reused");
        using (var cancellation = new CancellationTokenSource())
        {
            var run = runner.RunAsync(new() { Name = "stop", Steps = [new() { Type = "Wait", Seconds = 60 }] }, cancellation.Token);
            cancellation.Cancel();
            try { await run; } catch (OperationCanceledException) { }
        }
        check(events.Count == 6 && events[5].Outcome == ScenarioOutcome.Stopped && captures == 3 && File.Exists(events[5].ScreenshotPath),
            "Notifications: cancellation still captures current screen exactly once");
        captureFails = true;
        try { await runner.RunAsync(new() { Name = "capture failed", Steps = [new() { Type = "unknown" }] }); }
        catch (InvalidOperationException) { }
        check(events.Count == 8 && events[7].Outcome == ScenarioOutcome.Failed && events[7].ScreenshotPath is null &&
            events[7].Error!.Contains("unknown"), "Notifications: screenshot failure preserves original scenario error");

        var texts = new List<string>();
        var documents = 0;
        var chatIds = new List<long>();
        var polling = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var handler = new Handler(async (request, token) =>
        {
            switch (request.RequestUri!.Segments.Last())
            {
                case "getUpdates":
                    polling.TrySetResult();
                    await Task.Delay(Timeout.Infinite, token);
                    break;
                case "sendMessage":
                    using (var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token)))
                    {
                        chatIds.Add(body.RootElement.GetProperty("chat_id").GetInt64());
                        var text = body.RootElement.GetProperty("text").GetString()!;
                        texts.Add(text);
                        if (text.Contains("delivery barrier")) delivered.TrySetResult();
                        if (text.Contains("text failure")) return new(HttpStatusCode.BadGateway);
                    }
                    break;
                case "sendDocument":
                    documents++;
                    // First upload fails; subsequent events and error texts must still be delivered.
                    if (documents == 1) return new(HttpStatusCode.BadGateway);
                    break;
            }
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"ok\":true,\"result\":true}") };
        });
        using var http = new HttpClient(handler);
        var telegramSettings = new TelegramSettings { Enabled = true, BotToken = "123:fake", AllowedUserId = 5000000001 };
        var service = new TelegramBotService(http, telegramSettings,
            _ => throw new Exception(), _ => throw new Exception(), _ => throw new Exception("must reuse screenshot"));
        service.QueueNotification(new("disabled queued", ScenarioOutcome.Completed, ScreenshotPath: events[1].ScreenshotPath));
        telegramSettings.NotifyDemoEvents = false;
        service.QueueNotification(new("disabled immediate", ScenarioOutcome.Started));
        var transport = service.RunAsync(lifetime.Token);
        await polling.Task.WaitAsync(lifetime.Token);
        telegramSettings.NotifyDemoEvents = true;
        foreach (var notification in events) service.QueueNotification(notification);
        service.QueueNotification(new("text failure", ScenarioOutcome.Failed, "error", events[1].ScreenshotPath));
        service.QueueNotification(new("delivery barrier", ScenarioOutcome.Started));
        await delivered.Task.WaitAsync(lifetime.Token);
        lifetime.Cancel();
        await transport.WaitAsync(TimeSpan.FromSeconds(2));
        check(texts[0] == "Сценарий запущен: success" && texts[1] == "Сценарий завершён: success" &&
            texts.Any(t => t == "Сценарий остановлен: stop"), "Notifications: ordered start/success/stop messages while polling waits");
        check(texts.All(t => t.Length <= 4000) && string.Concat(texts).Contains(longError),
            "Notifications: long error delivered completely in bounded messages");
        check(documents == 4 && texts.Any(t => t.Contains("Не удалось получить снимок")) &&
            texts.Last().Contains("delivery barrier"), "Notifications: failed text/upload/capture does not block other notifications");
        check(chatIds.All(id => id == 5000000001), "Notifications: automatic messages go only to configured owner");
        check(texts.All(text => !text.Contains("disabled")),
            "Notifications: setting suppresses new and queued notifications; enabling applies without restart");
        check(File.Exists(events[1].ScreenshotPath), "Notifications: uploaded diagnostic screenshots retained");
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }
}
