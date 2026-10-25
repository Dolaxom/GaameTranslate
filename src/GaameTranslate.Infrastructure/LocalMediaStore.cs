namespace GaameTranslate.Infrastructure;

public sealed class LocalMediaStore(string directory)
{
    public string DirectoryPath => directory;
    public async Task<string> SaveAsync(byte[] data, string extension, CancellationToken token = default)
    {
        if (extension is not (".png" or ".wav")) throw new ArgumentException("Only captured PNG and synthesized WAV media are supported.");
        Directory.CreateDirectory(directory); var name = Guid.NewGuid().ToString("N") + extension; await File.WriteAllBytesAsync(Path.Combine(directory, name), data, token); return name;
    }
    public string? Resolve(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || Path.GetFileName(name) != name) return null;
        var root = Path.GetFullPath(directory) + Path.DirectorySeparatorChar; var full = Path.GetFullPath(Path.Combine(directory, name)); return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(full) ? full : null;
    }
}
