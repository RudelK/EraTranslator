namespace EraTranslator.Services;

public sealed class EzTransXpTranslationProvider : ITranslationProvider
{
    public Task<TranslationProviderResult> TranslateAsync(
        IReadOnlyList<ProtectedSegment> requests,
        ProviderSettings settings,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("EzTransXP는 로컬 설치 환경별 COM/DLL 바인딩 차이가 커서 현재는 연결 골격만 포함했습니다.");
    }
}
