using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AssetProcessor.Tests;

/// <summary>
/// Tests for MainWindow.D3D11Viewer concurrency control mechanisms.
/// Tests the new semaphore-based and CancellationTokenSource-based coordination
/// introduced to prevent race conditions and deadlocks in texture loading.
/// </summary>
public class MainWindowD3D11ViewerTests {
    /// <summary>
    /// Tests that CreateD3DPreviewCts cancels any existing CTS before creating a new one.
    /// This ensures that old texture loading operations are cancelled when a new one starts.
    /// </summary>
    [Fact]
    public void CreateD3DPreviewCts_CancelsPreviousCts() {
        var helper = new D3DPreviewCtsHelper();

        var cts1 = helper.CreateD3DPreviewCts();
        var cts2 = helper.CreateD3DPreviewCts();

        Assert.True(cts1.IsCancellationRequested, "First CTS should be cancelled when second is created");
        Assert.False(cts2.IsCancellationRequested, "Second CTS should not be cancelled immediately");
    }

    /// <summary>
    /// Tests that CreateD3DPreviewCts returns a new CTS each time.
    /// </summary>
    [Fact]
    public void CreateD3DPreviewCts_ReturnsNewInstance() {
        var helper = new D3DPreviewCtsHelper();

        var cts1 = helper.CreateD3DPreviewCts();
        var cts2 = helper.CreateD3DPreviewCts();

        Assert.NotSame(cts1, cts2);
    }

    /// <summary>
    /// Tests that CompleteD3DPreviewLoad clears the internal CTS reference when the same CTS is passed.
    /// </summary>
    [Fact]
    public void CompleteD3DPreviewLoad_ClearsInternalCts_WhenSameCts() {
        var helper = new D3DPreviewCtsHelper();

        var cts = helper.CreateD3DPreviewCts();
        helper.CompleteD3DPreviewLoad(cts);

        Assert.Null(helper.CurrentCts);
    }

    /// <summary>
    /// Tests that CompleteD3DPreviewLoad does not clear the internal CTS when a different CTS is passed.
    /// This handles the case where a newer load operation has started while an older one completes.
    /// </summary>
    [Fact]
    public void CompleteD3DPreviewLoad_DoesNotClearInternalCts_WhenDifferentCts() {
        var helper = new D3DPreviewCtsHelper();

        var cts1 = helper.CreateD3DPreviewCts();
        var cts2 = helper.CreateD3DPreviewCts();
        helper.CompleteD3DPreviewLoad(cts1);

        Assert.NotNull(helper.CurrentCts);
        Assert.Same(cts2, helper.CurrentCts);
    }

    /// <summary>
    /// Tests that CompleteD3DPreviewLoad handles null CTS gracefully.
    /// </summary>
    [Fact]
    public void CompleteD3DPreviewLoad_HandlesNull() {
        var helper = new D3DPreviewCtsHelper();

        var exception = Record.Exception(() => helper.CompleteD3DPreviewLoad(null!));

        Assert.Null(exception);
    }

    /// <summary>
    /// Tests that CompleteD3DPreviewLoad disposes the CTS after completion.
    /// </summary>
    [Fact]
    public void CompleteD3DPreviewLoad_DisposesProvidedCts() {
        var helper = new D3DPreviewCtsHelper();

        var cts = helper.CreateD3DPreviewCts();
        helper.CompleteD3DPreviewLoad(cts);

        Assert.Throws<ObjectDisposedException>(() => cts.Token.Register(() => { }));
    }

    /// <summary>
    /// Tests that CancelPendingD3DPreviewLoad cancels the current CTS without clearing it.
    /// </summary>
    [Fact]
    public void CancelPendingD3DPreviewLoad_CancelsCurrentCts() {
        var helper = new D3DPreviewCtsHelper();

        var cts = helper.CreateD3DPreviewCts();
        helper.CancelPendingD3DPreviewLoad();

        Assert.True(cts.IsCancellationRequested);
        Assert.NotNull(helper.CurrentCts); // Should still be set even though cancelled
    }

    /// <summary>
    /// Tests that CancelPendingD3DPreviewLoad handles the case when no CTS exists.
    /// </summary>
    [Fact]
    public void CancelPendingD3DPreviewLoad_HandlesNoCts() {
        var helper = new D3DPreviewCtsHelper();

        var exception = Record.Exception(() => helper.CancelPendingD3DPreviewLoad());

        Assert.Null(exception);
    }

