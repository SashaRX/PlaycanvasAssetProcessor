using AssetProcessor.Infrastructure.Enums;
using AssetProcessor.Resources;
using AssetProcessor.Services.Models;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AssetProcessor.Services;

/// <summary>
/// Реализация сервиса управления состоянием подключения к PlayCanvas.
/// </summary>
public class ConnectionStateService : IConnectionStateService {
    private static readonly Logger logger = LogManager.GetCurrentClassLogger();

    private readonly IProjectAssetService _projectAssetService;
    private readonly ILogService _logService;
    private ConnectionState _currentState = ConnectionState.Disconnected;

    public ConnectionState CurrentState => _currentState;

    public event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;

    public ConnectionStateService(IProjectAssetService projectAssetService, ILogService logService) {
        _projectAssetService = projectAssetService ?? throw new ArgumentNullException(nameof(projectAssetService));
        _logService = logService ?? throw new ArgumentNullException(nameof(logService));
    }

    public void SetState(ConnectionState newState) {
        if (_currentState == newState) {
            return;
        }

        var oldState = _currentState;
        _currentState = newState;

        logger.Info($"ConnectionState changed: {oldState} → {newState}");
        _logService.LogInfo($"Connection state changed to {newState}");

        StateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(oldState, newState));
    }

    public async Task<bool> CheckForUpdatesAsync(
        string projectFolderPath,
        string projectId,
        string branchId,
        string apiKey,
        CancellationToken cancellationToken) {
        try {
            if (string.IsNullOrEmpty(projectFolderPath) ||
                string.IsNullOrEmpty(projectId) ||
                string.IsNullOrEmpty(branchId) ||
                string.IsNullOrEmpty(apiKey)) {
                return false;
            }

            var context = new ProjectUpdateContext(projectFolderPath, projectId, branchId, apiKey);
            return await _projectAssetService.HasUpdatesAsync(context, cancellationToken);
        } catch (Exception ex) {
            _logService.LogError($"Error checking for updates: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Статусы, указывающие что файл отсутствует или повреждён и требует загрузки.
    /// Должен соответствовать AssetDownloadCoordinator.DownloadableStatuses.
    /// </summary>
    private static readonly HashSet<string> DownloadableStatuses = new(
        ["On Server", "Missing", "Error", "Size Mismatch", "Hash ERROR", "Corrupted", "Empty File"],
        StringComparer.OrdinalIgnoreCase);

    public bool HasMissingFiles<TTexture, TModel, TMaterial>(
        IEnumerable<TTexture> textures,
        IEnumerable<TModel> models,
        IEnumerable<TMaterial> materials)
        where TTexture : BaseResource
        where TModel : BaseResource
        where TMaterial : BaseResource {
        // Проверяем текстуры
        bool hasMissingTextures = textures.Any(t => NeedsDownload(t.Status));

        // Проверяем модели
        bool hasMissingModels = models.Any(m => NeedsDownload(m.Status));

        // Проверяем материалы
        bool hasMissingMaterials = materials.Any(m => NeedsDownload(m.Status));

        bool hasMissing = hasMissingTextures || hasMissingModels || hasMissingMaterials;

        if (hasMissing) {
            logger.Info($"HasMissingFiles: textures={hasMissingTextures}, models={hasMissingModels}, materials={hasMissingMaterials}");
        }

        return hasMissing;
    }

    /// <summary>
    /// Проверяет, требует ли статус загрузки файла.
    /// </summary>
    private static bool NeedsDownload(string? status) {
        if (string.IsNullOrEmpty(status)) return true;
        return DownloadableStatuses.Contains(status);
    }

    public ConnectionState DetermineState(bool hasUpdates, bool hasMissingFiles) {
        if (hasUpdates || hasMissingFiles) {
            return ConnectionState.NeedsDownload;
        }
        return ConnectionState.UpToDate;
    }

    public ConnectionButtonInfo GetButtonInfo(bool hasProjectSelection) {
        return _currentState switch {
            ConnectionState.Disconnected => new ConnectionButtonInfo {
                Content = "Connect",
                ToolTip = "Connect to PlayCanvas and load projects",
                IsEnabled = true,
                ColorR = 70,
                ColorG = 70,
                ColorB = 70 // Dark grey
            },

            ConnectionState.UpToDate => new ConnectionButtonInfo {
                Content = "Refresh",
                ToolTip = "Check for updates from PlayCanvas server",
                IsEnabled = hasProjectSelection,
                ColorR = 70,
                ColorG = 130,
                ColorB = 180 // Steel blue
            },

            ConnectionState.NeedsDownload => new ConnectionButtonInfo {
                Content = "Download",
                ToolTip = "Download assets from PlayCanvas (list + files)",
                IsEnabled = hasProjectSelection,
                ColorR = 60,
                ColorG = 150,
                ColorB = 60 // Dark green
            },

            _ => new ConnectionButtonInfo {
                Content = "Connect",
                ToolTip = "Connect to PlayCanvas",
                IsEnabled = true,
                ColorR = 70,
                ColorG = 70,
                ColorB = 70
            }
        };
    }
}
