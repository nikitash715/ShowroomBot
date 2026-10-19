using System.Text.Json;

namespace ShowroomBot.Telegram;

/// <summary>Explicit messages for the active development session; never executes commands.</summary>
public sealed class DevelopmentMailbox(string directory)
{
    public string DirectoryPath { get; } = Path.GetFullPath(directory);
    private string PathFor(string name) => Path.Combine(DirectoryPath, name);

    public string ReadReport() => File.Exists(PathFor("report.txt"))
        ? File.ReadAllText(PathFor("report.txt")) : "Отчёт тестовой серии пока не подготовлен.";

    public void SaveInstruction(long userId, long messageId, string text)
    {
        Directory.CreateDirectory(DirectoryPath);
        if (File.Exists(PathFor("instructions.jsonl")))
            foreach (var line in File.ReadLines(PathFor("instructions.jsonl")))
            {
                using var previous = JsonDocument.Parse(line);
                if (previous.RootElement.GetProperty("userId").GetInt64() == userId &&
                    previous.RootElement.GetProperty("messageId").GetInt64() == messageId) return;
            }
        File.AppendAllText(PathFor("instructions.jsonl"), JsonSerializer.Serialize(new
        {
            receivedUtc = DateTimeOffset.UtcNow, userId, messageId, text
        }) + Environment.NewLine);
    }

    public async Task DeliverPendingReportAsync(Func<string, CancellationToken, Task> send, CancellationToken token)
    {
        var pending = PathFor("report.pending.txt");
        if (!File.Exists(pending)) return;
        var text = await File.ReadAllTextAsync(pending, token);
        if (string.IsNullOrWhiteSpace(text)) return;
        await send(text, token);
        Directory.CreateDirectory(DirectoryPath);
        await File.WriteAllTextAsync(PathFor("report.txt"), text, token);
        // Only acknowledge the report that was actually delivered.
        if (File.Exists(pending) && await File.ReadAllTextAsync(pending, token) == text)
            File.Delete(pending);
    }
}
