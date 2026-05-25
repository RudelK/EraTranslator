namespace EraTranslator.Services;

public interface IRequestResponseLogger
{
    string LogFilePath { get; }

    void LogRequest(string providerName, string endpoint, string content, IReadOnlyDictionary<string, string>? headers = null);

    void LogResponse(string providerName, string endpoint, int statusCode, string content, IReadOnlyDictionary<string, string>? headers = null);

    void LogError(string providerName, string endpoint, string message);
}
