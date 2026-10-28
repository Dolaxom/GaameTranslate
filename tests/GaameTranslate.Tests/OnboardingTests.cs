using GaameTranslate.Core;
using Xunit;

namespace GaameTranslate.Tests;

public sealed class OnboardingTests
{
    [Fact] public void EveryConnectorHasOfflineBilingualSetupInstructions()
    {
        foreach (var provider in ProviderCatalog.All.Select(p => p.Name).Append("None"))
        {
            var guide = ProviderSetupGuides.Get(provider);
            Assert.Contains("1.", guide.Steps); Assert.Contains("2.", guide.Steps); Assert.Contains("3.", guide.Steps);
            foreach (var key in new[] { guide.Category, guide.Steps, guide.Note })
            {
                Assert.True(UiText.Catalogue.ContainsKey(key), key);
                Assert.NotEqual(UiText.Get(key, "en"), UiText.Get(key, "ru"));
            }
            if (guide.DocumentationUrl is not null) Assert.Equal("https", new Uri(guide.DocumentationUrl).Scheme);
        }
    }
    [Fact] public void StartupTipsAreEnabledForExistingSettingsWithoutChangingTranslationChoices()
    {
        var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>("{\"Provider\":\"LibreTranslate\",\"TargetLanguage\":\"ru\"}")!;
        Assert.True(settings.ShowStartupTips); Assert.Equal("LibreTranslate", settings.Provider);
    }
    [Theory]
    [InlineData("OpenAI compatible")][InlineData("Ollama")][InlineData("YandexGPT")]
    public void IncompleteConnectorsAreIdentified(string provider)
        => Assert.True(ProviderCatalog.MissingSetupFields(new AppSettings { Provider = provider, CompatibleModel = "", OllamaModel = "", YandexFolderId = "" }));
}
