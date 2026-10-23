namespace GitGraph.App.ViewModels;

/// <summary>
/// The background loads a view model starts without awaiting them (it shows each result as it arrives), so that
/// the snapshot hook and tests can wait until what is on screen is complete instead of guessing a delay.
/// </summary>
public sealed class PendingLoads
{
    private readonly List<Task> _tasks = [];

    /// <summary>Tracks a load and returns it, so the caller can still await it.</summary>
    public Task Track(Task load)
    {
        // Loads go on from thread-pool threads where there is no UI thread (tests).
        lock (_tasks)
        {
            _tasks.RemoveAll(t => t.IsCompleted);
            _tasks.Add(load);
        }
        return load;
    }

    /// <summary>Waits for every tracked load, including those started while waiting, to finish.</summary>
    public async Task WhenIdleAsync()
    {
        while (Pending() is { } pending)
        {
            // Each load reports its own errors; waiting only needs it to be over.
            try { await pending; }
            catch (Exception) { }
        }
    }

    private Task? Pending()
    {
        lock (_tasks) return _tasks.Find(t => !t.IsCompleted);
    }
}
