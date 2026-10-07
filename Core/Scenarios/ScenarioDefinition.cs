namespace ShowroomBot.Core.Scenarios;

public sealed class ScenarioDefinition
{
    public string Name { get; set; } = string.Empty;
    public List<ScenarioStepDefinition> Steps { get; set; } = [];

    [YamlDotNet.Serialization.YamlIgnore]
    public bool RequiresRdp => Steps.Any(step => step.ExecutionContext == ScenarioExecutionContext.Rdp);
}

public sealed class ScenarioStepDefinition
{
    public string Type { get; set; } = string.Empty;
    [YamlDotNet.Serialization.YamlIgnore]
    public ScenarioExecutionContext ExecutionContext => ScenarioStepFactory.GetExecutionContext(Type);
    public int Seconds { get; set; }
    public string Section { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public string FallbackLink { get; set; } = string.Empty;
    public int SectionOpenTimeoutSeconds { get; set; } = 5;
    public int CommandTimeoutSeconds { get; set; } = 15;
    public string Executable { get; set; } = string.Empty;
    public string Server { get; set; } = string.Empty;
    public string Database { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public int AfterActivationDelayMs { get; set; } = 1000;
    public int AfterRunDialogDelayMs { get; set; } = 500;
    public string PanelColor { get; set; } = "#C0DCC0";
    public int ColorTolerance { get; set; } = 9;
    public int ReadyTimeoutSeconds { get; set; } = 420;
    private int? _pollIntervalMs;
    public int PollIntervalMs
    {
        get => _pollIntervalMs ?? (string.Equals(Type, "Open1C", StringComparison.OrdinalIgnoreCase) ? 30000 : 500);
        set => _pollIntervalMs = value;
    }
    public int WindowSwitchDelayMs { get; set; } = 700;
    private int? _maxWindowsToCheck;
    public int MaxWindowsToCheck
    {
        get => _maxWindowsToCheck ?? (string.Equals(Type, "OpenConfig", StringComparison.OrdinalIgnoreCase) ? 15 : 50);
        set => _maxWindowsToCheck = value;
    }
    public string QueryFile { get; set; } = string.Empty;
    public string QueryInputMode { get; set; } = "typing";
    public int ConsoleTimeoutSeconds { get; set; } = 30;
    public int QueryInputTimeoutSeconds { get; set; } = 20;
    public int QueryTimeoutSeconds { get; set; } = 120;
    public int ScrollSpeed { get; set; } = 5;
    [YamlDotNet.Serialization.YamlIgnore]
    public int ScrollNotches => ScrollSpeed <= 5 ? 1 + (ScrollSpeed - 1) / 4 : 2 + (int)Math.Round((ScrollSpeed - 5) * 8d / 5);
    [YamlDotNet.Serialization.YamlIgnore]
    public int ScrollPauseMs => ScrollSpeed <= 5 ? 1500 - (ScrollSpeed - 1) * 200 : 700 - (ScrollSpeed - 5) * 120;
    // Safety limits are independent of speed and can be configured separately in YAML.
    public int MaxScrollAttempts { get; set; } = 300;
    public int ScrollTimeoutSeconds { get; set; } = 660;
    public int ScrollUnchangedAttempts { get; set; } = 4;
    public int ResultStablePolls { get; set; } = 3;
    public int MaxModuleAttempts { get; set; } = 8;
    public int ReadDurationSeconds { get; set; } = 43;
    // Legacy YAML option; visual reading does not count source lines.
    public int MinimumCodeLines { get; set; } = 10;
    public int ReadLinePauseMs { get; set; } = 900;
}

public sealed record ScenarioDescriptor(
    string Name,
    string FilePath,
    ScenarioDefinition Definition);
