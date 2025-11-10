using AssetProcessor.Helpers;
using System.Security.Cryptography;

namespace AssetProcessor.Settings {
    public sealed partial class AppSettings : System.Configuration.ApplicationSettingsBase {
        public static AppSettings Default { get; } = (AppSettings)Synchronized(new AppSettings());

        [System.Configuration.UserScopedSetting()]
        [System.Diagnostics.DebuggerNonUserCode()]
        [System.Configuration.DefaultSettingValue("")]
        public string UserName {
            get => (string)this[nameof(UserName)];
            set => this[nameof(UserName)] = value;
        }

        [System.Configuration.UserScopedSetting()]
        [System.Diagnostics.DebuggerNonUserCode()]
        [System.Configuration.DefaultSettingValue("")]
        public string ProjectsFolderPath {
            get => (string)this[nameof(ProjectsFolderPath)];
            set => this[nameof(ProjectsFolderPath)] = value;
        }

        [System.Configuration.UserScopedSetting()]
        [System.Diagnostics.DebuggerNonUserCode()]
        [System.Configuration.DefaultSettingValue("default_project_id")]
        public string ProjectId {
            get => (string)this[nameof(ProjectId)];
            set => this[nameof(ProjectId)] = value;
        }

        [System.Configuration.UserScopedSetting()]
        [System.Diagnostics.DebuggerNonUserCode()]
        [System.Configuration.DefaultSettingValue("default_branch_id")]
        public string BranchId {
            get => (string)this[nameof(BranchId)];
            set => this[nameof(BranchId)] = value;
        }

        [System.Configuration.UserScopedSetting()]
        [System.Diagnostics.DebuggerNonUserCode()]
        [System.Configuration.DefaultSettingValue("")]
        public string PlaycanvasApiKey {
            get => (string)this[nameof(PlaycanvasApiKey)];
            set {
                if (string.IsNullOrEmpty(value)) {
                    this[nameof(PlaycanvasApiKey)] = string.Empty;
                } else {
                    this[nameof(PlaycanvasApiKey)] = SecureStorageHelper.Protect(value);
                }
            }
        }

        [System.Configuration.UserScopedSetting()]
        [System.Diagnostics.DebuggerNonUserCode()]
        [System.Configuration.DefaultSettingValue("https://default.url")]
        public string BaseUrl {
            get => (string)this[nameof(BaseUrl)];
            set => this[nameof(BaseUrl)] = value;
        }

        [System.Configuration.UserScopedSetting()]
        [System.Diagnostics.DebuggerNonUserCode()]
        [System.Configuration.DefaultSettingValue("5")]
        public int SemaphoreLimit {
            get => (int)this[nameof(SemaphoreLimit)];
            set => this[nameof(SemaphoreLimit)] = value;
        }

        [System.Configuration.UserScopedSetting()]
        [System.Diagnostics.DebuggerNonUserCode()]
        [System.Configuration.DefaultSettingValue("5")]
        public int DownloadSemaphoreLimit {
            get => (int)this[nameof(DownloadSemaphoreLimit)];
            set => this[nameof(DownloadSemaphoreLimit)] = value;
        }

        [System.Configuration.UserScopedSetting()]
        [System.Diagnostics.DebuggerNonUserCode()]
        [System.Configuration.DefaultSettingValue("5")]
        public int GetTexturesSemaphoreLimit {
            get => (int)this[nameof(GetTexturesSemaphoreLimit)];
            set => this[nameof(GetTexturesSemaphoreLimit)] = value;
        }

        [System.Configuration.UserScopedSetting()]
        [System.Diagnostics.DebuggerNonUserCode()]
        [System.Configuration.DefaultSettingValue("")]
        public string LastSelectedProjectId {
            get => (string)this[nameof(LastSelectedProjectId)];
            set => this[nameof(LastSelectedProjectId)] = value;
        }

        [System.Configuration.UserScopedSetting()]
        [System.Diagnostics.DebuggerNonUserCode()]
        [System.Configuration.DefaultSettingValue("")]
        public string LastSelectedBranchId {
            get => (string)this[nameof(LastSelectedBranchId)];
            set => this[nameof(LastSelectedBranchId)] = value;
        }

