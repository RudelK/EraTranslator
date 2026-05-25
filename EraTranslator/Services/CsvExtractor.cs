namespace EraTranslator.Services;

public sealed class CsvExtractor
{
    private readonly CsvSchemaClassifier _classifier = new();

    public (CsvDocumentKind kind, List<TextSegment> segments, List<string> warnings) Extract(string documentId, string relativePath, string content)
    {
        var segments = new List<TextSegment>();
        var warnings = new List<string>();
        var lines = content.Split('\n');
        var kind = _classifier.DetectKind(relativePath, lines);
        var absoluteOffset = 0;
        var segmentIndex = 0;

        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var line = lines[lineIndex];
            var normalizedLine = line.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(normalizedLine) || normalizedLine.TrimStart().StartsWith(';'))
            {
                absoluteOffset += line.Length + 1;
                continue;
            }

            var fields = CsvLineParser.ParseFields(normalizedLine);
            var sourceKey = _classifier.BuildSourceKey(kind, fields);

            for (var fieldIndex = 0; fieldIndex < fields.Count; fieldIndex++)
            {
                var field = fields[fieldIndex];
                var classification = _classifier.ClassifyExtractableField(relativePath, kind, fields, fieldIndex);
                if (!classification.ShouldExtract)
                {
                    continue;
                }

                if (!classification.IsReferenceBearingKey && !TextHeuristics.ContainsTranslatableText(field.Value))
                {
                    continue;
                }

                if (field.Value.TrimStart().StartsWith(';'))
                {
                    continue;
                }

                segments.Add(new TextSegment
                {
                    SegmentId = $"{documentId}:{segmentIndex++}",
                    DocumentId = documentId,
                    SegmentType = $"csv-{kind}-field-{fieldIndex}",
                    AbsoluteStart = absoluteOffset + field.ValueStartWithinLine,
                    Length = field.Value.Length,
                    LineNumber = lineIndex + 1,
                    OriginalText = field.Value,
                    FieldIndex = fieldIndex,
                    SourceKey = sourceKey,
                    CsvFieldRole = classification.Role,
                    SymbolNamespace = classification.SymbolNamespace,
                    OriginalSymbolKey = classification.OriginalSymbolKey,
                    IsReferenceBearingKey = classification.IsReferenceBearingKey,
                });
            }

            absoluteOffset += line.Length + 1;
        }

        if (kind == CsvDocumentKind.GenericTable)
        {
            warnings.Add("CSV 문서 유형을 일반 테이블로 분류했습니다. 저장 전 결과를 검토하세요.");
        }

        return (kind, segments, warnings);
    }
}
