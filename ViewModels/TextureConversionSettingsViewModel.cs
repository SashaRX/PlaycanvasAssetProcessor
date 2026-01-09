using AssetProcessor.Resources;
using AssetProcessor.Services;
using AssetProcessor.TextureConversion.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NLog;
using System;

namespace AssetProcessor.ViewModels;

/// <summary>
/// ViewModel for texture conversion settings management.
/// Handles loading/saving settings from ResourceSettingsService and auto-detecting presets.
/// Raises events for UI to apply settings to the ConversionSettingsPanel.
/// </summary>
public partial class TextureConversionSettingsViewModel : ObservableObject {
    private static readonly Logger logger = LogManager.GetCurrentClassLogger();

    private readonly ILogService logService;
    private readonly PresetManager presetManager;

    #region Observable Properties

    [ObservableProperty]
    private TextureResource? currentTexture;

    [ObservableProperty]
    private bool isLoadingSettings;

    [ObservableProperty]
    private string? currentPresetName;

    [ObservableProperty]
    private bool hasUnsavedChanges;

    #endregion

    #region Events

    /// <summary>
    /// Raised when settings are loaded (from saved or auto-detected preset).
    /// UI should apply these settings to the ConversionSettingsPanel.
    /// </summary>
    public event EventHandler<SettingsLoadedEventArgs>? SettingsLoaded;

    /// <summary>
    /// Raised when settings are saved successfully.
    /// </summary>
    public event EventHandler<SettingsSavedEventArgs>? SettingsSaved;

    /// <summary>
    /// Raised when an error occurs during settings operations.
    /// </summary>
    public event EventHandler<SettingsErrorEventArgs>? ErrorOccurred;

    /// <summary>
    /// Raised when preset is auto-detected for a texture.
    /// </summary>
    public event EventHandler<PresetDetectedEventArgs>? PresetDetected;

    #endregion

    public TextureConversionSettingsViewModel(ILogService logService) {
        this.logService = logService ?? throw new ArgumentNullException(nameof(logService));
        this.presetManager = new PresetManager();
    }

    /// <summary>
    /// Loads settings for a texture. First checks for saved settings,
    /// then falls back to auto-detecting preset by filename.
    /// </summary>
    [RelayCommand]
    private void LoadSettingsForTexture(SettingsLoadRequest request) {
        if (request?.Texture == null) return;

        var texture = request.Texture;
        var projectId = request.ProjectId;

        logService.LogInfo($"[TextureConversionSettingsViewModel] LoadSettings START for: {texture.Name}");

        CurrentTexture = texture;
        IsLoadingSettings = true;
        HasUnsavedChanges = false;

        try {
            // First check for saved settings in ResourceSettingsService
            if (projectId > 0) {
                var savedSettings = ResourceSettingsService.Instance.GetTextureSettings(projectId, texture.ID);
                if (savedSettings != null) {
                    logService.LogInfo($"[TextureConversionSettingsViewModel] Found saved settings for texture {texture.Name} (ID={texture.ID})");
                    CurrentPresetName = savedSettings.PresetName;

                    SettingsLoaded?.Invoke(this, new SettingsLoadedEventArgs(
                        texture,
                        savedSettings,
                        SettingsSource.Saved
                    ));

                    logService.LogInfo($"[TextureConversionSettingsViewModel] LoadSettings END (loaded from saved) for: {texture.Name}");
                    return;
                }
            }

            // No saved settings - auto-detect preset by filename
            var matchedPreset = presetManager.FindPresetByFileName(texture.Name ?? "");
            logService.LogInfo($"[TextureConversionSettingsViewModel] PresetManager.FindPresetByFileName returned: {matchedPreset?.Name ?? "null"}");

            if (matchedPreset != null) {
                texture.PresetName = matchedPreset.Name;
                CurrentPresetName = matchedPreset.Name;
                logService.LogInfo($"Auto-detected preset '{matchedPreset.Name}' for texture {texture.Name}");

                PresetDetected?.Invoke(this, new PresetDetectedEventArgs(
                    texture,
                    matchedPreset.Name
                ));

                SettingsLoaded?.Invoke(this, new SettingsLoadedEventArgs(
                    texture,
                    null, // No saved settings, UI should apply preset
                    SettingsSource.AutoDetectedPreset,
                    matchedPreset.Name
                ));
            } else {
                // No preset matched - use default/Custom
                texture.PresetName = "";
                CurrentPresetName = null;
                logService.LogInfo($"No preset matched for '{texture.Name}', using Custom");

                SettingsLoaded?.Invoke(this, new SettingsLoadedEventArgs(
                    texture,
                    null,
                    SettingsSource.Default
                ));
            }

            logService.LogInfo($"[TextureConversionSettingsViewModel] LoadSettings END for: {texture.Name}");

        } catch (Exception ex) {
            logService.LogError($"Error loading settings for texture {texture.Name}: {ex.Message}");
            ErrorOccurred?.Invoke(this, new SettingsErrorEventArgs("Load Failed", ex.Message));
        } finally {
            IsLoadingSettings = false;
        }
    }

