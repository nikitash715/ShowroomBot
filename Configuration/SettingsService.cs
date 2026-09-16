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

    public string SettingsPath { get; } = Path.Combine(AppContext.BaseDirectory, FileName);

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
        catch
        {
            settings = new AppSettings();
        }

        Normalize(settings);
        Save(settings);
        return settings;
    }

    public void Save(AppSettings settings)
    {
        Normalize(settings);
        var yaml = Serializer.Serialize(settings);
        File.WriteAllText(SettingsPath, yaml);
    }

    private static void Normalize(AppSettings settings)
    {
        var defaultSettings = new AppSettings();

        if (settings.IdleMinutes < 1)
        {
            settings.IdleMinutes = defaultSettings.IdleMinutes;
        }

        settings.Vpn ??= new VpnSettings();
        settings.Rdp ??= new RdpSettings();

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
    }
}
