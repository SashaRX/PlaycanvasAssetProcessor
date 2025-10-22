namespace AssetProcessor.Exceptions {
    /// <summary>
    /// Exception thrown when PlayCanvas API returns an error or unexpected response
    /// </summary>
    public class PlayCanvasApiException : Exception {
        public string? ApiEndpoint { get; }
        public int? StatusCode { get; }

        public PlayCanvasApiException() { }

        public PlayCanvasApiException(string message) : base(message) { }

        public PlayCanvasApiException(string message, Exception innerException)
            : base(message, innerException) { }

        public PlayCanvasApiException(string message, string apiEndpoint, int? statusCode = null)
            : base(message) {
            ApiEndpoint = apiEndpoint;
            StatusCode = statusCode;
        }

        public override string ToString() {
            var baseMessage = base.ToString();
            if (ApiEndpoint != null) {
                baseMessage += $"\nAPI Endpoint: {ApiEndpoint}";
            }
            if (StatusCode.HasValue) {
                baseMessage += $"\nStatus Code: {StatusCode}";
            }
            return baseMessage;
        }
    }
}
