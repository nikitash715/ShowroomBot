namespace ShowroomBot.Core.Scenarios;

// The UI boundary lets checks exercise retries, cancellation and ordering without live input.
public interface IReadCodeUi
{
    Task AttachAsync(CancellationToken token);
    Task OpenCommonModulesAsync(CancellationToken token);
    Task<string?> OpenRandomModuleAsync(ISet<string> visited, CancellationToken token);
    Task<bool> CanReadModuleAsync(CancellationToken token);
    Task ReadModuleAsync(TimeSpan duration, CancellationToken token);
    Task<bool> TryNextConfigurationAsync(CancellationToken token) => Task.FromResult(false);
}

public sealed class ReadCodeStep(ScenarioStepDefinition definition, IReadCodeUi ui) : IScenarioStep
{
    public string Name => "Просмотр кода в конфигураторе 1С";

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Validate(definition);
        await ui.AttachAsync(cancellationToken);
        await ui.OpenCommonModulesAsync(cancellationToken);
        do
        {
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var attempt = 0; attempt < definition.MaxModuleAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = await ui.OpenRandomModuleAsync(visited, cancellationToken);
                if (name is null) break;
                if (!visited.Add(name)) throw new InvalidOperationException("ReadCode: повторный выбор уже проверенного модуля.");
                if (!await ui.CanReadModuleAsync(cancellationToken))
                {
                    ScenarioExecution.Log($"ReadCode: модуль {name} пустой или исходный текст отсутствует; пробуем другой.");
                    continue;
                }
                cancellationToken.ThrowIfCancellationRequested();
                ScenarioExecution.Log($"ReadCode: визуальное чтение {name}, {definition.ReadDurationSeconds} с.");
                await ui.ReadModuleAsync(TimeSpan.FromSeconds(definition.ReadDurationSeconds), cancellationToken);
                ScenarioExecution.Log($"ReadCode: чтение {name} завершено.");
                return;
            }
        } while (await ui.TryNextConfigurationAsync(cancellationToken));
        throw new InvalidOperationException("ReadCode: не найден общий модуль с доступным исходным текстом.");
    }

    public static void Validate(ScenarioStepDefinition definition)
    {
        if (definition.MaxModuleAttempts <= 0 || definition.ReadDurationSeconds <= 0 || definition.ReadLinePauseMs <= 0 ||
            definition.ReadyTimeoutSeconds <= 0 || definition.PollIntervalMs <= 0 ||
            definition.MaxScrollAttempts <= 0 || definition.ScrollTimeoutSeconds <= 0)
            throw new InvalidOperationException("ReadCode: лимиты и интервалы должны быть положительными.");
    }
}
