namespace Shiyu.Core;

/// <summary>
/// Protects one secret at rest: the translation API key. The history database
/// stays unencrypted by ADR-0007 — that decision is about clipboard content,
/// and it explicitly does not cover credentials. A key is a billing account;
/// a leaked backup of settings.json must not carry a working credential
/// (ADR-0011).
///
/// The protecting side lives in the Windows layer (DPAPI, CurrentUser scope);
/// Core only knows this port, so the settings' load/save round trip stays
/// unit-testable. The stored form is the marker from <see cref="AppSettings"/>
/// plus whatever opaque text the protector produces.
/// </summary>
public interface ISecretProtector
{
    /// <summary>Marker + opaque protected text, or null when protection failed.</summary>
    string? Protect(string plain);

    /// <summary>The plain secret, or null when it cannot be recovered.</summary>
    string? Unprotect(string stored);
}
