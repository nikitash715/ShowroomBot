using System.Text;
using ShowroomBot.Rdp;
using ShowroomBot.Windows;

namespace ShowroomBot.Core.Scenarios;

public sealed class OpenOneCBaseStep : IScenarioStep
{
    private readonly ScenarioStepDefinition _definition;
    private readonly RdpController _rdpController;
    private readonly KeyboardInputSender _keyboardInputSender;

    public OpenOneCBaseStep(
        ScenarioStepDefinition definition,
        RdpController rdpController,
        KeyboardInputSender keyboardInputSender)
    {
        _definition = definition;
        _rdpController = rdpController;
        _keyboardInputSender = keyboardInputSender;
    }

    public string Name => "Открыть базу 1С";

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        ValidateDefinition();

        if (!_rdpController.TryActivateExistingWindow(out _))
        {
            throw new InvalidOperationException("Не найдено открытое окно mstsc или его не удалось активировать.");
        }

        await DelayAsync(_definition.AfterActivationDelayMs, cancellationToken);
        _keyboardInputSender.SendWindowsRun();
        await DelayAsync(_definition.AfterRunDialogDelayMs, cancellationToken);

        await _keyboardInputSender.SendTextAsync(
            BuildCommand(),
            TimeSpan.FromMilliseconds(Math.Max(0, _definition.TypingDelayMs)),
            cancellationToken);
        _keyboardInputSender.SendEnter();
        await DelayAsync(_definition.AfterLaunchDelayMs, cancellationToken);
    }

    private string BuildCommand()
    {
        var command = new StringBuilder();
        command.Append(Quote(_definition.Executable));
        command.Append(" ENTERPRISE /S ");
        command.Append(Quote($"{_definition.Server}\\{_definition.Database}"));

        if (!string.IsNullOrWhiteSpace(_definition.User))
        {
            command.Append(" /N ");
            command.Append(Quote(_definition.User));
        }

        if (!string.IsNullOrEmpty(_definition.Password))
        {
            command.Append(" /P ");
            command.Append(Quote(_definition.Password));
        }

        return command.ToString();
    }

    private void ValidateDefinition()
    {
        if (string.IsNullOrWhiteSpace(_definition.Executable))
        {
            throw new InvalidOperationException("В шаге Open1C не задан executable.");
        }

        if (string.IsNullOrWhiteSpace(_definition.Server))
        {
            throw new InvalidOperationException("В шаге Open1C не задан server.");
        }

        if (string.IsNullOrWhiteSpace(_definition.Database))
        {
            throw new InvalidOperationException("В шаге Open1C не задан database.");
        }
    }

    private static string Quote(string value)
    {
        return $"\"{value.Replace("\"", "\\\"")}\"";
    }

    private static Task DelayAsync(int milliseconds, CancellationToken cancellationToken)
    {
        return milliseconds > 0
            ? Task.Delay(milliseconds, cancellationToken)
            : Task.CompletedTask;
    }
}
