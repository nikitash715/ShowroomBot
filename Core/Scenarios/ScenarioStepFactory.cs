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

    public ScenarioStepFactory(
        RdpController rdpController,
        KeyboardInputSender keyboardInputSender,
        MouseInputSender mouseInputSender,
        WindowScreenshotService screenshots,
        OneCSectionRecognizer sectionRecognizer)
    {
        _rdpController = rdpController;
        _keyboardInputSender = keyboardInputSender;
        _mouseInputSender = mouseInputSender;
        _screenshots = screenshots;
        _sectionRecognizer = sectionRecognizer;
    }

    public IScenarioStep Create(ScenarioStepDefinition definition)
    {
        if (definition.Type.Equals("Wait", StringComparison.OrdinalIgnoreCase))
            return new WaitStep(definition);

        if (definition.Type.Equals("ExecuteToolkitQuery", StringComparison.OrdinalIgnoreCase))
            return new ExecuteToolkitQueryStep(definition, _rdpController, _keyboardInputSender,
                _mouseInputSender, _screenshots, _sectionRecognizer);

        if (definition.Type.Equals("Open1CCommand", StringComparison.OrdinalIgnoreCase))
        {
            return new OpenOneCCommandStep(definition, _rdpController, _mouseInputSender,
                _screenshots, _sectionRecognizer);
        }

        if (definition.Type.Equals("Open1CSection", StringComparison.OrdinalIgnoreCase))
        {
            return new OpenOneCSectionStep(definition, _rdpController, _mouseInputSender,
                _screenshots, _sectionRecognizer);
        }

        if (definition.Type.Equals("Open1C", StringComparison.OrdinalIgnoreCase))
        {
            return new OpenOneCBaseStep(definition, _rdpController, _keyboardInputSender, _screenshots);
        }

        throw new InvalidOperationException($"Неизвестный тип шага сценария: '{definition.Type}'.");
    }
}
