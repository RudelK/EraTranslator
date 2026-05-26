using System.Net;
using System.Text;
using EraTranslator.Models;
using EraTranslator.Services;

namespace EraTranslator.Tests;

public sealed class TranslationProviderTests
{
    [Fact]
    public async Task DeepLProvider_RestoresProviderSpecificMarkers()
    {
        var factory = new FakeHttpClientFactory(_ => JsonResponse("""
{
  "translations": [
    { "text": "번역 <era-ph idx=\"0\"/> 완료" }
  ]
}
"""));
        var provider = new DeepLTranslationProvider(factory);

        var result = await provider.TranslateAsync(
            [new ProtectedSegment("id-1", "원문 __PH0__", "원문 %CALLNAME%", ["%CALLNAME%"])],
            new ProviderSettings
            {
                ProviderType = TranslationProviderType.DeepLFree,
                ApiKey = "test-key",
                SourceLanguage = "ja",
                TargetLanguage = "ko",
            },
            CancellationToken.None);

        Assert.Equal("번역 __PH0__ 완료", result.Translations["id-1"]);
    }

    [Fact]
    public async Task DeepLFreeProvider_SendsAuthorizationHeaderToFreeEndpoint()
    {
        string? capturedUri = null;
        string? capturedAuthorization = null;
        string? capturedMediaType = null;
        string? capturedBody = null;
        var factory = new FakeHttpClientFactory(request =>
        {
            capturedUri = request.RequestUri?.ToString();
            capturedAuthorization = request.Headers.GetValues("Authorization").Single();
            capturedMediaType = request.Content?.Headers.ContentType?.MediaType;
            capturedBody = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonResponse("""
{
  "translations": [
    { "text": "안녕하세요" }
  ]
}
""");
        });
        var provider = new DeepLTranslationProvider(factory);

        var result = await provider.TranslateAsync(
            [new ProtectedSegment("id-1", "こんにちは", "こんにちは", [])],
            new ProviderSettings
            {
                ProviderType = TranslationProviderType.DeepLFree,
                ApiKey = "free-test-key",
                SourceLanguage = "ja",
                TargetLanguage = "ko",
            },
            CancellationToken.None);

        Assert.Equal("안녕하세요", result.Translations["id-1"]);
        Assert.Equal("https://api-free.deepl.com/v2/translate", capturedUri);
        Assert.Equal("DeepL-Auth-Key free-test-key", capturedAuthorization);
        Assert.Equal("application/x-www-form-urlencoded", capturedMediaType);
        Assert.DoesNotContain("auth_key=", capturedBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeepLProProvider_SendsAuthorizationHeaderToProEndpoint()
    {
        string? capturedUri = null;
        string? capturedAuthorization = null;
        string? capturedMediaType = null;
        var factory = new FakeHttpClientFactory(request =>
        {
            capturedUri = request.RequestUri?.ToString();
            capturedAuthorization = request.Headers.GetValues("Authorization").Single();
            capturedMediaType = request.Content?.Headers.ContentType?.MediaType;
            return JsonResponse("""
{
  "translations": [
    { "text": "안녕하세요" }
  ]
}
""");
        });
        var provider = new DeepLTranslationProvider(factory);

        var result = await provider.TranslateAsync(
            [new ProtectedSegment("id-1", "こんにちは", "こんにちは", [])],
            new ProviderSettings
            {
                ProviderType = TranslationProviderType.DeepLPro,
                ApiKey = "pro-test-key",
                SourceLanguage = "ja",
                TargetLanguage = "ko",
            },
            CancellationToken.None);

        Assert.Equal("안녕하세요", result.Translations["id-1"]);
        Assert.Equal("https://api.deepl.com/v2/translate", capturedUri);
        Assert.Equal("DeepL-Auth-Key pro-test-key", capturedAuthorization);
        Assert.Equal("application/x-www-form-urlencoded", capturedMediaType);
    }

    [Fact]
    public async Task PapagoProvider_RecordsHttpErrorsPerItem()
    {
        var callCount = 0;
        var factory = new FakeHttpClientFactory(_ =>
        {
            callCount++;
            return callCount == 1
                ? ErrorResponse(HttpStatusCode.Unauthorized, """{"errorMessage":"bad auth"}""")
                : JsonResponse("""
{
  "message": {
    "result": {
      "translatedText": "성공 ERAPHTOKEN0SAFE"
    }
  }
}
""");
        });
        var provider = new PapagoTranslationProvider(factory);

        var result = await provider.TranslateAsync(
            [
                new ProtectedSegment("id-1", "원문 __PH0__", "원문 %CALLNAME%", ["%CALLNAME%"]),
                new ProtectedSegment("id-2", "둘째 __PH0__", "둘째 %CALLNAME%", ["%CALLNAME%"]),
            ],
            new ProviderSettings
            {
                ProviderType = TranslationProviderType.Papago,
                PapagoClientId = "id",
                PapagoClientSecret = "secret",
                SourceLanguage = "ja",
                TargetLanguage = "ko",
            },
            CancellationToken.None);

        Assert.Equal(TranslationErrorKind.Http, result.Errors["id-1"].Kind);
        Assert.Equal(401, result.Errors["id-1"].HttpStatusCode);
        Assert.Equal("성공 __PH0__", result.Translations["id-2"]);
    }

    [Fact]
    public async Task OpenAiProvider_ThrowsJsonErrorForMalformedEnvelope()
    {
        var factory = new FakeHttpClientFactory(_ => JsonResponse("""{"choices":[{"message":{"content":"no-json"}}]}"""));
        var provider = new OpenAiCompatibleTranslationProvider(factory, false);

        var exception = await Assert.ThrowsAsync<TranslationProviderException>(() => provider.TranslateAsync(
            [new ProtectedSegment("id-1", "원문 __PH0__", "원문 %CALLNAME%", ["%CALLNAME%"])],
            new ProviderSettings
            {
                ProviderType = TranslationProviderType.OpenAi,
                ApiKey = "test-key",
                TargetLanguage = "ko",
            },
            CancellationToken.None));

        Assert.Equal(TranslationErrorKind.Json, exception.Kind);
    }

    [Fact]
    public async Task OpenAiProvider_StripsThinkTagsAndParsesJsonEnvelope()
    {
        var factory = new FakeHttpClientFactory(_ => JsonResponse("""
{"choices":[{"message":{"content":"<think>analyze</think>\n{\"translations\":[{\"id\":\"id-1\",\"translated\":\"번역 __PH0__\"}]}"}}]}
"""));
        var provider = new OpenAiCompatibleTranslationProvider(factory, true);

        var result = await provider.TranslateAsync(
            [new ProtectedSegment("id-1", "원문 __PH0__", "원문 %CALLNAME%", ["%CALLNAME%"])],
            new ProviderSettings
            {
                ProviderType = TranslationProviderType.LmStudio,
                TargetLanguage = "ko",
                DisableThinking = true,
            },
            CancellationToken.None);

        Assert.Equal("번역 __PH0__", result.Translations["id-1"]);
    }

    [Fact]
    public async Task LmStudioProvider_ParsesTokenizedBlocks()
    {
        var factory = new FakeHttpClientFactory(_ => JsonResponse("""
{"choices":[{"message":{"content":"|id-1|\n첫째 번역\n|\n|id-2|\n둘째 __PH0__\n|"} }]}
"""));
        var provider = new OpenAiCompatibleTranslationProvider(factory, true);

        var result = await provider.TranslateAsync(
            [
                new ProtectedSegment("id-1", "첫째", "첫째", []),
                new ProtectedSegment("id-2", "둘째 __PH0__", "둘째 %CALLNAME%", ["%CALLNAME%"]),
            ],
            new ProviderSettings
            {
                ProviderType = TranslationProviderType.LmStudio,
                TargetLanguage = "ko",
                DisableThinking = true,
            },
            CancellationToken.None);

        Assert.Equal("첫째 번역", result.Translations["id-1"]);
        Assert.Equal("둘째 __PH0__", result.Translations["id-2"]);
    }

    [Fact]
    public async Task LmStudioProvider_ParsesSingleSegmentWithoutSeparator()
    {
        var factory = new FakeHttpClientFactory(_ => JsonResponse("""
{"choices":[{"message":{"content":"주인님"} }]}
"""));
        var provider = new OpenAiCompatibleTranslationProvider(factory, true);

        var result = await provider.TranslateAsync(
            [new ProtectedSegment("id-1", "オーナーさん", "オーナーさん", [])],
            new ProviderSettings
            {
                ProviderType = TranslationProviderType.LmStudio,
                TargetLanguage = "ko",
                DisableThinking = true,
            },
            CancellationToken.None);

        Assert.Equal("주인님", result.Translations["id-1"]);
    }

    [Fact]
    public async Task LmStudioProvider_AcceptsSingleSegmentWithPlaceholderAndBracketLabel()
    {
        var factory = new FakeHttpClientFactory(_ => JsonResponse("""
{"choices":[{"message":{"content":"[양도]__PH0__"} }]}
"""));
        var provider = new OpenAiCompatibleTranslationProvider(factory, true);

        var result = await provider.TranslateAsync(
            [new ProtectedSegment("id-1", "[両刀]__PH0__", "[両刀]　", ["　"])],
            new ProviderSettings
            {
                ProviderType = TranslationProviderType.LmStudio,
                TargetLanguage = "ko",
                DisableThinking = true,
            },
            CancellationToken.None);

        Assert.Equal("[양도]__PH0__", result.Translations["id-1"]);
    }

    [Fact]
    public async Task LmStudioProvider_AcceptsSingleSegmentNoticeWithAsciiTerms()
    {
        var factory = new FakeHttpClientFactory(_ => JsonResponse("""
{"choices":[{"message":{"content":"※이것은 조교 SLG 제작 툴 erakanon의 개변·재배포입니다."} }]}
"""));
        var provider = new OpenAiCompatibleTranslationProvider(factory, true);

        var result = await provider.TranslateAsync(
            [new ProtectedSegment("id-1", "※これは調教SLG作成ツールerakanonの改変・再配布です。", "※これは調教SLG作成ツールerakanonの改変・再配布です。", [])],
            new ProviderSettings
            {
                ProviderType = TranslationProviderType.LmStudio,
                TargetLanguage = "ko",
                DisableThinking = true,
            },
            CancellationToken.None);

        Assert.Equal("※이것은 조교 SLG 제작 툴 erakanon의 개변·재배포입니다.", result.Translations["id-1"]);
    }

    [Fact]
    public async Task LmStudioProvider_AcceptsLegitimateSentenceContainingContextDependentPhrase()
    {
        var factory = new FakeHttpClientFactory(_ => JsonResponse("""
{"choices":[{"message":{"content":"이 표현은 문맥에 따라 의미가 달라진다."} }]}
"""));
        var provider = new OpenAiCompatibleTranslationProvider(factory, true);

        var result = await provider.TranslateAsync(
            [new ProtectedSegment("id-1", "この表現は文脈によって意味が変わる。", "この表現は文脈によって意味が変わる。", [])],
            new ProviderSettings
            {
                ProviderType = TranslationProviderType.LmStudio,
                TargetLanguage = "ko",
                DisableThinking = true,
            },
            CancellationToken.None);

        Assert.Equal("이 표현은 문맥에 따라 의미가 달라진다.", result.Translations["id-1"]);
    }

    [Fact]
    public async Task LmStudioProvider_AcceptsAsciiPipeWhenSourceUsesFullWidthPipe()
    {
        var factory = new FakeHttpClientFactory(_ => JsonResponse("""
{"choices":[{"message":{"content":"후보 비율 | 유두 색깔 | 갈색 피부 | 합계"} }]}
"""));
        var provider = new OpenAiCompatibleTranslationProvider(factory, true);

        var result = await provider.TranslateAsync(
            [new ProtectedSegment("id-1", "候補割合｜乳首の色｜褐色肌｜合計", "候補割合｜乳首の色｜褐色肌｜合計", [])],
            new ProviderSettings
            {
                ProviderType = TranslationProviderType.LmStudio,
                TargetLanguage = "ko",
                DisableThinking = true,
            },
            CancellationToken.None);

        Assert.Equal("후보 비율 | 유두 색깔 | 갈색 피부 | 합계", result.Translations["id-1"]);
    }

    [Fact]
    public async Task LmStudioProvider_RepairsSingleSegmentPlaceholderPipeArtifacts()
    {
        var factory = new FakeHttpClientFactory(_ => JsonResponse("""
{"choices":[{"message":{"content":"|__PH0__는 방 침대에 말없이 앉았다.__PH1__옆에 앉도록 이쪽을 재촉한다."} }]}
"""));
        var provider = new OpenAiCompatibleTranslationProvider(factory, true);

        var result = await provider.TranslateAsync(
            [new ProtectedSegment("id-1", "__PH0__はベッドに無言で座った。__PH1__隣に座るようこちらを促す。", "__PH0__はベッドに無言で座った。__PH1__隣に座るようこちらを促す。", ["「", "」"])],
            new ProviderSettings
            {
                ProviderType = TranslationProviderType.LmStudio,
                TargetLanguage = "ko",
                DisableThinking = true,
            },
            CancellationToken.None);

        Assert.Equal("__PH0__는 방 침대에 말없이 앉았다.__PH1__옆에 앉도록 이쪽을 재촉한다.", result.Translations["id-1"]);
    }

    [Fact]
    public async Task LmStudioProvider_RepairsPlaceholderBoundaryPipes()
    {
        var factory = new FakeHttpClientFactory(_ => JsonResponse("""
{"choices":[{"message":{"content":"|__PH0__나는 당신과 다르게 바빠.__|__PH1__제대로 호출에는 응하고 있으니까, 이 정도는 눈감아 주었으면 해.__|__PH2__|"} }]}
"""));
        var provider = new OpenAiCompatibleTranslationProvider(factory, true);

        var result = await provider.TranslateAsync(
            [new ProtectedSegment("id-1", "__PH0__あなたと違って忙しいの。__PH1__ちゃんと呼び出しには応じてるんだから、そのくらいは大目に見てほしいな。__PH2__", "__PH0__あなたと違って忙しいの。__PH1__ちゃんと呼び出しには応じてるんだから、そのくらいは大目に見てほしいな。__PH2__", ["「", "」", "　"])],
            new ProviderSettings
            {
                ProviderType = TranslationProviderType.LmStudio,
                TargetLanguage = "ko",
                DisableThinking = true,
            },
            CancellationToken.None);

        Assert.Equal("__PH0__나는 당신과 다르게 바빠.__PH1__제대로 호출에는 응하고 있으니까, 이 정도는 눈감아 주었으면 해.__PH2__", result.Translations["id-1"]);
    }

    [Fact]
    public async Task LmStudioProvider_RepairsTrailingPlaceholderPipeArtifacts()
    {
        var factory = new FakeHttpClientFactory(_ => JsonResponse("""
{"choices":[{"message":{"content":"|__PH0__는 페니스를 찔리면서, 설마 정말로 다른 아이 옆에서 하게 될 줄은 몰랐다고_|_"} }]}
"""));
        var provider = new OpenAiCompatibleTranslationProvider(factory, true);

        var result = await provider.TranslateAsync(
            [new ProtectedSegment("id-1", "__PH0__はペニスを突き刺されながら、まさか本当に他の子の隣でするとは思わなかったと", "__PH0__はペニスを突き刺されながら、まさか本当に他の子の隣でするとは思わなかったと", ["%CALLNAME%"])],
            new ProviderSettings
            {
                ProviderType = TranslationProviderType.LmStudio,
                TargetLanguage = "ko",
                DisableThinking = true,
            },
            CancellationToken.None);

        Assert.Equal("__PH0__는 페니스를 찔리면서, 설마 정말로 다른 아이 옆에서 하게 될 줄은 몰랐다고", result.Translations["id-1"]);
    }

    [Fact]
    public async Task LmStudioProvider_RecoversFromSourcePipeAndExplanationNoise()
    {
        var factory = new FakeHttpClientFactory(_ => JsonResponse("""
{"choices":[{"message":{"content":"가랑이/밑서구/음부(context dependent, but for 'Omat-a' usually refers to crotch area): 밑서구/사타구니/음부 (Depending on context)\n\nSince the prompt asks not even for labels or separators if there is only one segment:\n\nおまた|밑서구"} }]}
"""));
        var provider = new OpenAiCompatibleTranslationProvider(factory, true);

        var result = await provider.TranslateAsync(
            [new ProtectedSegment("id-1", "おまた", "おまた", [])],
            new ProviderSettings
            {
                ProviderType = TranslationProviderType.LmStudio,
                TargetLanguage = "ko",
                DisableThinking = true,
            },
            CancellationToken.None);

        Assert.Equal("밑서구", result.Translations["id-1"]);
    }

    [Fact]
    public async Task LmStudioProvider_RecoversFromWrappedSourceAndNote()
    {
        var factory = new FakeHttpClientFactory(_ => JsonResponse("""
{"choices":[{"message":{"content":"엉덩이 구멍(또는 항문 성교/항문 가리키기 등 문맥에 따라 다름))\n\n*Note: Since \"おしりまんこ\" is a slang term, the translation depends on context. I will provide a natural Korean equivalent for game scripts.*\n\n|おしりまんこ|\n엉덩이 구멍"} }]}
"""));
        var provider = new OpenAiCompatibleTranslationProvider(factory, true);

        var result = await provider.TranslateAsync(
            [new ProtectedSegment("id-1", "おしりまんこ", "おしりまんこ", [])],
            new ProviderSettings
            {
                ProviderType = TranslationProviderType.LmStudio,
                TargetLanguage = "ko",
                DisableThinking = true,
            },
            CancellationToken.None);

        Assert.Equal("엉덩이 구멍", result.Translations["id-1"]);
    }

    [Fact]
    public async Task LmStudioProvider_RemovesPromptEchoAndPipeWrapper()
    {
        var factory = new FakeHttpClientFactory(_ => JsonResponse("""
{"choices":[{"message":{"content":"|대상 언어: 한국어 (ko, 한국어)|\n;MILKPOINT에서 중복되므로 줄이지 말 것|"} }]}
"""));
        var provider = new OpenAiCompatibleTranslationProvider(factory, true);

        var result = await provider.TranslateAsync(
            [new ProtectedSegment("id-1", ";MILKPOINTで被るので略さない", ";MILKPOINTで被るので略さない", [])],
            new ProviderSettings
            {
                ProviderType = TranslationProviderType.LmStudio,
                TargetLanguage = "ko",
                DisableThinking = true,
            },
            CancellationToken.None);

        Assert.Equal(";MILKPOINT에서 중복되므로 줄이지 말 것", result.Translations["id-1"]);
    }

    [Fact]
    public async Task LmStudioProvider_RemovesOuterPipesFromSingleSegment()
    {
        var factory = new FakeHttpClientFactory(_ => JsonResponse("""
{"choices":[{"message":{"content":"|후보 비율 / 유두 색깔 / 갈색 피부 / 합계|"} }]}
"""));
        var provider = new OpenAiCompatibleTranslationProvider(factory, true);

        var result = await provider.TranslateAsync(
            [new ProtectedSegment("id-1", "候補割合／乳首の色／褐色肌／合計", "候補割合／乳首の色／褐色肌／合計", [])],
            new ProviderSettings
            {
                ProviderType = TranslationProviderType.LmStudio,
                TargetLanguage = "ko",
                DisableThinking = true,
            },
            CancellationToken.None);

        Assert.Equal("후보 비율 / 유두 색깔 / 갈색 피부 / 합계", result.Translations["id-1"]);
    }

    [Fact]
    public async Task LmStudioProvider_AllowsAlternativeCandidatesForManualReview()
    {
        var factory = new FakeHttpClientFactory(_ => JsonResponse("""
{"choices":[{"message":{"content":"모찌모찌/쫀득쫀득"} }]}
"""));
        var provider = new OpenAiCompatibleTranslationProvider(factory, true);

        var result = await provider.TranslateAsync(
            [new ProtectedSegment("id-1", "もちもち", "もちもち", [])],
            new ProviderSettings
            {
                ProviderType = TranslationProviderType.LmStudio,
                TargetLanguage = "ko",
                DisableThinking = true,
            },
            CancellationToken.None);

        Assert.Equal("모찌모찌/쫀득쫀득", result.Translations["id-1"]);
    }

    [Fact]
    public async Task LmStudioProvider_RejectsPromptLeakAndAsciiGarbage()
    {
        var factory = new FakeHttpClientFactory(_ => JsonResponse("""
{"choices":[{"message":{"content":"정멸(고등어/정치류의 일종)|이와시|전갱이_세력s_|Iwashi|_군함조 떼 (가칭)_... | Iwashi |_ ... _!  The translation engine for Emuera game scripts."} }]}
"""));
        var provider = new OpenAiCompatibleTranslationProvider(factory, true);

        var exception = await Assert.ThrowsAsync<TranslationProviderException>(() => provider.TranslateAsync(
            [new ProtectedSegment("id-1", "いわし", "いわし", [])],
            new ProviderSettings
            {
                ProviderType = TranslationProviderType.LmStudio,
                TargetLanguage = "ko",
                DisableThinking = true,
            },
            CancellationToken.None));

        Assert.Equal(TranslationErrorKind.Validation, exception.Kind);
    }

    [Fact]
    public async Task OpenAiProvider_LogsRequestAndResponseInOrder()
    {
        var factory = new FakeHttpClientFactory(_ => JsonResponse("""
{"choices":[{"message":{"content":"{\"translations\":[{\"id\":\"id-1\",\"translated\":\"로그 테스트\"}]}"}}]}
"""));
        var logger = new FakeRequestResponseLogger();
        var provider = new OpenAiCompatibleTranslationProvider(factory, false, logger);

        var result = await provider.TranslateAsync(
            [new ProtectedSegment("id-1", "원문", "원문", [])],
            new ProviderSettings
            {
                ProviderType = TranslationProviderType.OpenAi,
                ApiKey = "test-key",
                TargetLanguage = "ko",
            },
            CancellationToken.None);

        Assert.Equal("로그 테스트", result.Translations["id-1"]);
        Assert.Equal(["REQUEST", "RESPONSE"], logger.Events.Select(item => item.Kind));
        Assert.Contains("원문", logger.Events[0].Content, StringComparison.Ordinal);
        Assert.Contains("Korean (ko, 한국어)", logger.Events[0].Content, StringComparison.Ordinal);
        Assert.DoesNotContain("Input JSON:", logger.Events[0].Content, StringComparison.Ordinal);
        Assert.Contains("Choose exactly one final translation", logger.Events[0].Content, StringComparison.Ordinal);
        Assert.Contains("prefer a Hangul reading of the Japanese term", logger.Events[0].Content, StringComparison.Ordinal);
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    private static HttpResponseMessage ErrorResponse(HttpStatusCode statusCode, string body)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
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

    private sealed class FakeRequestResponseLogger : IRequestResponseLogger
    {
        public string LogFilePath => "fake.log";

        public List<(string Kind, string Provider, string Endpoint, string Content)> Events { get; } = [];

        public void LogRequest(string providerName, string endpoint, string content, IReadOnlyDictionary<string, string>? headers = null)
        {
            Events.Add(("REQUEST", providerName, endpoint, content));
        }

        public void LogResponse(string providerName, string endpoint, int statusCode, string content, IReadOnlyDictionary<string, string>? headers = null)
        {
            Events.Add(("RESPONSE", providerName, endpoint, content));
        }

        public void LogError(string providerName, string endpoint, string message)
        {
            Events.Add(("ERROR", providerName, endpoint, message));
        }
    }
}
