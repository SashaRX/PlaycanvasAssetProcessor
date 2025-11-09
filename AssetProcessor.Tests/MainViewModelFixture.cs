using AssetProcessor.Services;
using AssetProcessor.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AssetProcessor.Tests;

public sealed class MainViewModelFixture : IDisposable {
    public ServiceProvider ServiceProvider { get; }

    public MainViewModelFixture() {
        var services = new ServiceCollection();
        services.AddSingleton<IPlayCanvasService, FakePlayCanvasService>();
        services.AddTransient<MainViewModel>();

        ServiceProvider = services.BuildServiceProvider();
    }

    public MainViewModel CreateMainViewModel() => ServiceProvider.GetRequiredService<MainViewModel>();

    public void Dispose() {
        ServiceProvider.Dispose();
    }

    private sealed class FakePlayCanvasService : IPlayCanvasService {
        public Task<JObject> GetAssetByIdAsync(string assetId, string apiKey, CancellationToken cancellationToken) => Task.FromResult(new JObject());

        public Task<JArray> GetAssetsAsync(string projectId, string branchId, string apiKey, CancellationToken cancellationToken) => Task.FromResult(new JArray());

        public Task<List<Branch>> GetBranchesAsync(string projectId, string apiKey, List<Branch> branches, CancellationToken cancellationToken) => Task.FromResult(new List<Branch>());

        public Task<Dictionary<string, string>> GetProjectsAsync(string? userId, string? apiKey, Dictionary<string, string> projects, CancellationToken cancellationToken) => Task.FromResult(new Dictionary<string, string>());

        public Task<string> GetUserIdAsync(string? username, string? apiKey, CancellationToken cancellationToken) => Task.FromResult("user");
    }
}
