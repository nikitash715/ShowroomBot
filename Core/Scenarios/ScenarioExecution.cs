using System.Globalization;

namespace ShowroomBot.Core.Scenarios;

/// <summary>Per-run cancellation, logging, and a shared gate for native automation actions.</summary>
public sealed class ScenarioExecution : IDisposable
{
    private static readonly AsyncLocal<ScenarioExecution?> Local = new();
    private static readonly object ActionGate = new();
    private static ScenarioExecution? _active;
    private readonly CancellationTokenSource _source;
    private readonly object _logGate = new();
    public static ScenarioExecution? Current => Local.Value;
    public CancellationToken Token { get; }
    public string DirectoryPath { get; }
    public string LogPath => Path.Combine(DirectoryPath, "scenario.log");

    public ScenarioExecution(CancellationToken cancellationToken)
    {
        DirectoryPath = Path.Combine(AppContext.BaseDirectory, "diagnostics",
            $"scenario-{DateTime.Now:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(DirectoryPath);
        _source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Token = _source.Token;
        lock (ActionGate)
        {
            if (_active is not null)
            {
                _source.Dispose();
                throw new InvalidOperationException("Другой сценарий уже выполняется.");
            }
            _active = this;
            Local.Value = this;
        }
    }

    public static void CancelCurrent()
    {
        // The token is marked immediately; callbacks run asynchronously so a keyboard hook
        // never waits for OCR/WinRT cancellation callbacks to finish.
        lock (ActionGate)
            if (_active is not null) _ = _active._source.CancelAsync();
    }

    public static void Perform(Action action)
    {
        lock (ActionGate)
        {
            Current?.Token.ThrowIfCancellationRequested();
            action();
        }
    }

    public static void CheckCancellation() => Current?.Token.ThrowIfCancellationRequested();

    public void Write(string message)
    {
        var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture);
        lock (_logGate) File.AppendAllText(LogPath, $"{timestamp} {message}{Environment.NewLine}");
    }

    public static void Log(string message) => Current?.Write(message);

    public void Dispose()
    {
        lock (ActionGate)
        {
            _source.Cancel();
            if (ReferenceEquals(_active, this)) _active = null;
            Local.Value = null;
            _source.Dispose();
        }
    }
}
