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
    public string Executable { get; set; } = string.Empty;
    public string Server { get; set; } = string.Empty;
    public string Database { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public int TypingDelayMs { get; set; }
    public int AfterActivationDelayMs { get; set; }
    public int AfterRunDialogDelayMs { get; set; }
    public int AfterLaunchDelayMs { get; set; }
}

public sealed record ScenarioDescriptor(
    string Name,
    string FilePath,
    ScenarioDefinition Definition);
