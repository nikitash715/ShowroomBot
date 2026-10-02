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
    public int TypingDelayMs { get; set; }
    public int AfterActivationDelayMs { get; set; }
    public int AfterRunDialogDelayMs { get; set; }
    public int AfterLaunchDelayMs { get; set; }
    public bool ReuseExistingWindow { get; set; } = true;
    public string PanelColor { get; set; } = "#C0DCC0";
    public int ColorTolerance { get; set; } = 8;
    public int ReadyTimeoutSeconds { get; set; } = 60;
    public int PollIntervalMs { get; set; } = 500;
    public int WindowSwitchDelayMs { get; set; } = 700;
    public int MaxWindowsToCheck { get; set; } = 50;
}

public sealed record ScenarioDescriptor(
    string Name,
    string FilePath,
    ScenarioDefinition Definition);
