using System.Security.Cryptography;

namespace Shiyu.Core;

/// <summary>
/// 更新包的信任根（ADR-0010）：对安装包完整字节的 ECDSA P-256 签名。
/// 发布流水线用 openssl dgst -sha256 -sign 产出 DER 序列签名，随发布上传
/// <c>.zip.sig</c>；这里用内置公钥按同一份字节、同一个格式验它。大小与
/// SHA-256 只管完整性——能不能装，只由这一验说了算。
/// </summary>
public static class UpdateSignature
{
    /// <summary>
    /// 内置信任根。与 .github/workflows/release.yml 里发布前复验用的是同一把：
    /// 两处不一致时，发布流水线会在 runner 上红掉，而不是把没验过的包发出去。
    /// 换钥 = 改这里 + 改 workflow，且过渡版本用旧钥签名（ADR-0010 的换钥路径）。
    /// </summary>
    public const string BuiltInPublicKey = """
        -----BEGIN PUBLIC KEY-----
        MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEJ12pHGKuZN4YEGgNi8ij3M8W3FPf
        DvnRF5rToPQDvLPloNU3CrpiVuc7zoN97BeWFiOvaowf8J9p1jBU8VD6uw==
        -----END PUBLIC KEY-----
        """;

    /// <summary>
    /// 验签。公钥解析不了、签名不是合法 DER——都算"不过"，不是异常：
    /// 调用方只该见到过/不过两个世界，出错的输入和被篡改的输入同一个下场。
    /// </summary>
    public static bool Verify(byte[] data, byte[] signature, string? publicKeyPem = null)
    {
        if (data.Length == 0 || signature.Length == 0)
        {
            return false;
        }

        try
        {
            using var key = ECDsa.Create();
            key.ImportFromPem(publicKeyPem is { Length: > 0 } injected ? injected : BuiltInPublicKey);
            return key.VerifyData(
                data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        }
        catch (CryptographicException)
        {
            // 公钥不合法，或签名字节根本排不成一个 DER 序列。
            return false;
        }
        catch (ArgumentException)
        {
            // ImportFromPem 对非 PEM 输入走这一条。
            return false;
        }
    }
}
