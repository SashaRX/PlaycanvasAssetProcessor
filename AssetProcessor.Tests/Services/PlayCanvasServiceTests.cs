using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AssetProcessor.Exceptions;
using AssetProcessor.Services;
using AssetProcessor.Services.Models;
using Polly;
using Xunit;

namespace AssetProcessor.Tests.Services;

public class PlayCanvasServiceTests {
    [Fact]
    public async Task GetAssetsAsync_PaginatesAcrossMultiplePages() {
        QueueMessageHandler handler = new();
        handler.EnqueueResponse(CreateAssetsResponse(GenerateAssets(1, 200)));
        handler.EnqueueResponse(CreateAssetsResponse(GenerateAssets(201, 50)));

        using HttpClient client = new(handler) {
            BaseAddress = new Uri("https://playcanvas.com/")
        };

        using PlayCanvasService service = new(client, Policy.NoOpAsync<HttpResponseMessage>());

        List<int> receivedIds = [];
        await foreach (PlayCanvasAssetSummary asset in service.GetAssetsAsync("123", "456", "token", CancellationToken.None)) {
            receivedIds.Add(asset.Id);
        }

        Assert.Equal(250, receivedIds.Count);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("skip=200", handler.Requests[1].RequestUri!.Query);
        Assert.All(handler.Requests, request => Assert.Equal("Bearer", request.Headers.Authorization?.Scheme));
        Assert.All(handler.Requests, request => Assert.Equal("token", request.Headers.Authorization?.Parameter));
        Assert.Equal(Enumerable.Range(1, 250), receivedIds.OrderBy(id => id));
    }

    [Fact]
    public async Task GetAssetByIdAsync_WhenServerReturnsError_ThrowsPlayCanvasApiException() {
        QueueMessageHandler handler = new();
        handler.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.InternalServerError));

        using HttpClient client = new(handler);
        using PlayCanvasService service = new(client, Policy.NoOpAsync<HttpResponseMessage>());

        await Assert.ThrowsAsync<PlayCanvasApiException>(() => service.GetAssetByIdAsync("42", "token", CancellationToken.None));
        Assert.Single(handler.Requests);
        Assert.Equal("Bearer", handler.Requests[0].Headers.Authorization?.Scheme);
        Assert.Equal("token", handler.Requests[0].Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task GetUserIdAsync_WhenApiReturnsError_ThrowsPlayCanvasApiException() {
        QueueMessageHandler handler = new();
        handler.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.Unauthorized) {
            Content = new StringContent("{\"error\":\"invalid\"}", Encoding.UTF8, "application/json")
        });

        using HttpClient client = new(handler);
        using PlayCanvasService service = new(client, Policy.NoOpAsync<HttpResponseMessage>());

        await Assert.ThrowsAsync<PlayCanvasApiException>(() => service.GetUserIdAsync("user", "api-key", CancellationToken.None));
        Assert.Single(handler.Requests);
        Assert.Equal("Bearer", handler.Requests[0].Headers.Authorization?.Scheme);
        Assert.Equal("api-key", handler.Requests[0].Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task GetAssetsAsync_RetriesWhenPolicyHandlesFailure() {
        QueueMessageHandler handler = new();
        handler.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        handler.EnqueueResponse(CreateAssetsResponse(GenerateAssets(1, 1)));

        int retryCount = 0;
        AsyncPolicy<HttpResponseMessage> policy = Policy<HttpResponseMessage>
            .HandleResult(response => !response.IsSuccessStatusCode)
            .RetryAsync(1, onRetryAsync: (outcome, _, _) => {
                outcome.Result?.Dispose();
                Interlocked.Increment(ref retryCount);
                return Task.CompletedTask;
            });

        using HttpClient client = new(handler);
        using PlayCanvasService service = new(client, policy);

        List<int> receivedIds = [];
        await foreach (PlayCanvasAssetSummary asset in service.GetAssetsAsync("123", "main", "token", CancellationToken.None)) {
            receivedIds.Add(asset.Id);
        }

        Assert.Equal(new[] { 1 }, receivedIds);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(1, retryCount);
    }

    private static HttpResponseMessage CreateAssetsResponse(IEnumerable<object> assets) {
        string json = JsonSerializer.Serialize(new { result = assets }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        return new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private static IEnumerable<object> GenerateAssets(int startId, int count) {
        foreach (int id in Enumerable.Range(startId, count)) {
            yield return new {
                id,
                type = "texture",
                name = $"Asset{id}",
                path = $"path/{id}",
                parent = (int?)null,
                file = new {
                    size = 1024,
                    hash = $"hash{id}",
                    filename = $"asset{id}.bin",
                    url = $"/api/assets/{id}/file"
                }
            };
        }
    }

    private sealed class QueueMessageHandler : HttpMessageHandler {
        private readonly Queue<HttpResponseMessage> responses = new();
        public List<HttpRequestMessage> Requests { get; } = new();

        public void EnqueueResponse(HttpResponseMessage response) {
            responses.Enqueue(response);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            Requests.Add(request);
            if (responses.Count == 0) {
                throw new InvalidOperationException("No response configured for request");
            }

            HttpResponseMessage response = responses.Dequeue();
            return Task.FromResult(response);
        }
    }
}
