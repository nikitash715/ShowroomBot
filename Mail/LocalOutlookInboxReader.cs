using System.Diagnostics;
using System.Runtime.InteropServices;
using ShowroomBot.Core.Scenarios;

namespace ShowroomBot.Mail;

/// <summary>Classic Outlook automation on a dedicated STA. No RDP input, OCR or screenshots.</summary>
public sealed class LocalOutlookInboxReader : IOutlookInboxReader
{
    private const string Executable = @"C:\Program Files\Microsoft Office\root\Office16\OUTLOOK.EXE";
    private static readonly Guid ApplicationClsid = new("0006F03A-0000-0000-C000-000000000046");

    public Task<IReadOnlyList<OutlookMail>> ReadUnreadInboxAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<IReadOnlyList<OutlookMail>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { completion.SetResult(ReadInbox(cancellationToken)); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            { completion.SetCanceled(cancellationToken); }
            catch (COMException exception)
            {
                // Avoid Outlook-provided descriptions (which can contain mailbox data).
                completion.SetException(new InvalidOperationException(
                    $"CheckMail: ошибка Outlook COM (0x{exception.HResult:X8}). Проверьте профиль, диалоги Outlook и разрешение программного доступа."));
            }
            catch (Exception exception) { completion.SetException(exception); }
        }) { IsBackground = true, Name = "ShowroomBot Outlook STA" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        // Do not return early while a COM call still owns Outlook objects or can alter its UI.
        return completion.Task;
    }

    private static IReadOnlyList<OutlookMail> ReadInbox(CancellationToken token)
    {
        EnsureProcess(token);
        using var com = new ComScope();
        dynamic app = com.Keep(WaitForApplication(token));
        dynamic session = com.Keep(Call<object>(() => app.GetNamespace("MAPI"), token));
        dynamic inbox = com.Keep(Call<object>(() => session.GetDefaultFolder(6), token)); // olFolderInbox
        // Read through the folder object only. Changing Explorer panes or saving a view
        // persists UI settings and can mark selected messages as read.

        var storeId = Call<string>(() => inbox.StoreID, token);
        dynamic items = com.Keep(Call<object>(() => inbox.Items, token));
        dynamic unread = com.Keep(Call<object>(() => items.Restrict("[UnRead] = true"), token));
        Call(() => { unread.Sort("[ReceivedTime]", false); }, token);
        var result = new List<OutlookMail>();
        object? current = Call<object?>(() => unread.GetFirst(), token);
        while (current is not null)
        {
            using (var itemScope = new ComScope())
            {
                dynamic mail = itemScope.Keep(current);
                token.ThrowIfCancellationRequested();
                if (Call<int>(() => mail.Class, token) == 43 && Call<bool>(() => mail.UnRead, token)) // olMail
                {
                    var id = Call<string>(() => mail.EntryID, token);
                    if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(storeId))
                        throw new InvalidOperationException("CheckMail: Outlook не предоставил постоянный идентификатор письма.");
                    var name = Call<string?>(() => mail.SenderName, token) ?? "";
                    var address = Call<string?>(() => mail.SenderEmailAddress, token) ?? "";
                    result.Add(new(storeId, id, string.IsNullOrWhiteSpace(address) ? name : $"{name} <{address}>",
                        Call<string?>(() => mail.Subject, token) ?? "",
                        Call<string?>(() => mail.Body, token) ?? ""));
                }
                // Never Display(), Save() or assign UnRead on a MailItem.
            }
            current = Call<object?>(() => unread.GetNext(), token);
        }
        token.ThrowIfCancellationRequested();
        return result;
    }

    private static void EnsureProcess(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (LocalProcessIds().Count > 0) return;
        if (!File.Exists(Executable))
            throw new FileNotFoundException($"CheckMail: классический Outlook не найден: {Executable}");
        ScenarioExecution.Perform(() =>
        {
            token.ThrowIfCancellationRequested();
            // Start for COM automation without opening a mail window or changing its view.
            if (LocalProcessIds().Count == 0)
                using (Process.Start(new ProcessStartInfo(Executable, "/embedding") { UseShellExecute = true })) { }
        });
    }

    private static HashSet<uint> LocalProcessIds()
    {
        using var ownProcess = Process.GetCurrentProcess();
        var result = new HashSet<uint>();
        foreach (var process in Process.GetProcessesByName("OUTLOOK"))
        {
            using (process)
            {
                try { if (process.SessionId == ownProcess.SessionId) result.Add((uint)process.Id); }
                catch (InvalidOperationException) { /* Process exited during enumeration. */ }
            }
        }
        return result;
    }

    private static object WaitForApplication(CancellationToken token)
    {
        var timer = Stopwatch.StartNew();
        var activationAttempted = false;
        ScenarioExecution.Log("CheckMail: подключение к локальному Outlook через GetActiveObject.");
        while (timer.Elapsed < TimeSpan.FromSeconds(60))
        {
            token.ThrowIfCancellationRequested();
            var clsid = ApplicationClsid;
            var hr = GetActiveObject(ref clsid, IntPtr.Zero, out var app);
            if (hr >= 0 && app is not null)
            {
                ScenarioExecution.Log("CheckMail: COM подключён через GetActiveObject.");
                return app;
            }
            if (hr != unchecked((int)0x800401E3)) Marshal.ThrowExceptionForHR(hr); // MK_E_UNAVAILABLE
            if (!activationAttempted && timer.Elapsed >= TimeSpan.FromSeconds(3) && LocalProcessIds().Count > 0)
            {
                activationAttempted = true;
                token.ThrowIfCancellationRequested();
                // Outlook's COM activation returns the current instance when it is running.
                // Unlike ROT lookup, this does not require registration in the ROT first.
                ScenarioExecution.Log("CheckMail: объект отсутствует в ROT; резервное подключение через COM-активацию.");
                try
                {
                    object? activated = null;
                    ScenarioExecution.Perform(() =>
                    {
                        token.ThrowIfCancellationRequested();
                        if (LocalProcessIds().Count > 0)
                            activated = Activator.CreateInstance(Type.GetTypeFromCLSID(ApplicationClsid, throwOnError: true)!);
                    });
                    if (activated is not null)
                    {
                        ScenarioExecution.Log("CheckMail: COM подключён через COM-активацию.");
                        return activated;
                    }
                }
                catch (COMException exception)
                {
                    ScenarioExecution.Log($"CheckMail: резервное подключение не удалось (0x{exception.HResult:X8}); продолжаем ожидание ROT.");
                }
            }
            Pause(token);
        }
        throw new TimeoutException("CheckMail: Outlook не предоставил COM-интерфейс за 60 секунд. Завершите вход/выбор профиля; Outlook и ShowroomBot должны работать от одного пользователя с одинаковыми правами. Способы подключения и код ошибки резервного подключения записаны в журнал.");
    }

    private static void Call(Action action, CancellationToken token) => Call(() => { action(); return true; }, token);

    private static T Call<T>(Func<T> action, CancellationToken token)
    {
        var timer = Stopwatch.StartNew();
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try { return action(); }
            catch (COMException exception) when (exception.HResult is unchecked((int)0x80010001) or unchecked((int)0x8001010A))
            {
                if (timer.Elapsed >= TimeSpan.FromSeconds(30))
                    throw new TimeoutException("CheckMail: Outlook занят более 30 секунд. Закройте диалоги и повторите шаг.");
                Pause(token);
            }
        }
    }

    private static void Pause(CancellationToken token)
    {
        Application.DoEvents(); // Pump this dedicated STA while waiting for Outlook.
        if (token.WaitHandle.WaitOne(200)) token.ThrowIfCancellationRequested();
    }

    private sealed class ComScope : IDisposable
    {
        private readonly List<object> _objects = [];
        public object Keep(object value) { _objects.Add(value); return value; }
        public void Dispose()
        {
            for (var i = _objects.Count - 1; i >= 0; i--)
                if (Marshal.IsComObject(_objects[i])) Marshal.ReleaseComObject(_objects[i]);
        }
    }

    [DllImport("oleaut32.dll", PreserveSig = true)]
    private static extern int GetActiveObject(ref Guid clsid, IntPtr reserved,
        [MarshalAs(UnmanagedType.IUnknown)] out object? application);
}
