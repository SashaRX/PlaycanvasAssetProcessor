using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace AssetProcessor.Services {
    public class PlayCanvasServiceBase {
        private static readonly HttpClient client = new();

        public static async Task<string> GetUserIdAsync(string username, CancellationToken cancellationToken) {
            string url = $"https://playcanvas.com/api/users/{username}";
            HttpResponseMessage response = await client.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();
            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            JObject json = JObject.Parse(responseBody);
            return json["id"]?.ToString() ?? throw new Exception("User ID not found in response");
        }
    }
}
