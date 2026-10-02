using System.Security.Cryptography;
using System.Text;
using Shiyu.Core;

namespace Shiyu.Windows;

/// <summary>
/// The API key at rest, protected for this Windows user only (ADR-0011):
/// DPAPI's CurrentUser scope means the settings file can wander — a sync
/// folder, a shared backup, a sold disk — and the key still opens for
/// nobody. That is also why the protected form is never exported: backups
/// carry the plain key inside their own encryption instead.
/// </summary>
public sealed class DpapiSecretProtector : ISecretProtector
{
    // Entropy so a blob protected for "any purpose by this user" cannot be
    // repurposed by unrelated software that also uses CurrentUser DPAPI.
    private static readonly byte[] Entropy = "Shiyu.BackendApiKey.v1"u8.ToArray();

    public string? Protect(string plain)
    {
        try
        {
            var blob = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser);
            return AppSettings.SecretMarker + Convert.ToBase64String(blob);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public string? Unprotect(string stored)
    {
        if (!stored.StartsWith(AppSettings.SecretMarker, StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            var blob = Convert.FromBase64String(stored[AppSettings.SecretMarker.Length..]);
            return Encoding.UTF8.GetString(
                ProtectedData.Unprotect(blob, Entropy, DataProtectionScope.CurrentUser));
        }
        catch (CryptographicException)
        {
            // A blob from another machine or user profile — not ours to open.
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
