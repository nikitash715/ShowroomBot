namespace ShowroomBot.Core.Scenarios;

public enum ScenarioOutcome { Started, Completed, Failed, Stopped }

public sealed record ScenarioNotification(string Name, ScenarioOutcome Outcome,
    string? Error = null, string? ScreenshotPath = null, string? LogPath = null);
