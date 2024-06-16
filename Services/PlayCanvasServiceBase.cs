using Newtonsoft.Json.Linq;
using System.Net.Http;

namespace TexTool.Services {
    public class PlayCanvasServiceBase {
        private static readonly HttpClient? client = new();

        public static async Task<string> GetUserIdAsync(string? username, CancellationToken cancellationToken) {
            if (client == null)
                throw new System.Exception("Client is null");

            string? url = $"https://playcanvas.com/api/users/{username}";
            HttpResponseMessage? response = await client.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();
            string? responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var json = JObject.Parse(responseBody);
            return json["id"]?.ToString() ?? throw new System.Exception("User ID not found in response");
        }
    }
}