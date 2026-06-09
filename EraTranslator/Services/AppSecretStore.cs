using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EraTranslator.Services;

public interface IAppSecretStore
{
    string FilePath { get; }

    AppSecrets Load();

    void Save(AppSecrets secrets);
}

public sealed class ProtectedAppSecretStore(string? baseDirectory = null, string fileName = "EraTranslator.secrets.dat") : IAppSecretStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public string FilePath { get; } = Path.Combine(baseDirectory ?? AppContext.BaseDirectory, fileName);

    public AppSecrets Load()
    {
        if (!File.Exists(FilePath))
        {
            return new AppSecrets();
        }

        try
        {
            var protectedBytes = File.ReadAllBytes(FilePath);
            var plainBytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
            var json = Encoding.UTF8.GetString(plainBytes);
            return JsonSerializer.Deserialize<AppSecrets>(json, JsonOptions) ?? new AppSecrets();
        }
        catch
        {
            return new AppSecrets();
        }
    }

    public void Save(AppSecrets secrets)
    {
        if (!secrets.HasAnySecrets)
        {
            if (File.Exists(FilePath))
            {
                File.Delete(FilePath);
            }

            return;
        }

        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(secrets, JsonOptions);
        var plainBytes = Encoding.UTF8.GetBytes(json);
        var protectedBytes = ProtectedData.Protect(plainBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(FilePath, protectedBytes);
    }
}

public sealed class AppSecrets
{
    public string PapagoClientSecret { get; init; } = string.Empty;

    public string TeamAuthToken { get; init; } = string.Empty;

    public Dictionary<TranslationProviderType, string> ProviderApiKeys { get; init; } = [];

    public bool HasAnySecrets =>
        !string.IsNullOrWhiteSpace(PapagoClientSecret)
        || !string.IsNullOrWhiteSpace(TeamAuthToken)
        || ProviderApiKeys.Any(pair => !string.IsNullOrWhiteSpace(pair.Value));
}
