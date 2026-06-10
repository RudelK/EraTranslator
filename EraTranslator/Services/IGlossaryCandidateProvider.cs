using EraTranslator.Models;

namespace EraTranslator.Services;

public interface IGlossaryCandidateProvider
{
    IReadOnlyList<GlossaryHint> LoadCandidates(IReadOnlyList<ExtractedTextItem> batchItems);
}
