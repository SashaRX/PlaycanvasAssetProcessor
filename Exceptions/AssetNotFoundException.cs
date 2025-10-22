namespace AssetProcessor.Exceptions {
    /// <summary>
    /// Exception thrown when a requested asset cannot be found in PlayCanvas project
    /// </summary>
    public class AssetNotFoundException : Exception {
        public string? AssetId { get; }
        public string? AssetName { get; }
        public string? AssetType { get; }

        public AssetNotFoundException() { }

        public AssetNotFoundException(string message) : base(message) { }

        public AssetNotFoundException(string message, Exception innerException)
            : base(message, innerException) { }

        public AssetNotFoundException(string message, string? assetId, string? assetName = null, string? assetType = null)
            : base(message) {
            AssetId = assetId;
            AssetName = assetName;
            AssetType = assetType;
        }

        public override string ToString() {
            var baseMessage = base.ToString();
            if (AssetId != null) {
                baseMessage += $"\nAsset ID: {AssetId}";
            }
            if (AssetName != null) {
                baseMessage += $"\nAsset Name: {AssetName}";
            }
            if (AssetType != null) {
                baseMessage += $"\nAsset Type: {AssetType}";
            }
            return baseMessage;
        }
    }
}
