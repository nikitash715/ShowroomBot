using ShowroomBot.Core.Scenarios;

namespace ShowroomBot.Windows;

public sealed class RdpKeyboardQueryInput(KeyboardInputSender keyboard)
{
    public async Task ReplaceAsync(string query, int timeoutSeconds, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        var sourceLines = query.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var indentation = sourceLines.Select(line =>
        {
            var prefix = line[..(line.Length - line.TrimStart(' ', '\t').Length)];
            return (prefix.Replace("\t", "    ").Length + 3) / 4;
        }).ToArray();
        var lines = sourceLines.Select(line =>
            line.TrimStart(' ', '\t').Replace("\t", "    ") + " ").ToArray();
        var characterCount = lines.Sum(line => (long)line.Length);
        var budgetMs = Math.Max(timeoutSeconds * 1000L,
            keyboard.MaximumTextDurationMilliseconds(characterCount) + lines.Length * 2000L + 10000L);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(budgetMs));
        ScenarioExecution.Log($"Toolkit: лимит ввода {budgetMs / 1000.0:F1} с с учётом длины запроса.");
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
                    await keyboard.SendKeyAsync(0x0D, inputToken);
                    await Task.Delay(150, inputToken);
                    // Enter inherits the previous line's indentation.
                }
                var previousIndent = i > 0 ? indentation[i - 1] : 0;
                var indentChange = indentation[i] - previousIndent;
                for (var level = 0; level < Math.Abs(indentChange); level++)
                    await keyboard.SendKeyAsync(indentChange > 0 ? (ushort)0x09 : (ushort)0x08,
                        inputToken); // Tab adds a level; Backspace removes inherited indentation.
                // Type only content: unchanged indentation is supplied by the editor.
                // Each prepared line ends with a space to dismiss completion before the next Enter.
                await keyboard.SendTextAsync(lines[i],
                    inputToken);
            }
            inputToken.ThrowIfCancellationRequested();
            await Task.Delay(300, inputToken);
            ScenarioExecution.Log($"Toolkit: запрос набран с клавиатуры, {query.Length} символов; буфер обмена не используется.");
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException("Toolkit: истекло время набора запроса; увеличьте queryInputTimeoutSeconds. Запрос не запущен.");
        }
    }
}
