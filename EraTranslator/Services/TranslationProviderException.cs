using System.Net;
using EraTranslator.Models;

namespace EraTranslator.Services;

public sealed class TranslationProviderException : Exception
{
    public TranslationProviderException(
        TranslationErrorKind kind,
        string message,
        HttpStatusCode? statusCode = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        StatusCode = statusCode;
    }

    public TranslationErrorKind Kind { get; }

    public HttpStatusCode? StatusCode { get; }
}
