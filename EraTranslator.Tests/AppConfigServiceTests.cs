using EraTranslator.Models;
using EraTranslator.Services;

namespace EraTranslator.Tests;

public sealed class AppConfigServiceTests : IDisposable
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
    public void SaveAndLoad_RoundTripsConfigInTargetDirectory()
    {
        Directory.CreateDirectory(_rootPath);
        var service = new AppConfigService(_rootPath);
        var expected = new AppConfig
        {
            GameDirectory = @"D:\Games\Emuera",
            OutputDirectory = @"D:\Games\Emuera\translated",
            SaveMode = SaveMode.InPlaceWithBackup,
            ProviderType = TranslationProviderType.LmStudio,
            BaseUrl = "http://127.0.0.1:1234/v1",
            Model = "gemma",
            SourceLanguage = "ja",
            TargetLanguage = "ko",
            BatchSize = 5,
            RetryCount = 3,
            Temperature = 0.7,
            DisableThinking = false,
            EnableRequestResponseLogging = true,
            EnableResultStateLogging = true,
            ExcludeNonSourceText = true,
            RefreshGridDuringTranslatedTextEdit = true,
            SystemPromptTemplate = "system",
            RetryPromptTemplate = "retry",
            PapagoClientId = "papago-id",
            PapagoClientSecret = "papago-secret",
            EzTransInstallationPath = @"C:\Utils\ezTransXPggudor",
            EzTransProcessCount = 4,
            ProviderApiKeys = new Dictionary<TranslationProviderType, string>
            {
                [TranslationProviderType.OpenAi] = "openai-key",
            },
        };

        service.Save(expected);
        var actual = service.Load();
        var configText = File.ReadAllText(service.ConfigPath);

        Assert.Equal(Path.Combine(_rootPath, "EraTranslator.config.json"), service.ConfigPath);
        Assert.Equal(Path.Combine(_rootPath, "EraTranslator.secrets.dat"), service.SecretPath);
        Assert.Equal(expected.GameDirectory, actual.GameDirectory);
        Assert.Equal(expected.OutputDirectory, actual.OutputDirectory);
        Assert.Equal(expected.SaveMode, actual.SaveMode);
        Assert.Equal(expected.ProviderType, actual.ProviderType);
        Assert.Equal(expected.BaseUrl, actual.BaseUrl);
        Assert.Equal(expected.Model, actual.Model);
        Assert.Equal(expected.SourceLanguage, actual.SourceLanguage);
        Assert.Equal(expected.TargetLanguage, actual.TargetLanguage);
        Assert.Equal(expected.BatchSize, actual.BatchSize);
        Assert.Equal(expected.RetryCount, actual.RetryCount);
        Assert.Equal(expected.Temperature, actual.Temperature);
        Assert.Equal(expected.DisableThinking, actual.DisableThinking);
        Assert.Equal(expected.EnableRequestResponseLogging, actual.EnableRequestResponseLogging);
        Assert.Equal(expected.EnableResultStateLogging, actual.EnableResultStateLogging);
        Assert.Equal(expected.ExcludeNonSourceText, actual.ExcludeNonSourceText);
        Assert.Equal(expected.RefreshGridDuringTranslatedTextEdit, actual.RefreshGridDuringTranslatedTextEdit);
        Assert.Equal(expected.SystemPromptTemplate, actual.SystemPromptTemplate);
        Assert.Equal(expected.RetryPromptTemplate, actual.RetryPromptTemplate);
        Assert.Equal(expected.PapagoClientId, actual.PapagoClientId);
        Assert.Equal(expected.PapagoClientSecret, actual.PapagoClientSecret);
        Assert.Equal(expected.EzTransInstallationPath, actual.EzTransInstallationPath);
        Assert.Equal(expected.EzTransProcessCount, actual.EzTransProcessCount);
        Assert.Equal("openai-key", actual.ProviderApiKeys[TranslationProviderType.OpenAi]);
        Assert.DoesNotContain("openai-key", configText, StringComparison.Ordinal);
        Assert.DoesNotContain("papago-secret", configText, StringComparison.Ordinal);
        Assert.True(File.Exists(service.SecretPath));
    }

    [Fact]
    public void Load_NormalizesLegacyPlaceholderExampleInPrompts()
    {
        Directory.CreateDirectory(_rootPath);
        var service = new AppConfigService(_rootPath);
        File.WriteAllText(
            service.ConfigPath,
            """
            {
              "SystemPromptTemplate": "Preserve [[[ERA_PH_0]]] exactly.",
              "RetryPromptTemplate": "Retry [[[ERA_PH_0]]] exactly."
            }
            """);

        var actual = service.Load();

        Assert.Equal("Preserve __PH0__ exactly.", actual.SystemPromptTemplate);
        Assert.Equal("Retry __PH0__ exactly.", actual.RetryPromptTemplate);
    }

    [Fact]
    public void Load_MigratesLegacyPlaintextSecretsIntoProtectedStore()
    {
        Directory.CreateDirectory(_rootPath);
        var service = new AppConfigService(_rootPath);
        File.WriteAllText(
            service.ConfigPath,
            """
            {
              "PapagoClientSecret": "legacy-secret",
              "ProviderApiKeys": {
                "OpenAi": "legacy-openai-key"
              }
            }
            """);

        var actual = service.Load();
        var configText = File.ReadAllText(service.ConfigPath);

        Assert.Equal("legacy-secret", actual.PapagoClientSecret);
        Assert.Equal("legacy-openai-key", actual.ProviderApiKeys[TranslationProviderType.OpenAi]);
        Assert.DoesNotContain("legacy-secret", configText, StringComparison.Ordinal);
        Assert.DoesNotContain("legacy-openai-key", configText, StringComparison.Ordinal);
        Assert.True(File.Exists(service.SecretPath));
    }
}
