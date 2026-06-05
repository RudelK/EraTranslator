namespace EraTranslator.Services;

public sealed class ProjectStatePersistenceService(
    ScanSessionStateService? scanSessionStateService = null,
    TranslationProgressStateService? translationProgressStateService = null,
    SqliteProjectStateStore? sqliteProjectStateStore = null)
{
    private readonly ScanSessionStateService _scanSessionStateService = scanSessionStateService ?? new ScanSessionStateService();
    private readonly TranslationProgressStateService _translationProgressStateService = translationProgressStateService ?? new TranslationProgressStateService();
    private readonly SqliteProjectStateStore _sqliteProjectStateStore = sqliteProjectStateStore ?? new SqliteProjectStateStore();

    public void SaveScanSession(ScanSession session, string projectDataDirectory)
    {
        _sqliteProjectStateStore.SaveScanSession(session, projectDataDirectory);
    }

    public ScanSession? LoadScanSession(string projectDataDirectory)
    {
        EnsureMigrated(projectDataDirectory);
        return _sqliteProjectStateStore.LoadScanSession(projectDataDirectory);
    }

    public int ApplyTranslationProgress(string projectDataDirectory, IEnumerable<ExtractedTextItem> items)
    {
        EnsureMigrated(projectDataDirectory);
        return _sqliteProjectStateStore.ApplyTranslationProgress(projectDataDirectory, items);
    }

    public TranslationProgressState LoadTranslationProgress(string projectDataDirectory)
    {
        EnsureMigrated(projectDataDirectory);
        return _sqliteProjectStateStore.LoadTranslationProgress(projectDataDirectory);
    }

    public void SaveTranslationProgressSnapshot(string projectDataDirectory, IEnumerable<ExtractedTextItem> items)
    {
        _sqliteProjectStateStore.SaveTranslationProgressSnapshot(projectDataDirectory, items);
    }

    public void UpsertTranslationProgressItems(string projectDataDirectory, IEnumerable<ExtractedTextItem> items)
    {
        _sqliteProjectStateStore.UpsertTranslationProgressItems(projectDataDirectory, items);
    }

    public void DeleteTranslationProgressItems(string projectDataDirectory, IEnumerable<string> segmentIds)
    {
        _sqliteProjectStateStore.DeleteTranslationProgressItems(projectDataDirectory, segmentIds);
    }

    public void DeleteTranslationProgress(string projectDataDirectory)
    {
        _sqliteProjectStateStore.DeleteTranslationProgress(projectDataDirectory);
        _translationProgressStateService.Delete(projectDataDirectory);
    }

    public void DeleteAll(string projectDataDirectory)
    {
        _sqliteProjectStateStore.DeleteAll(projectDataDirectory);
        _translationProgressStateService.Delete(projectDataDirectory);
        _scanSessionStateService.Delete(projectDataDirectory);
    }

    public bool HasPersistedState(string projectDataDirectory)
    {
        if (string.IsNullOrWhiteSpace(projectDataDirectory))
        {
            return false;
        }

        if (_sqliteProjectStateStore.Exists(projectDataDirectory))
        {
            return true;
        }

        var scanStatePath = _scanSessionStateService.GetStateFilePath(projectDataDirectory);
        if (!string.IsNullOrWhiteSpace(scanStatePath) && File.Exists(scanStatePath))
        {
            return true;
        }

        var progressPath = _translationProgressStateService.GetProgressFilePath(projectDataDirectory);
        return !string.IsNullOrWhiteSpace(progressPath) && File.Exists(progressPath);
    }

    private void EnsureMigrated(string projectDataDirectory)
    {
        if (string.IsNullOrWhiteSpace(projectDataDirectory) || _sqliteProjectStateStore.Exists(projectDataDirectory))
        {
            return;
        }

        var scanStatePath = _scanSessionStateService.GetStateFilePath(projectDataDirectory);
        var progressPath = _translationProgressStateService.GetProgressFilePath(projectDataDirectory);
        var hasLegacyScan = !string.IsNullOrWhiteSpace(scanStatePath) && File.Exists(scanStatePath);
        var hasLegacyProgress = !string.IsNullOrWhiteSpace(progressPath) && File.Exists(progressPath);
        if (!hasLegacyScan && !hasLegacyProgress)
        {
            return;
        }

        var scanSession = hasLegacyScan ? _scanSessionStateService.Load(projectDataDirectory) : null;
        var progressState = hasLegacyProgress ? _translationProgressStateService.Load(projectDataDirectory) : new TranslationProgressState();
        if (scanSession is null && progressState.Items.Count == 0)
        {
            return;
        }

        if (scanSession is not null)
        {
            _sqliteProjectStateStore.SaveScanSession(scanSession, projectDataDirectory);
        }

        if (progressState.Items.Count > 0)
        {
            _sqliteProjectStateStore.SaveTranslationProgressSnapshot(projectDataDirectory, progressState);
        }

        BackupLegacyFile(scanStatePath, projectDataDirectory);
        BackupLegacyFile(progressPath, projectDataDirectory);
    }

    private static void BackupLegacyFile(string legacyFilePath, string projectDataDirectory)
    {
        if (string.IsNullOrWhiteSpace(legacyFilePath) || !File.Exists(legacyFilePath))
        {
            return;
        }

        var backupDirectory = Path.Combine(
            projectDataDirectory,
            ".era-translator",
            "legacy-json-backup",
            DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff"));
        Directory.CreateDirectory(backupDirectory);
        var destinationPath = Path.Combine(backupDirectory, Path.GetFileName(legacyFilePath));
        File.Move(legacyFilePath, destinationPath);
    }
}