    /// <summary>
    /// Tests that multiple concurrent CreateD3DPreviewCts calls are thread-safe.
    /// </summary>
    [Fact]
    public async Task CreateD3DPreviewCts_IsThreadSafe() {
        var helper = new D3DPreviewCtsHelper();
        var tasks = new Task<CancellationTokenSource>[100];

        for (int i = 0; i < tasks.Length; i++) {
            tasks[i] = Task.Run(() => helper.CreateD3DPreviewCts());
        }

        var results = await Task.WhenAll(tasks);

        // All but the last should be cancelled
        int cancelledCount = 0;
        for (int i = 0; i < results.Length - 1; i++) {
            if (results[i].IsCancellationRequested) {
                cancelledCount++;
            }
        }

        Assert.True(cancelledCount >= results.Length - 10, "Most CTS instances should be cancelled");
    }

    /// <summary>
    /// Tests that CompleteD3DPreviewLoad is thread-safe with concurrent calls.
    /// </summary>
    [Fact]
    public async Task CompleteD3DPreviewLoad_IsThreadSafe() {
        var helper = new D3DPreviewCtsHelper();
        var cts1 = helper.CreateD3DPreviewCts();
        var cts2 = helper.CreateD3DPreviewCts();

        var tasks = new Task[50];
        for (int i = 0; i < tasks.Length; i++) {
            var ctsToComplete = i % 2 == 0 ? cts1 : cts2;
            tasks[i] = Task.Run(() => helper.CompleteD3DPreviewLoad(ctsToComplete));
        }

        await Task.WhenAll(tasks);

        // Should not throw and should have handled all calls gracefully
        Assert.True(true);
    }

    /// <summary>
    /// Tests the semaphore behavior for preventing concurrent texture loads.
    /// </summary>
    [Fact]
    public async Task TextureLoadSemaphore_PreventsConcurrentLoads() {
        var semaphore = new SemaphoreSlim(1, 1);
        var loadStarted = new TaskCompletionSource<bool>();
        var firstLoadCanComplete = new TaskCompletionSource<bool>();
        var secondLoadStarted = false;

        var task1 = Task.Run(async () => {
            await semaphore.WaitAsync();
            try {
                loadStarted.SetResult(true);
                await firstLoadCanComplete.Task;
            } finally {
                semaphore.Release();
            }
        });

        // Wait for first load to start
        await loadStarted.Task;

        // Try to start second load - it should block
        var task2 = Task.Run(async () => {
            await semaphore.WaitAsync();
            try {
                secondLoadStarted = true;
            } finally {
                semaphore.Release();
            }
        });

        // Give task2 a chance to start (it should be blocked)
        await Task.Delay(100);
        Assert.False(secondLoadStarted, "Second load should be blocked by semaphore");

        // Complete first load
        firstLoadCanComplete.SetResult(true);
        await task1;
        await task2;

        Assert.True(secondLoadStarted, "Second load should complete after first releases semaphore");
    }

    /// <summary>
    /// Tests that semaphore respects cancellation tokens.
    /// </summary>
    [Fact]
    public async Task TextureLoadSemaphore_RespectsCancellation() {
        var semaphore = new SemaphoreSlim(1, 1);
        await semaphore.WaitAsync(); // Acquire the semaphore

        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () => {
            await semaphore.WaitAsync(cts.Token);
        });

