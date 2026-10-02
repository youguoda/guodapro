using System.Text;
using Shiyu.Core;

namespace Shiyu.Core.Tests;

/// <summary>
/// 发布包演练（票 09）：对着一次真实 dotnet publish 的产物，把更新客户端
/// 的验收路径整个走一遍——校验和格式、zip 内部布局、openssl 签名互通、
/// 版本尾巴。CI 与普通测试运行里它静默通过；本地演练时设三个变量点亮：
///   SHIYU_DRILL_DIR     — 放 shiyu-win-x64.zip / .sha256 / .sig 的目录
///   SHIYU_DRILL_PUB     — 签名所用测试公钥的 PEM 文件（演练自造，非真钥）
///   SHIYU_DRILL_VERSION — 发布用的 -p:Version（默认 0.9.0-rc1）
/// </summary>
public class ReleasePackagingDrillTests
{
    private static bool Armed
        => Environment.GetEnvironmentVariable("SHIYU_DRILL_DIR") is { Length: > 0 }
            && Environment.GetEnvironmentVariable("SHIYU_DRILL_PUB") is { Length: > 0 };

    private static string Dir => Environment.GetEnvironmentVariable("SHIYU_DRILL_DIR")!;

    [Fact]
    public void A_published_package_survives_the_updater_path()
    {
        if (!Armed)
        {
            return;
        }

        var zip = Path.Combine(Dir, "shiyu-win-x64.zip");
        var checksum = File.ReadAllText(Path.Combine(Dir, "shiyu-win-x64.zip.sha256"));

        // .sha256 的格式要与 VerifyInstaller 的解析一致：首个空白分隔字段是哈希。
        var (ok, error) = UpdateStaging.VerifyInstaller(zip, new FileInfo(zip).Length, checksum);
        Assert.True(ok, error);

        // 布局：解压后 Shiyu.App.exe 必须落在暂存目录根部——ApplyAndRestart
        // 只认 staged\Shiyu.App.exe，多一层文件夹它就找不到新程序。
        using var staged = new TempDirectory();
        UpdateStaging.Extract(zip, staged.Path);
        Assert.True(
            File.Exists(Path.Combine(staged.Path, "Shiyu.App.exe")),
            "zip 根目录必须直接是 Shiyu.App.exe，不能有外层文件夹");
    }

    [Fact]
    public void The_openssl_signature_verifies_in_dotnet()
    {
        if (!Armed)
        {
            return;
        }

        var package = File.ReadAllBytes(Path.Combine(Dir, "shiyu-win-x64.zip"));
        var signature = File.ReadAllBytes(Path.Combine(Dir, "shiyu-win-x64.zip.sig"));
        var publicKey = File.ReadAllText(Environment.GetEnvironmentVariable("SHIYU_DRILL_PUB")!);

        // release.yml 用 openssl dgst -sha256 -sign 产签名、客户端用
        // ECDsa.ImportFromPem + Rfc3279DerSequence 验它：这对组合必须互通。
        Assert.True(UpdateSignature.Verify(package, signature, publicKey), "openssl 的签名必须被内置验签器接受");

        package[0] ^= 1;
        Assert.False(UpdateSignature.Verify(package, signature, publicKey));
    }

    [Fact]
    public void The_published_version_keeps_its_prerelease_tail_for_display()
    {
        if (!Armed)
        {
            return;
        }

        var version = Environment.GetEnvironmentVariable("SHIYU_DRILL_VERSION") is { Length: > 0 } v
            ? v
            : "0.9.0-rc1";

        using var staged = new TempDirectory();
        UpdateStaging.Extract(Path.Combine(Dir, "shiyu-win-x64.zip"), staged.Path);

        // InformationalVersion 是属性块里的字符串字面量：-p:Version=0.9.0-rc1
        // 时它必须带着尾巴，否则关于页/更新窗显示的就是个说谎的 0.9.0。
        var assembly = Encoding.Latin1.GetString(
            File.ReadAllBytes(Path.Combine(staged.Path, "Shiyu.App.dll")));
        Assert.Contains(version, assembly);
    }
}
