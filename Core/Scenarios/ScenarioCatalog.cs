using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ShowroomBot.Core.Scenarios;

public sealed class ScenarioCatalog
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public string ScenariosDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "Scenarios");

    public IReadOnlyList<ScenarioDescriptor> Load()
    {
        if (!Directory.Exists(ScenariosDirectory))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(ScenariosDirectory, "*.yaml", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.CurrentCultureIgnoreCase)
            .Select(LoadScenario)
            .ToArray();
    }

    private static ScenarioDescriptor LoadScenario(string filePath)
    {
        var yaml = File.ReadAllText(filePath);
        var definition = Deserializer.Deserialize<ScenarioDefinition>(yaml)
            ?? throw new InvalidDataException($"Сценарий '{filePath}' пуст.");

        if (string.IsNullOrWhiteSpace(definition.Name))
        {
            definition.Name = Path.GetFileNameWithoutExtension(filePath);
        }

        definition.Steps ??= [];
        return new ScenarioDescriptor(definition.Name, filePath, definition);
    }
}
