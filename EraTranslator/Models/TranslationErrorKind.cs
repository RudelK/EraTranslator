namespace EraTranslator.Models;

public enum TranslationErrorKind
{
    None,
    Configuration,
    Timeout,
    Http,
    Json,
    MissingResult,
    Validation,
    Unknown,
}
