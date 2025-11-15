using System.IO;
using System.IO.Abstractions;
using System.Net.Http;

namespace AssetProcessor.Services;

public sealed class DownloadService : IDownloadService {
    private readonly IHttpClientFactory httpClientFactory;
    private readonly IFileSystem fileSystem;
    private readonly ILogService logService;

    public DownloadService(IHttpClientFactory httpClientFactory, IFileSystem fileSystem, ILogService logService) {
        this.httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        this.fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        this.logService = logService ?? throw new ArgumentNullException(nameof(logService));
    }

    public async Task<bool> DownloadFileAsync(string url, string destinationPath, CancellationToken cancellationToken) {
        ArgumentException.ThrowIfNullOrEmpty(url);
        ArgumentException.ThrowIfNullOrEmpty(destinationPath);

        try {
            HttpClient client = httpClientFactory.CreateClient("Downloads");
            using HttpResponseMessage response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using Stream destinationStream = fileSystem.File.Create(destinationPath);

            await response.Content.CopyToAsync(destinationStream, cancellationToken).ConfigureAwait(false);
            return true;
        } catch (Exception ex) {
            logService.LogError($"Error downloading file from {url}: {ex.Message}");
            return false;
        }
    }
}
