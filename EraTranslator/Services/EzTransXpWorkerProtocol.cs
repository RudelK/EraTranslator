using System.Text;
using System.Text.Json;

namespace EraTranslator.Services;

internal static class EzTransXpWorkerProtocol
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string EncodeRequest(EzTransXpWorkerRequest request)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, JsonOptions)));
    }

    public static EzTransXpWorkerResponse DecodeResponse(string payload)
    {
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
        return JsonSerializer.Deserialize<EzTransXpWorkerResponse>(json, JsonOptions)
            ?? throw new InvalidOperationException("EzTransXP 워커 응답을 읽지 못했습니다.");
    }

    public static string EncodeResponse(EzTransXpWorkerResponse response)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(response, JsonOptions)));
    }

    public static EzTransXpWorkerRequest DecodeRequest(string payload)
    {
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
        return JsonSerializer.Deserialize<EzTransXpWorkerRequest>(json, JsonOptions)
            ?? throw new InvalidOperationException("EzTransXP 워커 요청을 읽지 못했습니다.");
    }
}

internal sealed record EzTransXpWorkerRequest(string RequestId, IReadOnlyList<string> Texts);

internal sealed record EzTransXpWorkerResponse(string RequestId, IReadOnlyList<string>? Texts, string? Error);
