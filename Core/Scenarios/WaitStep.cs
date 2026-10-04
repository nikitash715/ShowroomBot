namespace ShowroomBot.Core.Scenarios;

public sealed class WaitStep(ScenarioStepDefinition definition) : IScenarioStep
{
    public string Name => $"Ожидание: {definition.Seconds} с";

    public Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (definition.Seconds <= 0)
            throw new InvalidOperationException("Wait: seconds должен быть положительным.");

        return Task.Delay(TimeSpan.FromSeconds(definition.Seconds), cancellationToken);
    }
}
