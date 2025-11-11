using System;
using System.Threading;

namespace AssetProcessor.Helpers {
    /// <summary>
    /// Throttles progress reports to reduce UI update frequency.
    /// Batches multiple Report() calls and flushes them at a fixed interval.
    /// Reduces Dispatcher.Invoke calls from thousands to dozens.
    /// </summary>
    /// <typeparam name="T">Progress value type (typically int)</typeparam>
    public sealed class ThrottledProgress<T> : IProgress<T>, IDisposable {
        private readonly IProgress<T> innerProgress;
        private readonly Timer timer;
        private int pendingReports;
        private bool disposed;

        /// <summary>
        /// Creates a throttled progress reporter
        /// </summary>
        /// <param name="innerProgress">Inner progress to report batched values to</param>
        /// <param name="intervalMs">Interval in milliseconds between batched reports (default: 100ms)</param>
        public ThrottledProgress(IProgress<T> innerProgress, int intervalMs = 100) {
            this.innerProgress = innerProgress ?? throw new ArgumentNullException(nameof(innerProgress));

            if (intervalMs <= 0) {
                throw new ArgumentOutOfRangeException(nameof(intervalMs), "Interval must be positive");
            }

            timer = new Timer(_ => Flush(), null, intervalMs, intervalMs);
        }

        /// <summary>
        /// Reports progress (batched, will be flushed at next interval)
        /// </summary>
        public void Report(T value) {
            if (disposed) {
                throw new ObjectDisposedException(nameof(ThrottledProgress<T>));
            }

            // Only support int for now (most common case)
            if (value is int count) {
                Interlocked.Add(ref pendingReports, count);
            } else {
                // For non-int types, report immediately
                innerProgress.Report(value);
            }
        }

        /// <summary>
        /// Flushes pending reports to inner progress
        /// </summary>
        private void Flush() {
            int current = Interlocked.Exchange(ref pendingReports, 0);
            if (current > 0) {
                innerProgress.Report((T)(object)current);
            }
        }

        /// <summary>
        /// Flushes any remaining reports and disposes timer
        /// </summary>
        public void Dispose() {
            if (disposed) {
                return;
            }

            disposed = true;
            timer?.Dispose();

            // Flush any remaining reports
            Flush();
        }
    }
}
