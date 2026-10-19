namespace ShowroomBot.Core.Scenarios;

public static class RemoteWindowSearch
{
    // Reverse Alt+Tab takes the MRU tail and moves it to the front. Repeating
    // that rotation visits every window before returning to the starting one.
    public static async Task<bool> FindAsync(int limit, Func<CancellationToken, Task> next,
        Func<int, CancellationToken, Task<bool>> inspect, CancellationToken token)
    {
        if (limit <= 0) throw new ArgumentOutOfRangeException(nameof(limit));
        for (var index = 0; index < limit; index++)
        {
            token.ThrowIfCancellationRequested();
            if (index > 0) await next(token);
            token.ThrowIfCancellationRequested();
            if (await inspect(index, token))
            {
                token.ThrowIfCancellationRequested();
                return true;
            }
        }
        return false;
    }
}
