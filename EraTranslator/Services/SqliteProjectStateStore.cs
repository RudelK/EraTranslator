using System.Text;
using Microsoft.Data.Sqlite;
using EraTranslator.Models;

namespace EraTranslator.Services;

public class SqliteProjectStateStore
{
    private const int SchemaVersion = 1;

    public string GetDatabasePath(string projectDataDirectory)
    {
        if (string.IsNullOrWhiteSpace(projectDataDirectory))
        {
            return string.Empty;
        }

        return Path.Combine(projectDataDirectory, ".era-translator", "state.db");
    }

    public bool Exists(string projectDataDirectory)
    {
        var path = GetDatabasePath(projectDataDirectory);
        return !string.IsNullOrWhiteSpace(path) && File.Exists(path);
    }

    public void SaveScanSession(ScanSession session, string projectDataDirectory)
    {
        var connection = OpenConnection(projectDataDirectory);
        using (connection)
        {
            EnsureSchema(connection);
            using var transaction = connection.BeginTransaction();
            SetProjectMeta(connection, transaction, "game_root", session.GameRoot);
            SetProjectMeta(connection, transaction, "saved_at_utc", DateTimeOffset.UtcNow.ToString("O"));
            SetProjectMeta(connection, transaction, "last_scan_saved_at_utc", DateTimeOffset.UtcNow.ToString("O"));

            ExecuteNonQuery(connection, transaction, "DELETE FROM symbol_reference_candidates;");
            ExecuteNonQuery(connection, transaction, "DELETE FROM symbol_references;");
            ExecuteNonQuery(connection, transaction, "DELETE FROM variable_literal_occurrences;");
            ExecuteNonQuery(connection, transaction, "DELETE FROM identifier_occurrences;");
            ExecuteNonQuery(connection, transaction, "DELETE FROM scan_warnings;");
            ExecuteNonQuery(connection, transaction, "DELETE FROM glossary_cache_entries;");
            ExecuteNonQuery(connection, transaction, "DELETE FROM segments;");
            ExecuteNonQuery(connection, transaction, "DELETE FROM documents;");
            ExecuteNonQuery(connection, transaction, "DELETE FROM session_metrics;");
            ExecuteNonQuery(connection, transaction, "DELETE FROM josa_supported_particles;");
            ExecuteNonQuery(connection, transaction, "DELETE FROM josa_package_info;");

            SaveJosaPackageInfo(connection, transaction, session.JosaPackageInfo);
            using var commandSet = new ScanSessionSaveCommandSet(connection, transaction);

            foreach (var document in session.Documents.Values)
            {
                commandSet.SaveDocument(document);
            }

            foreach (var metric in session.Metrics)
            {
                commandSet.SaveMetric(metric.Key, metric.Value);
            }

            transaction.Commit();
        }
    }

    public ScanSession? LoadScanSession(string projectDataDirectory)
    {
        if (!Exists(projectDataDirectory))
        {
            return null;
        }

        try
        {
            using var connection = OpenConnection(projectDataDirectory);
            EnsureSchema(connection);

            var documentIds = ReadStringList(connection, "SELECT document_id FROM documents ORDER BY relative_path, document_id;");
            if (documentIds.Count == 0)
            {
                return null;
            }

            var gameRoot = ReadProjectMeta(connection, "game_root");
            if (string.IsNullOrWhiteSpace(gameRoot))
            {
                gameRoot = projectDataDirectory;
            }

            var session = new ScanSession
            {
                GameRoot = gameRoot,
                JosaPackageInfo = LoadJosaPackageInfo(connection),
            };

            var documents = LoadDocuments(connection);
            foreach (var document in documents.Values)
            {
                session.Documents[document.DocumentId] = document;
            }

            session.Items.AddRange(BuildItemsFromDocuments(session.Documents.Values));
            LoadMetrics(connection, session);
            new SymbolReferenceAnalyzer().Analyze(session);
            return session;
        }
        catch
        {
            return null;
        }
    }

    public virtual void SaveTranslationProgressSnapshot(string projectDataDirectory, IEnumerable<ExtractedTextItem> items)
    {
        var snapshot = BuildProgressSnapshot(items);
        SaveTranslationProgressSnapshot(projectDataDirectory, snapshot);
    }

