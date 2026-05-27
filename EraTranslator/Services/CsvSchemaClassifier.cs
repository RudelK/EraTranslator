namespace EraTranslator.Services;

public sealed class CsvSchemaClassifier
{
    private static readonly Dictionary<string, string> CsvNamespaceByFileName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Cflag.csv"] = "CFLAG",
        ["Tflag.csv"] = "TFLAG",
        ["Flag.csv"] = "FLAG",
        ["Cstr.csv"] = "CSTR",
        ["Str.csv"] = "STR",
        ["Savestr.csv"] = "SAVESTR",
        ["Item.csv"] = "ITEM",
        ["Base.csv"] = "BASE",
        ["Abl.csv"] = "ABL",
        ["Palam.csv"] = "PALAM",
        ["Exp.csv"] = "EXP",
        ["Mark.csv"] = "MARK",
        ["Talent.csv"] = "TALENT",
        ["Source.csv"] = "SOURCE",
        ["Juel.csv"] = "JUEL",
        ["Tequip.csv"] = "TEQUIP",
        ["Nowex.csv"] = "NOWEX",
        ["Ex.csv"] = "EX",
        ["Tcvar.csv"] = "TCVAR",
    };

    private static readonly HashSet<string> CharacterValueKeys =
    [
        "呼び名",
        "ニックネーム",
        "名前",
    ];

    private static readonly HashSet<string> CharacterMetaContainers =
    [
        "CSTR",
        "CALLNAME",
        "NAME",
    ];

    private static readonly HashSet<string> CharacterWhitespacePreservingContainers =
    [
        "CSTR",
        "CALLNAME",
        "NAME",
    ];

    private static readonly Dictionary<string, string> CharacterContainerNamespaces = new(StringComparer.Ordinal)
    {
        ["CSTR"] = "CSTR",
        ["CALLNAME"] = "CALLNAME",
        ["NAME"] = "NAME",
        ["フラグ"] = "CFLAG",
        ["素質"] = "TALENT",
        ["能力"] = "ABL",
        ["基礎"] = "BASE",
        ["装着物"] = "TEQUIP",
        ["汚れ"] = "STAIN",
        ["ジュエル"] = "JUEL",
        ["経験"] = "EXP",
        ["刻印"] = "MARK",
        ["欲情"] = "PALAM",
        ["源"] = "SOURCE",
    };

    public CsvDocumentKind DetectKind(string relativePath, IReadOnlyList<string> lines)
    {
        if (IsVariableSizeFile(relativePath))
        {
            return CsvDocumentKind.GenericTable;
        }

        if (Path.GetFileName(relativePath).StartsWith("Chara", StringComparison.OrdinalIgnoreCase))
        {
            return CsvDocumentKind.CharacterSheet;
        }

        var firstDataLine = lines
            .Select(line => line.Trim())
            .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith(';'));

        if (firstDataLine is null)
        {
            return CsvDocumentKind.GenericTable;
        }

        var fields = CsvLineParser.ParseFields(firstDataLine);
        if (fields.Count == 2 && fields[0].Value.Equals("番号", StringComparison.Ordinal))
        {
            return CsvDocumentKind.CharacterSheet;
        }

        if (fields.Count >= 2 && int.TryParse(fields[0].Value, out _))
        {
            return CsvDocumentKind.IdFirstTable;
        }

        if (fields.Count == 2)
        {
            return CsvDocumentKind.KeyValue;
        }

        return CsvDocumentKind.GenericTable;
    }

    public CsvFieldRole ClassifyField(CsvDocumentKind kind, IReadOnlyList<CsvFieldInfo> fields, int fieldIndex)
    {
        if (fieldIndex >= fields.Count)
        {
            return CsvFieldRole.None;
        }

        var current = fields[fieldIndex].Value.Trim();
        var first = fields[0].Value.Trim();

        return kind switch
        {
            CsvDocumentKind.KeyValue => ClassifyKeyValue(fields, fieldIndex),
            CsvDocumentKind.IdFirstTable => ClassifyIdFirstTable(fields, fieldIndex),
            CsvDocumentKind.CharacterSheet => ClassifyCharacterSheet(fields, fieldIndex),
            CsvDocumentKind.GenericTable => ClassifyGenericTable(fields, fieldIndex),
            _ => fieldIndex == 0 || TextHeuristics.IsNumericLike(current)
                ? CsvFieldRole.Key
                : CsvFieldRole.TranslatableValue,
        };
    }

    public string BuildSourceKey(CsvDocumentKind kind, IReadOnlyList<CsvFieldInfo> fields)
    {
        if (fields.Count == 0)
        {
            return string.Empty;
        }

        if (kind == CsvDocumentKind.CharacterSheet && fields.Count >= 2 && CharacterMetaContainers.Contains(fields[0].Value.Trim()))
        {
            return $"{fields[0].Value.Trim()}/{fields[1].Value.Trim()}";
        }

        return fields[0].Value.Trim();
    }

    public CsvFieldRole ClassifyField(string relativePath, CsvDocumentKind kind, IReadOnlyList<CsvFieldInfo> fields, int fieldIndex)
    {
        if (IsVariableSizeFile(relativePath))
        {
            return CsvFieldRole.NonTranslatableValue;
        }

        return ClassifyField(kind, fields, fieldIndex);
    }

    public CsvFieldClassification ClassifyExtractableField(string relativePath, CsvDocumentKind kind, IReadOnlyList<CsvFieldInfo> fields, int fieldIndex)
    {
        var role = ClassifyField(relativePath, kind, fields, fieldIndex);
        if (role == CsvFieldRole.None || role == CsvFieldRole.NonTranslatableValue)
        {
            return new CsvFieldClassification
            {
                Role = role,
                ShouldExtract = false,
            };
        }

        if (IsGameBaseFile(relativePath) && fieldIndex == 0)
        {
            return new CsvFieldClassification
            {
                Role = role,
                ShouldExtract = false,
            };
        }

        if (kind == CsvDocumentKind.CharacterSheet)
        {
            return ClassifyCharacterSheetField(fields, fieldIndex, role);
        }

        var symbolNamespace = ResolveFileNamespace(relativePath);
        var current = fieldIndex < fields.Count ? fields[fieldIndex].Value.Trim() : string.Empty;
        var isNumericReferenceTableIdField = kind == CsvDocumentKind.IdFirstTable
            && fieldIndex == 0
            && role == CsvFieldRole.Key
            && !string.IsNullOrWhiteSpace(symbolNamespace);
        var isReferenceBearingNameField = kind == CsvDocumentKind.IdFirstTable
            && fieldIndex == 1
            && role == CsvFieldRole.TranslatableValue
            && !string.IsNullOrWhiteSpace(symbolNamespace);
        var isKeyField = fieldIndex == 0
            && role == CsvFieldRole.Key
            && !isNumericReferenceTableIdField;
        var shouldExtract = isKeyField || isReferenceBearingNameField || role == CsvFieldRole.TranslatableValue;

        return new CsvFieldClassification
        {
            Role = role,
            ShouldExtract = shouldExtract,
            SymbolNamespace = isReferenceBearingNameField || isKeyField ? symbolNamespace : string.Empty,
            OriginalSymbolKey = isReferenceBearingNameField || isKeyField ? current : string.Empty,
            IsReferenceBearingKey = (isReferenceBearingNameField || isKeyField) && !string.IsNullOrWhiteSpace(symbolNamespace),
        };
    }

    private static CsvFieldRole ClassifyKeyValue(IReadOnlyList<CsvFieldInfo> fields, int fieldIndex)
    {
        if (fieldIndex == 0)
        {
            return CsvFieldRole.Key;
        }

        return IsTranslatableValue(fields[fieldIndex].Value)
            ? CsvFieldRole.TranslatableValue
            : CsvFieldRole.NonTranslatableValue;
    }

    private static CsvFieldRole ClassifyIdFirstTable(IReadOnlyList<CsvFieldInfo> fields, int fieldIndex)
    {
        if (fieldIndex == 0)
        {
            return CsvFieldRole.Key;
        }

        return IsTranslatableValue(fields[fieldIndex].Value)
            ? CsvFieldRole.TranslatableValue
            : CsvFieldRole.NonTranslatableValue;
    }

    private static CsvFieldRole ClassifyCharacterSheet(IReadOnlyList<CsvFieldInfo> fields, int fieldIndex)
    {
        var first = fields[0].Value.Trim();
        if (fieldIndex == 0)
        {
            return CsvFieldRole.NonTranslatableValue;
        }

        if (CharacterValueKeys.Contains(first))
        {
            return fieldIndex == 1 && IsTranslatableValue(fields[fieldIndex].Value)
                ? CsvFieldRole.TranslatableValue
                : CsvFieldRole.NonTranslatableValue;
        }

        if (CharacterMetaContainers.Contains(first))
        {
            if (fieldIndex == 1)
            {
                return CsvFieldRole.MetaKey;
            }

            return IsTranslatableValue(fields[fieldIndex].Value)
                ? CsvFieldRole.TranslatableValue
                : CsvFieldRole.NonTranslatableValue;
        }

        if (fieldIndex == 1)
        {
            return CsvFieldRole.Key;
        }

        return CsvFieldRole.NonTranslatableValue;
    }

    private static CsvFieldRole ClassifyGenericTable(IReadOnlyList<CsvFieldInfo> fields, int fieldIndex)
    {
        if (fieldIndex == 0)
        {
            return CsvFieldRole.Key;
        }

        return IsTranslatableValue(fields[fieldIndex].Value)
            ? CsvFieldRole.TranslatableValue
            : CsvFieldRole.NonTranslatableValue;
    }

    private static bool IsTranslatableValue(string value)
    {
        return TextHeuristics.ContainsTranslatableText(value)
            && !TextHeuristics.LooksLikeCodeOnly(value)
            && !TextHeuristics.IsNumericLike(value);
    }

    private static bool IsGameBaseFile(string relativePath)
    {
        return string.Equals(Path.GetFileName(relativePath), "GameBase.csv", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsVariableSizeFile(string relativePath)
    {
        return string.Equals(Path.GetFileName(relativePath), "VariableSize.csv", StringComparison.OrdinalIgnoreCase);
    }

    private CsvFieldClassification ClassifyCharacterSheetField(IReadOnlyList<CsvFieldInfo> fields, int fieldIndex, CsvFieldRole role)
    {
        var container = fields[0].Value.Trim();
        var symbolNamespace = ResolveCharacterContainerNamespace(container);
        var value = fieldIndex < fields.Count ? fields[fieldIndex].Value.Trim() : string.Empty;

        if (CharacterValueKeys.Contains(container))
        {
            return new CsvFieldClassification
            {
                Role = role,
                ShouldExtract = role == CsvFieldRole.TranslatableValue,
                PreserveWhitespace = role == CsvFieldRole.TranslatableValue,
            };
        }

        if (CharacterMetaContainers.Contains(container))
        {
            if (fieldIndex == 1)
            {
                return new CsvFieldClassification
                {
                    Role = role,
                    ShouldExtract = true,
                    PreserveWhitespace = false,
                    SymbolNamespace = symbolNamespace,
                    OriginalSymbolKey = value,
                    IsReferenceBearingKey = !string.IsNullOrWhiteSpace(symbolNamespace),
                };
            }

            return new CsvFieldClassification
            {
                Role = role,
                ShouldExtract = role == CsvFieldRole.TranslatableValue,
                PreserveWhitespace = role == CsvFieldRole.TranslatableValue
                    && CharacterWhitespacePreservingContainers.Contains(container),
            };
        }

        if (fieldIndex == 1 && role == CsvFieldRole.Key)
        {
            return new CsvFieldClassification
            {
                Role = role,
                ShouldExtract = true,
                SymbolNamespace = symbolNamespace,
                OriginalSymbolKey = value,
                IsReferenceBearingKey = !string.IsNullOrWhiteSpace(symbolNamespace),
            };
        }

        return new CsvFieldClassification
        {
            Role = role,
            ShouldExtract = role == CsvFieldRole.TranslatableValue,
        };
    }

    private static string ResolveFileNamespace(string relativePath)
    {
        var fileName = Path.GetFileName(relativePath);
        return CsvNamespaceByFileName.GetValueOrDefault(fileName, string.Empty);
    }

    private static string ResolveCharacterContainerNamespace(string container)
    {
        return CharacterContainerNamespaces.GetValueOrDefault(container, string.Empty);
    }

}
