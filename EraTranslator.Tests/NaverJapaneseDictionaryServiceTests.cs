using System.Net;
using System.Text;
using EraTranslator.Services;

namespace EraTranslator.Tests;

public sealed class NaverJapaneseDictionaryServiceTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), "EraTranslatorTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }

    [Fact]
    public void Store_UpsertsAndReadsNaverDictionaryEntry()
    {
        Directory.CreateDirectory(_rootPath);
        var store = new NaverJapaneseDictionaryStore(_rootPath);

        store.Upsert(new NaverJapaneseDictionaryEntry("快楽", "かいらく", "쾌락", "https://example.test", false));

        Assert.True(store.TryGet("快楽", out var entry));
        Assert.Equal("쾌락", entry.KoTarget);
        Assert.Equal("https://example.test", entry.SourceUrl);
        Assert.False(entry.ReviewRequired);
        Assert.True(File.Exists(Path.Combine(_rootPath, "EraTranslator.naver-japanese-dictionary.sqlite")));
    }

    [Fact]
    public void Parser_ExtractsExactJsonHeadword()
    {
        var parser = new NaverJapaneseDictionaryParser();
        var json = """
            {
              "items": [
                { "headword": "快楽", "reading": "かいらく", "mean": "쾌락" }
              ]
            }
            """;

        var entry = parser.TryParse("快楽", json, "https://example.test");

        Assert.NotNull(entry);
        Assert.Equal("快楽", entry.Surface);
        Assert.Equal("かいらく", entry.ReadingKana);
        Assert.Equal("쾌락", entry.KoTarget);
        Assert.False(entry.ReviewRequired);
    }

    [Fact]
    public void Parser_ExtractsAutocompleteJsonCandidate()
    {
        var parser = new NaverJapaneseDictionaryParser();
        var json = """
            {
              "query": ["美脚"],
              "items": [
                [
                  [["びきゃく"],["美脚"],[""],["날씬하고 긴 여성의 다리"],["id"],["jako"]]
                ],
                []
              ]
            }
            """;

        var entry = parser.TryParse("美脚", json, "https://ac-dict.naver.com/jako/ac?q=%E7%BE%8E%E8%84%9A");

        Assert.NotNull(entry);
        Assert.Equal("美脚", entry.Surface);
        Assert.Equal("びきゃく", entry.ReadingKana);
        Assert.Equal("날씬하고 긴 여성의 다리", entry.KoTarget);
        Assert.False(entry.ReviewRequired);
    }

    [Fact]
    public void Parser_RejectsLooseJsonHeadword()
    {
        var parser = new NaverJapaneseDictionaryParser();
        var json = """
            {
              "items": [
                { "headword": "快楽値", "reading": "かいらくち", "mean": "쾌락치" }
              ]
            }
            """;

        var entry = parser.TryParse("快楽", json, "https://example.test");

        Assert.Null(entry);
    }

    [Fact]
    public async Task Lookup_UsesChromeLikeHeadersAndParsesResponse()
    {
        HttpRequestMessage? capturedRequest = null;
        var service = new NaverJapaneseDictionaryLookupService(
            new FakeHttpClientFactory(request =>
            {
                capturedRequest = request;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"items":[{"headword":"快楽","reading":"かいらく","mean":"쾌락"}]}""",
                        Encoding.UTF8,
                        "application/json"),
                };
            }));

        var entry = await service.TryLookupAsync("快楽", CancellationToken.None);

        Assert.NotNull(entry);
        Assert.Equal("쾌락", entry.KoTarget);
        Assert.NotNull(capturedRequest);
        Assert.Contains("https://ac-dict.naver.com/jako/ac", capturedRequest!.RequestUri?.ToString(), StringComparison.Ordinal);
        Assert.Contains("Chrome", capturedRequest!.Headers.UserAgent.ToString(), StringComparison.Ordinal);
        Assert.Contains("ko-KR", capturedRequest.Headers.AcceptLanguage.ToString(), StringComparison.Ordinal);
        Assert.Equal("https://ja.dict.naver.com/", capturedRequest.Headers.Referrer?.ToString());
        Assert.True(capturedRequest.Headers.CacheControl?.NoCache);
    }

    private sealed class FakeHttpClientFactory(Func<HttpRequestMessage, HttpResponseMessage> responder) : ISimpleHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new FakeHttpMessageHandler(responder));
    }

    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(responder(request));
        }
    }
}
