using System.Text.RegularExpressions;
using GaameTranslate.Core;
using Xunit;

namespace GaameTranslate.Tests;

public sealed class LocalizationTests
{
    [Fact] public void EveryResourceHasBothLanguages()
    {
        Assert.True(UiText.Catalogue.Count > 600);
        foreach (var resource in UiText.Catalogue)
        {
            Assert.False(string.IsNullOrWhiteSpace(resource.Value["en"]), resource.Key);
            Assert.False(string.IsNullOrWhiteSpace(resource.Value["ru"]), resource.Key);
            Assert.DoesNotMatch("[А-Яа-яЁё]", resource.Value["en"]);
        }
    }
    [Fact] public void TranslationArgumentsMatchInBothLanguages()
    {
        foreach (var resource in UiText.Catalogue)
        {
            var en = Regex.Matches(resource.Value["en"], @"\{\d+[^}]*\}").Select(m => m.Value).Order().ToArray();
            var ru = Regex.Matches(resource.Value["ru"], @"\{\d+[^}]*\}").Select(m => m.Value).Order().ToArray();
            Assert.True(en.SequenceEqual(ru), resource.Key);
        }
    }
    [Theory]
    [InlineData("Home", "Главная", "Home")]
    [InlineData("Сохранить", "Сохранить", "Save")]
    [InlineData("СЛОВО ПОД КУРСОРОМ", "СЛОВО ПОД КУРСОРОМ", "WORD UNDER THE POINTER")]
    [InlineData("Exit", "Выход", "Exit")]
    public void MessagesHaveExpectedTranslations(string key, string ru, string en)
    { Assert.Equal(ru, UiText.Get(key, "ru")); Assert.Equal(en, UiText.Get(key, "en")); }
    [Fact] public void ExistingSettingsDefaultToRussian() => Assert.Equal("ru", new AppSettings().InterfaceLanguage);
    [Fact] public void UnsupportedInterfaceLanguageIsRejected() => Assert.Throws<UserFacingException>(() => (new AppSettings { InterfaceLanguage = "de" }).Validate());
}
