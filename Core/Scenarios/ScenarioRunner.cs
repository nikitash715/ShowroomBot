namespace ShowroomBot.Core.Scenarios;

public sealed class ScenarioRunner
{
    private readonly ScenarioStepFactory _stepFactory;

    public ScenarioRunner(ScenarioStepFactory stepFactory)
    {
        _stepFactory = stepFactory;
    }

    public async Task RunAsync(
        ScenarioDefinition scenario,
        CancellationToken cancellationToken = default)
    {
        foreach (var definition in scenario.Steps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var step = _stepFactory.Create(definition);
            await step.ExecuteAsync(cancellationToken);
        }
    }
}
