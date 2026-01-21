using System;
using AssetProcessor.Resources;
using AssetProcessor.Services;
using AssetProcessor.Services.Models;
using AssetProcessor.ViewModels;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AssetProcessor.TextureConversion.Core;
using AssetProcessor.TextureConversion.Settings;
using AssetProcessor.Settings;
using AssetProcessor.MasterMaterials.Models;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AssetProcessor.Tests;

public class MainViewModelTests {
    [Fact]
    public void SettingSelectedMaterial_FiltersTexturesByMaterialMaps() {
        var viewModel = CreateViewModelWithTextures();

        var material = new MaterialResource {
            Name = "TestMaterial",
            DiffuseMapId = 2,
            NormalMapId = 3,
            GlossMapId = 999,
            EmissiveMapId = 1
        };

        viewModel.SelectedMaterial = material;

        var filteredIds = viewModel.FilteredTextures.Select(texture => texture.ID).ToList();

        Assert.Equal(new[] { 1, 2, 3 }, filteredIds.OrderBy(id => id));
        Assert.DoesNotContain(viewModel.Textures.First(texture => texture.ID == 4), viewModel.FilteredTextures);
    }

    [Fact]
    public async Task ProcessTexturesCommand_RaisesCompletionEvent() {
        var service = new RecordingTextureProcessingService();
        var viewModel = new MainViewModel(new FakePlayCanvasService(), service, new DummyLocalCacheService(), new TestProjectSyncService(), new TestAssetDownloadCoordinator(), new DummyProjectSelectionService(), CreateTextureSelectionViewModel(), CreateORMTextureViewModel(), CreateConversionSettingsViewModel(), CreateAssetLoadingViewModel(), CreateMaterialSelectionViewModel(), CreateMasterMaterialsViewModel()) {
            Textures = new ObservableCollection<TextureResource> {
                new() { Name = "Texture1", Path = "file.png" }
            }
        };

        viewModel.ConversionSettingsProvider = new StubSettingsProvider();
        viewModel.SelectedTexture = viewModel.Textures[0];

        TextureProcessingResult? capturedResult = null;
        viewModel.TextureProcessingCompleted += (_, e) => capturedResult = e.Result;

        var selection = new List<TextureResource> { viewModel.Textures[0] };
        await viewModel.ProcessTexturesCommand.ExecuteAsync(selection);

        Assert.True(service.Called);
        Assert.NotNull(capturedResult);
        Assert.Equal(1, capturedResult!.SuccessCount);
        Assert.Equal("Conversion completed. Success: 1, errors: 0.", viewModel.StatusMessage);
    }

    [Fact]
    public async Task DownloadAssetsCommand_UsesLocalCacheService() {
        string originalProjectsPath = AppSettings.Default.ProjectsFolderPath;
        var tempDir = Directory.CreateTempSubdirectory();
        try {
            AppSettings.Default.ProjectsFolderPath = tempDir.FullName;

            var localCache = new RecordingLocalCacheService();
            var projectSync = new TestProjectSyncService(localCache);
            var coordinator = new TestAssetDownloadCoordinator {
                ResultToReturn = new AssetDownloadResult(true, "Downloaded 1 assets. Failed: 0", new ResourceDownloadBatchResult(1, 0, 1))
            };

            var viewModel = new MainViewModel(new FakePlayCanvasService(), new FakeTextureProcessingService(), localCache, projectSync, coordinator, new DummyProjectSelectionService(), CreateTextureSelectionViewModel(), CreateORMTextureViewModel(), CreateConversionSettingsViewModel(), CreateAssetLoadingViewModel(), CreateMaterialSelectionViewModel(), CreateMasterMaterialsViewModel()) {
                ApiKey = "token",
                SelectedProjectId = "proj1",
                SelectedBranchId = "branch1",
                Projects = new ObservableCollection<KeyValuePair<string, string>> {
                    new("proj1", "1, Sample Project")
                },
                Assets = new ObservableCollection<BaseResource> {
                    new TextureResource { ID = 7, Name = "brick_albedo.png", Url = "https://example.com/tex.png", Status = "Ready" }
                }
            };

            await viewModel.DownloadAssetsCommand.ExecuteAsync(null);

            Assert.NotNull(coordinator.LastContext);
            Assert.Equal("Sample Project", coordinator.LastContext!.ProjectName);
            Assert.Single(coordinator.LastContext.Resources);
            Assert.Equal("Downloaded 1 assets. Failed: 0", viewModel.StatusMessage);
        } finally {
            AppSettings.Default.ProjectsFolderPath = originalProjectsPath;
            tempDir.Delete(true);
        }
    }

