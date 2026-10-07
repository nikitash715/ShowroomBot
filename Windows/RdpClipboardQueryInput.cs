using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using ShowroomBot.Core.Scenarios;

namespace ShowroomBot.Windows;

/// <summary>Paste as one text block, verify through remote copy, restore local clipboard.</summary>
public sealed class RdpClipboardQueryInput(KeyboardInputSender keyboard)
{
    public async Task<string> CopyAsync(bool selectAll, int timeoutSeconds, Func<CancellationToken, Task> ensureEditor,
        CancellationToken token)
    {
        using var clipboard = new ClipboardApartment();
        var snapshot = await clipboard.Invoke(() =>
        {
            var saved = new DataObject();
            var current = Clipboard.GetDataObject();
            if (current != null)
                foreach (var format in current.GetFormats(false))
                    if (current.GetData(format, false) is { } value) saved.SetData(format, false, value);
            return saved;
        }, token);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
            var ct = timeout.Token;
            try
            {
                await ensureEditor(ct);
                var sentinel = $"ShowroomBot-read-{Guid.NewGuid():N}";
                await clipboard.Invoke(() => { Clipboard.SetText(sentinel); return true; }, ct);
                await Task.Delay(700, ct);
                await ensureEditor(ct);
                if (selectAll) await keyboard.SendControlShortcutAsync(0x1E, ct);
                await keyboard.SendControlShortcutAsync(0x2E, ct);
                string? previous = null;
                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    ShowroomBot.Rdp.RdpInputGuard.Check();
                    var actual = await clipboard.Invoke(() => Clipboard.GetText(TextDataFormat.UnicodeText), ct);
                    if (actual != sentinel && actual.Length > 0 && actual == previous) return actual;
                    previous = actual;
                    await Task.Delay(200, ct);
                }
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                throw new TimeoutException("ReadCode: не получен свежий текст из RDP; проверьте перенаправление буфера обмена.");
            }
        }
        finally
        {
            await clipboard.Invoke(() =>
            {
                if (snapshot.GetFormats(false).Length == 0) Clipboard.Clear();
                else Clipboard.SetDataObject(snapshot, true, 5, 100);
                return true;
            }, CancellationToken.None);
        }
    }

    public static string NormalizeNewlines(string value) => value.Replace("\r\n", "\n").Replace('\r', '\n');
    public static void ValidateCopiedText(string query, string actual)
    {
        if (NormalizeNewlines(actual) != NormalizeNewlines(query))
            throw new InvalidOperationException("Toolkit: скопированный из редактора текст отличается от файла запроса; выполнение отменено. Проверьте перенаправление буфера обмена mstsc.");
    }

    public async Task ReplaceAndVerifyAsync(string query, int timeoutSeconds, CancellationToken token)
    {
        using var clipboard = new ClipboardApartment();
        var snapshot = await clipboard.Invoke(() =>
        {
            var saved = new DataObject();
            var current = Clipboard.GetDataObject();
            if (current != null)
                foreach (var format in current.GetFormats(false))
                {
                    var value = current.GetData(format, false);
                    if (value != null) saved.SetData(format, false, value);
                }
            return saved;
        }, token);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
            var operationToken = timeout.Token;
            try
            {
                var text = NormalizeNewlines(query).Replace("\n", "\r\n");
                await clipboard.Invoke(() => { Clipboard.SetText(text, TextDataFormat.UnicodeText); return true; }, operationToken);
                await Task.Delay(700, operationToken);
                await keyboard.SendControlShortcutAsync(0x1E, operationToken); // Ctrl+A
                await keyboard.SendControlShortcutAsync(0x2F, operationToken); // Ctrl+V
                await Task.Delay(700, operationToken);

                // A local clipboard already containing query text cannot prove that
                // remote paste worked. Seed a different value before remote Ctrl+C.
                var sentinel = $"ShowroomBot-verify-{Guid.NewGuid():N}";
                await clipboard.Invoke(() => { Clipboard.SetText(sentinel); return true; }, operationToken);
                await Task.Delay(700, operationToken);
                await keyboard.SendControlShortcutAsync(0x1E, operationToken);
                await keyboard.SendControlShortcutAsync(0x2E, operationToken); // Ctrl+C
                while (true)
                {
                    operationToken.ThrowIfCancellationRequested();
                    var actual = await clipboard.Invoke(() => Clipboard.GetText(TextDataFormat.UnicodeText), operationToken);
                    if (actual != sentinel && actual.Length > 0)
                    {
                        ValidateCopiedText(query, actual);
                        ScenarioExecution.Log($"Toolkit: содержимое редактора проверено обратным копированием, {query.Length} символов.");
                        return;
                    }
                    await Task.Delay(150, operationToken);
                }
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                throw new TimeoutException("Toolkit: не получен текст из удалённого редактора. Включите передачу буфера обмена в mstsc; запрос не запущен.");
            }
        }
        finally
        {
            // Restoration sends no keyboard input and must also run on cancellation.
            await clipboard.Invoke(() =>
            {
                if (snapshot.GetFormats(false).Length == 0) Clipboard.Clear();
                else Clipboard.SetDataObject(snapshot, true, 5, 100);
                return true;
            }, CancellationToken.None);
            ScenarioExecution.Log("Toolkit: прежний буфер обмена восстановлен.");
        }
    }

    internal sealed class ClipboardApartment : IDisposable
    {
        private readonly BlockingCollection<Action> _work = new();
        private readonly Thread _thread;
        public ClipboardApartment()
        {
            _thread = new Thread(() => { foreach (var action in _work.GetConsumingEnumerable()) action(); }) { IsBackground = true };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
        }
        public Task<T> Invoke<T>(Func<T> action, CancellationToken token)
        {
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            _work.Add(() =>
            {
                try
                {
                    for (var attempt = 0; ; attempt++)
                    {
                        token.ThrowIfCancellationRequested();
                        try { completion.SetResult(action()); break; }
                        catch (ExternalException) when (attempt < 10) { token.WaitHandle.WaitOne(50); }
                    }
                }
                catch (OperationCanceledException) { completion.SetCanceled(token); }
                catch (Exception exception) { completion.SetException(exception); }
            });
            return completion.Task;
        }
        public void Dispose()
        {
            _work.CompleteAdding();
            _thread.Join();
            _work.Dispose();
        }
    }
}
