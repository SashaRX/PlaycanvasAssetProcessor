namespace AssetProcessor.Exceptions {
    /// <summary>
    /// Exception thrown when network-related errors occur during API calls or downloads
    /// </summary>
    public class NetworkException : Exception {
        public string? Url { get; }
        public int RetryCount { get; }

        public NetworkException() { }

        public NetworkException(string message) : base(message) { }

        public NetworkException(string message, Exception innerException)
            : base(message, innerException) { }

        public NetworkException(string message, string url, int retryCount = 0)
            : base(message) {
            Url = url;
            RetryCount = retryCount;
        }

        public NetworkException(string message, string url, int retryCount, Exception innerException)
            : base(message, innerException) {
            Url = url;
            RetryCount = retryCount;
        }

        public override string ToString() {
            var baseMessage = base.ToString();
            if (Url != null) {
                baseMessage += $"\nURL: {Url}";
            }
            if (RetryCount > 0) {
                baseMessage += $"\nRetry Attempts: {RetryCount}";
            }
            return baseMessage;
        }
    }
}
