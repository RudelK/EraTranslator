using System.Text;
using EraTranslator.Models;

namespace EraTranslator.Services;

public interface IDictionaryHitLogger
{
    void LogHit(DictionaryHitLogEntry entry);
}

public sealed class FileDictionaryHitLogger(string? baseDirectory = null) : IDictionaryHitLogger
{
    private static readonly object FileLock = new();
    private static long _sequence;

    public string LogFilePath { get; } = Path.Combine(baseDirectory ?? AppContext.BaseDirectory, "EraTranslator.dictionary-hit.log");

    public void LogHit(DictionaryHitLogEntry entry)
    {
        var sequence = Interlocked.Increment(ref _sequence);
        var builder = new StringBuilder();
        builder.AppendLine($"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}] #{sequence} DICTIONARY_HIT");
        builder.AppendLine($"Source: {entry.TranslationSource}");
        builder.AppendLine($"MatchKind: {entry.MatchKind}");
        if (!string.IsNullOrWhiteSpace(entry.DictionaryStore))
        {
            builder.AppendLine($"DictionaryStore: {entry.DictionaryStore}");
        }

        builder.AppendLine($"Original: {entry.OriginalText}");
        builder.AppendLine($"Translated: {entry.TranslatedText}");
        builder.AppendLine($"MatchedTerm: {entry.MatchedTerm}");
        if (!string.IsNullOrWhiteSpace(entry.SourceUrl))
        {
            builder.AppendLine($"SourceUrl: {entry.SourceUrl}");
        }

        builder.AppendLine($"File: {entry.RelativePath}");
        builder.AppendLine($"Line: {entry.LineNumber}");
        builder.AppendLine($"SegmentId: {entry.SegmentId}");
        if (!string.IsNullOrWhiteSpace(entry.SourceKey))
        {
            builder.AppendLine($"SourceKey: {entry.SourceKey}");
        }

        if (!string.IsNullOrWhiteSpace(entry.SymbolNamespace) || !string.IsNullOrWhiteSpace(entry.OriginalSymbolKey))
        {
            builder.AppendLine($"Symbol: {entry.SymbolNamespace}:{entry.OriginalSymbolKey}");
        }

        builder.AppendLine($"AffectedItems: {entry.AffectedItemCount}");
        builder.AppendLine($"ForceReview: {entry.ForceReview}");
        builder.AppendLine($"ReviewRequired: {entry.ReviewRequired}");
        if (entry.PersistedToNaverDictionary)
        {
            builder.AppendLine("PersistedToNaverDictionary: true");
        }

        if (!string.IsNullOrWhiteSpace(entry.ReviewReason))
        {
            builder.AppendLine($"ReviewReason: {entry.ReviewReason}");
        }

        builder.AppendLine(new string('-', 80));

        lock (FileLock)
        {
            File.AppendAllText(LogFilePath, builder.ToString(), Encoding.UTF8);
        }
    }
}

public sealed record DictionaryHitLogEntry(
    string SegmentId,
    string RelativePath,
    int LineNumber,
    string OriginalText,
    string TranslatedText,
    string TranslationSource,
    string MatchKind,
    string MatchedTerm,
    string? SourceKey,
    string SymbolNamespace,
    string OriginalSymbolKey,
    int AffectedItemCount,
    bool ForceReview,
    string ReviewReason,
    string DictionaryStore = "",
    bool PersistedToNaverDictionary = false,
    string SourceUrl = "",
    bool ReviewRequired = false);
