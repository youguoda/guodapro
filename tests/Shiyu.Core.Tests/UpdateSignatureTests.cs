using System.Security.Cryptography;
using Shiyu.Core;

namespace Shiyu.Core.Tests;

public class UpdateSignatureTests
{
    /// <summary>
    /// 测试自造的临时密钥对（每把只用一次）：公钥从参数注入，从不碰内置
    /// 信任根——内置钥只该在"格式是否合法"的意义上被测，它对应的私钥
    /// 按设计就不在本仓库。
    /// </summary>
    private static (byte[] Signature, string PublicKeyPem) SignWithFreshKey(byte[] data)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (
            key.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence),
            key.ExportSubjectPublicKeyInfoPem());
    }

    private static byte[] Package() => "a zip archive worth protecting"u8.ToArray();

    [Fact]
    public void A_correct_signature_passes()
    {
        var package = Package();
        var (signature, publicKey) = SignWithFreshKey(package);

        Assert.True(UpdateSignature.Verify(package, signature, publicKey));
    }

    [Fact]
    public void A_single_flipped_byte_in_the_package_is_rejected()
    {
        var package = Package();
        var (signature, publicKey) = SignWithFreshKey(package);

        package[3] ^= 1;

        Assert.False(UpdateSignature.Verify(package, signature, publicKey));
    }

    [Fact]
    public void A_tampered_signature_is_rejected()
    {
        var package = Package();
        var (signature, publicKey) = SignWithFreshKey(package);

        signature[signature.Length / 2] ^= 1;

        Assert.False(UpdateSignature.Verify(package, signature, publicKey));
    }

    [Fact]
    public void A_missing_or_empty_signature_is_rejected()
    {
        var package = Package();
        var (_, publicKey) = SignWithFreshKey(package);

        Assert.False(UpdateSignature.Verify(package, [], publicKey));
        Assert.False(UpdateSignature.Verify(package, new byte[64], publicKey));
    }

    [Fact]
    public void A_signature_from_another_key_is_rejected()
    {
        var package = Package();
        var (signature, _) = SignWithFreshKey(package);
        var (_, otherKey) = SignWithFreshKey("something else"u8.ToArray());

        Assert.False(UpdateSignature.Verify(package, signature, otherKey));
    }

    [Fact]
    public void A_signature_in_the_wrong_format_is_not_accepted()
    {
        // 只认 DER 序列（openssl dgst -sign 的产出形状）：裸 r||s 拼接
        // 即便值是对的也不放行，省得两种格式将来各说各话。
        var package = Package();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var raw = key.SignData(
            package, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        Assert.False(UpdateSignature.Verify(package, raw, key.ExportSubjectPublicKeyInfoPem()));
    }

    [Fact]
    public void The_builtin_key_is_a_valid_p256_public_key()
    {
        // 常量抄错一个字符，老客户端就再也无法更新——这里把"内置的确实
        // 是一把 P-256 公钥"钉住；与发布流水线的互通由 runner 上的复验兜底。
        using var key = ECDsa.Create();
        key.ImportFromPem(UpdateSignature.BuiltInPublicKey);

        Assert.Equal(256, key.KeySize);
        Assert.False(UpdateSignature.Verify(Package(), new byte[72]));
    }
}
