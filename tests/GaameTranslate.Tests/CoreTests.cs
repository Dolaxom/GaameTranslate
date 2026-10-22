using GaameTranslate.Core;
using Xunit;

namespace GaameTranslate.Tests;

public class CoreTests
{
    [Theory]
    [InlineData("  Hello\tworld  \r\n\r\nTest ", "Hello world\nTest")]
    [InlineData(" ＡＢＣ　１２３ ", "ABC 123")]
    [InlineData("日本語\n中文", "日本語\n中文")]
    public void NormalizationPreservesLinesAndScripts(string input, string expected) => Assert.Equal(expected, TextProcessing.Normalize(input));
    [Fact] public void PhysicalMappingPreservesNegativeOrigin()
    {
        Assert.Equal(new PixelRect(-1800, 150, 100, 40), new PixelRect(200, 100, 200, 80).ToScreen(new(-1900, 100, 1920, 1080), 2));
    }
    [Fact] public void IntersectionClipsToDesktop() => Assert.Equal(new PixelRect(0, 0, 20, 30), new PixelRect(-10, -10, 30, 40).Intersect(new(0, 0, 100, 100)));
    [Fact] public void InvalidRectCannotBeSelected() => Assert.False(new PixelRect(0, 0, double.NaN, 20).IsValid);
    [Fact] public void OcrLanguageFallsBackToRegionalPack() => Assert.Equal("ja-JP", Languages.ResolveOcr("ja", ["en-US", "ja-JP"]));
    [Fact] public void MissingOcrPackHasActionableError() => Assert.Throws<UserFacingException>(() => Languages.ResolveOcr("ko", ["en-US"]));
    [Fact] public void OcrAutoNeedsInstalledPack() => Assert.Throws<UserFacingException>(() => Languages.ResolveOcr("auto", []));
    [Fact] public void SentenceSplittingUnderstandsJapanesePunctuation() => Assert.Equal(new[] { "こんにちは。", "元気ですか?", "Yes!", "Next." }, TextProcessing.SplitSentences("こんにちは。元気ですか？Yes! Next."));
    [Fact] public void UsesOcrWordSegmentation() => Assert.Equal(new[] { "日本", "語" }, TextProcessing.SelectableWords(new(0, "日本語", new(0,0,60,20), [new("日本",new(0,0,40,20)),new("語",new(40,0,20,20))])));
    [Theory]
    [InlineData(ReviewGrade.Again, 10d / 1440)] [InlineData(ReviewGrade.Hard, 1)] [InlineData(ReviewGrade.Good, 1)] [InlineData(ReviewGrade.Easy, 4)]
    public void NewCardSchedulesPredictably(ReviewGrade grade, double interval)
    {
        var now = DateTimeOffset.Parse("2026-10-23T12:00:00Z"); var result = ReviewScheduler.Apply(new(), grade, now);
        Assert.Equal(interval, result.IntervalDays, 8); Assert.Equal(now.AddDays(interval), result.DueAt); Assert.Equal(1, result.Reviews);
    }
    [Fact] public void RepeatedAgainNeverDropsEaseBelowFloor()
    {
        var entry = new VocabularyEntry(); for (var i=0; i<100; i++) entry = ReviewScheduler.Apply(entry, ReviewGrade.Again, DateTimeOffset.UtcNow);
        Assert.Equal(1.3, entry.Ease); Assert.True(entry.IntervalDays > 0);
    }
    [Theory] [InlineData("http://192.168.1.2:5000")] [InlineData("file:///tmp/test")] [InlineData("https://user:password@example.com")] [InlineData("https://example.com?key=x")]
    public void EndpointRejectsUnsafeConfiguration(string endpoint) => Assert.Throws<UserFacingException>(() => AppSettings.ValidateEndpoint(endpoint));
    [Theory] [InlineData("http://127.0.0.1:5000")] [InlineData("http://localhost:5000")] [InlineData("https://translate.example.com")]
    public void EndpointAcceptsTlsOrLocalhost(string endpoint) => Assert.NotNull(AppSettings.ValidateEndpoint(endpoint));
    [Fact] public void InvalidSettingsRejected() => Assert.Throws<UserFacingException>(() => (new AppSettings { OverlayOpacity = .01 }).Validate());
}
