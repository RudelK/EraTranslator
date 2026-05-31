using EraTranslator.Models;
using EraTranslator.Services;

namespace EraTranslator.Tests;

public sealed class PhaseScopedGlossaryBuilderTests
{
    private readonly PhaseScopedGlossaryBuilder _builder = new();

    [Fact]
    public void BuildForPhase_UsesReferenceKeysForCsvGeneralAndCarriesCsvErhSourcesForward()
    {
        var csvValue = BuildItem("CSV", "快楽", "쾌락", csvFieldRole: CsvFieldRole.TranslatableValue);
        var csvReferenceKey = BuildItem("CSV", "快楽値", "쾌락치", isReferenceBearingKey: true);
        var csvPlainKey = BuildItem("CSV", "ABL", "능력", csvFieldRole: CsvFieldRole.Key);
        var erhValue = BuildItem("ERH", "快楽値", "쾌락치");
        var review = BuildItem("ERH", "発情", "발정", status: "검수 필요", canSave: true);
        var failed = BuildItem("ERH", "失敗", "실패", status: "번역 실패", validationStatus: "검증 전", canSave: false);
        var placeholder = BuildItem("CSV", "%CALLNAME%", "이름", csvFieldRole: CsvFieldRole.TranslatableValue);
        var longText = BuildItem("ERH", "これはとても長い説明文で、glossaryの候補としては扱いたくない文章であり、さらに長くして除外対象を明確にします。", "아주 긴 설명문");
        var blockedReview = BuildItem("ERH", "危険", "위험", status: "검수 필요", canSave: false);

        var csvGeneralHints = _builder.BuildForPhase(
            [csvValue, csvReferenceKey, csvPlainKey, erhValue, review, failed, placeholder, longText, blockedReview],
            TranslationPhaseKind.CsvGeneral);
        var erhHints = _builder.BuildForPhase(
            [csvValue, csvReferenceKey, csvPlainKey, erhValue, review, failed, placeholder, longText, blockedReview],
            TranslationPhaseKind.Erh);
        var erbHints = _builder.BuildForPhase(
            [csvValue, csvReferenceKey, csvPlainKey, erhValue, review, failed, placeholder, longText, blockedReview],
            TranslationPhaseKind.Erb);

        Assert.Equal(["快楽値"], csvGeneralHints.Select(static hint => hint.Source).ToList());
        Assert.Equal(["快楽値", "快楽"], erhHints.Select(static hint => hint.Source).ToList());
        Assert.Equal(["快楽値", "快楽", "発情"], erbHints.Select(static hint => hint.Source).ToList());
    }

    [Fact]
    public void SelectForBatch_ReturnsOverlapOnlyAndDedupesRepeatedTargets()
    {
        var availableHints = new[]
        {
            new GlossaryHint("快楽値", "쾌락치", "ERH"),
            new GlossaryHint("快楽", "쾌락", "CSV"),
            new GlossaryHint("快", "쾌락", "ERH"),
            new GlossaryHint("ご主人さま", "주인님", "CSV"),
        };
        var batch = new[]
        {
            BuildItem("ERB", "快楽値が上がった", string.Empty, status: "번역 대기", validationStatus: "검증 전", canSave: true),
        };

        var selected = _builder.SelectForBatch(availableHints, batch);

        Assert.Equal(["快楽値", "快楽"], selected.Select(static hint => hint.Source).ToList());
    }

    private static ExtractedTextItem BuildItem(
        string fileType,
        string originalText,
        string translatedText,
        CsvFieldRole csvFieldRole = CsvFieldRole.TranslatableValue,
        bool isReferenceBearingKey = false,
        string status = "번역 완료",
        string validationStatus = "통과",
        bool canSave = true)
    {
        var item = new ExtractedTextItem
        {
            SegmentId = Guid.NewGuid().ToString("N"),
            DocumentId = $"{fileType}/Doc",
            FileType = fileType,
            RelativePath = $"{fileType}/Doc",
            EncodingName = "utf-8",
            SegmentType = "quoted-string",
            LineNumber = 1,
            OriginalText = originalText,
            CsvFieldRole = csvFieldRole,
            IsReferenceBearingKey = isReferenceBearingKey,
            WarningText = string.Empty,
        };
        item.ApplyTranslationState(status, validationStatus, string.Empty, canSave, translatedText);
        return item;
    }
}
