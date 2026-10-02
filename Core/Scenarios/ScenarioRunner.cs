using System.Diagnostics;
using ShowroomBot.Windows;

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
        using var execution = new ScenarioExecution(cancellationToken);
        execution.Write($"Начало сценария: {scenario.Name}");
        var stepType = string.Empty;
        var timer = Stopwatch.StartNew();
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
            execution.Write($"Отмена сценария: {stepType}; затрачено {timer.Elapsed.TotalSeconds:F3} с");
            throw;
        }
        catch (Exception) when (execution.Token.IsCancellationRequested)
        {
            execution.Write($"Отмена сценария: {stepType}; затрачено {timer.Elapsed.TotalSeconds:F3} с");
            throw new OperationCanceledException(execution.Token);
        }
        catch (Exception exception)
        {
            execution.Write($"{(exception is TimeoutException ? "Таймаут" : "Ошибка")}: {stepType}; " +
                $"затрачено {timer.Elapsed.TotalSeconds:F3} с; {exception.Message}");
            // Capture as-is: error handling must never restore or activate RDP.
            if (!execution.Token.IsCancellationRequested)
            {
                try
                {
                    var path = new WindowScreenshotService().CaptureDesktop(execution.DirectoryPath);
                    execution.Write($"Диагностический screenshot: {path}");
                }
                catch (Exception screenshotError)
                {
                    execution.Write($"Не удалось сохранить screenshot: {screenshotError.Message}");
                }
            }
            throw;
        }
    }
}
