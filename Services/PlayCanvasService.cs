using AssetProcessor.Exceptions;
using Newtonsoft.Json.Linq;
using System.Net.Http;
using System.Net.Http.Headers;

namespace AssetProcessor.Services {
    public class PlayCanvasService : IPlayCanvasService {
        private readonly HttpClient client;

        public PlayCanvasService() {
            client = new HttpClient();
        }

        private void AddAuthorizationHeader(string? apiKey) {
            if (string.IsNullOrEmpty(apiKey)) {
                throw new InvalidConfigurationException(
                    "API key is required but was not provided",
                    "PlaycanvasApiKey",
                    apiKey);
            }
            if (client.DefaultRequestHeaders.Contains("Authorization")) {
                client.DefaultRequestHeaders.Remove("Authorization");
            }
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        public async Task<string> GetUserIdAsync(string? username, string? apiKey, CancellationToken cancellationToken) {
            string url = $"https://playcanvas.com/api/users/{username}";

            AddAuthorizationHeader(apiKey: apiKey);

            try {
                HttpResponseMessage response = await client.GetAsync(url, cancellationToken);

                if (!response.IsSuccessStatusCode) {
                    throw new PlayCanvasApiException(
                        $"Failed to get user ID for username '{username}'",
                        url,
                        (int)response.StatusCode);
                }

                string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                JObject json = JObject.Parse(responseBody);
                return json["id"]?.ToString() ?? throw new PlayCanvasApiException(
                    "User ID is null in API response",
                    url);
            } catch (HttpRequestException ex) {
                throw new NetworkException(
                    $"Network error while fetching user ID for '{username}'",
                    url,
                    0,
                    ex);
            }
        }

        public async Task<Dictionary<string, string>> GetProjectsAsync(string? userId, string? apiKey, Dictionary<string, string> projects, CancellationToken cancellationToken) {
            string url = $"https://playcanvas.com/api/users/{userId}/projects";

            AddAuthorizationHeader(apiKey);

            try {
                HttpResponseMessage response = await client.GetAsync(url, cancellationToken);

                if (!response.IsSuccessStatusCode) {
                    throw new PlayCanvasApiException(
                        $"Failed to get projects for user ID '{userId}'",
                        url,
                        (int)response.StatusCode);
                }

                string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                JObject json = JObject.Parse(responseBody);
                JArray projectsArray = json["result"] as JArray ?? throw new PlayCanvasApiException(
                    "Projects array is null in API response",
                    url);

                foreach (JToken project in projectsArray) {
                    string? projectId = project["id"]?.ToString();
                    string? projectName = project["name"]?.ToString();
                    if (projectId != null && projectName != null) {
                        projects.Add(projectId, projectName);
                    }
                }

                return projects;
            } catch (HttpRequestException ex) {
                throw new NetworkException(
                    $"Network error while fetching projects for user ID '{userId}'",
                    url,
                    0,
                    ex);
            }
        }

        public async Task<List<Branch>> GetBranchesAsync(string projectId, string apiKey, List<Branch> branches, CancellationToken cancellationToken) {
            string url = $"https://playcanvas.com/api/projects/{projectId}/branches";

            AddAuthorizationHeader(apiKey);

            try {
                HttpResponseMessage response = await client.GetAsync(url, cancellationToken);

                if (!response.IsSuccessStatusCode) {
                    throw new PlayCanvasApiException(
                        $"Failed to get branches for project ID '{projectId}'",
                        url,
                        (int)response.StatusCode);
                }

                string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                JObject json = JObject.Parse(responseBody);
                JArray branchesArray = json["result"] as JArray ?? throw new PlayCanvasApiException(
                    "Branches array is null in API response",
                    url);

                foreach (JToken branch in branchesArray) {
                    string? branchID = branch["id"]?.ToString();
                    string? branchName = branch["name"]?.ToString();
                    if (branchID != null && branchName != null) {
                        branches.Add(new Branch { Id = branchID, Name = branchName });
                    }
                }

                return branches;
            } catch (HttpRequestException ex) {
                throw new NetworkException(
                    $"Network error while fetching branches for project ID '{projectId}'",
                    url,
                    0,
                    ex);
            }
        }

        public async Task<JArray> GetAssetsAsync(string projectId, string branchId, string apiKey, CancellationToken cancellationToken) {
            string url = $"https://playcanvas.com/api/projects/{projectId}/assets?branch={branchId}&skip=0&limit=10000";

            AddAuthorizationHeader(apiKey);

            try {
                HttpResponseMessage response = await client.GetAsync(url, cancellationToken);

                if (!response.IsSuccessStatusCode) {
                    throw new PlayCanvasApiException(
                        $"Failed to get assets for project ID '{projectId}' and branch ID '{branchId}'",
                        url,
                        (int)response.StatusCode);
                }

                string responseData = await response.Content.ReadAsStringAsync(cancellationToken);
                JObject assetsResponse = JObject.Parse(responseData);

                return assetsResponse["result"] as JArray ?? throw new PlayCanvasApiException(
                    "Assets array is null in API response",
                    url);
            } catch (HttpRequestException ex) {
                throw new NetworkException(
                    $"Network error while fetching assets for project ID '{projectId}'",
                    url,
                    0,
                    ex);
            }
        }

        public async Task<JObject> GetAssetByIdAsync(string assetId, string apiKey, CancellationToken cancellationToken) {
            string url = $"https://playcanvas.com/api/assets/{assetId}";

            AddAuthorizationHeader(apiKey);

            try {
                HttpResponseMessage response = await client.GetAsync(url, cancellationToken);

                if (!response.IsSuccessStatusCode) {
                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound) {
                        throw new AssetNotFoundException(
                            $"Asset with ID '{assetId}' was not found",
                            assetId);
                    }
                    throw new PlayCanvasApiException(
                        $"Failed to get asset with ID '{assetId}'",
                        url,
                        (int)response.StatusCode);
                }

                string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                return JObject.Parse(responseBody);
            } catch (HttpRequestException ex) {
                throw new NetworkException(
                    $"Network error while fetching asset with ID '{assetId}'",
                    url,
                    0,
                    ex);
            }
        }
    }

    public class Branch {
        public required string Id { get; set; }
        public required string Name { get; set; }
    }
}
