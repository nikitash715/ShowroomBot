using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ShowroomBot.Configuration;

public sealed class SettingsService
{
    private const string FileName = "config.yaml";

    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public string SettingsPath { get; }
    public SettingsService(string? path = null) => SettingsPath = path ?? Path.Combine(AppContext.BaseDirectory, FileName);

    public AppSettings Load()
    {
        AppSettings settings;

        if (!File.Exists(SettingsPath))
        {
            settings = new AppSettings();
            Save(settings);
            return settings;
        }

        try
        {
            var yaml = File.ReadAllText(SettingsPath);
            settings = Deserializer.Deserialize<AppSettings>(yaml) ?? new AppSettings();
        }
        catch (Exception error)
        {
            throw new InvalidDataException($"Не удалось прочитать настройки {SettingsPath}; файл сохранён без изменений.", error);
        }

        Normalize(settings);
        return settings;
    }

    public void Save(AppSettings settings)
    {
        Normalize(settings);
        var yaml = Serializer.Serialize(settings);
        if (File.Exists(SettingsPath)) yaml = CommentedYaml.Update(File.ReadAllText(SettingsPath), yaml);
        File.WriteAllText(SettingsPath, yaml);
    }

    private static void Normalize(AppSettings settings)
    {
        var defaultSettings = new AppSettings();
        if (!TimeOnly.TryParseExact(settings.AutoStartStartTime, "HH:mm", out _))
            settings.AutoStartStartTime = defaultSettings.AutoStartStartTime;
        if (!TimeOnly.TryParseExact(settings.AutoStartEndTime, "HH:mm", out _))
            settings.AutoStartEndTime = defaultSettings.AutoStartEndTime;

        if (settings.IdleMinutes < 1)
        {
            settings.IdleMinutes = defaultSettings.IdleMinutes;
        }

        settings.Vpn ??= new VpnSettings();
        settings.Rdp ??= new RdpSettings();
        settings.Automation ??= new AutomationSettings();
        settings.Telegram ??= new TelegramSettings();
        settings.Automation.Mouse ??= new MouseSettings();
        settings.Automation.Typing ??= new TypingSettings();

        var defaultVpnSettings = new VpnSettings();
        if (settings.Vpn.ConnectionNames.Length == 0)
        {
            settings.Vpn.ConnectionNames = defaultVpnSettings.ConnectionNames;
        }

        if (settings.Vpn.CheckIntervalSeconds < 1)
        {
            settings.Vpn.CheckIntervalSeconds = defaultVpnSettings.CheckIntervalSeconds;
        }

        var defaultRdpSettings = new RdpSettings();
        if (string.IsNullOrWhiteSpace(settings.Rdp.Host))
        {
            settings.Rdp.Host = defaultRdpSettings.Host;
        }

        if (settings.Rdp.Port <= 0 || settings.Rdp.Port > 65535)
        {
            settings.Rdp.Port = defaultRdpSettings.Port;
        }

        if (string.IsNullOrWhiteSpace(settings.Rdp.UserName))
        {
            settings.Rdp.UserName = defaultRdpSettings.UserName;
        }

        if (settings.Rdp.CheckIntervalSeconds < 1)
        {
            settings.Rdp.CheckIntervalSeconds = defaultRdpSettings.CheckIntervalSeconds;
        }

        if (settings.Rdp.ConnectTimeoutSeconds < 1)
        {
            settings.Rdp.ConnectTimeoutSeconds = defaultRdpSettings.ConnectTimeoutSeconds;
        }

        settings.Automation.Mouse.MovementDurationMilliseconds =
            Math.Max(1, settings.Automation.Mouse.MovementDurationMilliseconds);
        settings.Automation.Mouse.StepDelayMilliseconds =
            Math.Max(1, settings.Automation.Mouse.StepDelayMilliseconds);
        var mouse = settings.Automation.Mouse;
        mouse.CurvatureRatio = double.IsFinite(mouse.CurvatureRatio) ? Math.Clamp(mouse.CurvatureRatio, 0, 0.2) : 0.08;
        mouse.DeviationPixels = double.IsFinite(mouse.DeviationPixels) ? Math.Clamp(mouse.DeviationPixels, 0, 10) : 2;
        mouse.DurationVariation = double.IsFinite(mouse.DurationVariation) ? Math.Clamp(mouse.DurationVariation, 0, 0.9) : 0.25;
        var typing = settings.Automation.Typing;
        typing.MinimumDelayMilliseconds = Math.Max(1, typing.MinimumDelayMilliseconds);
        typing.SlowdownFactor = double.IsFinite(typing.SlowdownFactor) ? Math.Clamp(typing.SlowdownFactor, 1, 10) : 2;
        typing.TempoSegmentCharacters = Math.Max(2, typing.TempoSegmentCharacters);
        typing.JitterMilliseconds = Math.Max(0, typing.JitterMilliseconds);
        typing.PauseProbability = double.IsFinite(typing.PauseProbability) ? Math.Clamp(typing.PauseProbability, 0, 1) : 0.008;
        typing.PauseMinimumMilliseconds = Math.Max(0, typing.PauseMinimumMilliseconds);
        typing.PauseMaximumMilliseconds = Math.Max(typing.PauseMinimumMilliseconds, typing.PauseMaximumMilliseconds);
    }
}