    /// <summary>
    /// Saves texture settings to ResourceSettingsService.
    /// Called by MainWindow when UI settings change.
    /// </summary>
    [RelayCommand]
    private void SaveSettings(SettingsSaveRequest request) {
        if (request?.Texture == null || request.Settings == null) return;

        var texture = request.Texture;
        var settings = request.Settings;
        var projectId = request.ProjectId;

        if (projectId <= 0) {
            logService.LogWarn("Cannot save settings - no valid project ID");
            return;
        }

        try {
            ResourceSettingsService.Instance.SaveTextureSettings(projectId, texture.ID, settings);
            HasUnsavedChanges = false;
            CurrentPresetName = settings.PresetName;

            logService.LogInfo($"Saved texture settings: {texture.Name} (ID={texture.ID})");

            SettingsSaved?.Invoke(this, new SettingsSavedEventArgs(texture, settings));

        } catch (Exception ex) {
            logService.LogError($"Error saving settings for texture {texture.Name}: {ex.Message}");
            ErrorOccurred?.Invoke(this, new SettingsErrorEventArgs("Save Failed", ex.Message));
        }
    }

    /// <summary>
    /// Auto-detects preset for a texture by filename without loading full settings.
    /// Used during batch initialization.
    /// </summary>
    public string? AutoDetectPreset(string textureName) {
        var matchedPreset = presetManager.FindPresetByFileName(textureName ?? "");
        return matchedPreset?.Name;
    }

    /// <summary>
    /// Marks that settings have been changed in UI.
    /// </summary>
    public void MarkAsChanged() {
        HasUnsavedChanges = true;
    }

    /// <summary>
    /// Clears current texture selection.
    /// </summary>
    public void ClearCurrentTexture() {
        CurrentTexture = null;
        CurrentPresetName = null;
        HasUnsavedChanges = false;
    }
}

#region Event Args and Request Types

/// <summary>
/// Request for loading settings
/// </summary>
public class SettingsLoadRequest {
    public TextureResource? Texture { get; init; }
    public int ProjectId { get; init; }
}

/// <summary>
/// Request for saving settings
/// </summary>
public class SettingsSaveRequest {
    public TextureResource? Texture { get; init; }
    public TextureSettings? Settings { get; init; }
    public int ProjectId { get; init; }
}

/// <summary>
/// Source of loaded settings
/// </summary>
public enum SettingsSource {
    Saved,
    AutoDetectedPreset,
    Default
}

/// <summary>
/// Event args when settings are loaded
/// </summary>
public class SettingsLoadedEventArgs : EventArgs {
    public TextureResource Texture { get; }
    public TextureSettings? Settings { get; }
    public SettingsSource Source { get; }
    public string? PresetName { get; }

    public SettingsLoadedEventArgs(
        TextureResource texture,
        TextureSettings? settings,
        SettingsSource source,
        string? presetName = null) {
        Texture = texture;
        Settings = settings;
        Source = source;
        PresetName = presetName;
    }
}

/// <summary>
/// Event args when settings are saved
/// </summary>
public class SettingsSavedEventArgs : EventArgs {
    public TextureResource Texture { get; }
    public TextureSettings Settings { get; }

    public SettingsSavedEventArgs(TextureResource texture, TextureSettings settings) {
        Texture = texture;
        Settings = settings;
    }
}

/// <summary>
/// Event args when a preset is auto-detected
/// </summary>
public class PresetDetectedEventArgs : EventArgs {
    public TextureResource Texture { get; }
    public string PresetName { get; }

    public PresetDetectedEventArgs(TextureResource texture, string presetName) {
        Texture = texture;
        PresetName = presetName;
    }
}

/// <summary>
/// Event args for errors during settings operations
/// </summary>
public class SettingsErrorEventArgs : EventArgs {
    public string Title { get; }
    public string Message { get; }

    public SettingsErrorEventArgs(string title, string message) {
        Title = title;
        Message = message;
    }
}

#endregion
