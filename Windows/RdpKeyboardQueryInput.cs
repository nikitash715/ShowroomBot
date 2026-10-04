using ShowroomBot.Core.Scenarios;

namespace ShowroomBot.Windows;

public sealed class RdpKeyboardQueryInput(KeyboardInputSender keyboard)
{
    public async Task ReplaceAsync(string query, int delayMs, int timeoutSeconds, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        var lines = query.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var characterDelay = Math.Max(10, delayMs);
        var characterCount = lines.Sum(line => (long)line.Replace("\t", "    ").Length);
        var budgetMs = Math.Max(timeoutSeconds * 1000L,
            characterCount * (characterDelay + 20L) + lines.Length * 2000L + 10000L);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(budgetMs));
        ScenarioExecution.Log($"Toolkit: пауза набора {characterDelay} мс; лимит ввода {budgetMs / 1000.0:F1} с с учётом длины запроса.");
        var inputToken = timeout.Token;
        try
        {
            await keyboard.SendControlShortcutAsync(0x1E, inputToken); // Ctrl+A
            await keyboard.SendKeyAsync(0x08, inputToken); // Backspace
            await Task.Delay(150, inputToken);
            for (var i = 0; i < lines.Length; i++)
            {
                if (i > 0)
                {
                    inputToken.ThrowIfCancellationRequested();
                    await keyboard.SendKeyAsync(0x1B, inputToken); // Close completion before Enter.
                    await keyboard.SendKeyAsync(0x0D, inputToken);
                    await Task.Delay(150, inputToken);
                    await keyboard.ClearAutoIndentAsync(inputToken);
                }
                // Spaces preserve indentation without triggering Tab completion.
                await keyboard.SendTextAsync(lines[i].Replace("\t", "    "),
                    TimeSpan.FromMilliseconds(characterDelay), inputToken);
            }
            inputToken.ThrowIfCancellationRequested();
            await keyboard.SendKeyAsync(0x1B, inputToken);
            await Task.Delay(300, inputToken);
            ScenarioExecution.Log($"Toolkit: запрос набран с клавиатуры, {query.Length} символов; буфер обмена не используется.");
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException("Toolkit: истекло время набора запроса; увеличьте queryInputTimeoutSeconds. Запрос не запущен.");
        }
    }
}
