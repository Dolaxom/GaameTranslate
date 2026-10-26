using System.Media;
using System.Runtime.InteropServices.WindowsRuntime;
using GaameTranslate.Core;
using Windows.Media.SpeechSynthesis;

namespace GaameTranslate.Windows;

public sealed class WindowsSpeechService : ISpeechService, IDisposable
{
    private SoundPlayer? player;
    public IReadOnlyList<LanguageChoice> Voices
    {
        get
        {
            using var sapi = new System.Speech.Synthesis.SpeechSynthesizer();
            return SpeechSynthesizer.AllVoices.Select(v => new LanguageChoice(v.Id, $"{v.DisplayName} · {v.Language}")).Concat(sapi.GetInstalledVoices().Where(v => v.Enabled).Select(v => new LanguageChoice("sapi:" + v.VoiceInfo.Name, $"{v.VoiceInfo.Name} · {v.VoiceInfo.Culture.Name}"))).ToArray();
        }
    }
    public async Task<byte[]> SynthesizeAsync(string text, string language, string voice, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 2000) throw new UserFacingException("Для озвучки выберите слово или короткую фразу до 2 000 символов.");
        using var synthesizer = new SpeechSynthesizer();
        var selected = SpeechSynthesizer.AllVoices.FirstOrDefault(v => v.Id == voice && Languages.Base(v.Language) == Languages.Base(language))
            ?? SpeechSynthesizer.AllVoices.FirstOrDefault(v => Languages.Base(v.Language) == Languages.Base(language));
        if (selected is null || voice.StartsWith("sapi:", StringComparison.Ordinal)) return await SynthesizeSapiAsync(text, language, voice, token);
        synthesizer.Voice = selected; using var stream = await synthesizer.SynthesizeTextToStreamAsync(text).AsTask(token); using var source = stream.AsStreamForRead(); using var output = new MemoryStream(); await source.CopyToAsync(output, token); return output.ToArray();
    }
    public async Task SpeakAsync(string text, string language, string voice, CancellationToken token)
    {
        var audio = await SynthesizeAsync(text, language, voice, token); Stop(); var local = new SoundPlayer(new MemoryStream(audio)); player = local;
        try { using var cancel = token.Register(local.Stop); await Task.Run(local.PlaySync, token); }
        finally { Interlocked.CompareExchange(ref player, null, local); local.Dispose(); }
    }
    private static async Task<byte[]> SynthesizeSapiAsync(string text, string language, string voice, CancellationToken token)
    {
        using var synthesizer = new System.Speech.Synthesis.SpeechSynthesizer();
        var installed = synthesizer.GetInstalledVoices().Where(v => v.Enabled && Languages.Base(v.VoiceInfo.Culture.Name) == Languages.Base(language)).Select(v => v.VoiceInfo).ToArray();
        var selected = installed.FirstOrDefault(v => "sapi:" + v.Name == voice) ?? installed.FirstOrDefault();
        if (selected is null) throw new UserFacingException(L.M("Для языка '{0}' нет установленного голоса Windows. Добавьте голос в параметрах речи Windows.", language));
        synthesizer.SelectVoice(selected.Name); using var stream = new MemoryStream(); synthesizer.SetOutputToWaveStream(stream);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        synthesizer.SpeakCompleted += (_, e) => { if (e.Cancelled) completion.TrySetCanceled(token); else if (e.Error is { } error) completion.TrySetException(error); else completion.TrySetResult(); };
        token.ThrowIfCancellationRequested(); synthesizer.SpeakAsync(text); using var cancel = token.Register(synthesizer.SpeakAsyncCancelAll); await completion.Task.WaitAsync(token); synthesizer.SetOutputToNull(); return stream.ToArray();
    }
    public void Stop() { Interlocked.Exchange(ref player, null)?.Stop(); }
    public void Dispose() => Stop();
}
