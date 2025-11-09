using AssetProcessor.Resources;
using AssetProcessor.ViewModels;
using System.Collections.ObjectModel;
using System.Linq;
using Xunit;

namespace AssetProcessor.Tests;

public class MainViewModelTests : IClassFixture<MainViewModelFixture> {
    private readonly MainViewModelFixture fixture;

    public MainViewModelTests(MainViewModelFixture fixture) {
        this.fixture = fixture;
    }

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

    private MainViewModel CreateViewModelWithTextures() {
        var viewModel = fixture.CreateMainViewModel();
        viewModel.Textures = new ObservableCollection<TextureResource> {
            new() { ID = 1, Name = "Diffuse" },
            new() { ID = 2, Name = "Normal" },
            new() { ID = 3, Name = "Gloss" },
            new() { ID = 4, Name = "Unused" }
        };

        return viewModel;
    }
}
