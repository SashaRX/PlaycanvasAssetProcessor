using AssetProcessor.Settings;
using Newtonsoft.Json.Linq;
using System.Net.Http;
using System.Net.Http.Headers;

namespace AssetProcessor.Services {
    public class PlayCanvasService {
        private readonly HttpClient client;
        private readonly string? apiKey;
        private readonly ProjectConfig? config;

        public PlayCanvasService(ProjectConfig config) {
            if (string.IsNullOrEmpty(AppSettings.Default.PlaycanvasApiKey)) {
                throw new ArgumentNullException(
                    nameof(AppSettings.Default.PlaycanvasApiKey), "PlaycanvasApiKey is null or empty in AppSettings.");
            }
            this.config = config ?? throw new ArgumentNullException(nameof(config), "ProjectConfig instance is required to initialize PlayCanvasService.");
            this.apiKey = AppSettings.Default.PlaycanvasApiKey;
            client = new HttpClient();
        }

        private void AddAuthorizationHeader() {
            if (client.DefaultRequestHeaders.Contains("Authorization")) {
                client.DefaultRequestHeaders.Remove("Authorization");
            }
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        private static async Task<HttpResponseMessage> SafeExecuteAsync(Func<Task<HttpResponseMessage>> action) {
            try {
                return await action();
            } catch (HttpRequestException ex) {
                throw new Exception("Network error occurred", ex);
            } catch (Exception ex) {
                throw new Exception("An error occurred while executing the request", ex);
            }
        }

        public async Task<string> GetUserIdAsync(string username, CancellationToken cancellationToken) {
            if (string.IsNullOrWhiteSpace(username)) {
                throw new ArgumentNullException(nameof(username), "Username is null or empty");
            }

            string? url = $"https://playcanvas.com/api/users/{username}";
            AddAuthorizationHeader();

            HttpResponseMessage? response = await SafeExecuteAsync(() => client.GetAsync(url, cancellationToken));
            response.EnsureSuccessStatusCode();

            string? responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            JObject? json = JObject.Parse(responseBody);
            string? userId = json["id"]?.ToString();
            if (string.IsNullOrEmpty(userId)) {
                throw new Exception("User ID is null");
            }
            return userId;
        }

        public async Task<Dictionary<string, string>> GetProjectsAsync(string userId, CancellationToken cancellationToken) {
            if (string.IsNullOrWhiteSpace(userId)) {
                throw new ArgumentNullException(nameof(userId), "User ID is null or empty");
            }

            string? url = $"https://playcanvas.com/api/users/{userId}/projects";
            AddAuthorizationHeader();

            HttpResponseMessage? response = await SafeExecuteAsync(() => client.GetAsync(url, cancellationToken));
            response.EnsureSuccessStatusCode();

            string? responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            JObject? json = JObject.Parse(responseBody);
            if (json["result"] is not JArray projectsArray) {
                throw new Exception("Projects array is null");
            }

            Dictionary<string, string>? projects = [];
            foreach (JToken project in projectsArray) {
                string? projectId = project["id"]?.ToString();
                string? projectName = project["name"]?.ToString();
                if (string.IsNullOrEmpty(projectId) || string.IsNullOrEmpty(projectName)) {
                    throw new Exception("Project ID or name is null");
                }
                projects.Add(projectId, projectName);
            }
            return projects;
        }

        public async Task<List<Branch>> GetBranchesAsync(string projectId, CancellationToken cancellationToken) {
            if (string.IsNullOrWhiteSpace(projectId)) {
                throw new ArgumentNullException(nameof(projectId), "Project ID is null or empty");
            }

            string? url = $"https://playcanvas.com/api/projects/{projectId}/branches";
            AddAuthorizationHeader();

            HttpResponseMessage? response = await SafeExecuteAsync(() => client.GetAsync(url, cancellationToken));
            response.EnsureSuccessStatusCode();

            string? responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            JObject? json = JObject.Parse(responseBody);
            if (json["result"] is not JArray branchesArray) {
                throw new Exception("Branches array is null");
            }

            List<Branch>? branches = [];
            foreach (JToken branch in branchesArray) {
                string? branchId = branch["id"]?.ToString();
                string? branchName = branch["name"]?.ToString();
                if (string.IsNullOrEmpty(branchId) || string.IsNullOrEmpty(branchName)) {
                    throw new Exception("Branch ID or name is null");
                }
                branches.Add(new Branch { Id = branchId, Name = branchName });
            }
            return branches;
        }

        public async Task<JArray> GetAssetsAsync(string projectId, string branchId, CancellationToken cancellationToken) {
            if (string.IsNullOrWhiteSpace(projectId)) {
                throw new ArgumentNullException(nameof(projectId), "Project ID is null or empty");
            }
            if (string.IsNullOrWhiteSpace(branchId)) {
                throw new ArgumentNullException(nameof(branchId), "Branch ID is null or empty");
            }

            string? url = $"https://playcanvas.com/api/projects/{projectId}/assets?branch={branchId}&skip=0&limit=10000";
            AddAuthorizationHeader();

            HttpResponseMessage? response = await SafeExecuteAsync(() => client.GetAsync(url, cancellationToken));
            response.EnsureSuccessStatusCode();

            string? responseData = await response.Content.ReadAsStringAsync(cancellationToken);
            JObject? assetsResponse = JObject.Parse(responseData);
            if (assetsResponse["result"] is not JArray result) {
                throw new Exception("Assets array is null");
            }
            return result;
        }

        public async Task<JObject> GetAssetByIdAsync(string assetId, CancellationToken cancellationToken) {
            if (string.IsNullOrWhiteSpace(assetId)) {
                throw new ArgumentNullException(nameof(assetId), "Asset ID is null or empty");
            }

            string? url = $"https://playcanvas.com/api/assets/{assetId}";
            AddAuthorizationHeader();

            HttpResponseMessage? response = await SafeExecuteAsync(() => client.GetAsync(url, cancellationToken));
            response.EnsureSuccessStatusCode();

            string? responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            JObject? asset = JObject.Parse(responseBody);
            return asset ?? throw new Exception("Asset response is null");
        }

        public override bool Equals(object? obj) {
            return obj is PlayCanvasService service &&
                   EqualityComparer<ProjectConfig?>.Default.Equals(config, service.config);
        }

        public override int GetHashCode() {
            return HashCode.Combine(config);
        }
    }

    public class Branch {
        public required string Id { get; set; }
        public required string Name { get; set; }
    }
}
