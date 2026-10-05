using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using ShowroomBot.Configuration;
using ShowroomBot.Telegram;

internal static class TelegramChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var verify = check;
        var failedChecks = new List<string>();
        // Service error isolation must not accidentally swallow a test assertion.
        check = (value, message) =>
        {
            if (!value) failedChecks.Add(message);
            else verify(true, message);
        };
        const long owner = 5000000001; // Int64 IDs, deliberately outside Int32.
        var settings = new TelegramSettings { Enabled = true, BotToken = "123:fake_token", AllowedUserId = owner, NotifyDemoEvents = false };
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var starts = 0;
        var stops = 0;
        var captures = 0;
        var configurations = new List<(bool? Enabled, int? Minutes)>();
        string? screenshotDirectory = null;
        var replies = new List<string>();
        var polls = 0;
        var resets = 0;
        var uploaded = false;
        var recovered = false;
        var pollWaiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        object Update(long id, long user, string command, string type = "private", long? chat = null) => new
        {
            update_id = id,
            message = new { from = new { id = user }, chat = new { id = chat ?? user, type }, text = command }
        };

        using var handler = new Handler(async (request, token) =>
        {
            var method = request.RequestUri!.Segments.Last();
            if (method == "deleteWebhook")
            {
                resets++;
                check((await request.Content!.ReadAsStringAsync(token)).Contains("\"drop_pending_updates\":true"),
                    "Telegram: startup discards stale commands");
            }
            else if (method == "getUpdates")
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                check(body.RootElement.GetProperty("timeout").GetInt32() > 0 &&
                    body.RootElement.GetProperty("allowed_updates")[0].GetString() == "message",
                    "Telegram: long polling requests messages only");
                polls++;
                if (polls == 1)
                    return Ok(new object[]
                    {
                        Update(1, owner + 1, "/demo"), Update(2, owner + 1, "/screenshot"),
                        Update(3, owner, "/demo", "group"), Update(4, owner, "/screenshot", chat: owner + 1),
                        new { update_id = 5, edited_message = new { text = "/demo" } },
                        Update(6, owner, "/start"), Update(7, owner, "/demo"),
                        Update(8, owner, "/stop"), Update(9, owner, "/screenshot"),
                        Update(10, owner, "/demo"), Update(11, owner, "/unknown"),
                        Update(12, owner + 1, "/autostart on"), Update(13, owner, "/idle 5", "group"),
                        Update(14, owner, "/autostart"), Update(15, owner, "/autostart on"),
                        Update(16, owner, "/autostart off"), Update(17, owner, "/autostart status"),
                        Update(18, owner, "/idle 25"), Update(19, owner, "/idle 0"),
                        Update(20, owner, "/idle 1441"), Update(21, owner, "/idle 1.5"),
                        Update(22, owner, "/idle -1"), Update(23, owner, "/idle 5 extra"),
                        Update(24, owner, "/autostart on extra"), Update(25, owner, "/idle abc")
                    });
                check(body.RootElement.GetProperty("offset").GetInt64() == 26,
                    "Telegram: offset advances past all processed updates");
                if (polls == 2) throw new HttpRequestException("simulated network failure");
                if (polls == 3)
                {
                    recovered = true;
                    return Ok(new[] { Update(7, owner, "/demo") });
                }
                pollWaiting.TrySetResult();
                await Task.Delay(Timeout.Infinite, token);
            }
            else if (method == "sendMessage")
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                check(body.RootElement.GetProperty("chat_id").GetInt64() == owner,
                    "Telegram: reply goes only to owner");
                replies.Add(body.RootElement.GetProperty("text").GetString()!);
                // Losing the launch reply must not launch the demo again.
                if (replies.Last() == "started") return new HttpResponseMessage(HttpStatusCode.BadGateway);
            }
            else if (method == "sendDocument")
            {
                var body = await request.Content!.ReadAsStringAsync(token);
                uploaded = body.Contains("desktop.png") && body.Contains("image/png") && body.Contains(owner.ToString());
            }
            else throw new Exception("Unexpected method");
            return Ok(true);
        });
        using var http = new HttpClient(handler);
        var service = new TelegramBotService(http, settings,
            _ => { starts++; return starts == 1 ? Task.FromResult("started") : throw new Exception("private exception"); },
            _ => { stops++; return Task.FromResult("stopped"); },
            directory =>
            {
                captures++;
                screenshotDirectory = directory;
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, "test.png");
                File.WriteAllBytes(path, [137, 80, 78, 71]);
                return path;
            }, (enabled, minutes, _) =>
            {
                configurations.Add((enabled, minutes));
                return Task.FromResult("settings");
            });
        var run = service.RunAsync(deadline.Token);
        await pollWaiting.Task.WaitAsync(deadline.Token);
        deadline.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(2));
        check(resets == 1 && recovered, "Telegram: network failure retries without resetting pending updates");
        check(starts == 2 && stops == 1 && captures == 1,
            "Telegram: unauthorized/group/edited/duplicate messages have no side effects");
        check(uploaded && screenshotDirectory != null && !Directory.Exists(screenshotDirectory),
            "Telegram: PNG upload and temporary screenshot cleanup");
        check(replies.Any(r => r.Contains("Не удалось")) && replies.All(r => !r.Contains("private exception")),
            "Telegram: command errors are safe and do not terminate polling");
        check(run.IsCompletedSuccessfully, "Telegram: cancellation interrupts pending HTTP polling");
        check(configurations.SequenceEqual(new (bool?, int?)[] { (null, null), (true, null), (false, null), (null, null), (null, 25) }),
            "Telegram: authorized settings commands parsed; invalid/group/foreign commands have no effects");
        check(replies.Any(r => r.Contains("/autostart") && r.Contains("/idle") && r.Contains("снимок экрана")),
            "Telegram: help includes settings and lifecycle notifications");

        foreach (var invalid in new[]
        {
            new TelegramSettings(),
            new TelegramSettings { Enabled = true, BotToken = "123:fake", AllowedUserId = 0 },
            new TelegramSettings { Enabled = true, BotToken = "123:fake/../../other", AllowedUserId = owner }
        })
        {
            using var rejectHandler = new Handler((_, _) => throw new Exception("must not access network"));
            using var rejectHttp = new HttpClient(rejectHandler);
            var disabled = new TelegramBotService(rejectHttp, invalid, _ => throw new Exception(),
                _ => throw new Exception(), _ => throw new Exception());
            disabled.QueueNotification(new("ignored", ShowroomBot.Core.Scenarios.ScenarioOutcome.Started));
            await disabled.RunAsync(CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(1));
            check(rejectHandler.Calls == 0, "Telegram: disabled/invalid settings fail closed");
        }
        verify(failedChecks.Count == 0, "Telegram transport checks: " + string.Join("; ", failedChecks));
        await CheckUiBridgeAsync(verify);
        await TelegramNotificationChecks.RunAsync(verify);
    }

    private static async Task CheckUiBridgeAsync(Action<bool, string> check)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var settingsDirectory = Path.Combine(Path.GetTempPath(), "showroombot-settings-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(settingsDirectory);
                var settingsService = new SettingsService(Path.Combine(settingsDirectory, "config.yaml"));
                var appSettings = new AppSettings { AutoStartDemo = false, IdleMinutes = 10 };
                settingsService.Save(appSettings);
                File.AppendAllText(settingsService.SettingsPath, "\n# preserve Telegram test comment\n");
                var controller = new ShowroomBot.Core.DemoController();
                var rdp = new ShowroomBot.Rdp.RdpController("");
                var runner = new ShowroomBot.Core.Scenarios.ScenarioRunner(
                    new ShowroomBot.Core.Scenarios.ScenarioStepFactory(rdp,
                        new ShowroomBot.Windows.KeyboardInputSender(), new ShowroomBot.Windows.MouseInputSender(),
                        new ShowroomBot.Windows.WindowScreenshotService(), new ShowroomBot.Windows.OneCSectionRecognizer()));
                var definition = new ShowroomBot.Core.Scenarios.ScenarioDefinition
                {
                    Name = "Telegram bridge check",
                    Steps = [new() { Type = "Wait", Seconds = 60 }]
                };
                var events = new List<ShowroomBot.Core.Scenarios.ScenarioNotification>();
                runner.ScenarioChanged += events.Add;
                using var form = new ShowroomBot.UI.MainForm(settingsService, appSettings,
                    [new(definition.Name, "test-only", definition)], new IdleDetector(),
                    new ShowroomBot.Windows.VpnDetector(), new ShowroomBot.Rdp.RdpAvailabilityChecker(), rdp,
                    new ShowroomBot.Core.Scenarios.DemoScenario(runner), controller);
                form.Opacity = 0;
                form.ShowInTaskbar = false;
                form.Shown += async (_, _) =>
                {
                    try
                    {
                        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        var telegramGroup = (System.Windows.Forms.GroupBox)form.Controls.Find("telegramSettingsGroup", true).Single();
                        var notifications = (System.Windows.Forms.CheckBox)telegramGroup.Controls.Find("telegramNotificationsCheckBox", true).Single();
                        check(notifications.Checked, "Telegram UI: old configs keep notifications enabled by default");
                        notifications.Checked = false;
                        check(!appSettings.Telegram.NotifyDemoEvents && !settingsService.Load().Telegram.NotifyDemoEvents,
                            "Telegram UI: notification checkbox saves shared and persisted settings");
                        notifications.Checked = true;
                        check(settingsService.Load().Telegram.NotifyDemoEvents,
                            "Telegram UI: notifications can be enabled again without restart");
                        if (Environment.GetEnvironmentVariable("SHOWROOMBOT_UI_PREVIEW") is { } previewPath)
                        {
                            using var preview = new System.Drawing.Bitmap(form.Width, form.Height);
                            form.DrawToBitmap(preview, new System.Drawing.Rectangle(0, 0, form.Width, form.Height));
                            preview.Save(previewPath, System.Drawing.Imaging.ImageFormat.Png);
                        }
                        var idle = await Task.Run(() => form.StopDemoFromTelegramAsync(timeout.Token));
                        check(idle.Contains("не выполнялась"), "Telegram UI: stop while idle");
                        var status = await Task.Run(() => form.ConfigureAutoStartFromTelegramAsync(null, null, timeout.Token));
                        check(status.Contains("выключен") && status.Contains("10 мин"), "Telegram UI: reads current settings");
                        await Task.Run(() => form.ConfigureAutoStartFromTelegramAsync(true, 25, timeout.Token));
                        var saved = settingsService.Load();
                        check(saved.AutoStartDemo && saved.IdleMinutes == 25 && appSettings.AutoStartDemo && appSettings.IdleMinutes == 25,
                            "Telegram UI: controls update shared settings and persist YAML");
                        check(File.ReadAllText(settingsService.SettingsPath).Contains("# preserve Telegram test comment"),
                            "Telegram UI: existing settings persistence preserves comments");
                        await form.ConfigureAutoStartFromTelegramAsync(false, null, timeout.Token);
                        check(!settingsService.Load().AutoStartDemo && settingsService.Load().IdleMinutes == 25,
                            "Telegram UI: disabling preserves interval");
                        await form.ConfigureAutoStartFromTelegramAsync(true, 0, timeout.Token);
                        check(!appSettings.AutoStartDemo && appSettings.IdleMinutes == 25,
                            "Telegram UI: invalid interval rejects entire settings change");
                        using (var cancelled = new CancellationTokenSource())
                        {
                            cancelled.Cancel();
                            try { await form.ConfigureAutoStartFromTelegramAsync(true, 99, cancelled.Token); }
                            catch (OperationCanceledException) { }
                            check(!appSettings.AutoStartDemo && appSettings.IdleMinutes == 25,
                                "Telegram UI: cancelled settings command has no side effects");
                        }
                        var started = await Task.Run(() => form.StartDemoFromTelegramAsync(timeout.Token));
                        check(started.Contains("начат") && controller.State == ShowroomBot.Core.AppState.DemoRunning &&
                            !controller.WasStartedAutomatically, "Telegram UI: existing manual start runs selected Wait scenario");
                        var duplicate = await Task.Run(() => form.StartDemoFromTelegramAsync(timeout.Token));
                        check(duplicate.Contains("уже выполняется"), "Telegram UI: concurrent start rejected");
                        form.Hide();
                        var stopped = await Task.Run(() => form.StopDemoFromTelegramAsync(timeout.Token));
                        check(stopped.Contains("запрошена") && controller.State == ShowroomBot.Core.AppState.Waiting,
                            "Telegram UI: hidden form uses existing cancellation");
                        while (!(await form.StopDemoFromTelegramAsync(timeout.Token)).Contains("не выполнялась"))
                            await Task.Delay(10, timeout.Token);
                        check(true, "Telegram UI: cancellation completes scenario and resets running guard");
                        check(events.Select(e => e.Outcome).SequenceEqual(new[] {
                            ShowroomBot.Core.Scenarios.ScenarioOutcome.Started, ShowroomBot.Core.Scenarios.ScenarioOutcome.Stopped }),
                            "Telegram UI: Telegram start/stop emits exactly one lifecycle pair");
                        // Exercise the same method used by UI and the idle timer, without waiting for infrastructure.
                        var startMethod = typeof(ShowroomBot.UI.MainForm).GetMethod("StartDemoAsync",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
                        foreach (var automatic in new[] { false, true })
                        {
                            events.Clear();
                            var scenarioTask = (Task)startMethod.Invoke(form, [automatic])!;
                            check(controller.WasStartedAutomatically == automatic, "Telegram UI: original launch source preserved");
                            await form.StopDemoFromTelegramAsync(timeout.Token);
                            await scenarioTask;
                            check(events.Count == 2 && events[0].Outcome == ShowroomBot.Core.Scenarios.ScenarioOutcome.Started &&
                                events[1].Outcome == ShowroomBot.Core.Scenarios.ScenarioOutcome.Stopped,
                                $"Telegram UI: lifecycle events also emitted for automatic={automatic}");
                        }
                        Directory.Delete(settingsDirectory, recursive: true);
                        completion.TrySetResult();
                    }
                    catch (Exception error) { completion.TrySetException(error); }
                    finally { System.Windows.Forms.Application.ExitThread(); }
                };
                System.Windows.Forms.Application.Run(form);
            }
            catch (Exception error) { completion.TrySetException(error); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
        check(thread.Join(TimeSpan.FromSeconds(2)), "Telegram UI: message loop exits cleanly");
    }

    private sealed class IdleDetector : ShowroomBot.Windows.IIdleDetector
    {
        public TimeSpan GetIdleTime() => TimeSpan.Zero;
    }

    private static HttpResponseMessage Ok(object result) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new { ok = true, result }), Encoding.UTF8, "application/json")
    };

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return send(request, cancellationToken);
        }
    }
}
