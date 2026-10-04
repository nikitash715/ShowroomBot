namespace ShowroomBot.Core.Scenarios;

public sealed class ScenarioDefinition
{
    public string Name { get; set; } = string.Empty;
    public List<ScenarioStepDefinition> Steps { get; set; } = [];
}

public sealed class ScenarioStepDefinition
{
    public string Type { get; set; } = string.Empty;
    public string Section { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
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
    public int ReadyTimeoutSeconds { get; set; } = 60;
    public int PollIntervalMs { get; set; } = 500;
    public int WindowSwitchDelayMs { get; set; } = 700;
    public int MaxWindowsToCheck { get; set; } = 50;
    public string QueryFile { get; set; } = string.Empty;
    public string QueryInputMode { get; set; } = "typing";
    public int ConsoleTimeoutSeconds { get; set; } = 30;
    public int QueryInputTimeoutSeconds { get; set; } = 20;
    public int QueryTimeoutSeconds { get; set; } = 120;
    public int ScrollTimeoutSeconds { get; set; } = 50;
    public int ScrollPauseMs { get; set; } = 700;
    public int ScrollNotches { get; set; } = 2;
    public int MaxScrollAttempts { get; set; } = 300;
    public int ScrollUnchangedAttempts { get; set; } = 4;
    public int ResultStablePolls { get; set; } = 3;
}

public sealed record ScenarioDescriptor(
    string Name,
    string FilePath,
    ScenarioDefinition Definition);
