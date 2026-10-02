namespace ShowroomBot.Core.Scenarios;

public sealed class DemoScenario
{
    private readonly ScenarioRunner _runner;

    public DemoScenario(ScenarioRunner runner)
    {
        _runner = runner;
    }

    public Task RunAsync(
        ScenarioDefinition scenario,
        CancellationToken cancellationToken = default)
    {
        return _runner.RunAsync(scenario, cancellationToken);
    }
}
