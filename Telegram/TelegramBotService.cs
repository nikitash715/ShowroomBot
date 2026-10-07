using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using ShowroomBot.Configuration;
using ShowroomBot.Core.Scenarios;
using ShowroomBot.Mail;

namespace ShowroomBot.Telegram;

/// <summary>Transport and authorization only; demo execution stays in the application.</summary>
public sealed class TelegramBotService
{
    private readonly HttpClient _http;
    private readonly TelegramSettings _settings;
    private readonly Func<CancellationToken, Task<string>> _start;
    private readonly Func<CancellationToken, Task<string>> _stop;
    private readonly Func<string, string> _captureDesktop;
    private readonly Func<bool?, int?, CancellationToken, Task<string>>? _configureAutoStart;
    private readonly Func<CancellationToken, Task<string>>? _connectVpn;
    private readonly Func<CancellationToken, Task<string>>? _status;
    private static readonly object MainMenu = Keyboard(
        ["Запустить демонстрацию", "Остановить демонстрацию"],
        ["Скриншот", "Статус"],
        ["Автозапуск", "Подключить VPN"]);
    private static readonly object AutoStartMenu = Keyboard(
        ["Включить автозапуск", "Выключить автозапуск"],
        ["Бездействие 5 мин", "Бездействие 10 мин", "Бездействие 15 мин"],
        ["Бездействие 30 мин", "Бездействие 60 мин"],
        ["Другой интервал", "Главное меню"]);

    private static object Keyboard(params string[][] rows) => new
    {
        keyboard = rows, resize_keyboard = true, is_persistent = true, one_time_keyboard = false
    };
    private readonly Channel<ScenarioNotification> _notifications = Channel.CreateUnbounded<ScenarioNotification>(
        new UnboundedChannelOptions { SingleReader = true, AllowSynchronousContinuations = false });
    public bool IsEnabled => _settings.Enabled && _settings.AllowedUserId > 0 &&
        Regex.IsMatch(_settings.BotToken ?? "", @"\A[0-9]+:[A-Za-z0-9_-]+\z");

    public TelegramBotService(HttpClient http, TelegramSettings settings,
        Func<CancellationToken, Task<string>> start,
        Func<CancellationToken, Task<string>> stop, Func<string, string> captureDesktop,
        Func<bool?, int?, CancellationToken, Task<string>>? configureAutoStart = null,
        Func<CancellationToken, Task<string>>? connectVpn = null,
        Func<CancellationToken, Task<string>>? status = null)
    {
        _http = http;
        _settings = settings;
        _start = start;
        _stop = stop;
        _captureDesktop = captureDesktop;
        _configureAutoStart = configureAutoStart;
        _connectVpn = connectVpn;
        _status = status;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        // Fail closed. Also prevent a malformed token from changing the request URL.
        if (!IsEnabled) return;
        await Task.WhenAll(PollAsync(cancellationToken), SendNotificationsAsync(cancellationToken));
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        long offset = 0;
        var initialized = false;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!initialized)
                {
                    // Old commands must not unexpectedly start automation after a restart.
                    using var reset = await CallAsync("deleteWebhook",
                        JsonContent.Create(new { drop_pending_updates = true }), cancellationToken);
                    initialized = true;
                }

                using var updates = await CallAsync("getUpdates", JsonContent.Create(new
                {
                    offset, timeout = 25, allowed_updates = new[] { "message" }
                }), cancellationToken);
                foreach (var update in updates.RootElement.GetProperty("result").EnumerateArray())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var id = update.GetProperty("update_id").GetInt64();
                    if (id < offset) continue;
                    // Advance before executing: a failed reply must not repeat a command.
                    offset = id + 1;
                    if (update.TryGetProperty("message", out var message))
                    {
                        try { await HandleMessageAsync(message, cancellationToken); }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                        catch (Exception) { /* A failed reply must not discard the rest of this batch. */ }
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (Exception)
            {
                // Never log HTTP exceptions or response bodies: they may include the bot token.
                try { await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            }
        }
    }

