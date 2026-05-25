using EraTranslator.Models;

namespace EraTranslator.Services;

public sealed record TranslationErrorDetail(
    TranslationErrorKind Kind,
    string Message,
    int? HttpStatusCode = null);
