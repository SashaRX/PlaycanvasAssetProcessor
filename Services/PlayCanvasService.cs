using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using TexTool.Resources;

namespace TexTool.Services {
    public class PlayCanvasService {
        private readonly HttpClient client;

        public PlayCanvasService() {
            client = new HttpClient();
        }

        private void AddAuthorizationHeader(string? apiKey) {
            if (string.IsNullOrEmpty(apiKey)) {
                throw new Exception("API key is null or empty");
            }
            if (client.DefaultRequestHeaders.Contains("Authorization")) {
                client.DefaultRequestHeaders.Remove("Authorization");
            }
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        public async Task<string> GetUserIdAsync(string? username, string? apiKey, CancellationToken cancellationToken) {
            string url = $"https://playcanvas.com/api/users/{username}";

            AddAuthorizationHeader(apiKey: apiKey);

            HttpResponseMessage response = await client.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var json = JObject.Parse(responseBody);
            return json["id"]?.ToString() ?? throw new Exception("User ID is null");
        }

        public async Task<Dictionary<string, string>> GetProjectsAsync(string? userId, string? apiKey, CancellationToken cancellationToken) {
            string url = $"https://playcanvas.com/api/users/{userId}/projects";

            AddAuthorizationHeader(apiKey);

            HttpResponseMessage response = await client.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var json = JObject.Parse(responseBody);
            var projectsArray = json["result"] as JArray ?? throw new Exception("Projects array is null");

            var projects = new Dictionary<string, string>();
            foreach (var project in projectsArray) {
                string? projectId = project["id"]?.ToString();
                string? projectName = project["name"]?.ToString();
                if (projectId != null && projectName != null) {
                    projects.Add(projectId, projectName);
                }
            }

            return projects;
        }

        public async Task<List<Branch>> GetBranchesAsync(string projectId, string apiKey, CancellationToken cancellationToken) {
            string url = $"https://playcanvas.com/api/projects/{projectId}/branches";

            AddAuthorizationHeader(apiKey);

            HttpResponseMessage response = await client.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var json = JObject.Parse(responseBody);
            var branchesArray = json["result"] as JArray ?? throw new Exception("Branches array is null");

            var branches = new List<Branch>();
            foreach (var branch in branchesArray) {
                string? branchID = branch["id"]?.ToString();
                string? branchName = branch["name"]?.ToString();
                if (branchID != null && branchName != null) {
                    branches.Add(new Branch { Id = branchID, Name = branchName });
                }
            }

            return branches;
        }

        public async Task<JArray> GetAssetsAsync(string projectId, string branchId, string apiKey, CancellationToken cancellationToken) {
            string url = $"https://playcanvas.com/api/projects/{projectId}/assets?branch={branchId}&skip=0&limit=10000";

            AddAuthorizationHeader(apiKey);

            HttpResponseMessage response = await client.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            string responseData = await response.Content.ReadAsStringAsync(cancellationToken);
            var assetsResponse = JObject.Parse(responseData);

            return assetsResponse["result"] as JArray ?? throw new Exception("Assets array is null");
        }

        public async Task<JObject> GetAssetByIdAsync(string assetId, string apiKey, CancellationToken cancellationToken) {
            string url = $"https://playcanvas.com/api/assets/{assetId}";

            AddAuthorizationHeader(apiKey);

            HttpResponseMessage response = await client.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            return JObject.Parse(responseBody);
        }
    }

    public class Branch {
        public required string Id { get; set; }
        public required string Name { get; set; }
    }
}
