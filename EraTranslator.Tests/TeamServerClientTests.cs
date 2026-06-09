using System.Net;
using EraTranslator.Services;

namespace EraTranslator.Tests;

public sealed class TeamServerClientTests
{
    [Fact]
    public async Task GetProjectsAsync_CanRefreshRepeatedlyWithSameHttpClient()
    {
        var requests = new List<HttpRequestMessage>();
        using var httpClient = new HttpClient(new FakeHttpMessageHandler(request =>
        {
            requests.Add(CloneForAssert(request));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"projects":[]}"""),
            };
        }));
        var client = new TeamServerClient(httpClient);

        await client.GetProjectsAsync("http://localhost:8000/", "token-1", CancellationToken.None);
        await client.GetProjectsAsync("http://127.0.0.1:8000/", "token-2", CancellationToken.None);

        Assert.Equal(2, requests.Count);
        Assert.Equal("http://localhost:8000/api/projects", requests[0].RequestUri?.ToString());
        Assert.Equal("http://127.0.0.1:8000/api/projects", requests[1].RequestUri?.ToString());
        Assert.Equal("Bearer token-1", requests[0].Headers.Authorization?.ToString());
        Assert.Equal("Bearer token-2", requests[1].Headers.Authorization?.ToString());
    }

    private static HttpRequestMessage CloneForAssert(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return clone;
    }

    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(responder(request));
        }
    }
}
