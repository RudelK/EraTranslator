using EraTranslator.Models;
using EraTranslator.Services;

namespace EraTranslator.Tests;

public sealed class CsvSchemaClassifierTests
{
    [Fact]
    public void GameBaseCsv_ExtractsOnlyValueColumn()
    {
        var classifier = new CsvSchemaClassifier();
        var fields = CsvLineParser.ParseFields("タイトル,era魔界牧場");
        var keyField = classifier.ClassifyExtractableField("CSV\\GameBase.csv", CsvDocumentKind.KeyValue, fields, 0);
        var valueField = classifier.ClassifyExtractableField("CSV\\GameBase.csv", CsvDocumentKind.KeyValue, fields, 1);

        Assert.Equal(CsvDocumentKind.KeyValue, classifier.DetectKind("CSV\\GameBase.csv", ["タイトル,era魔界牧場"]));
        Assert.Equal(CsvFieldRole.Key, classifier.ClassifyField("CSV\\GameBase.csv", CsvDocumentKind.KeyValue, fields, 0));
        Assert.Equal(CsvFieldRole.TranslatableValue, classifier.ClassifyField("CSV\\GameBase.csv", CsvDocumentKind.KeyValue, fields, 1));
        Assert.False(keyField.ShouldExtract);
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

        var boyfriendSurnameFields = CsvLineParser.ParseFields("彼氏姓,山田");
        var boyfriendNameFields = CsvLineParser.ParseFields("彼氏名,太郎");
        Assert.Equal(CsvFieldRole.TranslatableValue, classifier.ClassifyField(CsvDocumentKind.CharacterSheet, boyfriendSurnameFields, 1));
        Assert.Equal(CsvFieldRole.TranslatableValue, classifier.ClassifyField(CsvDocumentKind.CharacterSheet, boyfriendNameFields, 1));

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
    public void IdFirstReferenceTable_ExtractsSecondFieldAsReferenceBearingKey()
    {
        var classifier = new CsvSchemaClassifier();
        var fields = CsvLineParser.ParseFields("178,永久発情,;(エロいことを常に求めている)");

        var idField = classifier.ClassifyExtractableField("CSV\\Talent.csv", CsvDocumentKind.IdFirstTable, fields, 0);
        var nameField = classifier.ClassifyExtractableField("CSV\\Talent.csv", CsvDocumentKind.IdFirstTable, fields, 1);

        Assert.False(idField.ShouldExtract);
        Assert.False(idField.IsReferenceBearingKey);
        Assert.True(nameField.ShouldExtract);
        Assert.True(nameField.IsReferenceBearingKey);
        Assert.Equal("TALENT", nameField.SymbolNamespace);
        Assert.Equal("永久発情", nameField.OriginalSymbolKey);
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
