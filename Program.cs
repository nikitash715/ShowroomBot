using ShowroomBot.Configuration;
using ShowroomBot.Core;
using ShowroomBot.Core.Scenarios;
using ShowroomBot.Rdp;
using ShowroomBot.UI;
using ShowroomBot.Windows;

namespace ShowroomBot;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        var settingsService = new SettingsService();
        var settings = settingsService.Load();
        settings.AutoStartDemo = false;
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
        var scenarioStepFactory = new ScenarioStepFactory(rdpController, keyboardInputSender,
            new MouseInputSender(settings.Automation.Mouse), new WindowScreenshotService(), new OneCSectionRecognizer());
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
        Application.Run(mainForm);
    }
}
