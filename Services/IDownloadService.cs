namespace AssetProcessor.Services;

public interface IDownloadService {
    Task<bool> DownloadFileAsync(string url, string destinationPath, CancellationToken cancellationToken);
}