    [Fact]
    public void AutoDetectPresetsCommand_UpdatesStatusMessage() {
        var service = new RecordingTextureProcessingService();
        var viewModel = new MainViewModel(new FakePlayCanvasService(), service, new DummyLocalCacheService(), new TestProjectSyncService(), new TestAssetDownloadCoordinator(), new DummyProjectSelectionService(), CreateTextureSelectionViewModel(), CreateORMTextureViewModel(), CreateConversionSettingsViewModel(), CreateAssetLoadingViewModel(), CreateMaterialSelectionViewModel(), CreateMasterMaterialsViewModel()) {
            ConversionSettingsProvider = new StubSettingsProvider(),
            Textures = new ObservableCollection<TextureResource> {
                new() { Name = "rock_albedo.png", Path = "c:/tex/rock_albedo.png" }
            }
        };

        var selection = new List<TextureResource> { viewModel.Textures[0] };

        viewModel.AutoDetectPresetsCommand.Execute(selection);

        Assert.Equal("Auto-detect: found 1, not found 0.", viewModel.StatusMessage);
        Assert.Equal(1, service.AutoDetectCallCount);
        Assert.Single(service.LastAutoDetectTextures!);
        Assert.Equal(viewModel.Textures[0], service.LastAutoDetectTextures![0]);
    }

    [Fact]
    public void SettingSelectedMaterialToNull_ClearsFilteredTextures() {
        var viewModel = CreateViewModelWithTextures();
        viewModel.SelectedMaterial = new MaterialResource {
            DiffuseMapId = 1
        };
        Assert.NotEmpty(viewModel.FilteredTextures);

        viewModel.SelectedMaterial = null;

        Assert.Empty(viewModel.FilteredTextures);
    }

    private static MainViewModel CreateViewModelWithTextures() {
        var viewModel = new MainViewModel(new FakePlayCanvasService(), new FakeTextureProcessingService(), new DummyLocalCacheService(), new TestProjectSyncService(), new TestAssetDownloadCoordinator(), new DummyProjectSelectionService(), CreateTextureSelectionViewModel(), CreateORMTextureViewModel(), CreateConversionSettingsViewModel(), CreateAssetLoadingViewModel(), CreateMaterialSelectionViewModel(), CreateMasterMaterialsViewModel()) {
            Textures = new ObservableCollection<TextureResource> {
                new() { ID = 1, Name = "Diffuse" },
                new() { ID = 2, Name = "Normal" },
                new() { ID = 3, Name = "Gloss" },
                new() { ID = 4, Name = "Unused" }
            }
        };

        return viewModel;
    }

    private static TextureSelectionViewModel CreateTextureSelectionViewModel() {
        return new TextureSelectionViewModel(new DummyLogService());
    }

    private static ORMTextureViewModel CreateORMTextureViewModel() {
        return new ORMTextureViewModel(new DummyORMTextureService(), new DummyLogService());
    }

    private static TextureConversionSettingsViewModel CreateConversionSettingsViewModel() {
        return new TextureConversionSettingsViewModel(new DummyLogService());
    }

    private static AssetLoadingViewModel CreateAssetLoadingViewModel() {
        return new AssetLoadingViewModel(new DummyLogService(), new DummyAssetLoadCoordinator());
    }

