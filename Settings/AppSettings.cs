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
        [System.Configuration.DefaultSettingValue("True")]
        public bool GroupTexturesByType {
            get => (bool)this[nameof(GroupTexturesByType)];
            set => this[nameof(GroupTexturesByType)] = value;
        }

        /// <summary>
        /// If true, texture groups are collapsed by default. If false, groups are expanded.
        /// </summary>
        [System.Configuration.UserScopedSetting()]
        [System.Diagnostics.DebuggerNonUserCode()]
        [System.Configuration.DefaultSettingValue("True")]
        public bool CollapseTextureGroups {
            get => (bool)this[nameof(CollapseTextureGroups)];
            set => this[nameof(CollapseTextureGroups)] = value;
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
        [System.Configuration.DefaultSettingValue("1.0")]
        public double ServerTableScale {
            get => (double)this[nameof(ServerTableScale)];
            set => this[nameof(ServerTableScale)] = value;
        }

        [System.Configuration.UserScopedSetting()]
        [System.Diagnostics.DebuggerNonUserCode()]
        [System.Configuration.DefaultSettingValue("400")]
        public double ModelPreviewRowHeight {
            get => (double)this[nameof(ModelPreviewRowHeight)];
            set => this[nameof(ModelPreviewRowHeight)] = value;
        }

        /// <summary>
        /// Ширины столбцов таблицы текстур в формате "width1,width2,width3,..."
        /// </summary>
        [System.Configuration.UserScopedSetting()]
        [System.Diagnostics.DebuggerNonUserCode()]
        [System.Configuration.DefaultSettingValue("")]
        public string TexturesColumnWidths {
            get => (string)this[nameof(TexturesColumnWidths)];
            set => this[nameof(TexturesColumnWidths)] = value;
        }

        /// <summary>
        /// Видимость столбцов таблицы текстур в формате "1,1,1,0,1,..." (1=visible, 0=hidden)
        /// </summary>
        [System.Configuration.UserScopedSetting()]
        [System.Diagnostics.DebuggerNonUserCode()]
        [System.Configuration.DefaultSettingValue("")]
        public string TexturesColumnVisibility {
            get => (string)this[nameof(TexturesColumnVisibility)];
            set => this[nameof(TexturesColumnVisibility)] = value;
        }

        /// <summary>
        /// Видимость столбцов таблицы моделей
        /// </summary>
        [System.Configuration.UserScopedSetting()]
        [System.Diagnostics.DebuggerNonUserCode()]
        [System.Configuration.DefaultSettingValue("")]
        public string ModelsColumnVisibility {
            get => (string)this[nameof(ModelsColumnVisibility)];
            set => this[nameof(ModelsColumnVisibility)] = value;
        }

        /// <summary>
        /// Видимость столбцов таблицы материалов
        /// </summary>
        [System.Configuration.UserScopedSetting()]
        [System.Diagnostics.DebuggerNonUserCode()]
        [System.Configuration.DefaultSettingValue("")]
        public string MaterialsColumnVisibility {
            get => (string)this[nameof(MaterialsColumnVisibility)];
            set => this[nameof(MaterialsColumnVisibility)] = value;
        }

        /// <summary>
        /// Ширины столбцов таблицы моделей в формате "width1,width2,width3,..."
        /// </summary>
        [System.Configuration.UserScopedSetting()]
        [System.Diagnostics.DebuggerNonUserCode()]
        [System.Configuration.DefaultSettingValue("")]
        public string ModelsColumnWidths {
            get => (string)this[nameof(ModelsColumnWidths)];
            set => this[nameof(ModelsColumnWidths)] = value;
        }

        /// <summary>
        /// Ширины столбцов таблицы материалов в формате "width1,width2,width3,..."
        /// </summary>
        [System.Configuration.UserScopedSetting()]
        [System.Diagnostics.DebuggerNonUserCode()]
        [System.Configuration.DefaultSettingValue("")]
        public string MaterialsColumnWidths {
            get => (string)this[nameof(MaterialsColumnWidths)];
            set => this[nameof(MaterialsColumnWidths)] = value;
        }

        /// <summary>
        /// Порядок столбцов таблицы текстур в формате "displayIndex1,displayIndex2,..."
        /// </summary>
        [System.Configuration.UserScopedSetting()]
        [System.Diagnostics.DebuggerNonUserCode()]
        [System.Configuration.DefaultSettingValue("")]
        public string TexturesColumnOrder {
            get => (string)this[nameof(TexturesColumnOrder)];
            set => this[nameof(TexturesColumnOrder)] = value;
        }

        /// <summary>
        /// Порядок столбцов таблицы моделей в формате "displayIndex1,displayIndex2,..."
        /// </summary>
        [System.Configuration.UserScopedSetting()]
        [System.Diagnostics.DebuggerNonUserCode()]
        [System.Configuration.DefaultSettingValue("")]
        public string ModelsColumnOrder {
            get => (string)this[nameof(ModelsColumnOrder)];
            set => this[nameof(ModelsColumnOrder)] = value;
        }

        /// <summary>
        /// Порядок столбцов таблицы материалов в формате "displayIndex1,displayIndex2,..."
        /// </summary>
        [System.Configuration.UserScopedSetting()]
        [System.Diagnostics.DebuggerNonUserCode()]
        [System.Configuration.DefaultSettingValue("")]
        public string MaterialsColumnOrder {
            get => (string)this[nameof(MaterialsColumnOrder)];
            set => this[nameof(MaterialsColumnOrder)] = value;
        }

        public bool TryGetDecryptedPlaycanvasApiKey(out string? apiKey) {
            bool success = SecureStorageHelper.TryUnprotect(
                (string)this[nameof(PlaycanvasApiKey)],
                out apiKey,
                out bool wasProtected);

            if (success && !string.IsNullOrEmpty(apiKey) && !wasProtected) {
                // Attempt to migrate legacy plaintext API key to encrypted storage
                try {
                    this[nameof(PlaycanvasApiKey)] = SecureStorageHelper.Protect(apiKey);
                    Save();
                } catch (InvalidOperationException) {
                    // Master password not set on Linux/macOS - cannot encrypt yet
                    // Continue with plaintext key and let user configure later
                } catch (CryptographicException) {
                    // Encryption failed - continue with plaintext key
                }
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

        /// <summary>
        /// Ширина правой панели (Preview/Settings)
        /// </summary>
        [System.Configuration.UserScopedSetting()]
        [System.Diagnostics.DebuggerNonUserCode()]
        [System.Configuration.DefaultSettingValue("350")]
        public double RightPanelWidth {
            get => (double)this[nameof(RightPanelWidth)];
            set => this[nameof(RightPanelWidth)] = value;
        }

        /// <summary>
        /// Предыдущая ширина правой панели (для восстановления после скрытия)
        /// </summary>
        [System.Configuration.UserScopedSetting()]
        [System.Diagnostics.DebuggerNonUserCode()]
        [System.Configuration.DefaultSettingValue("350")]
        public double RightPanelPreviousWidth {
            get => (double)this[nameof(RightPanelPreviousWidth)];
            set => this[nameof(RightPanelPreviousWidth)] = value;
        }

        #region B2/CDN Upload Settings

        /// <summary>
        /// Backblaze B2 Application Key ID
        /// </summary>
        [System.Configuration.UserScopedSetting()]
        [System.Diagnostics.DebuggerNonUserCode()]
        [System.Configuration.DefaultSettingValue("")]
        public string B2KeyId {
            get => (string)this[nameof(B2KeyId)];
            set => this[nameof(B2KeyId)] = value;
        }

        /// <summary>
        /// Backblaze B2 Application Key (encrypted)
        /// </summary>
        [System.Configuration.UserScopedSetting()]
        [System.Diagnostics.DebuggerNonUserCode()]
        [System.Configuration.DefaultSettingValue("")]
        public string B2ApplicationKey {
            get => (string)this[nameof(B2ApplicationKey)];
            set {
                if (string.IsNullOrEmpty(value)) {
                    this[nameof(B2ApplicationKey)] = string.Empty;
                } else {
                    this[nameof(B2ApplicationKey)] = SecureStorageHelper.Protect(value);
                }
            }
        }

        /// <summary>
        /// Backblaze B2 Bucket Name
        /// </summary>
        [System.Configuration.UserScopedSetting()]
        [System.Diagnostics.DebuggerNonUserCode()]
        [System.Configuration.DefaultSettingValue("")]
        public string B2BucketName {
            get => (string)this[nameof(B2BucketName)];
            set => this[nameof(B2BucketName)] = value;
        }

        /// <summary>
        /// Backblaze B2 Bucket ID
        /// </summary>
        [System.Configuration.UserScopedSetting()]
        [System.Diagnostics.DebuggerNonUserCode()]
        [System.Configuration.DefaultSettingValue("")]
        public string B2BucketId {
            get => (string)this[nameof(B2BucketId)];
            set => this[nameof(B2BucketId)] = value;
        }

        /// <summary>
        /// CDN Base URL для доступа к файлам (например: https://cdn.example.com/project-name)
        /// </summary>
        [System.Configuration.UserScopedSetting()]
        [System.Diagnostics.DebuggerNonUserCode()]
        [System.Configuration.DefaultSettingValue("")]
        public string CdnBaseUrl {
            get => (string)this[nameof(CdnBaseUrl)];
            set => this[nameof(CdnBaseUrl)] = value;
        }

        /// <summary>
        /// Префикс пути в bucket (например: "projects/my-game")
        /// </summary>
        [System.Configuration.UserScopedSetting()]
        [System.Diagnostics.DebuggerNonUserCode()]
        [System.Configuration.DefaultSettingValue("")]
        public string B2PathPrefix {
            get => (string)this[nameof(B2PathPrefix)];
            set => this[nameof(B2PathPrefix)] = value;
        }

        /// <summary>
        /// Максимальное количество параллельных загрузок
        /// </summary>
        [System.Configuration.UserScopedSetting()]
        [System.Diagnostics.DebuggerNonUserCode()]
        [System.Configuration.DefaultSettingValue("4")]
        public int B2MaxConcurrentUploads {
            get => (int)this[nameof(B2MaxConcurrentUploads)];
            set => this[nameof(B2MaxConcurrentUploads)] = value;
        }

        /// <summary>
        /// Автоматически загружать mapping.json после генерации
        /// </summary>
        [System.Configuration.UserScopedSetting()]
        [System.Diagnostics.DebuggerNonUserCode()]
        [System.Configuration.DefaultSettingValue("False")]
        public bool B2AutoUploadMapping {
            get => (bool)this[nameof(B2AutoUploadMapping)];
            set => this[nameof(B2AutoUploadMapping)] = value;
        }

        public bool TryGetDecryptedB2ApplicationKey(out string? applicationKey) {
            bool success = SecureStorageHelper.TryUnprotect(
                (string)this[nameof(B2ApplicationKey)],
                out applicationKey,
                out bool wasProtected);

            if (success && !string.IsNullOrEmpty(applicationKey) && !wasProtected) {
                try {
                    this[nameof(B2ApplicationKey)] = SecureStorageHelper.Protect(applicationKey);
                    Save();
                } catch (InvalidOperationException) {
                    // Master password not set - continue with plaintext
                } catch (CryptographicException) {
                    // Encryption failed - continue with plaintext
                }
            }

            return success;
        }

        public string? GetDecryptedB2ApplicationKey() {
            if (!TryGetDecryptedB2ApplicationKey(out string? applicationKey)) {
                throw new CryptographicException("Stored B2 Application Key could not be decrypted.");
            }
            return applicationKey;
        }

        public bool HasStoredB2Credentials =>
            !string.IsNullOrEmpty(B2KeyId) &&
            !string.IsNullOrEmpty((string)this[nameof(B2ApplicationKey)]) &&
            !string.IsNullOrEmpty(B2BucketName);

        #endregion
    }
}
