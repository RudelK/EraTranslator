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
    public void Load_ReturnsResultStateLoggingDisabledByDefault()
    {
        Directory.CreateDirectory(_rootPath);
        var service = new AppConfigService(_rootPath);

        var actual = service.Load();

        Assert.False(actual.EnableResultStateLogging);
        Assert.False(actual.EnableDictionaryHitLogging);
    }

    [Fact]
    public void Load_ReturnsExcludeNonSourceTextEnabledByDefault()
    {
        Directory.CreateDirectory(_rootPath);
        var service = new AppConfigService(_rootPath);

        var actual = service.Load();

        Assert.True(actual.ExcludeNonSourceText);
    }

    [Fact]
    public void Load_IncludesFullWidthColonInDefaultProtectedCharacters()
    {
        Directory.CreateDirectory(_rootPath);
        var service = new AppConfigService(_rootPath);

        var actual = service.Load();

        Assert.Contains("：", actual.ProtectedFullWidthCharacters, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_UpgradesLegacyDefaultProtectedCharacters()
    {
        Directory.CreateDirectory(_rootPath);
        var service = new AppConfigService(_rootPath);
        File.WriteAllText(
            service.ConfigPath,
            """
            {
              "ProtectedFullWidthCharacters": "／【】＜＞「」（）『』％"
            }
            """);

        var actual = service.Load();

        Assert.Equal(new AppConfig().ProtectedFullWidthCharacters, actual.ProtectedFullWidthCharacters);
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
            ProjectMode = ProjectMode.Team,
            TeamServerUrl = "http://localhost:8000",
            TeamProjectId = "project-1",
            TeamDisplayName = "translator",
            ClientId = "client-1",
            TeamWorkspaceRoot = @"D:\EraTranslatorTeam",
            TeamAuthToken = "team-token",
            ProviderType = TranslationProviderType.LmStudio,
            BaseUrl = "http://127.0.0.1:1234/v1",
            Model = "gemma",
            LmStudioPresetProfile = LmStudioPresetProfile.Qwen35_9B,
            PromptProfile = PromptProfile.HyMt2,
            SourceLanguage = "ja",
            TargetLanguage = "ko",
            BatchSize = 5,
            RetryCount = 3,
            Temperature = 0.7,
            TopP = 0.85,
            TopK = -1,
            RepeatPenalty = 1.1,
            PresencePenalty = 1.5,
            Seed = 1234,
            MaxTokens = 512,
            DisableThinking = false,
            EnableRequestResponseLogging = true,
            EnableResultStateLogging = true,
            EnableDictionaryHitLogging = true,
            EnableNaverJapaneseDictionaryLookup = true,
            ExcludeNonSourceText = true,
            RefreshGridDuringTranslatedTextEdit = true,
            ProtectedFullWidthCharacters = "（）",
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
        Assert.Equal(expected.ProjectMode, actual.ProjectMode);
        Assert.Equal(expected.TeamServerUrl, actual.TeamServerUrl);
        Assert.Equal(expected.TeamProjectId, actual.TeamProjectId);
        Assert.Equal(expected.TeamDisplayName, actual.TeamDisplayName);
        Assert.Equal(expected.ClientId, actual.ClientId);
        Assert.Equal(expected.TeamWorkspaceRoot, actual.TeamWorkspaceRoot);
        Assert.Equal(expected.TeamAuthToken, actual.TeamAuthToken);
        Assert.Equal(expected.ProviderType, actual.ProviderType);
        Assert.Equal(expected.BaseUrl, actual.BaseUrl);
        Assert.Equal(expected.Model, actual.Model);
        Assert.Equal(expected.LmStudioPresetProfile, actual.LmStudioPresetProfile);
        Assert.Equal(expected.PromptProfile, actual.PromptProfile);
        Assert.Equal(expected.SourceLanguage, actual.SourceLanguage);
        Assert.Equal(expected.TargetLanguage, actual.TargetLanguage);
        Assert.Equal(expected.BatchSize, actual.BatchSize);
        Assert.Equal(expected.RetryCount, actual.RetryCount);
        Assert.Equal(expected.Temperature, actual.Temperature);
        Assert.Equal(expected.TopP, actual.TopP);
        Assert.Equal(expected.TopK, actual.TopK);
        Assert.Equal(expected.RepeatPenalty, actual.RepeatPenalty);
        Assert.Equal(expected.PresencePenalty, actual.PresencePenalty);
        Assert.Equal(expected.Seed, actual.Seed);
        Assert.Equal(expected.MaxTokens, actual.MaxTokens);
        Assert.Equal(expected.DisableThinking, actual.DisableThinking);
        Assert.Equal(expected.EnableRequestResponseLogging, actual.EnableRequestResponseLogging);
        Assert.Equal(expected.EnableResultStateLogging, actual.EnableResultStateLogging);
        Assert.Equal(expected.EnableDictionaryHitLogging, actual.EnableDictionaryHitLogging);
        Assert.Equal(expected.EnableNaverJapaneseDictionaryLookup, actual.EnableNaverJapaneseDictionaryLookup);
        Assert.Equal(expected.ExcludeNonSourceText, actual.ExcludeNonSourceText);
        Assert.Equal(expected.RefreshGridDuringTranslatedTextEdit, actual.RefreshGridDuringTranslatedTextEdit);
        Assert.Equal(expected.ProtectedFullWidthCharacters, actual.ProtectedFullWidthCharacters);
        Assert.Equal(expected.SystemPromptTemplate, actual.SystemPromptTemplate);
        Assert.Equal(expected.RetryPromptTemplate, actual.RetryPromptTemplate);
        Assert.Equal(expected.PapagoClientId, actual.PapagoClientId);
        Assert.Equal(expected.PapagoClientSecret, actual.PapagoClientSecret);
        Assert.Equal(expected.EzTransInstallationPath, actual.EzTransInstallationPath);
        Assert.Equal(expected.EzTransProcessCount, actual.EzTransProcessCount);
        Assert.Equal("openai-key", actual.ProviderApiKeys[TranslationProviderType.OpenAi]);
        Assert.DoesNotContain("openai-key", configText, StringComparison.Ordinal);
        Assert.DoesNotContain("papago-secret", configText, StringComparison.Ordinal);
        Assert.DoesNotContain("team-token", configText, StringComparison.Ordinal);
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

    [Fact]
    public void Load_GeneratesAndPersistsClientIdWhenMissing()
    {
        Directory.CreateDirectory(_rootPath);
        var service = new AppConfigService(_rootPath);

        var first = service.Load();
        var second = service.Load();

        Assert.False(string.IsNullOrWhiteSpace(first.ClientId));
        Assert.Equal(first.ClientId, second.ClientId);
        Assert.Contains(first.ClientId, File.ReadAllText(service.ConfigPath), StringComparison.Ordinal);
    }
}
