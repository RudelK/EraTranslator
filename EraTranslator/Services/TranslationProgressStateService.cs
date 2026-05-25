using System.Text.Encodings.Web;
using System.Text.Json;

namespace EraTranslator.Services;

public sealed class TranslationProgressStateService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string GetProgressFilePath(string gameDirectory)
    {
        if (string.IsNullOrWhiteSpace(gameDirectory))
        {
            return string.Empty;
        }

        return Path.Combine(gameDirectory, ".era-translator", "translation-progress.json");
    }

    public int Apply(string gameDirectory, IEnumerable<ExtractedTextItem> items)
    {
        if (string.IsNullOrWhiteSpace(gameDirectory))
        {
            return 0;
        }

        var snapshot = Load(gameDirectory);
        if (snapshot.Items.Count == 0)
        {
            return 0;
        }

        var stateMap = snapshot.Items.ToDictionary(item => item.SegmentId, StringComparer.Ordinal);
        var restoredCount = 0;

        foreach (var item in items)
        {
            if (!stateMap.TryGetValue(item.SegmentId, out var state))
            {
                continue;
            }

            item.ApplyPersistedState(state);
            item.ReferenceImpactCount = state.ReferenceImpactCount;
            item.RequiresReferenceRewrite = state.RequiresReferenceRewrite;
            item.ReferenceResolutionStatus = state.ReferenceResolutionStatus;
            restoredCount++;
        }

        return restoredCount;
    }

    public TranslationProgressState Load(string gameDirectory)
    {
        var path = GetProgressFilePath(gameDirectory);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return new TranslationProgressState();
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<TranslationProgressState>(json, JsonOptions) ?? new TranslationProgressState();
        }
        catch
        {
            return new TranslationProgressState();
        }
    }

    public void Save(string gameDirectory, IEnumerable<ExtractedTextItem> items)
    {
        var path = GetProgressFilePath(gameDirectory);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var snapshot = new TranslationProgressState
        {
            SavedAtUtc = DateTimeOffset.UtcNow,
            Items = items
                .Where(item => item.HasPersistableState)
                .Select(item => new TranslationProgressItemState
                {
                    SegmentId = item.SegmentId,
                    Status = item.Status,
                    ValidationStatus = item.ValidationStatus,
                    TranslationError = item.TranslationError,
                    TranslatedText = item.TranslatedText,
                    CanSave = item.CanSave,
                    ReferenceImpactCount = item.ReferenceImpactCount,
                    RequiresReferenceRewrite = item.RequiresReferenceRewrite,
                    ReferenceResolutionStatus = item.ReferenceResolutionStatus,
                })
                .ToList(),
        };

        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        File.WriteAllText(path, json);
    }

    public void Delete(string gameDirectory)
    {
        var path = GetProgressFilePath(gameDirectory);
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
