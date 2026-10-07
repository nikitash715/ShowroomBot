using ShowroomBot.Rdp;
using ShowroomBot.Windows;
using ShowroomBot.Configuration;

namespace ShowroomBot.Core.Scenarios;

public sealed class ScenarioStepFactory
{
    // Type and context live in one registry; YAML never selects the execution environment.
    private sealed record Registration(ScenarioExecutionContext Context,
        Func<ScenarioStepFactory, ScenarioStepDefinition, IScenarioStep> Create);
    private static readonly Dictionary<string, Registration> Registrations = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Wait"] = new(ScenarioExecutionContext.None, (_, d) => new WaitStep(d)),
        ["OpenConfig"] = new(ScenarioExecutionContext.Rdp, (f, d) =>
            new OpenConfigStep(d, new OpenConfigUi(f._rdpController, f._keyboardInputSender,
                f._screenshots, f._sectionRecognizer, d))),
        ["ReadCode"] = new(ScenarioExecutionContext.Rdp, (f, d) =>
            f.CreateReadCode(d)),
        ["CheckMail"] = new(ScenarioExecutionContext.Local, (f, _) => f._checkMail
            ?? throw new InvalidOperationException("CheckMail: сервис проверки почты не настроен.")),
        ["ExecuteToolkitQuery"] = new(ScenarioExecutionContext.Rdp, (f, d) =>
            new ExecuteToolkitQueryStep(d, f._rdpController, f._keyboardInputSender,
                f._mouseInputSender, f._screenshots, f._sectionRecognizer)),
        ["Open1CCommand"] = new(ScenarioExecutionContext.Rdp, (f, d) =>
            new OpenOneCCommandStep(d, f._rdpController, f._mouseInputSender, f._screenshots, f._sectionRecognizer, f._keyboardInputSender)),
        ["Open1CSection"] = new(ScenarioExecutionContext.Rdp, (f, d) =>
            new OpenOneCSectionStep(d, f._rdpController, f._mouseInputSender, f._screenshots, f._sectionRecognizer)),
        ["Open1C"] = new(ScenarioExecutionContext.Rdp, (f, d) =>
            new OpenOneCBaseStep(d, f._rdpController, f._keyboardInputSender, f._screenshots))
    };
    private readonly CheckMailStep? _checkMail;
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
        OneCSectionRecognizer sectionRecognizer,
        CheckMailStep? checkMail = null)
    {
        _rdpController = rdpController;
        _keyboardInputSender = keyboardInputSender;
        _mouseInputSender = mouseInputSender;
        _screenshots = screenshots;
        _sectionRecognizer = sectionRecognizer;
        _checkMail = checkMail;
    }

    public IScenarioStep Create(ScenarioStepDefinition definition) =>
        GetRegistration(definition.Type).Create(this, definition);

    private ReadCodeStep CreateReadCode(ScenarioStepDefinition definition) =>
        new(definition, new ReadCodeUi(_rdpController, _keyboardInputSender,
            _mouseInputSender, _screenshots, _sectionRecognizer, definition));
    public static ScenarioExecutionContext GetExecutionContext(string type) => GetRegistration(type).Context;

    private static Registration GetRegistration(string type) =>
        type is not null && Registrations.TryGetValue(type, out var registration) ? registration :
            throw new InvalidOperationException($"Неизвестный тип шага сценария: '{type}'.");
}
