using ShowroomBot.Core.Scenarios;

namespace ShowroomBot.Configuration;

public sealed record LaunchOptions(string? Scenario, bool Run)
{
    public bool TestSeries { get; init; }
    public int SeriesCount { get; init; } = 1;
    public int SeriesStart { get; init; } = 1;
    public static LaunchOptions Parse(string[] args)
    {
        string? scenario = null;
        var run = false;
        var testSeries = false;
        int? seriesCount = null;
        int? seriesStart = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--scenario" && scenario is null && i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                scenario = args[++i];
            else if (args[i] == "--run" && !run) run = true;
            else if (args[i] == "--test-series" && !testSeries) testSeries = true;
            else if (args[i] == "--series-count" && seriesCount is null && i + 1 < args.Length &&
                int.TryParse(args[++i], out var count) && count is >= 1 and <= 100) seriesCount = count;
            else if (args[i] == "--series-start" && seriesStart is null && i + 1 < args.Length &&
                int.TryParse(args[++i], out var start) && start is >= 1 and <= 100) seriesStart = start;
            else throw new ArgumentException("Usage: ShowroomBot.exe [--scenario <number|name>] [--run] [--test-series [--series-count N] [--series-start N]]");
        }
        if (run && scenario is null) throw new ArgumentException("--run requires --scenario.");
        if (testSeries && !run) throw new ArgumentException("--test-series requires --run.");
        if ((seriesCount is not null || seriesStart is not null) && !testSeries)
            throw new ArgumentException("--series-count and --series-start require --test-series.");
        return new(scenario, run) { TestSeries = testSeries, SeriesCount = seriesCount ?? 1, SeriesStart = seriesStart ?? 1 };
    }

    public ScenarioDescriptor Select(IReadOnlyList<ScenarioDescriptor> scenarios) =>
        scenarios.SingleOrDefault(s => int.TryParse(Scenario, out var number)
            ? string.Equals(Path.GetFileNameWithoutExtension(s.FilePath), $"Scenario{number}", StringComparison.OrdinalIgnoreCase)
            : string.Equals(s.Name, Scenario, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"Scenario not found: {Scenario}");
}
