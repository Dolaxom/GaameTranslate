using System.Text.Json;
using GaameTranslate.Core;

namespace GaameTranslate.Infrastructure;

public sealed record GameProfile(string Name, string Executable, AppSettings Settings);
public sealed class GameProfileStore(string directory)
{
    private string FilePath => Path.Combine(directory, "game-profiles.json");
    private readonly SemaphoreSlim gate = new(1, 1);
    public async Task<IReadOnlyList<GameProfile>> LoadAsync(CancellationToken token = default)
    {
        if (!File.Exists(FilePath)) return []; if (new FileInfo(FilePath).Length > 4 * 1024 * 1024) throw new UserFacingException("Файл профилей слишком велик.");
        try { return JsonSerializer.Deserialize<GameProfile[]>(await File.ReadAllTextAsync(FilePath, token)) ?? []; } catch (JsonException e) { throw new UserFacingException("Не удалось прочитать игровые профили.", e); }
    }
    public async Task SaveAsync(GameProfile profile, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(profile.Name) || string.IsNullOrWhiteSpace(profile.Executable) || profile.Executable.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new UserFacingException("Укажите имя профиля и имя процесса, например disco.exe."); profile.Settings.Validate(); await gate.WaitAsync(token);
        try { var all = (await LoadAsync(token)).Where(p => !p.Executable.Equals(profile.Executable, StringComparison.OrdinalIgnoreCase)).Append(profile).Take(100).ToArray(); Directory.CreateDirectory(directory); var temp = FilePath + ".tmp"; await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }), token); File.Move(temp, FilePath, true); } finally { gate.Release(); }
    }
    public async Task<GameProfile?> FindAsync(string executable, CancellationToken token = default) => (await LoadAsync(token)).FirstOrDefault(p => p.Executable.Equals(executable, StringComparison.OrdinalIgnoreCase));
}
