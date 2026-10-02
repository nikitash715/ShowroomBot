using ShowroomBot.Rdp;
using ShowroomBot.Windows;
using ShowroomBot.Configuration;

namespace ShowroomBot.Core.Scenarios;

public sealed class ScenarioStepFactory
{
    private readonly RdpController _rdpController;
    private readonly KeyboardInputSender _keyboardInputSender;
    private readonly MouseInputSender _mouseInputSender;
    private readonly WindowScreenshotService _screenshots;
    private readonly OneCSectionRecognizer _sectionRecognizer;
    private readonly MouseSettings _mouseSettings;

    public ScenarioStepFactory(
        RdpController rdpController,
        KeyboardInputSender keyboardInputSender,
        MouseInputSender mouseInputSender,
        WindowScreenshotService screenshots,
        OneCSectionRecognizer sectionRecognizer,
        MouseSettings mouseSettings)
    {
        _rdpController = rdpController;
        _keyboardInputSender = keyboardInputSender;
        _mouseInputSender = mouseInputSender;
        _screenshots = screenshots;
        _sectionRecognizer = sectionRecognizer;
        _mouseSettings = mouseSettings;
    }

    public IScenarioStep Create(ScenarioStepDefinition definition)
    {
        if (definition.Type.Equals("Open1CCommand", StringComparison.OrdinalIgnoreCase))
        {
            return new OpenOneCCommandStep(definition, _rdpController, _mouseInputSender,
                _screenshots, _sectionRecognizer, _mouseSettings);
        }

        if (definition.Type.Equals("Open1CSection", StringComparison.OrdinalIgnoreCase))
        {
            return new OpenOneCSectionStep(definition, _rdpController, _mouseInputSender,
                _screenshots, _sectionRecognizer, _mouseSettings);
        }

        if (definition.Type.Equals("Open1C", StringComparison.OrdinalIgnoreCase))
        {
            return new OpenOneCBaseStep(definition, _rdpController, _keyboardInputSender, _screenshots);
        }

        throw new InvalidOperationException($"Неизвестный тип шага сценария: '{definition.Type}'.");
    }
}
