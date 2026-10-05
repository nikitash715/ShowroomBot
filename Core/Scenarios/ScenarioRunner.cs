using System.Diagnostics;
using ShowroomBot.Windows;

namespace ShowroomBot.Core.Scenarios;

public sealed class ScenarioRunner
{
    private readonly ScenarioStepFactory _stepFactory;
    private readonly Func<string, string> _captureDesktop;
    public event Action<ScenarioNotification>? ScenarioChanged;

    public ScenarioRunner(ScenarioStepFactory stepFactory, Func<string, string>? captureDesktop = null)
    {
        _stepFactory = stepFactory;
        _captureDesktop = captureDesktop ?? new WindowScreenshotService().CaptureDesktop;
    }

    public async Task RunAsync(
        ScenarioDefinition scenario,
        CancellationToken cancellationToken = default)
    {
        using var execution = new ScenarioExecution(cancellationToken);
        execution.Write($"Начало сценария: {scenario.Name}");
        var stepType = string.Empty;
        var timer = Stopwatch.StartNew();
        var outcome = ScenarioOutcome.Completed;
        string? error = null;
        Notify(new(scenario.Name, ScenarioOutcome.Started, LogPath: execution.LogPath), execution);
        try
        {
            foreach (var definition in scenario.Steps)
            {
                execution.Token.ThrowIfCancellationRequested();
                stepType = definition.Type;
                timer.Restart();
                execution.Write($"Начало шага: {stepType}");
                var step = _stepFactory.Create(definition);
                await step.ExecuteAsync(execution.Token);
                execution.Token.ThrowIfCancellationRequested();
                execution.Write($"Шаг завершён: {stepType}; затрачено {timer.Elapsed.TotalSeconds:F3} с");
            }
            execution.Write("Сценарий завершён");
        }
        catch (OperationCanceledException) when (execution.Token.IsCancellationRequested)
        {
            outcome = ScenarioOutcome.Stopped;
            execution.Write($"Отмена сценария: {stepType}; затрачено {timer.Elapsed.TotalSeconds:F3} с");
            throw;
        }
        catch (Exception) when (execution.Token.IsCancellationRequested)
        {
            outcome = ScenarioOutcome.Stopped;
            execution.Write($"Отмена сценария: {stepType}; затрачено {timer.Elapsed.TotalSeconds:F3} с");
            throw new OperationCanceledException(execution.Token);
        }
        catch (Exception exception)
        {
            outcome = ScenarioOutcome.Failed;
            error = exception.Message;
            execution.Write($"{(exception is TimeoutException ? "Таймаут" : "Ошибка")}: {stepType}; " +
                $"затрачено {timer.Elapsed.TotalSeconds:F3} с; {exception.Message}");
            throw;
        }
        finally
        {
            string? path = null;
            // Reuse the diagnostic screenshot for Telegram. Capture immediately, including
            // cancellation, without activating windows or using the cancelled scenario token.
            if (outcome == ScenarioOutcome.Failed || ScenarioChanged is not null)
            {
                try
                {
                    path = _captureDesktop(execution.DirectoryPath);
                    execution.Write($"Диагностический screenshot: {path}");
                }
                catch (Exception screenshotError)
                {
                    execution.Write($"Не удалось сохранить screenshot: {screenshotError.Message}");
                }
            }
            Notify(new(scenario.Name, outcome, error, path, execution.LogPath), execution);
        }
    }

    private void Notify(ScenarioNotification notification, ScenarioExecution execution)
    {
        if (ScenarioChanged is not { } subscribers) return;
        foreach (Action<ScenarioNotification> subscriber in subscribers.GetInvocationList())
        {
            try { subscriber(notification); }
            catch (Exception) { execution.Write("Не удалось передать уведомление о сценарии."); }
        }
    }
}