        [System.Configuration.UserScopedSetting()]
        [System.Diagnostics.DebuggerNonUserCode()]
        [System.Configuration.DefaultSettingValue("")]
        public string LastSelectedBranchName {
            get => (string)this[nameof(LastSelectedBranchName)];
            set => this[nameof(LastSelectedBranchName)] = value;
        }

        [System.Configuration.UserScopedSetting()]
        [System.Diagnostics.DebuggerNonUserCode()]
        [System.Configuration.DefaultSettingValue("True")]
        public bool UseD3D11Preview {
            get => (bool)this[nameof(UseD3D11Preview)];
            set => this[nameof(UseD3D11Preview)] = value;
        }

        [System.Configuration.UserScopedSetting()]
        [System.Diagnostics.DebuggerNonUserCode()]
        [System.Configuration.DefaultSettingValue("True")]
        public bool UseD3D11NativeKtx2 {
            get => (bool)this[nameof(UseD3D11NativeKtx2)];
            set => this[nameof(UseD3D11NativeKtx2)] = value;
        }

        [System.Configuration.UserScopedSetting()]
        [System.Diagnostics.DebuggerNonUserCode()]
        [System.Configuration.DefaultSettingValue("True")]
        public bool HistogramCorrectionEnabled {
            get => (bool)this[nameof(HistogramCorrectionEnabled)];
            set => this[nameof(HistogramCorrectionEnabled)] = value;
        }

        [System.Configuration.UserScopedSetting()]
        [System.Diagnostics.DebuggerNonUserCode()]
        [System.Configuration.DefaultSettingValue("1.0")]
        public double TexturesTableScale {
            get => (double)this[nameof(TexturesTableScale)];
            set => this[nameof(TexturesTableScale)] = value;
        }

        [System.Configuration.UserScopedSetting()]
        [System.Diagnostics.DebuggerNonUserCode()]
        [System.Configuration.DefaultSettingValue("1.0")]
        public double ModelsTableScale {
            get => (double)this[nameof(ModelsTableScale)];
            set => this[nameof(ModelsTableScale)] = value;
        }

        [System.Configuration.UserScopedSetting()]
        [System.Diagnostics.DebuggerNonUserCode()]
        [System.Configuration.DefaultSettingValue("1.0")]
        public double MaterialsTableScale {
            get => (double)this[nameof(MaterialsTableScale)];
            set => this[nameof(MaterialsTableScale)] = value;
        }

        [System.Configuration.UserScopedSetting()]
        [System.Diagnostics.DebuggerNonUserCode()]
        [System.Configuration.DefaultSettingValue("1.0")]
        public double LogsTableScale {
            get => (double)this[nameof(LogsTableScale)];
            set => this[nameof(LogsTableScale)] = value;
        }

        [System.Configuration.UserScopedSetting()]
        [System.Diagnostics.DebuggerNonUserCode()]
        [System.Configuration.DefaultSettingValue("400")]
        public double ModelPreviewRowHeight {
            get => (double)this[nameof(ModelPreviewRowHeight)];
            set => this[nameof(ModelPreviewRowHeight)] = value;
        }

        public bool TryGetDecryptedPlaycanvasApiKey(out string? apiKey) {
            bool success = SecureStorageHelper.TryUnprotect(
                (string)this[nameof(PlaycanvasApiKey)],
                out apiKey,
                out bool wasProtected);

            if (success && !string.IsNullOrEmpty(apiKey) && !wasProtected) {
                this[nameof(PlaycanvasApiKey)] = SecureStorageHelper.Protect(apiKey);
                Save();
            }

            return success;
        }

        public string? GetDecryptedPlaycanvasApiKey() {
            if (!TryGetDecryptedPlaycanvasApiKey(out string? apiKey)) {
                throw new CryptographicException("Stored API key could not be decrypted. It may be corrupted or protected with an invalid master password.");
            }

            return apiKey;
        }

        public bool HasStoredPlaycanvasApiKey => !string.IsNullOrEmpty((string)this[nameof(PlaycanvasApiKey)]);
    }
}
