namespace AssetProcessor.Exceptions {
    /// <summary>
    /// Exception thrown when application configuration is invalid or missing required values
    /// </summary>
    public class InvalidConfigurationException : Exception {
        public string? ConfigKey { get; }
        public string? ConfigValue { get; }

        public InvalidConfigurationException() { }

        public InvalidConfigurationException(string message) : base(message) { }

        public InvalidConfigurationException(string message, Exception innerException)
            : base(message, innerException) { }

        public InvalidConfigurationException(string message, string? configKey, string? configValue = null)
            : base(message) {
            ConfigKey = configKey;
            ConfigValue = configValue;
        }

        public override string ToString() {
            var baseMessage = base.ToString();
            if (ConfigKey != null) {
                baseMessage += $"\nConfiguration Key: {ConfigKey}";
            }
            if (ConfigValue != null) {
                baseMessage += $"\nConfiguration Value: {ConfigValue}";
            }
            return baseMessage;
        }
    }
}
