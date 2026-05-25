using System.Text.Encodings.Web;
using System.Text.Json;
using EraTranslator.Models;

namespace EraTranslator.Services;

public sealed class AppConfigService(string? baseDirectory = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string ConfigPath { get; } = Path.Combine(baseDirectory ?? AppContext.BaseDirectory, "EraTranslator.config.json");

    public AppConfig Load()
    {
        if (!File.Exists(ConfigPath))
        {
            return new AppConfig();
        }

        try
        {
            var json = File.ReadAllText(ConfigPath);
            var loaded = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
            return new AppConfig
            {
                GameDirectory = loaded.GameDirectory,
                OutputDirectory = loaded.OutputDirectory,
                SaveMode = loaded.SaveMode,
                ProviderType = loaded.ProviderType,
                BaseUrl = loaded.BaseUrl,
                Model = loaded.Model,
                SourceLanguage = loaded.SourceLanguage,
                TargetLanguage = loaded.TargetLanguage,
                BatchSize = loaded.BatchSize,
                RetryCount = loaded.RetryCount,
                Temperature = loaded.Temperature,
                DisableThinking = loaded.DisableThinking,
                EnableRequestResponseLogging = loaded.EnableRequestResponseLogging,
                SystemPromptTemplate = NormalizePromptPlaceholders(loaded.SystemPromptTemplate),
                RetryPromptTemplate = NormalizePromptPlaceholders(loaded.RetryPromptTemplate),
                ExcludeNonSourceText = loaded.ExcludeNonSourceText,
                PapagoClientId = loaded.PapagoClientId,
                PapagoClientSecret = loaded.PapagoClientSecret,
                ProviderApiKeys = loaded.ProviderApiKeys,
            };
        }
        catch
        {
            return new AppConfig();
        }
    }

    public void Save(AppConfig config)
    {
        var directory = Path.GetDirectoryName(ConfigPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(ConfigPath, json);
    }

    private static string NormalizePromptPlaceholders(string template)
    {
        return string.IsNullOrWhiteSpace(template)
            ? template
            : template.Replace("[[[ERA_PH_0]]]", "__PH0__", StringComparison.Ordinal);
    }
}
