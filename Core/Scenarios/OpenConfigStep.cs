namespace ShowroomBot.Core.Scenarios;

public interface IOpenConfigUi
{
    Task PrepareAsync(CancellationToken token);
    Task<bool> IsConfiguratorAsync(CancellationToken token);
    Task SelectWindowAsync(int index, CancellationToken token);
    Task LaunchAsync(ScenarioStepDefinition definition, CancellationToken token);
    Task WaitReadyAsync(CancellationToken token);
}

public sealed class OpenConfigStep(ScenarioStepDefinition definition, IOpenConfigUi ui) : IScenarioStep
{
    public string Name => "Открыть конфигуратор 1С";

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Validate(definition);
        await ui.PrepareAsync(cancellationToken);
        var limit = Math.Min(15, definition.MaxWindowsToCheck);
        for (var index = 0; index < limit; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (index > 0) await ui.SelectWindowAsync(index, cancellationToken);
            ScenarioExecution.Log($"OpenConfig: проверяем окно {index + 1}/{limit} через Alt+Tab.");
            if (await ui.IsConfiguratorAsync(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                ScenarioExecution.Log("OpenConfig: найден открытый конфигуратор в RDP.");
                return;
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        ScenarioExecution.Log($"OpenConfig: проверено {limit} окон; запускаем конфигуратор командой CONFIG.");
        await ui.LaunchAsync(definition, cancellationToken);
        await ui.WaitReadyAsync(cancellationToken);
        ScenarioExecution.Log("OpenConfig: запущенный конфигуратор подтверждён.");
    }

    public static void Validate(ScenarioStepDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.Executable) || string.IsNullOrWhiteSpace(definition.Server) ||
            string.IsNullOrWhiteSpace(definition.Database))
            throw new InvalidOperationException("OpenConfig: задайте executable, server и database в параметрах шага.");
        if (definition.MaxWindowsToCheck <= 0 || definition.WindowSwitchDelayMs <= 0 ||
            definition.ReadyTimeoutSeconds <= 0 || definition.PollIntervalMs <= 0 ||
            definition.AfterActivationDelayMs < 0 || definition.AfterRunDialogDelayMs < 0)
            throw new InvalidOperationException("OpenConfig: лимиты и интервалы должны быть положительными, задержки — неотрицательными.");
    }
}
