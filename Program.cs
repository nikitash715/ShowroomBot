using ShowroomBot.Configuration;
using ShowroomBot.Core;
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
        var idleDetector = new IdleDetector();
        var vpnDetector = new VpnDetector();
        var rdpAvailabilityChecker = new RdpAvailabilityChecker();
        var rdpController = new RdpController();
        var keyboardInputSender = new KeyboardInputSender();
        var screenshotService = new WindowScreenshotService();
        var rdpTestScenario = new RdpTestScenario(rdpController, keyboardInputSender, screenshotService);
        var demoController = new DemoController();

        using var mainForm = new MainForm(
            settingsService,
            settings,
            idleDetector,
            vpnDetector,
            rdpAvailabilityChecker,
            rdpController,
            rdpTestScenario,
            demoController);
        Application.Run(mainForm);
    }
}
