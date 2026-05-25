using EraTranslator.Models;
using EraTranslator.Services;

namespace EraTranslator.Tests;

public sealed class UserDictionaryApplierTests
{
    [Fact]
    public void Apply_AppendsDictionaryTargetsIntoPlaceholderRestoreChain()
    {
        var applier = new UserDictionaryApplier();
        var protector = new PlaceholderProtector();
        var protectedText = protector.Protect("%CALLNAME%勇者와 勇者");

        var applied = applier.Apply(
            protectedText.Text,
            protectedText.Placeholders,
            [
                new UserDictionaryEntry { IsEnabled = true, Source = "勇者", Target = "용사" },
            ]);

        Assert.Equal("__PH0____PH1__와 __PH2__", applied.Text);
        Assert.Equal("%CALLNAME%용사와 용사", protector.Restore(applied.Text, applied.Placeholders));
    }

    [Fact]
    public void Apply_DoesNotReplaceInsideProtectedTokens()
    {
        var applier = new UserDictionaryApplier();

        var applied = applier.Apply(
            "__PH0__勇者",
            ["%CALLNAME%"],
            [
                new UserDictionaryEntry { IsEnabled = true, Source = "ERA", Target = "에라" },
                new UserDictionaryEntry { IsEnabled = true, Source = "勇者", Target = "용사" },
            ]);

        Assert.Equal("__PH0____PH1__", applied.Text);
        Assert.Equal(["%CALLNAME%", "용사"], applied.Placeholders);
    }
}
