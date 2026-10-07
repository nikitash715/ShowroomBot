using ShowroomBot.Configuration;
using ShowroomBot.Core;
using ShowroomBot.Core.Scenarios;
using ShowroomBot.Rdp;
using ShowroomBot.UI;
using ShowroomBot.Windows;
using ShowroomBot.Telegram;
using ShowroomBot.Mail;
using System.Net.Http;

namespace ShowroomBot;

internal static class Program
{
    [STAThread]
    private static void Main()
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
            MessageBox.Show(
                $"Не удалось загрузить сценарии: {exception.Message}",
                "ShowroomBot",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            scenarios = [];
        }

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
        var scenarioRunner = new ScenarioRunner(scenarioStepFactory);
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
        mainForm.FormClosed += (_, _) => telegramCancellation.Cancel();
        try { Application.Run(mainForm); }
        finally
        {
            scenarioRunner.ScenarioChanged -= telegram.QueueNotification;
            telegramCancellation.Cancel();
            telegramTask.GetAwaiter().GetResult();
        }
    }
}
