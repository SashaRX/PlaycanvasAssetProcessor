using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace TexTool {
    public class PlayCanvasService {
        private readonly HttpClient? client;

        public PlayCanvasService() {
            client = new HttpClient();
        }

        public async Task<string> GetUserIdAsync(string? username, string? apiKey, CancellationToken cancellationToken) {
            string? url = $"https://playcanvas.com/api/users/{username}";

            if(client == null)
                throw new System.Exception("Client is null");
            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            HttpResponseMessage? response = await client.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            string? responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var json = JObject.Parse(responseBody);
            return json["id"]?.ToString();
        }

        public async Task<Dictionary<string, string>> GetProjectsAsync(string? userId, string? apiKey, CancellationToken cancellationToken) {
            string? url = $"https://playcanvas.com/api/users/{userId}/projects";

            if (client == null)
                throw new System.Exception("Client is null");
            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            HttpResponseMessage response = await client.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            string? responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var json = JObject.Parse(responseBody);
            var projectsArray = json["result"] as JArray ?? throw new System.Exception("Branches array is null");

            var projects = new Dictionary<string, string>();
            foreach (var project in projectsArray) {
                string? projectId = project["id"]?.ToString();
                string? projectName = project["name"]?.ToString();
                if(projectId != null && projectName != null)
                    projects.Add(projectId, projectName);
            }

            return projects;
        }

        public async Task<List<Branch>> GetBranchesAsync(string projectId, string apiKey, CancellationToken cancellationToken) {
            string? url = $"https://playcanvas.com/api/projects/{projectId}/branches";

            if (client == null) {
                throw new System.Exception("HttpClient is not initialized.");
            }

            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            try {
                HttpResponseMessage response = await client.GetAsync(url, cancellationToken);
                response.EnsureSuccessStatusCode();

                string? responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                var json = JObject.Parse(responseBody);
                var branchesArray = json["result"] as JArray ?? throw new System.Exception("Branches array is null");

                var branches = new List<Branch>();
                foreach (var branch in branchesArray) {
                    string? branchID = branch["id"]?.ToString();
                    string? branchName = branch["name"]?.ToString();
                    if (branchID != null && branchName != null) {
                        branches.Add(new Branch { Id = branchID, Name = branchName });
                    }
                }

                return branches;
            } catch (HttpRequestException httpEx) {
                throw new System.Exception("HttpRequestException: " + httpEx.Message);
            } catch (JsonException jsonEx) {
                throw new System.Exception("JsonException: " + jsonEx.Message);
            } catch (Exception ex) {
                throw new System.Exception("Exception: " + ex.Message);
            }
        }

        public async Task<JArray> GetAssetsAsync(string projectId, string branchId, string apiKey, CancellationToken cancellationToken) {
            string? url = $"https://playcanvas.com/api/projects/{projectId}/assets?branch={branchId}&skip=0&limit=10000";

            if (client == null)
                throw new System.Exception("Client is null");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            var response = await client.GetAsync(url, cancellationToken);

            response.EnsureSuccessStatusCode();

            var responseData = await response.Content.ReadAsStringAsync(cancellationToken);
            var assetsResponse = JObject.Parse(responseData);
            
            if (assetsResponse["result"] is JArray assetsArray)
                return assetsArray;
            else
                throw new System.Exception("Assets array is null");
        }
    }

    public class Branch {
        public required string Id { get; set; }
        public required string Name { get; set; }
    }
}
