using System.Net.Http;

namespace EraTranslator.Services;

public interface ISimpleHttpClientFactory
{
    HttpClient CreateClient(string name);
}
