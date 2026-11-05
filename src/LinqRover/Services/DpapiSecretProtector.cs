using System.Security.Cryptography;
using System.Text;
using LinqRover.Core.Storage;

namespace LinqRover.Services;

/// <summary>Cifra las contraseñas con DPAPI: solo el mismo usuario de Windows puede descifrarlas.</summary>
public sealed class DpapiSecretProtector : ISecretProtector
{
    public string Protect(string plainText)
    {
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plainText), null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(bytes);
    }

    public string Unprotect(string protectedText)
    {
        var bytes = ProtectedData.Unprotect(Convert.FromBase64String(protectedText), null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(bytes);
    }
}
