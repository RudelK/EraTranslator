using EraTranslator.Models;
using EraTranslator.Services;

namespace EraTranslator.Tests;

public sealed class CsvSchemaClassifierTests
{
    [Fact]
    public void KeyValueCsv_ExtractsPlainKeyAndValue()
    {
        var classifier = new CsvSchemaClassifier();
        var fields = CsvLineParser.ParseFields("タイトル,era魔界牧場");
        var keyField = classifier.ClassifyExtractableField("CSV\\GameBase.csv", CsvDocumentKind.KeyValue, fields, 0);
        var valueField = classifier.ClassifyExtractableField("CSV\\GameBase.csv", CsvDocumentKind.KeyValue, fields, 1);

        Assert.Equal(CsvDocumentKind.KeyValue, classifier.DetectKind("CSV\\GameBase.csv", ["タイトル,era魔界牧場"]));
        Assert.Equal(CsvFieldRole.Key, classifier.ClassifyField("CSV\\GameBase.csv", CsvDocumentKind.KeyValue, fields, 0));
        Assert.Equal(CsvFieldRole.TranslatableValue, classifier.ClassifyField("CSV\\GameBase.csv", CsvDocumentKind.KeyValue, fields, 1));
        Assert.True(keyField.ShouldExtract);
        Assert.False(keyField.IsReferenceBearingKey);
        Assert.True(valueField.ShouldExtract);
    }

    [Fact]
    public void CharacterCsv_ExtractsReferenceBearingMetaKeys()
    {
        var classifier = new CsvSchemaClassifier();

        var nameFields = CsvLineParser.ParseFields("呼び名,あなた");
        Assert.Equal(CsvFieldRole.NonTranslatableValue, classifier.ClassifyField(CsvDocumentKind.CharacterSheet, nameFields, 0));
        Assert.Equal(CsvFieldRole.TranslatableValue, classifier.ClassifyField(CsvDocumentKind.CharacterSheet, nameFields, 1));

        var cstrFields = CsvLineParser.ParseFields("CSTR,種族,魔界人");
        var cstrMetaField = classifier.ClassifyExtractableField("CSV\\Chara3.csv", CsvDocumentKind.CharacterSheet, cstrFields, 1);
        Assert.Equal(CsvFieldRole.NonTranslatableValue, classifier.ClassifyField(CsvDocumentKind.CharacterSheet, cstrFields, 0));
        Assert.Equal(CsvFieldRole.MetaKey, classifier.ClassifyField(CsvDocumentKind.CharacterSheet, cstrFields, 1));
        Assert.Equal(CsvFieldRole.TranslatableValue, classifier.ClassifyField(CsvDocumentKind.CharacterSheet, cstrFields, 2));
        Assert.True(cstrMetaField.ShouldExtract);
        Assert.True(cstrMetaField.IsReferenceBearingKey);
        Assert.Equal("CSTR", cstrMetaField.SymbolNamespace);
        Assert.Equal("種族", cstrMetaField.OriginalSymbolKey);

        var flagFields = CsvLineParser.ParseFields("フラグ,外見年齢,18");
        var flagKeyField = classifier.ClassifyExtractableField("CSV\\Chara3.csv", CsvDocumentKind.CharacterSheet, flagFields, 1);
        Assert.True(flagKeyField.ShouldExtract);
        Assert.True(flagKeyField.IsReferenceBearingKey);
        Assert.Equal("CFLAG", flagKeyField.SymbolNamespace);
        Assert.Equal("外見年齢", flagKeyField.OriginalSymbolKey);

        var abilityFields = CsvLineParser.ParseFields("能力,事務,10");
        Assert.Equal(CsvFieldRole.Key, classifier.ClassifyField(CsvDocumentKind.CharacterSheet, abilityFields, 1));
        Assert.Equal(CsvFieldRole.NonTranslatableValue, classifier.ClassifyField(CsvDocumentKind.CharacterSheet, abilityFields, 2));
    }

    [Fact]
    public void NumericLeadingTable_WithExtraColumns_UsesIdFirstRules()
    {
        var classifier = new CsvSchemaClassifier();
        var fields = CsvLineParser.ParseFields("10,奴隷候補設定変更チケット,2000000,\t\t;説明");

        Assert.Equal(CsvDocumentKind.IdFirstTable, classifier.DetectKind("CSV\\Item.csv", ["10,奴隷候補設定変更チケット,2000000,\t\t;説明"]));
        Assert.Equal(CsvFieldRole.Key, classifier.ClassifyField(CsvDocumentKind.IdFirstTable, fields, 0));
        Assert.Equal(CsvFieldRole.TranslatableValue, classifier.ClassifyField(CsvDocumentKind.IdFirstTable, fields, 1));
        Assert.Equal(CsvFieldRole.NonTranslatableValue, classifier.ClassifyField(CsvDocumentKind.IdFirstTable, fields, 2));
        Assert.Equal(CsvFieldRole.TranslatableValue, classifier.ClassifyField(CsvDocumentKind.IdFirstTable, fields, 3));
    }

    [Fact]
    public void VariableSizeCsv_DoesNotTranslateAnyField()
    {
        var classifier = new CsvSchemaClassifier();
        var fields = CsvLineParser.ParseFields("STR,100,테스트");

        Assert.Equal(CsvFieldRole.NonTranslatableValue, classifier.ClassifyField("CSV\\VariableSize.csv", CsvDocumentKind.GenericTable, fields, 0));
        Assert.Equal(CsvFieldRole.NonTranslatableValue, classifier.ClassifyField("CSV\\VariableSize.csv", CsvDocumentKind.GenericTable, fields, 1));
        Assert.Equal(CsvFieldRole.NonTranslatableValue, classifier.ClassifyField("CSV\\VariableSize.csv", CsvDocumentKind.GenericTable, fields, 2));
    }
}
