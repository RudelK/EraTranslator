namespace EraTranslator.Services;

public sealed class ProjectStatePersistenceService(
    ScanSessionStateService? scanSessionStateService = null,
    TranslationProgressStateService? translationProgressStateService = null)
{
    private readonly ScanSessionStateService _scanSessionStateService = scanSessionStateService ?? new ScanSessionStateService();
    private readonly TranslationProgressStateService _translationProgressStateService = translationProgressStateService ?? new TranslationProgressStateService();

    public void SaveScanSession(ScanSession session, string projectDataDirectory)
    {
        _scanSessionStateService.Save(session, projectDataDirectory);
    }

    public ScanSession? LoadScanSession(string projectDataDirectory)
    {
        return _scanSessionStateService.Load(projectDataDirectory);
    }

    public int ApplyTranslationProgress(string projectDataDirectory, IEnumerable<ExtractedTextItem> items)
    {
        return _translationProgressStateService.Apply(projectDataDirectory, items);
    }

    public TranslationProgressState LoadTranslationProgress(string projectDataDirectory)
    {
        return _translationProgressStateService.Load(projectDataDirectory);
    }

    public void SaveTranslationProgress(string projectDataDirectory, IEnumerable<ExtractedTextItem> items)
    {
        _translationProgressStateService.Save(projectDataDirectory, items);
    }

    public void DeleteTranslationProgress(string projectDataDirectory)
    {
        _translationProgressStateService.Delete(projectDataDirectory);
    }

    public void DeleteAll(string projectDataDirectory)
    {
        _translationProgressStateService.Delete(projectDataDirectory);
        _scanSessionStateService.Delete(projectDataDirectory);
    }
}
