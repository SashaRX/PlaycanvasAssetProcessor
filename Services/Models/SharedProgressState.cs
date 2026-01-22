namespace AssetProcessor.Services.Models;

/// <summary>
/// Thread-safe shared progress state that can be written by background threads
/// and polled by UI thread without using SynchronizationContext.
/// This avoids the UI freeze caused by Progress&lt;T&gt; marshalling callbacks.
/// </summary>
public sealed class SharedProgressState {
    private volatile int _current;
    private volatile int _total;
    private volatile string? _currentAsset;
    private volatile bool _isComplete;

    /// <summary>
    /// Current number of processed items.
    /// </summary>
    public int Current => _current;

    /// <summary>
    /// Total number of items to process.
    /// </summary>
    public int Total => _total;

    /// <summary>
    /// Name of the currently processing asset.
    /// </summary>
    public string? CurrentAsset => _currentAsset;

    /// <summary>
    /// Whether processing is complete.
    /// </summary>
    public bool IsComplete => _isComplete;

    /// <summary>
    /// Updates the progress state. Safe to call from any thread.
    /// </summary>
    public void Update(int current, int total, string? currentAsset = null) {
        _current = current;
        _total = total;
        _currentAsset = currentAsset;
    }

    /// <summary>
    /// Increments the current count atomically. Safe to call from any thread.
    /// Returns the new current value.
    /// </summary>
    public int IncrementCurrent(string? currentAsset = null) {
        _currentAsset = currentAsset;
        return Interlocked.Increment(ref _current);
    }

    /// <summary>
    /// Sets the total count. Safe to call from any thread.
    /// </summary>
    public void SetTotal(int total) {
        _total = total;
    }

    /// <summary>
    /// Marks processing as complete. Safe to call from any thread.
    /// </summary>
    public void Complete() {
        _isComplete = true;
    }

    /// <summary>
    /// Resets the state for a new operation.
    /// </summary>
    public void Reset() {
        _current = 0;
        _total = 0;
        _currentAsset = null;
        _isComplete = false;
    }
}