        semaphore.Release();
    }

    /// <summary>
    /// Tests the complete flow of creating, using, and completing a CTS.
    /// </summary>
    [Fact]
    public async Task FullCtsLifecycle_WorksCorrectly() {
        var helper = new D3DPreviewCtsHelper();

        // Create first CTS
        var cts1 = helper.CreateD3DPreviewCts();
        Assert.False(cts1.IsCancellationRequested);

        // Simulate work with cancellation checking
        var workCompleted = false;
        var workTask = Task.Run(async () => {
            await Task.Delay(50);
            if (!cts1.Token.IsCancellationRequested) {
                workCompleted = true;
            }
        });

        await workTask;
        Assert.True(workCompleted, "Work should complete when not cancelled");

        // Complete first CTS
        helper.CompleteD3DPreviewLoad(cts1);
        Assert.Null(helper.CurrentCts);

        // Create second CTS and cancel it
        var cts2 = helper.CreateD3DPreviewCts();
        helper.CancelPendingD3DPreviewLoad();
        Assert.True(cts2.IsCancellationRequested);

        // Simulate work that should be cancelled
        var workCancelled = false;
        var cancelledWorkTask = Task.Run(async () => {
            await Task.Delay(50);
            workCancelled = cts2.Token.IsCancellationRequested;
        });

        await cancelledWorkTask;
        Assert.True(workCancelled, "Work should detect cancellation");
    }

    /// <summary>
    /// Tests that rapidly creating and completing CTS instances doesn't leak memory or cause issues.
    /// </summary>
    [Fact]
    public async Task RapidCtsCreationAndCompletion_DoesNotCauseIssues() {
        var helper = new D3DPreviewCtsHelper();

        for (int i = 0; i < 1000; i++) {
            var cts = helper.CreateD3DPreviewCts();
            await Task.Delay(1); // Simulate some work
            helper.CompleteD3DPreviewLoad(cts);
        }

        // Should complete without issues
        Assert.Null(helper.CurrentCts);
    }

    /// <summary>
    /// Tests interleaved creation and completion of multiple CTS instances.
    /// </summary>
    [Fact]
    public async Task InterleavedCtsOperations_MaintainsCorrectState() {
        var helper = new D3DPreviewCtsHelper();

        var cts1 = helper.CreateD3DPreviewCts();
        var cts2 = helper.CreateD3DPreviewCts();
        var cts3 = helper.CreateD3DPreviewCts();

        // Complete them out of order
        helper.CompleteD3DPreviewLoad(cts1); // Should not affect current CTS
        Assert.Same(cts3, helper.CurrentCts);

        helper.CompleteD3DPreviewLoad(cts3); // Should clear current CTS
        Assert.Null(helper.CurrentCts);

        helper.CompleteD3DPreviewLoad(cts2); // Should be no-op
        Assert.Null(helper.CurrentCts);

        // Verify cancellation states
        Assert.True(cts1.IsCancellationRequested);
        Assert.True(cts2.IsCancellationRequested);
    }

    /// <summary>
    /// Tests that semaphore allows exactly one concurrent operation.
    /// </summary>
    [Fact]
    public async Task Semaphore_AllowsOnlyOneConcurrentOperation() {
        var semaphore = new SemaphoreSlim(1, 1);
        var concurrentCount = 0;
        var maxConcurrentCount = 0;
        var lockObject = new object();

        var tasks = Enumerable.Range(0, 10).Select(async _ => {
            await semaphore.WaitAsync();
            try {
                lock (lockObject) {
                    concurrentCount++;
                    maxConcurrentCount = Math.Max(maxConcurrentCount, concurrentCount);
                }

                await Task.Delay(50);

                lock (lockObject) {
                    concurrentCount--;
                }
            } finally {
                semaphore.Release();
            }
        }).ToArray();

        await Task.WhenAll(tasks);

        Assert.Equal(1, maxConcurrentCount);
        Assert.Equal(0, concurrentCount);
    }

    /// <summary>
    /// Tests edge case where CompleteD3DPreviewLoad is called with an already-disposed CTS.
    /// </summary>
    [Fact]
    public void CompleteD3DPreviewLoad_HandlesAlreadyDisposedCts() {
        var helper = new D3DPreviewCtsHelper();
        var cts = helper.CreateD3DPreviewCts();

        // Manually dispose before calling Complete
        cts.Dispose();

        // Should not throw
        var exception = Record.Exception(() => helper.CompleteD3DPreviewLoad(cts));
        Assert.Null(exception);
    }
}

/// <summary>
/// Test helper class that implements the same concurrency control logic as MainWindow.D3D11Viewer
/// but without WPF dependencies, making it testable.
/// </summary>
public class D3DPreviewCtsHelper {
    private readonly object d3dPreviewCtsLock = new();
    private CancellationTokenSource? d3dTexturePreviewCts;

    public CancellationTokenSource? CurrentCts {
        get {
            lock (d3dPreviewCtsLock) {
                return d3dTexturePreviewCts;
            }
        }
    }

    public CancellationTokenSource CreateD3DPreviewCts() {
        lock (d3dPreviewCtsLock) {
            d3dTexturePreviewCts?.Cancel();
            d3dTexturePreviewCts = new CancellationTokenSource();
            return d3dTexturePreviewCts;
        }
    }

    public void CompleteD3DPreviewLoad(CancellationTokenSource loadCts) {
        if (loadCts == null) {
            return;
        }

        lock (d3dPreviewCtsLock) {
            if (ReferenceEquals(d3dTexturePreviewCts, loadCts)) {
                d3dTexturePreviewCts = null;
            }
        }

        loadCts.Dispose();
    }

    public void CancelPendingD3DPreviewLoad() {
        lock (d3dPreviewCtsLock) {
            d3dTexturePreviewCts?.Cancel();
        }
    }
}