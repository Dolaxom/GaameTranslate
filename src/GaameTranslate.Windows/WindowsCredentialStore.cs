using System.Security.Cryptography;
using System.Text;
using GaameTranslate.Core;

namespace GaameTranslate.Windows;

public sealed class WindowsCredentialStore(string directory) : ICredentialStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("GaameTranslate.credentials.v1");
    private string FileFor(string name) => Path.Combine(directory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name))) + ".secrets");
    public string? Read(string name)
    {
        var path = FileFor(name);
        if (!File.Exists(path)) return null;
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser)); }
        catch (CryptographicException e) { throw new UserFacingException("The saved key cannot be unlocked for this Windows account. Save it again in Settings.", e); }
    }
    public void Write(string name, string value)
    {
        Directory.CreateDirectory(directory);
        var bytes = Encoding.UTF8.GetBytes(value);
        try
        {
            var encrypted = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
            var path = FileFor(name); File.WriteAllBytes(path + ".tmp", encrypted); File.Move(path + ".tmp", path, true);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    public void Delete(string name) { var path = FileFor(name); if (File.Exists(path)) File.Delete(path); }
}