    private static MaterialSelectionViewModel CreateMaterialSelectionViewModel() {
        return new MaterialSelectionViewModel(new DummyAssetResourceService(), new DummyLogService());
    }

    private static MasterMaterialsViewModel CreateMasterMaterialsViewModel() {
        return new MasterMaterialsViewModel(new DummyMasterMaterialService(), new DummyLogService());
    }

    private sealed class FakeTextureProcessingService : ITextureProcessingService {
        public TextureAutoDetectResult AutoDetectPresets(IEnumerable<TextureResource> textures, ITextureConversionSettingsProvider settingsProvider) =>
            new TextureAutoDetectResult { MatchedCount = 0, NotMatchedCount = 0 };

        public Task<TexturePreviewResult?> LoadKtxPreviewAsync(TextureResource texture, CancellationToken cancellationToken) =>
            Task.FromResult<TexturePreviewResult?>(null);

        public Task<TextureProcessingResult> ProcessTexturesAsync(TextureProcessingRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new TextureProcessingResult {
                SuccessCount = 0,
                ErrorCount = 0,
                ErrorMessages = Array.Empty<string>(),
                PreviewTexture = null,
                PreviewTexturePath = null
            });
    }

    private sealed class RecordingTextureProcessingService : ITextureProcessingService {
        public bool Called { get; private set; }
        public int AutoDetectCallCount { get; private set; }
        public List<TextureResource>? LastAutoDetectTextures { get; private set; }

        public TextureAutoDetectResult AutoDetectPresets(IEnumerable<TextureResource> textures, ITextureConversionSettingsProvider settingsProvider) {
            AutoDetectCallCount++;
            LastAutoDetectTextures = textures.ToList();
            int count = LastAutoDetectTextures.Count;
            return new TextureAutoDetectResult { MatchedCount = count, NotMatchedCount = 0 };
        }

        public Task<TexturePreviewResult?> LoadKtxPreviewAsync(TextureResource texture, CancellationToken cancellationToken) =>
            Task.FromResult<TexturePreviewResult?>(null);

        public Task<TextureProcessingResult> ProcessTexturesAsync(TextureProcessingRequest request, CancellationToken cancellationToken) {
            Called = true;
            return Task.FromResult(new TextureProcessingResult {
                SuccessCount = request.Textures.Count,
                ErrorCount = 0,
                ErrorMessages = Array.Empty<string>(),
                PreviewTexture = request.SelectedTexture,
                PreviewTexturePath = null
            });
        }
    }

    private sealed class TestAssetDownloadCoordinator : IAssetDownloadCoordinator {
        public AssetDownloadContext? LastContext { get; private set; }
        public AssetDownloadResult ResultToReturn { get; set; } = new(true, "OK", new ResourceDownloadBatchResult(0, 0, 0));

        public event EventHandler<ResourceStatusChangedEventArgs>? ResourceStatusChanged;

        public Task<AssetDownloadResult> DownloadAssetsAsync(AssetDownloadContext context, AssetDownloadOptions? options, CancellationToken cancellationToken) {
            LastContext = context;

            options?.ProgressCallback?.Invoke(new AssetDownloadProgress(0, context.Resources.Count, null));
            BaseResource? first = context.Resources.FirstOrDefault();
            if (first != null) {
                ResourceStatusChanged?.Invoke(this, new ResourceStatusChangedEventArgs(first, first.Status));
                options?.ResourceStatusCallback?.Invoke(first);
            }

            options?.ProgressCallback?.Invoke(new AssetDownloadProgress(context.Resources.Count, context.Resources.Count, first));
            return Task.FromResult(ResultToReturn);
        }
    }

    private sealed class RecordingLocalCacheService : ILocalCacheService {
        private readonly object gate = new();

        public List<BaseResource> DownloadedResources { get; } = new();

