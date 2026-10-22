using System.Text.Json;
using GaameTranslate.Core;

namespace GaameTranslate.Infrastructure;

public sealed class SettingsStore(string directory) : ISettingsStore
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private string FilePath => Path.Combine(directory, "settings.json");
    public static JsonSerializerOptions JsonOptions { get; } = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(FilePath)) return new();
            try
            {
                await using var stream = File.OpenRead(FilePath);
                return (await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken) ?? new()).Validate();
            }
            catch (Exception e) when (e is JsonException or UserFacingException or NotSupportedException)
            {
                throw new UserFacingException("Settings could not be read. Your file has been preserved. Rename settings.json in the data folder to start with defaults.", e);
            }
        }
        finally { gate.Release(); }
    }
    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        settings.Validate();
        await gate.WaitAsync(cancellationToken);
        var temporary = FilePath + ".tmp";
        try
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(settings, JsonOptions), cancellationToken);
            File.Move(temporary, FilePath, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            gate.Release();
        }
    }
}