    private async Task HandleMessageAsync(JsonElement message, CancellationToken cancellationToken)
    {
        if (!message.TryGetProperty("from", out var from) ||
            !from.TryGetProperty("id", out var userId) || userId.GetInt64() != _settings.AllowedUserId ||
            !message.TryGetProperty("chat", out var chat) ||
            chat.GetProperty("type").GetString() != "private" ||
            chat.GetProperty("id").GetInt64() != _settings.AllowedUserId ||
            !message.TryGetProperty("text", out var text)) return;

        var input = text.GetString()?.Trim();
        input = input switch
        {
            "Запустить демонстрацию" => "/demo",
            "Остановить демонстрацию" => "/stop",
            "Скриншот" => "/screenshot",
            "Статус" => "/status",
            "Подключить VPN" => "/vpn",
            "Автозапуск" => "/autostart",
            "Включить автозапуск" => "/autostart on",
            "Выключить автозапуск" => "/autostart off",
            "Бездействие 5 мин" => "/idle 5",
            "Бездействие 10 мин" => "/idle 10",
            "Бездействие 15 мин" => "/idle 15",
            "Бездействие 30 мин" => "/idle 30",
            "Бездействие 60 мин" => "/idle 60",
            "Другой интервал" => "/idle help",
            "Главное меню" => "/menu",
            _ => input
        };
        var words = input?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries) ?? [];
        var command = words.FirstOrDefault();
        var menu = command is "/autostart" or "/idle" ? AutoStartMenu : MainMenu;
        // Commands in private chat do not require a bot username suffix.
        string result;
        try
        {
            result = command switch
            {
                "/start" or "/menu" => "/demo — запустить выбранную демонстрацию\n/stop — остановить демонстрацию\n/screenshot — снимок локального рабочего стола\n" +
                    "/status — состояние ShowroomBot, VPN и RDP\n/vpn — подключить первый VPN из config.yaml\n/menu — кнопочное меню\n" +
                    "/autostart on|off — включить/выключить автозапуск\n/autostart status — состояние и интервал\n/idle N — интервал бездействия (1–1440 минут)\n" +
                    "О начале сценария придёт уведомление с названием. По завершении, ошибке или остановке — результат и снимок экрана.",
                "/demo" => await _start(cancellationToken),
                "/stop" => await _stop(cancellationToken),
                "/screenshot" => string.Empty,
                "/vpn" when words.Length == 1 => _connectVpn is null ? "Подключение VPN недоступно." : await _connectVpn(cancellationToken),
                "/status" => _status is null ? "Статус недоступен." : await _status(cancellationToken),
                "/autostart" or "/idle" => await ConfigureAutoStartAsync(words, cancellationToken),
                _ => "Доступные команды: /start, /menu, /demo, /stop, /screenshot, /autostart, /idle, /status, /vpn."
            };
            if (command == "/screenshot")
            {
                await SendScreenshotAsync(cancellationToken);
                return;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { result = "Не удалось выполнить команду. Проверьте состояние ShowroomBot."; }

        await SendTextAsync(result, cancellationToken, menu);
    }

    private Task<string> ConfigureAutoStartAsync(string[] words, CancellationToken token)
    {
        if (_configureAutoStart is null) return Task.FromResult("Управление автозапуском недоступно.");
        if (words.Length == 1 || (words.Length == 2 && words[1] == "status"))
            return _configureAutoStart(null, null, token);
        if (words.Length == 2 && words[0] == "/autostart" && words[1] is "on" or "off")
            return _configureAutoStart(words[1] == "on", null, token);
        if (words.Length == 2 && words[0] == "/idle" &&
            int.TryParse(words[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) && minutes is >= 1 and <= 1440)
            return _configureAutoStart(null, minutes, token);
        return Task.FromResult("Формат: /autostart on|off|status или /idle N (целое число от 1 до 1440 минут).");
    }

    public void QueueNotification(ScenarioNotification notification)
    {
        if (IsEnabled && _settings.NotifyDemoEvents) _notifications.Writer.TryWrite(notification);
    }

    private async Task SendNotificationsAsync(CancellationToken token)
    {
        try
        {
            await foreach (var notification in _notifications.Reader.ReadAllAsync(token))
            {
                // Apply changes from the UI immediately, including messages still in the queue.
                if (!_settings.NotifyDemoEvents) continue;
                var text = notification.Outcome switch
                {
                    ScenarioOutcome.Started => $"Сценарий запущен: {notification.Name}",
                    ScenarioOutcome.Completed => $"Сценарий завершён: {notification.Name}",
                    ScenarioOutcome.Failed => $"Сценарий завершился с ошибкой: {notification.Name}\n{notification.Error}",
                    _ => $"Сценарий остановлен: {notification.Name}"
                };
                if (notification.Outcome != ScenarioOutcome.Started && notification.ScreenshotPath is null)
                    text += "\nНе удалось получить снимок текущего экрана.";
                // Each part is independent: a failed upload must not hide the error text,
                // and failed text delivery must not prevent sending the available screenshot.
                try { await SendTextAsync(text, token); }
                catch (Exception) when (!token.IsCancellationRequested) { LogDeliveryFailure(notification); }
                if (_settings.NotifyDemoEvents && notification.ScreenshotPath is { } path)
                {
                    try { await SendDocumentAsync(path, token); }
                    catch (Exception) when (!token.IsCancellationRequested) { LogDeliveryFailure(notification); }
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private static void LogDeliveryFailure(ScenarioNotification notification)
    {
        try
        {
            if (notification.LogPath is { } path)
                ScenarioExecution.WriteLog(path, $"Telegram: не удалось отправить уведомление ({notification.Outcome}).");
        }
        catch (Exception) { /* Notification diagnostics cannot affect automation. */ }
    }

    public Task SendMailAsync(OutlookMail mail, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!IsEnabled)
            throw new InvalidOperationException("CheckMail: включите и настройте Telegram перед отправкой новых писем.");
        // Await delivery of every part. Unlike lifecycle events this is not a background queue
        // and does not depend on NotifyDemoEvents; the caller persists only confirmed delivery.
        return SendTextAsync($"Новое письмо Outlook\nОт: {mail.Sender}\nТема: {mail.Subject}\n\n{mail.Body}", token);
    }

    private async Task SendTextAsync(string text, CancellationToken token, object? menu = null)
    {
        // Split long errors without losing their tail or breaking a UTF-16 surrogate pair.
        for (var offset = 0; offset < text.Length;)
        {
            var length = Math.Min(4000, text.Length - offset);
            if (char.IsHighSurrogate(text[offset + length - 1])) length--;
            if (length == 0) length = 1;
            var payload = new Dictionary<string, object>
            {
                ["chat_id"] = _settings.AllowedUserId, ["text"] = text.Substring(offset, length)
            };
            if (menu is not null) payload["reply_markup"] = menu;
            using var response = await CallAsync("sendMessage", JsonContent.Create(payload), token);
            offset += length;
        }
    }

    private async Task SendScreenshotAsync(CancellationToken cancellationToken)
    {
        var directory = Path.Combine(Path.GetTempPath(), "ShowroomBot-Telegram", Guid.NewGuid().ToString("N"));
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = _captureDesktop(directory);
            await SendDocumentAsync(path, cancellationToken, MainMenu);
        }
        finally
        {
            try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private async Task SendDocumentAsync(string path, CancellationToken cancellationToken, object? menu = null)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(_settings.AllowedUserId.ToString(CultureInfo.InvariantCulture)), "chat_id");
        if (menu is not null) content.Add(new StringContent(JsonSerializer.Serialize(menu)), "reply_markup");
        var file = new StreamContent(File.OpenRead(path));
        file.Headers.ContentType = new("image/png");
        content.Add(file, "document", "desktop.png");
        // Same lossless screenshot upload for commands and lifecycle notifications.
        using var response = await CallAsync("sendDocument", content, cancellationToken);
    }

    private async Task<JsonDocument> CallAsync(string method, HttpContent content, CancellationToken cancellationToken)
    {
        using (content)
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(40));
            using var response = await _http.PostAsync(
                $"https://api.telegram.org/bot{_settings.BotToken}/{method}", content, timeout.Token);
            // Do not propagate Telegram's descriptions or request URLs.
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Telegram request failed.");
            var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            if (document.RootElement.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True)
                return document;
            document.Dispose();
            throw new InvalidOperationException("Telegram request failed.");
        }
    }
}
