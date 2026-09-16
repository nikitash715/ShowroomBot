namespace ShowroomBot.Core;

public sealed class DemoController
{
    public AppState State { get; private set; } = AppState.Waiting;
    public bool WasStartedAutomatically { get; private set; }

    public event EventHandler? StateChanged;

    public void StartDemo(bool automatic)
    {
        if (State == AppState.DemoRunning && WasStartedAutomatically == automatic)
        {
            return;
        }

        State = AppState.DemoRunning;
        WasStartedAutomatically = automatic;
        OnStateChanged();
    }

    public void StopDemo()
    {
        if (State == AppState.Waiting && !WasStartedAutomatically)
        {
            return;
        }

        State = AppState.Waiting;
        WasStartedAutomatically = false;
        OnStateChanged();
    }

    private void OnStateChanged()
    {
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
