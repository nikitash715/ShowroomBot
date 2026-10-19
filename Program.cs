using ShowroomBot.Configuration;
using ShowroomBot.Core;
using ShowroomBot.Core.Scenarios;
using ShowroomBot.Rdp;
using ShowroomBot.UI;
using ShowroomBot.Windows;
using ShowroomBot.Telegram;
using ShowroomBot.Mail;
using System.Net.Http;
using System.Diagnostics;
using System.Text.Json;

namespace ShowroomBot;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        string? reportDirectory = null;
        var exitCode = 1;
        string? error = null;
        ScenarioNotification? outcome = null;
        try
        {
            if (args.Length > 0)
            {
                reportDirectory = Path.GetFullPath(Path.Combine(".test-artifacts",
                    $"cli-{DateTime.Now:yyyyMMdd-HHmmssfff}-{Environment.ProcessId}"));
                Directory.CreateDirectory(reportDirectory);
                ScenarioExecution.DiagnosticsRoot = reportDirectory;
            }
            var options = LaunchOptions.Parse(args);
            using var instance = new Mutex(true, @"Local\ShowroomBot.SingleInstance", out var created);
            // Also detect older builds that do not yet acquire the mutex.
            using var own = Process.GetCurrentProcess();
            var another = false;
            foreach (var process in Process.GetProcessesByName("ShowroomBot"))
                using (process)
                    try { another |= process.Id != own.Id && process.SessionId == own.SessionId; }
                    catch (InvalidOperationException) { }
            if (!created || another) throw new InvalidOperationException("ShowroomBot is already running. Close it before starting another instance.");
            exitCode = Run(options, notification => outcome = notification);
        }
        catch (Exception exception)
        {
            error = exception.Message;
            if (args.Length == 0) MessageBox.Show(error, "ShowroomBot", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            if (reportDirectory is not null)
            {
                var report = new { status = exitCode == 0 ? "success" : exitCode == 2 ? "cancelled" : "error",
                    exitCode, scenario = outcome?.Name, error = error ?? outcome?.Error,
                    logPath = outcome?.LogPath, screenshotPath = outcome?.ScreenshotPath };
                File.WriteAllText(Path.Combine(reportDirectory, "result.json"),
                    JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            }
        }
        return exitCode;
    }

    private static int Run(LaunchOptions options, Action<ScenarioNotification> recordOutcome)
    {
        ApplicationConfiguration.Initialize();
        DiagnosticsCleanup.Clean(Path.Combine(AppContext.BaseDirectory, "diagnostics"));

        var settingsService = new SettingsService();
        var settings = settingsService.Load();
        settingsService.Save(settings);

        IReadOnlyList<ScenarioDescriptor> scenarios;
        try
        {
            scenarios = new ScenarioCatalog().Load();
        }
        catch (Exception exception)
        {
            if (options.Scenario is not null) throw;
            MessageBox.Show(
                $"Не удалось загрузить сценарии: {exception.Message}",
                "ShowroomBot",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            scenarios = [];
        }
        var selected = options.Scenario is null ? null : options.Select(scenarios);

        var idleDetector = new IdleDetector();
        var vpnDetector = new VpnDetector();
        var rdpAvailabilityChecker = new RdpAvailabilityChecker();
        var rdpController = new RdpController(settings.Rdp.Host);
        var keyboardInputSender = new KeyboardInputSender(settings.Automation.Typing);
        TelegramBotService? telegram = null;
        var checkMail = new CheckMailStep(new LocalOutlookInboxReader(), new MailDeliveryHistory(),
            (mail, token) => (telegram ?? throw new InvalidOperationException("Telegram ещё не инициализирован."))
                .SendMailAsync(mail, token));
        var scenarioStepFactory = new ScenarioStepFactory(rdpController, keyboardInputSender,
            new MouseInputSender(settings.Automation.Mouse), new WindowScreenshotService(), new OneCSectionRecognizer(), checkMail);
        var scenarioRunner = new ScenarioRunner(scenarioStepFactory,
            finishRdp: options.TestSeries ? RdpController.MinimizeAfterScenario : null);
        ScenarioNotification? latestOutcome = null;
        if (options.Run) scenarioRunner.ScenarioChanged += notification =>
        {
            latestOutcome = notification;
            recordOutcome(notification);
        };
        var demoScenario = new DemoScenario(scenarioRunner);
        var demoController = new DemoController();

        using var mainForm = new MainForm(
            settingsService,
            settings,
            scenarios,
            idleDetector,
            vpnDetector,
            rdpAvailabilityChecker,
            rdpController,
            demoScenario,
            demoController);
        using var telegramCancellation = new CancellationTokenSource();
        using var telegramHttp = new HttpClient();
        telegram = new TelegramBotService(telegramHttp, settings.Telegram,
            mainForm.StartDemoFromTelegramAsync, mainForm.StopDemoFromTelegramAsync,
            new WindowScreenshotService().CaptureDesktop, mainForm.ConfigureAutoStartFromTelegramAsync,
            new VpnConnector(settings.Vpn).ConnectAsync, mainForm.GetStatusFromTelegramAsync);
        if (telegram.IsEnabled) scenarioRunner.ScenarioChanged += telegram.QueueNotification;
        Task telegramTask = Task.CompletedTask;
        mainForm.Shown += (_, _) => telegramTask = Task.Run(() => telegram.RunAsync(telegramCancellation.Token));
        var exitCode = options.Run ? 2 : 0;
        mainForm.Shown += async (_, _) =>
        {
            if (selected is not null) mainForm.SelectScenario(selected);
            if (options.Run)
            {
                var results = new List<object>();
                exitCode = await mainForm.RunFromCommandLineAsync(options.SeriesCount, options.TestSeries,
                    async (index, code) =>
                    {
                        if (!options.TestSeries) return;
                        var number = options.SeriesStart + index;
                        results.Add(new { number, exitCode = code, error = latestOutcome?.Error,
                            logPath = latestOutcome?.LogPath, screenshotPath = latestOutcome?.ScreenshotPath });
                        await File.WriteAllTextAsync(Path.Combine(ScenarioExecution.DiagnosticsRoot, "series.json"),
                            JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
                        var result = code == 0 ? "успешно" : code == 2 ? "остановлен" : $"ошибка: {latestOutcome?.Error}";
                        try
                        {
                            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                            await telegram.SendDevelopmentReportAsync($"Запуск {number}: {result}.\n" +
                                (index == options.SeriesCount - 1 || code != 0
                                    ? "Тесты остановлены. ShowroomBot остаётся открыт. Инструкции: /codex ваш текст."
                                    : "Серия продолжается. Остановка: /stop или Ctrl+Alt+F12."), timeout.Token);
                        }
                        catch (Exception)
                        {
                            if (latestOutcome?.LogPath is { } path)
                                ScenarioExecution.WriteLog(path, "Telegram: не удалось доставить короткий отчёт серии.");
                        }
                    });
                if (options.TestSeries && telegram.IsEnabled)
                {
                    try
                    {
                        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                        await telegram.SendDevelopmentReportAsync(
                            "ShowroomBot остаётся открыт: отправьте /codex и текст инструкции. Ящик доступен активной сессии Codex.", timeout.Token);
                    }
                    catch (Exception) { /* Report delivery must not close the development session. */ }
                }
            }
        };
        mainForm.FormClosed += (_, _) => telegramCancellation.Cancel();
        try { Application.Run(mainForm); }
        finally
        {
            scenarioRunner.ScenarioChanged -= telegram.QueueNotification;
            telegramCancellation.Cancel();
            telegramTask.GetAwaiter().GetResult();
        }
        return exitCode;
    }
}
