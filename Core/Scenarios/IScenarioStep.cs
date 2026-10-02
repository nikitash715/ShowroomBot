namespace ShowroomBot.Core.Scenarios;

public interface IScenarioStep
{
    string Name { get; }

    Task ExecuteAsync(CancellationToken cancellationToken = default);
}