    public void SaveTranslationProgressSnapshot(string projectDataDirectory, TranslationProgressState snapshot)
    {
        using var connection = OpenConnection(projectDataDirectory);
        EnsureSchema(connection);
        using var transaction = connection.BeginTransaction();
        SetProgressSavedMeta(connection, transaction, snapshot.SavedAtUtc);

        var existingIds = ReadStringList(connection, "SELECT segment_id FROM translation_progress;");
        var targetIds = snapshot.Items
            .Select(item => item.SegmentId)
            .Where(segmentId => !string.IsNullOrWhiteSpace(segmentId))
            .ToHashSet(StringComparer.Ordinal);

        UpsertTranslationProgressItems(connection, transaction, snapshot.Items, snapshot.SavedAtUtc);

        foreach (var staleId in existingIds.Where(existingId => !targetIds.Contains(existingId)))
        {
            using var deleteCommand = connection.CreateCommand();
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = "DELETE FROM translation_progress WHERE segment_id = $segment_id;";
            deleteCommand.Parameters.AddWithValue("$segment_id", staleId);
            deleteCommand.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public virtual void UpsertTranslationProgressItems(string projectDataDirectory, IEnumerable<ExtractedTextItem> items)
    {
        var progressItems = BuildProgressItemStates(items);
        if (progressItems.Count == 0)
        {
            return;
        }

        var savedAtUtc = DateTimeOffset.UtcNow;
        using var connection = OpenConnection(projectDataDirectory);
        EnsureSchema(connection);
        using var transaction = connection.BeginTransaction();
        SetProgressSavedMeta(connection, transaction, savedAtUtc);
        UpsertTranslationProgressItems(connection, transaction, progressItems, savedAtUtc);
        transaction.Commit();
    }

    public virtual void DeleteTranslationProgressItems(string projectDataDirectory, IEnumerable<string> segmentIds)
    {
        var targetIds = segmentIds
            .Where(segmentId => !string.IsNullOrWhiteSpace(segmentId))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (targetIds.Count == 0)
        {
            return;
        }

        var savedAtUtc = DateTimeOffset.UtcNow;
        using var connection = OpenConnection(projectDataDirectory);
        EnsureSchema(connection);
        using var transaction = connection.BeginTransaction();
        SetProgressSavedMeta(connection, transaction, savedAtUtc);

        foreach (var segmentId in targetIds)
        {
            using var deleteCommand = connection.CreateCommand();
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = "DELETE FROM translation_progress WHERE segment_id = $segment_id;";
            deleteCommand.Parameters.AddWithValue("$segment_id", segmentId);
            deleteCommand.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public IReadOnlyList<GlossaryCacheEntry> LoadGlossaryCache(string projectDataDirectory)
    {
        if (!Exists(projectDataDirectory))
        {
            return [];
        }

        using var connection = OpenConnection(projectDataDirectory);
        EnsureSchema(connection);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                segment_id,
                source,
                target,
                file_type,
                source_status,
                segment_type,
                symbol_namespace,
                is_reference_bearing_key,
                rendered_length,
                static_score,
                first_char,
                phase_mask,
                eligibility_hash,
                scope_version,
                updated_at_utc
            FROM glossary_cache_entries
            ORDER BY source, target, segment_id;
            """;
        using var reader = command.ExecuteReader();
        var entries = new List<GlossaryCacheEntry>();
        while (reader.Read())
        {
            entries.Add(new GlossaryCacheEntry(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetInt32(7) != 0,
                reader.GetInt32(8),
                reader.GetInt32(9),
                reader.GetString(10),
                reader.GetInt32(11),
                reader.GetString(12),
                reader.GetInt32(13),
                DateTimeOffset.TryParse(reader.GetString(14), out var updatedAtUtc)
                    ? updatedAtUtc
                    : DateTimeOffset.MinValue));
        }

        return entries;
    }

    public IReadOnlyList<GlossaryCacheEntry> LoadGlossaryCandidatesForOriginals(
        string projectDataDirectory,
        TranslationPhaseKind phase,
        IReadOnlyList<string> originals,
        int scopeVersion)
    {
        if (!Exists(projectDataDirectory) || originals.Count == 0)
        {
            return [];
        }

        var firstChars = originals
            .Where(static original => !string.IsNullOrWhiteSpace(original))
            .SelectMany(static original => original.Select(static ch => ch.ToString()))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (firstChars.Count == 0)
        {
            return [];
        }

        using var connection = OpenConnection(projectDataDirectory);
        EnsureSchema(connection);
        using var command = connection.CreateCommand();
        var firstCharParameters = new List<string>(firstChars.Count);
        for (var index = 0; index < firstChars.Count; index++)
        {
            var parameterName = $"$first_char_{index}";
            firstCharParameters.Add(parameterName);
            command.Parameters.AddWithValue(parameterName, firstChars[index]);
        }

        command.CommandText = $"""
            SELECT
                segment_id,
                source,
                target,
                file_type,
                source_status,
                segment_type,
                symbol_namespace,
                is_reference_bearing_key,
                rendered_length,
                static_score,
                first_char,
                phase_mask,
                eligibility_hash,
                scope_version,
                updated_at_utc
            FROM glossary_cache_entries
            WHERE scope_version = $scope_version
              AND (phase_mask & $phase_mask) != 0
              AND first_char IN ({string.Join(", ", firstCharParameters)})
            ORDER BY source, target, segment_id;
            """;
        command.Parameters.AddWithValue("$scope_version", scopeVersion);
        command.Parameters.AddWithValue("$phase_mask", PhaseScopedGlossaryBuilder.GetPhaseMask(phase));
        using var reader = command.ExecuteReader();
        var entries = new List<GlossaryCacheEntry>();
        while (reader.Read())
        {
            entries.Add(new GlossaryCacheEntry(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetInt32(7) != 0,
                reader.GetInt32(8),
                reader.GetInt32(9),
                reader.GetString(10),
                reader.GetInt32(11),
                reader.GetString(12),
                reader.GetInt32(13),
                DateTimeOffset.TryParse(reader.GetString(14), out var updatedAtUtc)
                    ? updatedAtUtc
                    : DateTimeOffset.MinValue));
        }

        return entries;
    }

    public void UpsertGlossaryCacheEntries(string projectDataDirectory, IEnumerable<GlossaryCacheEntry> entries)
    {
        var entryList = entries
            .Where(static entry => !string.IsNullOrWhiteSpace(entry.SegmentId))
            .ToList();
        if (entryList.Count == 0)
        {
            return;
        }

        using var connection = OpenConnection(projectDataDirectory);
        EnsureSchema(connection);
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO glossary_cache_entries (
                segment_id,
                source,
                target,
                file_type,
                source_status,
                segment_type,
                symbol_namespace,
                is_reference_bearing_key,
                rendered_length,
                static_score,
                first_char,
                phase_mask,
                eligibility_hash,
                scope_version,
                updated_at_utc)
            VALUES (
                $segment_id,
                $source,
                $target,
                $file_type,
                $source_status,
                $segment_type,
                $symbol_namespace,
                $is_reference_bearing_key,
                $rendered_length,
                $static_score,
                $first_char,
                $phase_mask,
                $eligibility_hash,
                $scope_version,
                $updated_at_utc)
            ON CONFLICT(segment_id) DO UPDATE SET
                source = excluded.source,
                target = excluded.target,
                file_type = excluded.file_type,
                source_status = excluded.source_status,
                segment_type = excluded.segment_type,
                symbol_namespace = excluded.symbol_namespace,
                is_reference_bearing_key = excluded.is_reference_bearing_key,
                rendered_length = excluded.rendered_length,
                static_score = excluded.static_score,
                first_char = excluded.first_char,
                phase_mask = excluded.phase_mask,
                eligibility_hash = excluded.eligibility_hash,
                scope_version = excluded.scope_version,
                updated_at_utc = excluded.updated_at_utc;
            """;
        var segmentIdParameter = command.Parameters.Add("$segment_id", SqliteType.Text);
        var sourceParameter = command.Parameters.Add("$source", SqliteType.Text);
        var targetParameter = command.Parameters.Add("$target", SqliteType.Text);
        var fileTypeParameter = command.Parameters.Add("$file_type", SqliteType.Text);
        var sourceStatusParameter = command.Parameters.Add("$source_status", SqliteType.Text);
        var segmentTypeParameter = command.Parameters.Add("$segment_type", SqliteType.Text);
        var symbolNamespaceParameter = command.Parameters.Add("$symbol_namespace", SqliteType.Text);
        var isReferenceBearingKeyParameter = command.Parameters.Add("$is_reference_bearing_key", SqliteType.Integer);
        var renderedLengthParameter = command.Parameters.Add("$rendered_length", SqliteType.Integer);
        var staticScoreParameter = command.Parameters.Add("$static_score", SqliteType.Integer);
        var firstCharParameter = command.Parameters.Add("$first_char", SqliteType.Text);
        var phaseMaskParameter = command.Parameters.Add("$phase_mask", SqliteType.Integer);
        var eligibilityHashParameter = command.Parameters.Add("$eligibility_hash", SqliteType.Text);
        var scopeVersionParameter = command.Parameters.Add("$scope_version", SqliteType.Integer);
        var updatedAtUtcParameter = command.Parameters.Add("$updated_at_utc", SqliteType.Text);

        foreach (var entry in entryList)
        {
            segmentIdParameter.Value = entry.SegmentId;
            sourceParameter.Value = entry.Source;
            targetParameter.Value = entry.Target;
            fileTypeParameter.Value = entry.SourceFileType;
            sourceStatusParameter.Value = entry.SourceStatus;
            segmentTypeParameter.Value = entry.SourceSegmentType;
            symbolNamespaceParameter.Value = entry.SourceNamespace;
            isReferenceBearingKeyParameter.Value = entry.IsReferenceBearingKey ? 1 : 0;
            renderedLengthParameter.Value = entry.RenderedLength;
            staticScoreParameter.Value = entry.StaticScore;
            firstCharParameter.Value = entry.FirstChar;
            phaseMaskParameter.Value = entry.PhaseMask;
            eligibilityHashParameter.Value = entry.EligibilityHash;
            scopeVersionParameter.Value = entry.ScopeVersion;
            updatedAtUtcParameter.Value = entry.UpdatedAtUtc.ToString("O");
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public void DeleteGlossaryCacheEntries(string projectDataDirectory, IEnumerable<string> segmentIds)
    {
        var targetIds = segmentIds
            .Where(static segmentId => !string.IsNullOrWhiteSpace(segmentId))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (targetIds.Count == 0 || !Exists(projectDataDirectory))
        {
            return;
        }

        using var connection = OpenConnection(projectDataDirectory);
        EnsureSchema(connection);
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM glossary_cache_entries WHERE segment_id = $segment_id;";
        var segmentIdParameter = command.Parameters.Add("$segment_id", SqliteType.Text);
        foreach (var segmentId in targetIds)
        {
            segmentIdParameter.Value = segmentId;
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public void ClearGlossaryCache(string projectDataDirectory)
    {
        if (!Exists(projectDataDirectory))
        {
            return;
        }

        using var connection = OpenConnection(projectDataDirectory);
        EnsureSchema(connection);
        using var transaction = connection.BeginTransaction();
        ExecuteNonQuery(connection, transaction, "DELETE FROM glossary_cache_entries;");
        transaction.Commit();
    }

    public int ApplyTranslationProgress(string projectDataDirectory, IEnumerable<ExtractedTextItem> items)
    {
        var snapshot = LoadTranslationProgress(projectDataDirectory);
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
            item.ReferenceOriginalSymbolKey = string.IsNullOrWhiteSpace(state.ReferenceOriginalSymbolKey)
                ? item.OriginalSymbolKey
                : state.ReferenceOriginalSymbolKey;
            item.ReferenceImpactCount = state.ReferenceImpactCount;
            item.RequiresReferenceRewrite = state.RequiresReferenceRewrite;
            item.ReferenceResolutionStatus = state.ReferenceResolutionStatus;
            restoredCount++;
        }

        return restoredCount;
    }

    public TranslationProgressState LoadTranslationProgress(string projectDataDirectory)
    {
        if (!Exists(projectDataDirectory))
        {
            return new TranslationProgressState();
        }

        try
        {
            using var connection = OpenConnection(projectDataDirectory);
            EnsureSchema(connection);

            var savedAtUtcText = ReadProjectMeta(connection, "last_progress_saved_at_utc");
            var savedAtUtc = DateTimeOffset.TryParse(savedAtUtcText, out var parsedSavedAtUtc)
                ? parsedSavedAtUtc
                : DateTimeOffset.UtcNow;

            var items = new List<TranslationProgressItemState>();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                    segment_id,
                    status,
                    validation_status,
                    translation_error,
                    translated_text,
                    can_save,
                    reference_original_symbol_key,
                    reference_impact_count,
                    requires_reference_rewrite,
                    reference_resolution_status
                FROM translation_progress
                ORDER BY segment_id;
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                items.Add(new TranslationProgressItemState
                {
                    SegmentId = reader.GetString(0),
                    Status = reader.GetString(1),
                    ValidationStatus = reader.GetString(2),
                    TranslationError = reader.GetString(3),
                    TranslatedText = reader.GetString(4),
                    CanSave = reader.GetInt64(5) != 0,
                    ReferenceOriginalSymbolKey = reader.GetString(6),
                    ReferenceImpactCount = reader.GetInt32(7),
                    RequiresReferenceRewrite = reader.GetInt64(8) != 0,
                    ReferenceResolutionStatus = reader.GetString(9),
                });
            }

            return new TranslationProgressState
            {
                SavedAtUtc = savedAtUtc,
                Items = items,
            };
        }
        catch
        {
            return new TranslationProgressState();
        }
    }

    public void DeleteTranslationProgress(string projectDataDirectory)
    {
        if (!Exists(projectDataDirectory))
        {
            return;
        }

        using var connection = OpenConnection(projectDataDirectory);
        EnsureSchema(connection);
        using var transaction = connection.BeginTransaction();
        ExecuteNonQuery(connection, transaction, "DELETE FROM translation_progress;");
        ExecuteNonQuery(connection, transaction, "DELETE FROM glossary_cache_entries;");
        SetProjectMeta(connection, transaction, "last_progress_saved_at_utc", string.Empty);
        transaction.Commit();
    }

    public void DeleteAll(string projectDataDirectory)
    {
        var path = GetDatabasePath(projectDataDirectory);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        SqliteConnection.ClearAllPools();
        TryDeleteFile(path);
        TryDeleteFile($"{path}-wal");
        TryDeleteFile($"{path}-shm");
    }

    private static TranslationProgressState BuildProgressSnapshot(IEnumerable<ExtractedTextItem> items)
    {
        return new TranslationProgressState
        {
            SavedAtUtc = DateTimeOffset.UtcNow,
            Items = BuildProgressItemStates(items),
        };
    }

    private static List<TranslationProgressItemState> BuildProgressItemStates(IEnumerable<ExtractedTextItem> items)
    {
        return items
            .Where(item => item.HasPersistableState)
            .Select(item => new TranslationProgressItemState
            {
                SegmentId = item.SegmentId,
                Status = item.Status,
                ValidationStatus = item.ValidationStatus,
                TranslationError = item.TranslationError,
                TranslatedText = item.TranslatedText,
                CanSave = item.CanSave,
                ReferenceOriginalSymbolKey = string.IsNullOrWhiteSpace(item.ReferenceOriginalSymbolKey)
                    ? item.OriginalSymbolKey
                    : item.ReferenceOriginalSymbolKey,
                ReferenceImpactCount = item.ReferenceImpactCount,
                RequiresReferenceRewrite = item.RequiresReferenceRewrite,
                ReferenceResolutionStatus = item.ReferenceResolutionStatus,
            })
            .ToList();
    }

    private static void UpsertTranslationProgressItems(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IEnumerable<TranslationProgressItemState> items,
        DateTimeOffset savedAtUtc)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO translation_progress (
                segment_id,
                status,
                validation_status,
                translation_error,
                translated_text,
                can_save,
                reference_original_symbol_key,
                reference_impact_count,
                requires_reference_rewrite,
                reference_resolution_status,
                updated_at_utc)
            VALUES (
                $segment_id,
                $status,
                $validation_status,
                $translation_error,
                $translated_text,
                $can_save,
                $reference_original_symbol_key,
                $reference_impact_count,
                $requires_reference_rewrite,
                $reference_resolution_status,
                $updated_at_utc)
            ON CONFLICT(segment_id) DO UPDATE SET
                status = excluded.status,
                validation_status = excluded.validation_status,
                translation_error = excluded.translation_error,
                translated_text = excluded.translated_text,
                can_save = excluded.can_save,
                reference_original_symbol_key = excluded.reference_original_symbol_key,
                reference_impact_count = excluded.reference_impact_count,
                requires_reference_rewrite = excluded.requires_reference_rewrite,
                reference_resolution_status = excluded.reference_resolution_status,
                updated_at_utc = excluded.updated_at_utc;
            """;
        var segmentIdParameter = command.Parameters.Add("$segment_id", SqliteType.Text);
        var statusParameter = command.Parameters.Add("$status", SqliteType.Text);
        var validationStatusParameter = command.Parameters.Add("$validation_status", SqliteType.Text);
        var translationErrorParameter = command.Parameters.Add("$translation_error", SqliteType.Text);
        var translatedTextParameter = command.Parameters.Add("$translated_text", SqliteType.Text);
        var canSaveParameter = command.Parameters.Add("$can_save", SqliteType.Integer);
        var referenceOriginalSymbolKeyParameter = command.Parameters.Add("$reference_original_symbol_key", SqliteType.Text);
        var referenceImpactCountParameter = command.Parameters.Add("$reference_impact_count", SqliteType.Integer);
        var requiresReferenceRewriteParameter = command.Parameters.Add("$requires_reference_rewrite", SqliteType.Integer);
        var referenceResolutionStatusParameter = command.Parameters.Add("$reference_resolution_status", SqliteType.Text);
        var updatedAtUtcParameter = command.Parameters.Add("$updated_at_utc", SqliteType.Text);
        updatedAtUtcParameter.Value = savedAtUtc.ToString("O");

        foreach (var state in items)
        {
            segmentIdParameter.Value = state.SegmentId;
            statusParameter.Value = state.Status;
            validationStatusParameter.Value = state.ValidationStatus;
            translationErrorParameter.Value = state.TranslationError;
            translatedTextParameter.Value = state.TranslatedText;
            canSaveParameter.Value = state.CanSave ? 1 : 0;
            referenceOriginalSymbolKeyParameter.Value = state.ReferenceOriginalSymbolKey;
            referenceImpactCountParameter.Value = state.ReferenceImpactCount;
            requiresReferenceRewriteParameter.Value = state.RequiresReferenceRewrite ? 1 : 0;
            referenceResolutionStatusParameter.Value = state.ReferenceResolutionStatus;
            command.ExecuteNonQuery();
        }
    }

    private SqliteConnection OpenConnection(string projectDataDirectory)
    {
        var path = GetDatabasePath(projectDataDirectory);
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("프로젝트 상태 DB 경로를 만들 수 없습니다.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false,
        }.ToString());
        connection.Open();

        using var pragma = connection.CreateCommand();
        pragma.CommandText = """
            PRAGMA foreign_keys = ON;
            PRAGMA journal_mode = WAL;
            PRAGMA busy_timeout = 5000;
            """;
        pragma.ExecuteNonQuery();
        return connection;
    }

    private static void EnsureSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS schema_info (
                version INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS project_meta (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS documents (
                document_id TEXT PRIMARY KEY,
                full_path TEXT NOT NULL,
                relative_path TEXT NOT NULL,
                file_type TEXT NOT NULL,
                original_text TEXT NOT NULL,
                encoding_code_page INTEGER NOT NULL,
                encoding_name TEXT NOT NULL,
                encoding_kind INTEGER NOT NULL,
                has_bom INTEGER NOT NULL,
                newline_sequence TEXT NOT NULL,
                csv_kind INTEGER NOT NULL,
                josa_pattern_count INTEGER NOT NULL,
                josa_auto_convertible_count INTEGER NOT NULL,
                josa_generic_function_count INTEGER NOT NULL,
                josa_macro_pattern_count INTEGER NOT NULL,
                josa_legacy_shorthand_count INTEGER NOT NULL,
                josa_requires_erh INTEGER NOT NULL,
                josa_erh_linked INTEGER NOT NULL,
                josa_syntax_type TEXT NOT NULL,
                josa_erh_link_status TEXT NOT NULL,
                josa_package_compatibility_status TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS segments (
                segment_id TEXT PRIMARY KEY,
                document_id TEXT NOT NULL,
                segment_type TEXT NOT NULL,
                absolute_start INTEGER NOT NULL,
                length INTEGER NOT NULL,
                line_number INTEGER NOT NULL,
                original_text TEXT NOT NULL,
                field_index INTEGER NULL,
                source_key TEXT NULL,
                csv_field_role INTEGER NOT NULL,
                preserve_whitespace INTEGER NOT NULL,
                symbol_namespace TEXT NOT NULL,
                original_symbol_key TEXT NOT NULL,
                is_reference_bearing_key INTEGER NOT NULL,
                FOREIGN KEY(document_id) REFERENCES documents(document_id) ON DELETE CASCADE
            );
            CREATE TABLE IF NOT EXISTS symbol_references (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                document_id TEXT NOT NULL,
                namespace TEXT NOT NULL,
                kind INTEGER NOT NULL,
                resolution_kind INTEGER NOT NULL,
                original_key TEXT NOT NULL,
                variable_name TEXT NOT NULL,
                expression_text TEXT NOT NULL,
                absolute_start INTEGER NOT NULL,
                length INTEGER NOT NULL,
                line_number INTEGER NOT NULL,
                FOREIGN KEY(document_id) REFERENCES documents(document_id) ON DELETE CASCADE
            );
            CREATE TABLE IF NOT EXISTS symbol_reference_candidates (
                reference_id INTEGER NOT NULL,
                candidate_key TEXT NOT NULL,
                PRIMARY KEY(reference_id, candidate_key),
                FOREIGN KEY(reference_id) REFERENCES symbol_references(id) ON DELETE CASCADE
            );
            CREATE TABLE IF NOT EXISTS variable_literal_occurrences (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                document_id TEXT NOT NULL,
                variable_name TEXT NOT NULL,
                literal_value TEXT NOT NULL,
                absolute_start INTEGER NOT NULL,
                length INTEGER NOT NULL,
                line_number INTEGER NOT NULL,
                is_exact_value INTEGER NOT NULL,
                FOREIGN KEY(document_id) REFERENCES documents(document_id) ON DELETE CASCADE
            );
            CREATE TABLE IF NOT EXISTS identifier_occurrences (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                document_id TEXT NOT NULL,
                kind INTEGER NOT NULL,
                role INTEGER NOT NULL,
                original_name TEXT NOT NULL,
                absolute_start INTEGER NOT NULL,
                length INTEGER NOT NULL,
                line_number INTEGER NOT NULL,
                FOREIGN KEY(document_id) REFERENCES documents(document_id) ON DELETE CASCADE
            );
            CREATE TABLE IF NOT EXISTS scan_warnings (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                document_id TEXT NOT NULL,
                warning_text TEXT NOT NULL,
                FOREIGN KEY(document_id) REFERENCES documents(document_id) ON DELETE CASCADE
            );
            CREATE TABLE IF NOT EXISTS josa_package_info (
                id INTEGER PRIMARY KEY CHECK (id = 1),
                erb_exists INTEGER NOT NULL,
                erh_exists INTEGER NOT NULL,
                erb_path TEXT NOT NULL,
                erh_path TEXT NOT NULL,
                has_function_signatures INTEGER NOT NULL,
                has_macro_defines INTEGER NOT NULL,
                supports_l_batchim_roro_exception INTEGER NOT NULL,
                supports_implicit_yi_fallback INTEGER NOT NULL,
                supports_particle_pass_through INTEGER NOT NULL,
                supports_macro_defines INTEGER NOT NULL,
                has_erh_include_linkage INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS josa_supported_particles (
                particle TEXT PRIMARY KEY
            );
            CREATE TABLE IF NOT EXISTS session_metrics (
                metric_key TEXT PRIMARY KEY,
                metric_value INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS translation_progress (
                segment_id TEXT PRIMARY KEY,
                status TEXT NOT NULL,
                validation_status TEXT NOT NULL,
                translation_error TEXT NOT NULL,
                translated_text TEXT NOT NULL,
                can_save INTEGER NOT NULL,
                reference_original_symbol_key TEXT NOT NULL DEFAULT '',
                reference_impact_count INTEGER NOT NULL,
                requires_reference_rewrite INTEGER NOT NULL,
                reference_resolution_status TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS glossary_cache_entries (
                segment_id TEXT PRIMARY KEY,
                source TEXT NOT NULL,
                target TEXT NOT NULL,
                file_type TEXT NOT NULL,
                source_status TEXT NOT NULL,
                segment_type TEXT NOT NULL,
                symbol_namespace TEXT NOT NULL,
                is_reference_bearing_key INTEGER NOT NULL,
                rendered_length INTEGER NOT NULL,
                static_score INTEGER NOT NULL,
                first_char TEXT NOT NULL,
                phase_mask INTEGER NOT NULL,
                eligibility_hash TEXT NOT NULL,
                scope_version INTEGER NOT NULL DEFAULT 0,
                updated_at_utc TEXT NOT NULL,
                FOREIGN KEY(segment_id) REFERENCES segments(segment_id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS idx_segments_document_id ON segments(document_id);
            CREATE INDEX IF NOT EXISTS idx_symbol_references_document_id ON symbol_references(document_id);
            CREATE INDEX IF NOT EXISTS idx_variable_literal_occurrences_document_id ON variable_literal_occurrences(document_id);
            CREATE INDEX IF NOT EXISTS idx_identifier_occurrences_document_id ON identifier_occurrences(document_id);
            CREATE INDEX IF NOT EXISTS idx_scan_warnings_document_id ON scan_warnings(document_id);
            CREATE INDEX IF NOT EXISTS idx_documents_relative_path ON documents(relative_path);
            CREATE INDEX IF NOT EXISTS idx_translation_progress_updated_at_utc ON translation_progress(updated_at_utc);
            CREATE INDEX IF NOT EXISTS idx_glossary_cache_first_char ON glossary_cache_entries(first_char);
            CREATE INDEX IF NOT EXISTS idx_glossary_cache_phase_mask ON glossary_cache_entries(phase_mask);
            CREATE INDEX IF NOT EXISTS idx_glossary_cache_hash ON glossary_cache_entries(eligibility_hash);
            """;
        command.ExecuteNonQuery();
        EnsureColumnExists(connection, "translation_progress", "reference_original_symbol_key", "TEXT NOT NULL DEFAULT ''");
        EnsureColumnExists(connection, "glossary_cache_entries", "scope_version", "INTEGER NOT NULL DEFAULT 0");
        using var glossaryScopeIndexCommand = connection.CreateCommand();
        glossaryScopeIndexCommand.CommandText = "CREATE INDEX IF NOT EXISTS idx_glossary_cache_scope_version ON glossary_cache_entries(scope_version);";
        glossaryScopeIndexCommand.ExecuteNonQuery();

        using var countCommand = connection.CreateCommand();
        countCommand.CommandText = "SELECT COUNT(*) FROM schema_info;";
        var hasVersion = Convert.ToInt32(countCommand.ExecuteScalar()) > 0;
        if (!hasVersion)
        {
            using var insertVersionCommand = connection.CreateCommand();
            insertVersionCommand.CommandText = "INSERT INTO schema_info (version) VALUES ($version);";
            insertVersionCommand.Parameters.AddWithValue("$version", SchemaVersion);
            insertVersionCommand.ExecuteNonQuery();
            return;
        }

        using var updateVersionCommand = connection.CreateCommand();
        updateVersionCommand.CommandText = "UPDATE schema_info SET version = $version;";
        updateVersionCommand.Parameters.AddWithValue("$version", SchemaVersion);
        updateVersionCommand.ExecuteNonQuery();
    }

    private static void SaveJosaPackageInfo(SqliteConnection connection, SqliteTransaction transaction, JosaSupportPackageInfo packageInfo)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO josa_package_info (
                id,
                erb_exists,
                erh_exists,
                erb_path,
                erh_path,
                has_function_signatures,
                has_macro_defines,
                supports_l_batchim_roro_exception,
                supports_implicit_yi_fallback,
                supports_particle_pass_through,
                supports_macro_defines,
                has_erh_include_linkage)
            VALUES (
                1,
                $erb_exists,
                $erh_exists,
                $erb_path,
                $erh_path,
                $has_function_signatures,
                $has_macro_defines,
                $supports_l_batchim_roro_exception,
                $supports_implicit_yi_fallback,
                $supports_particle_pass_through,
                $supports_macro_defines,
                $has_erh_include_linkage);
            """;
        command.Parameters.AddWithValue("$erb_exists", packageInfo.ErbExists ? 1 : 0);
        command.Parameters.AddWithValue("$erh_exists", packageInfo.ErhExists ? 1 : 0);
        command.Parameters.AddWithValue("$erb_path", packageInfo.ErbPath ?? string.Empty);
        command.Parameters.AddWithValue("$erh_path", packageInfo.ErhPath ?? string.Empty);
        command.Parameters.AddWithValue("$has_function_signatures", packageInfo.HasFunctionSignatures ? 1 : 0);
        command.Parameters.AddWithValue("$has_macro_defines", packageInfo.HasMacroDefines ? 1 : 0);
        command.Parameters.AddWithValue("$supports_l_batchim_roro_exception", packageInfo.SupportsLBatchimRoroException ? 1 : 0);
        command.Parameters.AddWithValue("$supports_implicit_yi_fallback", packageInfo.SupportsImplicitYiFallback ? 1 : 0);
        command.Parameters.AddWithValue("$supports_particle_pass_through", packageInfo.SupportsParticlePassThrough ? 1 : 0);
        command.Parameters.AddWithValue("$supports_macro_defines", packageInfo.SupportsMacroDefines ? 1 : 0);
        command.Parameters.AddWithValue("$has_erh_include_linkage", packageInfo.HasErhIncludeLinkage ? 1 : 0);
        command.ExecuteNonQuery();

        foreach (var particle in packageInfo.SupportedParticles)
        {
            using var particleCommand = connection.CreateCommand();
            particleCommand.Transaction = transaction;
            particleCommand.CommandText = "INSERT INTO josa_supported_particles (particle) VALUES ($particle);";
            particleCommand.Parameters.AddWithValue("$particle", particle);
            particleCommand.ExecuteNonQuery();
        }
    }

    private static Dictionary<string, SourceFileDocument> LoadDocuments(SqliteConnection connection)
    {
        var documents = new Dictionary<string, SourceFileDocument>(StringComparer.Ordinal);
        using (var documentCommand = connection.CreateCommand())
        {
            documentCommand.CommandText = """
                SELECT
                    document_id,
                    full_path,
                    relative_path,
                    file_type,
                    original_text,
                    encoding_code_page,
                    encoding_name,
                    encoding_kind,
                    has_bom,
                    newline_sequence,
                    csv_kind,
                    josa_pattern_count,
                    josa_auto_convertible_count,
                    josa_generic_function_count,
                    josa_macro_pattern_count,
                    josa_legacy_shorthand_count,
                    josa_requires_erh,
                    josa_erh_linked,
                    josa_syntax_type,
                    josa_erh_link_status,
                    josa_package_compatibility_status
                FROM documents
                ORDER BY relative_path, document_id;
                """;
            using var reader = documentCommand.ExecuteReader();
            while (reader.Read())
            {
                var document = new SourceFileDocument
                {
                    DocumentId = reader.GetString(0),
                    FullPath = reader.GetString(1),
                    RelativePath = reader.GetString(2),
                    FileType = reader.GetString(3),
                    OriginalText = reader.GetString(4),
                    EncodingInfo = new DetectedEncodingInfo
                    {
                        Encoding = TryGetEncoding(
                            reader.GetInt32(5),
                            (DetectedEncodingKind)reader.GetInt32(7),
                            reader.GetInt64(8) != 0),
                        Name = reader.GetString(6),
                        Kind = (DetectedEncodingKind)reader.GetInt32(7),
                        HasBom = reader.GetInt64(8) != 0,
                    },
                    NewLineSequence = reader.GetString(9),
                    CsvKind = (CsvDocumentKind)reader.GetInt32(10),
                    JosaAnalysis = new JosaDocumentAnalysis
                    {
                        PatternCount = reader.GetInt32(11),
                        AutoConvertibleCount = reader.GetInt32(12),
                        GenericFunctionCount = reader.GetInt32(13),
                        MacroPatternCount = reader.GetInt32(14),
                        LegacyShorthandCount = reader.GetInt32(15),
                        RequiresErh = reader.GetInt64(16) != 0,
                        ErhLinked = reader.GetInt64(17) != 0,
                        SyntaxType = reader.GetString(18),
                        ErhLinkStatus = reader.GetString(19),
                        PackageCompatibilityStatus = reader.GetString(20),
                    },
                };
                documents[document.DocumentId] = document;
            }
        }

        using (var segmentCommand = connection.CreateCommand())
        {
            segmentCommand.CommandText = """
                SELECT
                    segment_id,
                    document_id,
                    segment_type,
                    absolute_start,
                    length,
                    line_number,
                    original_text,
                    field_index,
                    source_key,
                    csv_field_role,
                    preserve_whitespace,
                    symbol_namespace,
                    original_symbol_key,
                    is_reference_bearing_key
                FROM segments
                ORDER BY document_id, absolute_start, segment_id;
                """;
            using var reader = segmentCommand.ExecuteReader();
            while (reader.Read())
            {
                var documentId = reader.GetString(1);
                if (!documents.TryGetValue(documentId, out var document))
                {
                    continue;
                }

                document.Segments.Add(new TextSegment
                {
                    SegmentId = reader.GetString(0),
                    DocumentId = documentId,
                    SegmentType = reader.GetString(2),
                    AbsoluteStart = reader.GetInt32(3),
                    Length = reader.GetInt32(4),
                    LineNumber = reader.GetInt32(5),
                    OriginalText = reader.GetString(6),
                    FieldIndex = reader.IsDBNull(7) ? null : reader.GetInt32(7),
                    SourceKey = reader.IsDBNull(8) ? null : reader.GetString(8),
                    CsvFieldRole = (CsvFieldRole)reader.GetInt32(9),
                    PreserveWhitespace = reader.GetInt64(10) != 0,
                    SymbolNamespace = reader.GetString(11),
                    OriginalSymbolKey = reader.GetString(12),
                    IsReferenceBearingKey = reader.GetInt64(13) != 0,
                });
            }
        }

        var candidateLookup = LoadSymbolReferenceCandidateLookup(connection);
        using (var referenceCommand = connection.CreateCommand())
        {
            referenceCommand.CommandText = """
                SELECT
                    id,
                    document_id,
                    namespace,
                    kind,
                    resolution_kind,
                    original_key,
                    variable_name,
                    expression_text,
                    absolute_start,
                    length,
                    line_number
                FROM symbol_references
                ORDER BY document_id, absolute_start, id;
                """;
            using var reader = referenceCommand.ExecuteReader();
            while (reader.Read())
            {
                var referenceId = reader.GetInt64(0);
                var documentId = reader.GetString(1);
                if (!documents.TryGetValue(documentId, out var document))
                {
                    continue;
                }

                document.SymbolReferences.Add(new ErbSymbolReference
                {
                    DocumentId = documentId,
                    Namespace = reader.GetString(2),
                    Kind = (ErbSymbolReferenceKind)reader.GetInt32(3),
                    ResolutionKind = (SymbolReferenceResolutionKind)reader.GetInt32(4),
                    OriginalKey = reader.GetString(5),
                    VariableName = reader.GetString(6),
                    ExpressionText = reader.GetString(7),
                    AbsoluteStart = reader.GetInt32(8),
                    Length = reader.GetInt32(9),
                    LineNumber = reader.GetInt32(10),
                    CandidateKeys = candidateLookup.GetValueOrDefault(referenceId, []),
                });
            }
        }

        using (var occurrenceCommand = connection.CreateCommand())
        {
            occurrenceCommand.CommandText = """
                SELECT
                    document_id,
                    variable_name,
                    literal_value,
                    absolute_start,
                    length,
                    line_number,
                    is_exact_value
                FROM variable_literal_occurrences
                ORDER BY document_id, absolute_start, id;
                """;
            using var reader = occurrenceCommand.ExecuteReader();
            while (reader.Read())
            {
                var documentId = reader.GetString(0);
                if (!documents.TryGetValue(documentId, out var document))
                {
                    continue;
                }

                document.VariableLiteralOccurrences.Add(new ErbVariableLiteralOccurrence
                {
                    DocumentId = documentId,
                    VariableName = reader.GetString(1),
                    LiteralValue = reader.GetString(2),
                    AbsoluteStart = reader.GetInt32(3),
                    Length = reader.GetInt32(4),
                    LineNumber = reader.GetInt32(5),
                    IsExactValue = reader.GetInt64(6) != 0,
                });
            }
        }

        using (var identifierCommand = connection.CreateCommand())
        {
            identifierCommand.CommandText = """
                SELECT
                    document_id,
                    kind,
                    role,
                    original_name,
                    absolute_start,
                    length,
                    line_number
                FROM identifier_occurrences
                ORDER BY document_id, absolute_start, id;
                """;
            using var reader = identifierCommand.ExecuteReader();
            while (reader.Read())
            {
                var documentId = reader.GetString(0);
                if (!documents.TryGetValue(documentId, out var document))
                {
                    continue;
                }

                document.IdentifierOccurrences.Add(new ErbIdentifierOccurrence
                {
                    DocumentId = documentId,
                    Kind = (ErbIdentifierKind)reader.GetInt32(1),
                    Role = (ErbIdentifierRole)reader.GetInt32(2),
                    OriginalName = reader.GetString(3),
                    AbsoluteStart = reader.GetInt32(4),
                    Length = reader.GetInt32(5),
                    LineNumber = reader.GetInt32(6),
                });
            }
        }

        using (var warningCommand = connection.CreateCommand())
        {
            warningCommand.CommandText = """
                SELECT document_id, warning_text
                FROM scan_warnings
                ORDER BY document_id, id;
                """;
            using var reader = warningCommand.ExecuteReader();
            while (reader.Read())
            {
                var documentId = reader.GetString(0);
                if (!documents.TryGetValue(documentId, out var document))
                {
                    continue;
                }

                document.ScanWarnings.Add(reader.GetString(1));
            }
        }

        return documents;
    }

    private static Dictionary<long, List<string>> LoadSymbolReferenceCandidateLookup(SqliteConnection connection)
    {
        var result = new Dictionary<long, List<string>>();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT reference_id, candidate_key
            FROM symbol_reference_candidates
            ORDER BY reference_id, candidate_key;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var referenceId = reader.GetInt64(0);
            if (!result.TryGetValue(referenceId, out var candidates))
            {
                candidates = [];
                result[referenceId] = candidates;
            }

            candidates.Add(reader.GetString(1));
        }

        return result;
    }

    private static List<ExtractedTextItem> BuildItemsFromDocuments(IEnumerable<SourceFileDocument> documents)
    {
        var items = new List<ExtractedTextItem>();
        foreach (var document in documents)
        {
            var warningText = string.Join(" | ", document.ScanWarnings);
            foreach (var segment in document.Segments)
            {
                items.Add(new ExtractedTextItem
                {
                    SegmentId = segment.SegmentId,
                    DocumentId = document.DocumentId,
                    FileType = document.FileType,
                    RelativePath = document.RelativePath,
                    EncodingName = document.EncodingInfo.Name,
                    SegmentType = segment.SegmentType,
                    LineNumber = segment.LineNumber,
                    OriginalText = segment.OriginalText,
                    SourceKey = segment.SourceKey,
                    FieldIndex = segment.FieldIndex,
                    CsvFieldRole = segment.CsvFieldRole,
                    PreserveWhitespace = segment.PreserveWhitespace,
                    SymbolNamespace = segment.SymbolNamespace,
                    OriginalSymbolKey = segment.OriginalSymbolKey,
                    IsReferenceBearingKey = segment.IsReferenceBearingKey,
                    ReferenceOriginalSymbolKey = segment.OriginalSymbolKey,
                    WarningText = warningText,
                });
            }
        }

        return items;
    }

    private static void LoadMetrics(SqliteConnection connection, ScanSession session)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT metric_key, metric_value FROM session_metrics;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            session.Metrics[reader.GetString(0)] = reader.GetInt32(1);
        }
    }

    private static JosaSupportPackageInfo LoadJosaPackageInfo(SqliteConnection connection)
    {
        var supportedParticles = ReadStringList(connection, "SELECT particle FROM josa_supported_particles ORDER BY particle;");
        using var packageCommand = connection.CreateCommand();
        packageCommand.CommandText = """
            SELECT
                erb_exists,
                erh_exists,
                erb_path,
                erh_path,
                has_function_signatures,
                has_macro_defines,
                supports_l_batchim_roro_exception,
                supports_implicit_yi_fallback,
                supports_particle_pass_through,
                supports_macro_defines,
                has_erh_include_linkage
            FROM josa_package_info
            WHERE id = 1;
            """;
        using (var reader = packageCommand.ExecuteReader())
        {
            if (reader.Read())
            {
                return new JosaSupportPackageInfo
                {
                    ErbExists = reader.GetInt64(0) != 0,
                    ErhExists = reader.GetInt64(1) != 0,
                    ErbPath = reader.GetString(2),
                    ErhPath = reader.GetString(3),
                    HasFunctionSignatures = reader.GetInt64(4) != 0,
                    HasMacroDefines = reader.GetInt64(5) != 0,
                    SupportsLBatchimRoroException = reader.GetInt64(6) != 0,
                    SupportsImplicitYiFallback = reader.GetInt64(7) != 0,
                    SupportsParticlePassThrough = reader.GetInt64(8) != 0,
                    SupportsMacroDefines = reader.GetInt64(9) != 0,
                    HasErhIncludeLinkage = reader.GetInt64(10) != 0,
                    SupportedParticles = supportedParticles,
                };
            }
        }

        return new JosaSupportPackageInfo
        {
            SupportedParticles = supportedParticles,
        };
    }

    private static string? ReadProjectMeta(SqliteConnection connection, string key)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM project_meta WHERE key = $key LIMIT 1;";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() as string;
    }

    private static void SetProjectMeta(SqliteConnection connection, SqliteTransaction transaction, string key, string value)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO project_meta (key, value)
            VALUES ($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    private static void SetProgressSavedMeta(SqliteConnection connection, SqliteTransaction transaction, DateTimeOffset savedAtUtc)
    {
        SetProjectMeta(connection, transaction, "saved_at_utc", DateTimeOffset.UtcNow.ToString("O"));
        SetProjectMeta(connection, transaction, "last_progress_saved_at_utc", savedAtUtc.ToString("O"));
    }

    private static void ExecuteNonQuery(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void EnsureColumnExists(SqliteConnection connection, string tableName, string columnName, string columnDefinition)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        using var alterCommand = connection.CreateCommand();
        alterCommand.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};";
        alterCommand.ExecuteNonQuery();
    }

    private static List<string> ReadStringList(SqliteConnection connection, string sql)
    {
        var values = new List<string>();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    private static Encoding TryGetEncoding(int codePage, DetectedEncodingKind kind, bool hasBom)
    {
        if (kind is DetectedEncodingKind.Utf8 or DetectedEncodingKind.Utf8Bom)
        {
            return new UTF8Encoding(hasBom);
        }

        if (kind == DetectedEncodingKind.Unicode)
        {
            return new UnicodeEncoding(false, hasBom, true);
        }

        try
        {
            return Encoding.GetEncoding(codePage);
        }
        catch
        {
            return new UTF8Encoding(true);
        }
    }

    private sealed class ScanSessionSaveCommandSet : IDisposable
    {
        private readonly SqliteCommand _candidateCommand;
        private readonly SqliteCommand _documentCommand;
        private readonly SqliteCommand _identifierOccurrenceCommand;
        private readonly SqliteCommand _metricCommand;
        private readonly SqliteCommand _occurrenceCommand;
        private readonly SqliteCommand _referenceCommand;
        private readonly SqliteCommand _segmentCommand;
        private readonly SqliteCommand _warningCommand;

        public ScanSessionSaveCommandSet(SqliteConnection connection, SqliteTransaction transaction)
        {
            _documentCommand = CreateCommand(connection, transaction, """
                INSERT INTO documents (
                    document_id,
                    full_path,
                    relative_path,
                    file_type,
                    original_text,
                    encoding_code_page,
                    encoding_name,
                    encoding_kind,
                    has_bom,
                    newline_sequence,
                    csv_kind,
                    josa_pattern_count,
                    josa_auto_convertible_count,
                    josa_generic_function_count,
                    josa_macro_pattern_count,
                    josa_legacy_shorthand_count,
                    josa_requires_erh,
                    josa_erh_linked,
                    josa_syntax_type,
                    josa_erh_link_status,
                    josa_package_compatibility_status)
                VALUES (
                    $document_id,
                    $full_path,
                    $relative_path,
                    $file_type,
                    $original_text,
                    $encoding_code_page,
                    $encoding_name,
                    $encoding_kind,
                    $has_bom,
                    $newline_sequence,
                    $csv_kind,
                    $josa_pattern_count,
                    $josa_auto_convertible_count,
                    $josa_generic_function_count,
                    $josa_macro_pattern_count,
                    $josa_legacy_shorthand_count,
                    $josa_requires_erh,
                    $josa_erh_linked,
                    $josa_syntax_type,
                    $josa_erh_link_status,
                    $josa_package_compatibility_status);
                """);
            _segmentCommand = CreateCommand(connection, transaction, """
                INSERT INTO segments (
                    segment_id,
                    document_id,
                    segment_type,
                    absolute_start,
                    length,
                    line_number,
                    original_text,
                    field_index,
                    source_key,
                    csv_field_role,
                    preserve_whitespace,
                    symbol_namespace,
                    original_symbol_key,
                    is_reference_bearing_key)
                VALUES (
                    $segment_id,
                    $document_id,
                    $segment_type,
                    $absolute_start,
                    $length,
                    $line_number,
                    $original_text,
                    $field_index,
                    $source_key,
                    $csv_field_role,
                    $preserve_whitespace,
                    $symbol_namespace,
                    $original_symbol_key,
                    $is_reference_bearing_key);
                """);
            _referenceCommand = CreateCommand(connection, transaction, """
                INSERT INTO symbol_references (
                    document_id,
                    namespace,
                    kind,
                    resolution_kind,
                    original_key,
                    variable_name,
                    expression_text,
                    absolute_start,
                    length,
                    line_number)
                VALUES (
                    $document_id,
                    $namespace,
                    $kind,
                    $resolution_kind,
                    $original_key,
                    $variable_name,
                    $expression_text,
                    $absolute_start,
                    $length,
                    $line_number);
                SELECT last_insert_rowid();
                """);
            _candidateCommand = CreateCommand(connection, transaction, """
                INSERT INTO symbol_reference_candidates (reference_id, candidate_key)
                VALUES ($reference_id, $candidate_key);
                """);
            _occurrenceCommand = CreateCommand(connection, transaction, """
                INSERT INTO variable_literal_occurrences (
                    document_id,
                    variable_name,
                    literal_value,
                    absolute_start,
                    length,
                    line_number,
                    is_exact_value)
                VALUES (
                    $document_id,
                    $variable_name,
                    $literal_value,
                    $absolute_start,
                    $length,
                    $line_number,
                    $is_exact_value);
                """);
            _identifierOccurrenceCommand = CreateCommand(connection, transaction, """
                INSERT INTO identifier_occurrences (
                    document_id,
                    kind,
                    role,
                    original_name,
                    absolute_start,
                    length,
                    line_number)
                VALUES (
                    $document_id,
                    $kind,
                    $role,
                    $original_name,
                    $absolute_start,
                    $length,
                    $line_number);
                """);
            _warningCommand = CreateCommand(connection, transaction, """
                INSERT INTO scan_warnings (document_id, warning_text)
                VALUES ($document_id, $warning_text);
                """);
            _metricCommand = CreateCommand(connection, transaction, """
                INSERT INTO session_metrics (metric_key, metric_value)
                VALUES ($metric_key, $metric_value);
                """);
        }

        public void SaveDocument(SourceFileDocument document)
        {
            _documentCommand.Parameters.Clear();
            _documentCommand.Parameters.AddWithValue("$document_id", document.DocumentId);
            _documentCommand.Parameters.AddWithValue("$full_path", document.FullPath);
            _documentCommand.Parameters.AddWithValue("$relative_path", document.RelativePath);
            _documentCommand.Parameters.AddWithValue("$file_type", document.FileType);
            _documentCommand.Parameters.AddWithValue("$original_text", document.OriginalText);
            _documentCommand.Parameters.AddWithValue("$encoding_code_page", document.EncodingInfo.Encoding.CodePage);
            _documentCommand.Parameters.AddWithValue("$encoding_name", document.EncodingInfo.Name);
            _documentCommand.Parameters.AddWithValue("$encoding_kind", (int)document.EncodingInfo.Kind);
            _documentCommand.Parameters.AddWithValue("$has_bom", document.EncodingInfo.HasBom ? 1 : 0);
            _documentCommand.Parameters.AddWithValue("$newline_sequence", document.NewLineSequence);
            _documentCommand.Parameters.AddWithValue("$csv_kind", (int)document.CsvKind);
            _documentCommand.Parameters.AddWithValue("$josa_pattern_count", document.JosaAnalysis.PatternCount);
            _documentCommand.Parameters.AddWithValue("$josa_auto_convertible_count", document.JosaAnalysis.AutoConvertibleCount);
            _documentCommand.Parameters.AddWithValue("$josa_generic_function_count", document.JosaAnalysis.GenericFunctionCount);
            _documentCommand.Parameters.AddWithValue("$josa_macro_pattern_count", document.JosaAnalysis.MacroPatternCount);
            _documentCommand.Parameters.AddWithValue("$josa_legacy_shorthand_count", document.JosaAnalysis.LegacyShorthandCount);
            _documentCommand.Parameters.AddWithValue("$josa_requires_erh", document.JosaAnalysis.RequiresErh ? 1 : 0);
            _documentCommand.Parameters.AddWithValue("$josa_erh_linked", document.JosaAnalysis.ErhLinked ? 1 : 0);
            _documentCommand.Parameters.AddWithValue("$josa_syntax_type", document.JosaAnalysis.SyntaxType);
            _documentCommand.Parameters.AddWithValue("$josa_erh_link_status", document.JosaAnalysis.ErhLinkStatus);
            _documentCommand.Parameters.AddWithValue("$josa_package_compatibility_status", document.JosaAnalysis.PackageCompatibilityStatus);
            _documentCommand.ExecuteNonQuery();

            foreach (var segment in document.Segments)
            {
                _segmentCommand.Parameters.Clear();
                _segmentCommand.Parameters.AddWithValue("$segment_id", segment.SegmentId);
                _segmentCommand.Parameters.AddWithValue("$document_id", segment.DocumentId);
                _segmentCommand.Parameters.AddWithValue("$segment_type", segment.SegmentType);
                _segmentCommand.Parameters.AddWithValue("$absolute_start", segment.AbsoluteStart);
                _segmentCommand.Parameters.AddWithValue("$length", segment.Length);
                _segmentCommand.Parameters.AddWithValue("$line_number", segment.LineNumber);
                _segmentCommand.Parameters.AddWithValue("$original_text", segment.OriginalText);
                _segmentCommand.Parameters.AddWithValue("$field_index", segment.FieldIndex is null ? DBNull.Value : segment.FieldIndex.Value);
                _segmentCommand.Parameters.AddWithValue("$source_key", segment.SourceKey is null ? DBNull.Value : segment.SourceKey);
                _segmentCommand.Parameters.AddWithValue("$csv_field_role", (int)segment.CsvFieldRole);
                _segmentCommand.Parameters.AddWithValue("$preserve_whitespace", segment.PreserveWhitespace ? 1 : 0);
                _segmentCommand.Parameters.AddWithValue("$symbol_namespace", segment.SymbolNamespace);
                _segmentCommand.Parameters.AddWithValue("$original_symbol_key", segment.OriginalSymbolKey);
                _segmentCommand.Parameters.AddWithValue("$is_reference_bearing_key", segment.IsReferenceBearingKey ? 1 : 0);
                _segmentCommand.ExecuteNonQuery();
            }

            foreach (var reference in document.SymbolReferences)
            {
                _referenceCommand.Parameters.Clear();
                _referenceCommand.Parameters.AddWithValue("$document_id", reference.DocumentId);
                _referenceCommand.Parameters.AddWithValue("$namespace", reference.Namespace);
                _referenceCommand.Parameters.AddWithValue("$kind", (int)reference.Kind);
                _referenceCommand.Parameters.AddWithValue("$resolution_kind", (int)reference.ResolutionKind);
                _referenceCommand.Parameters.AddWithValue("$original_key", reference.OriginalKey);
                _referenceCommand.Parameters.AddWithValue("$variable_name", reference.VariableName);
                _referenceCommand.Parameters.AddWithValue("$expression_text", reference.ExpressionText);
                _referenceCommand.Parameters.AddWithValue("$absolute_start", reference.AbsoluteStart);
                _referenceCommand.Parameters.AddWithValue("$length", reference.Length);
                _referenceCommand.Parameters.AddWithValue("$line_number", reference.LineNumber);
                var referenceId = Convert.ToInt64(_referenceCommand.ExecuteScalar());

                foreach (var candidateKey in reference.CandidateKeys)
                {
                    _candidateCommand.Parameters.Clear();
                    _candidateCommand.Parameters.AddWithValue("$reference_id", referenceId);
                    _candidateCommand.Parameters.AddWithValue("$candidate_key", candidateKey);
                    _candidateCommand.ExecuteNonQuery();
                }
            }

            foreach (var occurrence in document.VariableLiteralOccurrences)
            {
                _occurrenceCommand.Parameters.Clear();
                _occurrenceCommand.Parameters.AddWithValue("$document_id", occurrence.DocumentId);
                _occurrenceCommand.Parameters.AddWithValue("$variable_name", occurrence.VariableName);
                _occurrenceCommand.Parameters.AddWithValue("$literal_value", occurrence.LiteralValue);
                _occurrenceCommand.Parameters.AddWithValue("$absolute_start", occurrence.AbsoluteStart);
                _occurrenceCommand.Parameters.AddWithValue("$length", occurrence.Length);
                _occurrenceCommand.Parameters.AddWithValue("$line_number", occurrence.LineNumber);
                _occurrenceCommand.Parameters.AddWithValue("$is_exact_value", occurrence.IsExactValue ? 1 : 0);
                _occurrenceCommand.ExecuteNonQuery();
            }

            foreach (var occurrence in document.IdentifierOccurrences)
            {
                _identifierOccurrenceCommand.Parameters.Clear();
                _identifierOccurrenceCommand.Parameters.AddWithValue("$document_id", occurrence.DocumentId);
                _identifierOccurrenceCommand.Parameters.AddWithValue("$kind", (int)occurrence.Kind);
                _identifierOccurrenceCommand.Parameters.AddWithValue("$role", (int)occurrence.Role);
                _identifierOccurrenceCommand.Parameters.AddWithValue("$original_name", occurrence.OriginalName);
                _identifierOccurrenceCommand.Parameters.AddWithValue("$absolute_start", occurrence.AbsoluteStart);
                _identifierOccurrenceCommand.Parameters.AddWithValue("$length", occurrence.Length);
                _identifierOccurrenceCommand.Parameters.AddWithValue("$line_number", occurrence.LineNumber);
                _identifierOccurrenceCommand.ExecuteNonQuery();
            }

            foreach (var warning in document.ScanWarnings)
            {
                _warningCommand.Parameters.Clear();
                _warningCommand.Parameters.AddWithValue("$document_id", document.DocumentId);
                _warningCommand.Parameters.AddWithValue("$warning_text", warning);
                _warningCommand.ExecuteNonQuery();
            }
        }

        public void SaveMetric(string key, int value)
        {
            _metricCommand.Parameters.Clear();
            _metricCommand.Parameters.AddWithValue("$metric_key", key);
            _metricCommand.Parameters.AddWithValue("$metric_value", value);
            _metricCommand.ExecuteNonQuery();
        }

        public void Dispose()
        {
            _documentCommand.Dispose();
            _segmentCommand.Dispose();
            _referenceCommand.Dispose();
            _candidateCommand.Dispose();
            _occurrenceCommand.Dispose();
            _identifierOccurrenceCommand.Dispose();
            _warningCommand.Dispose();
            _metricCommand.Dispose();
        }

        private static SqliteCommand CreateCommand(SqliteConnection connection, SqliteTransaction transaction, string commandText)
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = commandText;
            return command;
        }
    }

    private static void TryDeleteFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
