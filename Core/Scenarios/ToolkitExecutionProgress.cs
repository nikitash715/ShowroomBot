using ShowroomBot.Windows;

namespace ShowroomBot.Core.Scenarios;

/// <summary>Requires a fresh execution signal before accepting stable results.</summary>
public sealed class ToolkitExecutionProgress(ToolkitConsoleLayout before, string? baseline, int requiredStablePolls)
{
    private bool _started;
    private int _stable;
    private string? _previous;

    public bool Observe(ToolkitConsoleLayout layout, string? fingerprint)
    {
        // A temporarily missed OCR/frame match is not evidence that execution began.
        _started |= layout.Busy ||
            (layout.ExecutionStamp.Length > 0 && layout.ExecutionStamp != before.ExecutionStamp) ||
            (fingerprint != null && fingerprint != baseline) ||
            (layout.RowCount.HasValue && layout.RowCount != before.RowCount);
        var ready = _started && !layout.Busy && layout.Error == null && layout.RowCount.HasValue &&
            (layout.RowCount == 0 || layout.Result != null);
        var state = $"{layout.RowCount}:{layout.ExecutionStamp}:{fingerprint}";
        _stable = ready ? (state == _previous ? _stable + 1 : 1) : 0;
        _previous = state;
        return _stable >= requiredStablePolls;
    }
}