        public Task<ResourceDownloadResult> DownloadFileAsync(BaseResource resource, string apiKey, CancellationToken cancellationToken) {
            if (!string.IsNullOrEmpty(resource.Path)) {
                Directory.CreateDirectory(Path.GetDirectoryName(resource.Path)!);
                File.WriteAllText(resource.Path!, "data");
            }

            resource.Status = "Downloaded";

            lock (gate) {
                DownloadedResources.Add(resource);
            }

            return Task.FromResult(new ResourceDownloadResult(true, resource.Status!, 1));
        }

        public Task DownloadMaterialAsync(MaterialResource materialResource, Func<CancellationToken, Task<JObject>> fetchMaterialJsonAsync, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public string GetResourcePath(string projectsRoot, string projectName, IReadOnlyDictionary<int, string> folderPaths, string? fileName, int? parentId) =>
            Path.Combine(projectsRoot, projectName, LocalCacheService.AssetsDirectoryName, fileName ?? string.Empty);

        public Task<JArray?> LoadAssetsListAsync(string projectFolderPath, CancellationToken cancellationToken) =>
            Task.FromResult<JArray?>(null);

        public Task SaveAssetsListAsync(JToken jsonResponse, string projectFolderPath, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class StubSettingsProvider : ITextureConversionSettingsProvider {
        public CompressionSettingsData GetCompressionSettings() => new CompressionSettingsData();

        public HistogramSettings? GetHistogramSettings() => null;

        public bool SaveSeparateMipmaps => false;

        public ToksvigSettings GetToksvigSettings(string texturePath) => new ToksvigSettings();

        public string? PresetName => "TestPreset";
    }

    private sealed class FakePlayCanvasService : IPlayCanvasService {
        public Task<PlayCanvasAssetDetail> GetAssetByIdAsync(string assetId, string apiKey, CancellationToken cancellationToken) =>
            Task.FromResult(new PlayCanvasAssetDetail(0, string.Empty, null, default));

        public async IAsyncEnumerable<PlayCanvasAssetSummary> GetAssetsAsync(string projectId, string branchId, string apiKey, [EnumeratorCancellation] CancellationToken cancellationToken) {
            await Task.CompletedTask;
            yield break;
        }

        public Task<List<Branch>> GetBranchesAsync(string projectId, string apiKey, List<Branch> branches, CancellationToken cancellationToken) => Task.FromResult(new List<Branch>());

        public Task<Dictionary<string, string>> GetProjectsAsync(string? userId, string? apiKey, Dictionary<string, string> projects, CancellationToken cancellationToken) => Task.FromResult(new Dictionary<string, string>());

        public Task<string> GetUserIdAsync(string? username, string? apiKey, CancellationToken cancellationToken) => Task.FromResult("user");

        public Task<Branch> CreateBranchAsync(string projectId, string branchName, string apiKey, CancellationToken cancellationToken) =>
            Task.FromResult(new Branch { Id = "new-branch-id", Name = branchName });

        public void Dispose() {
            // No resources to dispose in fake implementation
        }
    }

    private sealed class DummyLocalCacheService : ILocalCacheService {
        public Task<ResourceDownloadResult> DownloadFileAsync(BaseResource resource, string apiKey, CancellationToken cancellationToken) =>
            Task.FromResult(new ResourceDownloadResult(true, "Downloaded", 1));

        public Task DownloadMaterialAsync(MaterialResource materialResource, Func<CancellationToken, Task<JObject>> fetchMaterialJsonAsync, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public string GetResourcePath(string projectsRoot, string projectName, IReadOnlyDictionary<int, string> folderPaths, string? fileName, int? parentId) =>
            Path.Combine(projectsRoot, projectName, LocalCacheService.AssetsDirectoryName, fileName ?? string.Empty);

        public Task<JArray?> LoadAssetsListAsync(string projectFolderPath, CancellationToken cancellationToken) =>
            Task.FromResult<JArray?>(null);

        public Task SaveAssetsListAsync(JToken jsonResponse, string projectFolderPath, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class DummyLogService : ILogService {
        public void LogDebug(string message) { }
        public void LogInfo(string message) { }
        public void LogWarn(string message) { }
        public void LogError(string? message) { }
        public void LogError(string message, Exception ex) { }
    }

    private sealed class DummyORMTextureService : IORMTextureService {
        public ChannelPackingMode DetectPackingMode(TextureResource? ao, TextureResource? gloss, TextureResource? metallic) =>
            ChannelPackingMode.None;

        public TextureResource? FindTextureById(int? mapId, IEnumerable<TextureResource> textures) => null;

        public WorkflowResult DetectWorkflow(MaterialResource material, IEnumerable<TextureResource> textures) =>
            new WorkflowResult { IsMetalnessWorkflow = true, WorkflowInfo = "Test", MapTypeLabel = "Metallic" };

        public ORMCreationResult CreateORMFromMaterial(MaterialResource material, IEnumerable<TextureResource> textures) =>
            new ORMCreationResult { Success = false };

        public ORMTextureResource CreateEmptyORM(IEnumerable<TextureResource> existingTextures) =>
            new ORMTextureResource { Name = "test_orm" };

        public string GenerateORMName(string? materialName, ChannelPackingMode mode) => "test_orm";

        public string GetBaseMaterialName(string? materialName) => materialName ?? "test";
    }

    private sealed class DummyProjectSelectionService : IProjectSelectionService {
        public string? ProjectFolderPath { get; private set; }
        public string? ProjectName { get; private set; }
        public string? UserName { get; private set; }
        public string? UserId { get; private set; }
        public string? SelectedBranchId { get; private set; }
        public string? SelectedBranchName { get; private set; }
        public bool IsBranchInitializationInProgress { get; private set; }
        public bool IsProjectInitializationInProgress { get; private set; }

        public void InitializeProjectsFolder(string? projectsFolderPath) {
            ProjectFolderPath = projectsFolderPath;
        }

        public Task<ProjectSelectionResult> LoadProjectsAsync(string userName, string apiKey, string lastSelectedProjectId, CancellationToken cancellationToken) {
            UserName = userName;
            UserId = "user";
            return Task.FromResult(new ProjectSelectionResult(new Dictionary<string, string>(), lastSelectedProjectId, UserId!, userName));
        }

        public Task<BranchSelectionResult> LoadBranchesAsync(string projectId, string apiKey, string? lastSelectedBranchName, CancellationToken cancellationToken) {
            SelectedBranchName = lastSelectedBranchName;
            return Task.FromResult(new BranchSelectionResult(new List<Branch>(), SelectedBranchId));
        }

        public void UpdateProjectPath(string projectsRoot, KeyValuePair<string, string> selectedProject) {
            ProjectName = selectedProject.Value;
            ProjectFolderPath = Path.Combine(projectsRoot, selectedProject.Value);
        }

        public void SetProjectInitializationInProgress(bool value) {
            IsProjectInitializationInProgress = value;
        }

        public void UpdateSelectedBranch(Branch branch) {
            SelectedBranchId = branch.Id;
            SelectedBranchName = branch.Name;
            IsBranchInitializationInProgress = false;
        }
    }

    private sealed class DummyAssetLoadCoordinator : IAssetLoadCoordinator {
        public Task<AssetLoadResult> LoadAssetsFromJsonAsync(
            string projectFolderPath,
            string projectName,
            string projectsRoot,
            int projectId,
            IProgress<AssetLoadProgress>? progress,
            CancellationToken cancellationToken) {
            return Task.FromResult(new AssetLoadResult {
                Success = true,
                Error = null,
                Textures = new List<TextureResource>(),
                Models = new List<ModelResource>(),
                Materials = new List<MaterialResource>(),
                FolderPaths = new Dictionary<int, string>()
            });
        }

        public IReadOnlyList<ORMTextureResource> GenerateVirtualORMTextures(
            IEnumerable<TextureResource> textures,
            int projectId) {
            return new List<ORMTextureResource>();
        }

        public Task<IReadOnlyList<ORMTextureResource>> DetectExistingORMTexturesAsync(
            string projectFolderPath,
            IEnumerable<TextureResource> existingTextures,
            int projectId,
            CancellationToken cancellationToken) {
            return Task.FromResult<IReadOnlyList<ORMTextureResource>>(new List<ORMTextureResource>());
        }
    }

    private sealed class DummyAssetResourceService : IAssetResourceService {
        public void BuildFolderHierarchy(JArray assetsResponse, IDictionary<int, string> targetFolderPaths) { }

        public Task<AssetProcessingResult?> ProcessAssetAsync(
            JToken asset,
            AssetProcessingParameters parameters,
            CancellationToken cancellationToken) {
            return Task.FromResult<AssetProcessingResult?>(null);
        }

        public Task<MaterialResource?> LoadMaterialFromFileAsync(string filePath, CancellationToken cancellationToken) {
            return Task.FromResult<MaterialResource?>(null);
        }

        public Task SaveAssetsListAsync(JToken jsonResponse, string projectFolderPath, CancellationToken cancellationToken) {
            return Task.CompletedTask;
        }
    }

    private sealed class DummyMasterMaterialService : IMasterMaterialService {
        public Task<MasterMaterialsConfig> LoadConfigAsync(string projectFolderPath, CancellationToken ct = default) {
            return Task.FromResult(new MasterMaterialsConfig());
        }

        public Task SaveConfigAsync(string projectFolderPath, MasterMaterialsConfig config, CancellationToken ct = default) {
            return Task.CompletedTask;
        }

        public Task SaveMasterAsync(string projectFolderPath, MasterMaterial master, CancellationToken ct = default) {
            return Task.CompletedTask;
        }

        public Task<MasterMaterial?> LoadMasterAsync(string projectFolderPath, string masterName, CancellationToken ct = default) {
            return Task.FromResult<MasterMaterial?>(null);
        }

        public IEnumerable<MasterMaterial> GetAllMasters(MasterMaterialsConfig config) {
            return Enumerable.Empty<MasterMaterial>();
        }

        public MasterMaterial? GetMasterForMaterial(MasterMaterialsConfig config, int materialId) {
            return null;
        }

        public void SetMaterialMaster(MasterMaterialsConfig config, int materialId, string masterName) { }

        public void RemoveMaterialMaster(MasterMaterialsConfig config, int materialId) { }

        // New methods with masterName parameter
        public Task<ShaderChunk?> LoadChunkFromFileAsync(string projectFolderPath, string masterName, string chunkName, CancellationToken ct = default) {
            return Task.FromResult<ShaderChunk?>(null);
        }

        public Task SaveChunkToFileAsync(string projectFolderPath, string masterName, ShaderChunk chunk, CancellationToken ct = default) {
            return Task.CompletedTask;
        }

        public Task DeleteChunkFileAsync(string projectFolderPath, string masterName, string chunkName, CancellationToken ct = default) {
            return Task.CompletedTask;
        }

        public Task<string> GenerateConsolidatedChunksAsync(string projectFolderPath, MasterMaterial master, CancellationToken ct = default) {
            return Task.FromResult(string.Empty);
        }

        public Task AddChunkToMasterAsync(string projectFolderPath, MasterMaterial master, ShaderChunk chunk, CancellationToken ct = default) {
            return Task.CompletedTask;
        }

        public Task RemoveChunkFromMasterAsync(string projectFolderPath, MasterMaterial master, string chunkName, CancellationToken ct = default) {
            return Task.CompletedTask;
        }

        // Path methods
        public string GetMaterialsFolderPath(string projectFolderPath) => string.Empty;
        public string GetMasterFilePath(string projectFolderPath, string masterName) => string.Empty;
        public string GetChunksFolderPath(string projectFolderPath, string masterName) => string.Empty;
        public string GetChunkFilePath(string projectFolderPath, string masterName, string chunkName) => string.Empty;
        public string GetChunkServerPath(string masterName, string chunkName) => string.Empty;

        // Legacy methods (deprecated)
        [Obsolete]
        public string GetChunksFolderPath(string projectFolderPath) => string.Empty;

        [Obsolete]
        public Task<ShaderChunk?> LoadChunkFromFileAsync(string projectFolderPath, string chunkId, CancellationToken ct = default) {
            return Task.FromResult<ShaderChunk?>(null);
        }

        [Obsolete]
        public Task SaveChunkToFileAsync(string projectFolderPath, ShaderChunk chunk, CancellationToken ct = default) {
            return Task.CompletedTask;
        }

        [Obsolete]
        public Task DeleteChunkFileAsync(string projectFolderPath, string chunkId, CancellationToken ct = default) {
            return Task.CompletedTask;
        }
    }

    private sealed class TestProjectSyncService : IProjectSyncService {
        private readonly ILocalCacheService? localCache;

        public TestProjectSyncService(ILocalCacheService? localCache = null) {
            this.localCache = localCache;
        }

        public ProjectDownloadRequest? LastDownloadRequest { get; private set; }

        public ResourceDownloadBatchResult? OverrideDownloadResult { get; set; }

        public Task<ProjectSyncResult> SyncProjectAsync(ProjectSyncRequest request, IProgress<ProjectSyncProgress>? progress, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public async Task<ResourceDownloadBatchResult> DownloadAsync(ProjectDownloadRequest request, IProgress<ResourceDownloadProgress>? progress, CancellationToken cancellationToken) {
            LastDownloadRequest = request;

            if (localCache != null) {
                int completed = 0;
                int total = request.Resources.Count;

                foreach (BaseResource resource in request.Resources) {
                    resource.Path ??= BuildPath(resource, request.ProjectName, request.ProjectsRoot, request.FolderPaths);
                    await localCache.DownloadFileAsync(resource, request.ApiKey, cancellationToken);
                    completed++;
                    progress?.Report(new ResourceDownloadProgress(resource, completed, total));
                }

                return OverrideDownloadResult ?? new ResourceDownloadBatchResult(total, 0, total);
            }

            int count = request.Resources.Count;
            return OverrideDownloadResult ?? new ResourceDownloadBatchResult(count, 0, count);
        }

        public Task<ResourceDownloadResult> DownloadResourceAsync(ResourceDownloadContext context, CancellationToken cancellationToken) {
            if (localCache == null) {
                return Task.FromResult(new ResourceDownloadResult(true, "Skipped", 1));
            }

            context.Resource.Path ??= BuildPath(context.Resource, context.ProjectName, context.ProjectsRoot, context.FolderPaths);
            return localCache.DownloadFileAsync(context.Resource, context.ApiKey, cancellationToken);
        }

        public async Task<ResourceDownloadResult> DownloadMaterialByIdAsync(MaterialDownloadContext context, CancellationToken cancellationToken) {
            if (localCache == null) {
                return new ResourceDownloadResult(true, "Skipped", 1);
            }

            context.Resource.Path ??= BuildPath(context.Resource, context.ProjectName, context.ProjectsRoot, context.FolderPaths);
            await localCache.DownloadMaterialAsync(context.Resource, _ => Task.FromResult(new JObject()), cancellationToken);
            return new ResourceDownloadResult(true, "Material downloaded", 1);
        }

        public Task<ResourceDownloadResult> DownloadFileAsync(ResourceDownloadContext context, CancellationToken cancellationToken) =>
            DownloadResourceAsync(context, cancellationToken);

        private string BuildPath(BaseResource resource, string projectName, string projectsRoot, IReadOnlyDictionary<int, string> folderPaths) {
            if (localCache == null) {
                return resource.Path ?? string.Empty;
            }

            string fileName = string.IsNullOrEmpty(resource.Name) ? $"asset_{resource.ID}" : resource.Name!;
            return localCache.GetResourcePath(projectsRoot, projectName, folderPaths, fileName, resource.Parent);
        }
    }
}
