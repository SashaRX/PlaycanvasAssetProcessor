using AssetProcessor.Resources;
using AssetProcessor.Services;
using AssetProcessor.ViewModels;
using Newtonsoft.Json.Linq;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
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
        var viewModel = new MainViewModel(new FakePlayCanvasService()) {
            Textures = new ObservableCollection<TextureResource> {
                new() { ID = 1, Name = "Diffuse" },
                new() { ID = 2, Name = "Normal" },
                new() { ID = 3, Name = "Gloss" },
                new() { ID = 4, Name = "Unused" }
            }
        };

        return viewModel;
    }

    private sealed class FakePlayCanvasService : IPlayCanvasService {
        public Task<JObject> GetAssetByIdAsync(string assetId, string apiKey, CancellationToken cancellationToken) => Task.FromResult(new JObject());

        public Task<JArray> GetAssetsAsync(string projectId, string branchId, string apiKey, CancellationToken cancellationToken) => Task.FromResult(new JArray());

        public Task<List<Branch>> GetBranchesAsync(string projectId, string apiKey, List<Branch> branches, CancellationToken cancellationToken) => Task.FromResult(new List<Branch>());

        public Task<Dictionary<string, string>> GetProjectsAsync(string? userId, string? apiKey, Dictionary<string, string> projects, CancellationToken cancellationToken) => Task.FromResult(new Dictionary<string, string>());

        public Task<string> GetUserIdAsync(string? username, string? apiKey, CancellationToken cancellationToken) => Task.FromResult("user");

        public void Dispose() {
            // No resources to dispose in fake implementation
        }
    }
}
