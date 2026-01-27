using AssetProcessor.Settings;
using NLog;

namespace AssetProcessor.Services {
    public class PlayCanvasCredentialsService : IPlayCanvasCredentialsService {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        private readonly AppSettings appSettings;

        public PlayCanvasCredentialsService(AppSettings appSettings) {
            this.appSettings = appSettings;
        }

        public bool HasStoredCredentials => !string.IsNullOrEmpty(appSettings.PlaycanvasApiKey)
            && !string.IsNullOrEmpty(appSettings.UserName);

        public string? Username => appSettings.UserName;

        public string? GetApiKeyOrNull() {
            if (TryGetApiKey(out string apiKey)) {
                return apiKey;
            }

            return null;
        }

        public bool TryGetApiKey(out string apiKey) {
            if (!appSettings.TryGetDecryptedPlaycanvasApiKey(out string? decryptedApiKey) ||
                string.IsNullOrEmpty(decryptedApiKey)) {
                logger.Error("Failed to decrypt PlayCanvas API key from settings.");
                apiKey = string.Empty;
                return false;
            }

            apiKey = decryptedApiKey;
            return true;
        }
    }
}
