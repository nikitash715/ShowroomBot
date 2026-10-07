using ShowroomBot.Configuration;

namespace ShowroomBot.Windows;

public sealed class VpnConnector
{
    private readonly VpnSettings _settings;
    private readonly Action<string> _launch;

    public VpnConnector(VpnSettings settings, Action<string>? launch = null)
    {
        _settings = settings;
        _launch = launch ?? SavedCredentialVpnDialer.Start;
    }

    public Task<string> ConnectAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var name = _settings.ConnectionNames?.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(name) || name.StartsWith('/') || name.Contains('\0'))
            return Task.FromResult("Не удалось запустить подключение: проверьте первое имя в vpn.connectionNames.");

        try
        {
            _launch(name);
            return Task.FromResult($"Запуск подключения VPN «{name}» с сохранёнными данными выполнен. Подтвердите авторизацию на телефоне. " +
                "Это ещё не подтверждение соединения; проверить его можно кнопкой «Статус» или /status.");
        }
        catch (Exception)
        {
            return Task.FromResult($"Не удалось запустить подключение VPN «{name}». Проверьте профиль и сохранённые логин и пароль в Windows: подключитесь вручную с сохранением данных.");
        }
    }
}
