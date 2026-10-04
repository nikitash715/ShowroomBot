using ShowroomBot.Windows;

namespace ShowroomBot.Core.Scenarios;

/// <summary>Requires a fresh execution signal before accepting stable results.</summary>
public sealed class ToolkitExecutionProgress(ToolkitConsoleLayout before, string? baseline, int requiredStablePolls)
{
    private bool _started;
    private bool _baselineErrorCleared;
    private int _errorStable;
    private string? _previousError;
    public bool HasConfirmedError { get; private set; }
    private int _stable;
    private string? _previous;

    public bool Observe(ToolkitConsoleLayout layout, string? fingerprint)
    {
        // A temporarily missed OCR/frame match is not evidence that execution began.
        var resultChanged =
            (layout.ExecutionStamp.Length > 0 && layout.ExecutionStamp != before.ExecutionStamp) ||
            (fingerprint != null && fingerprint != baseline) ||
            (layout.RowCount.HasValue && layout.RowCount != before.RowCount);
        _started |= layout.Busy || resultChanged;
        // Fresh results take precedence over stale or falsely recognized error text.
        var ready = _started && !layout.Busy && (layout.Error == null || resultChanged) && layout.RowCount.HasValue &&
            (layout.RowCount == 0 || layout.Result != null);
        var state = $"{layout.RowCount}:{layout.ExecutionStamp}:{fingerprint}";
        _stable = ready ? (state == _previous ? _stable + 1 : 1) : 0;
        _previous = state;
        _baselineErrorCleared |= layout.Error == null;
        var currentError = !layout.Busy && !ready && layout.Error != null &&
            (layout.Error != before.Error || _baselineErrorCleared);
        _errorStable = currentError ? (layout.Error == _previousError ? _errorStable + 1 : 1) : 0;
        _previousError = layout.Error;
        HasConfirmedError = _errorStable >= Math.Max(2, requiredStablePolls);
        return _stable >= requiredStablePolls;
    }
}
