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
        Assert.Equal(3, viewModel.FilteredTextures.Count);
        Assert.DoesNotContain(viewModel.Textures.First(texture => texture.ID == 4), viewModel.FilteredTextures);
    }

    [Fact]
    public async Task ProcessTexturesCommand_RaisesCompletionEvent() {
        var service = new RecordingTextureProcessingService();
        var viewModel = new MainViewModel(new FakePlayCanvasService(), service, new DummyLocalCacheService()) {
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
        Assert.Equal("Конвертация завершена. Успехов: 1, ошибок: 0.", viewModel.StatusMessage);
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
        var viewModel = new MainViewModel(new FakePlayCanvasService(), new FakeTextureProcessingService(), new DummyLocalCacheService()) {
            Textures = new ObservableCollection<TextureResource> {
                new() { ID = 1, Name = "Diffuse" },
                new() { ID = 2, Name = "Normal" },
                new() { ID = 3, Name = "Gloss" },
                new() { ID = 4, Name = "Unused" }
            }
        };

        return viewModel;
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

        public TextureAutoDetectResult AutoDetectPresets(IEnumerable<TextureResource> textures, ITextureConversionSettingsProvider settingsProvider) =>
            new TextureAutoDetectResult { MatchedCount = 0, NotMatchedCount = textures.Count() };

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
            Path.Combine(projectsRoot, projectName, fileName ?? string.Empty);

        public Task<JArray?> LoadAssetsListAsync(string projectFolderPath, CancellationToken cancellationToken) =>
            Task.FromResult<JArray?>(null);

        public string SanitizePath(string? path) => path ?? string.Empty;

        public Task SaveAssetsListAsync(JToken jsonResponse, string projectFolderPath, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
