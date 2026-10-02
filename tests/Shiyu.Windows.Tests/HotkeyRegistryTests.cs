using Shiyu.Windows;

namespace Shiyu.Windows.Tests;

/// <summary>
/// The retirement contract of the hotkey registry (O-43): the registry is
/// dropped and rebuilt as a set on every settings save (O-20), so any window
/// still holding the retired instance must hear a loud refusal rather than
/// silently re-arming a hotkey whose owner is gone.
///
/// The guards fire before any Win32 call, so these tests never take a real
/// global hotkey — safe to run beside the user's live instance.
/// </summary>
public sealed class HotkeyRegistryTests
{
    [Fact]
    public void Registering_after_dispose_throws()
    {
        using var window = new MessageWindow();
        var registry = new HotkeyRegistry(window);
        registry.Dispose();

        Assert.Throws<ObjectDisposedException>(() => registry.Register(
            new Hotkey(HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x5A, "测试"),
            () => { }));
    }

    [Fact]
    public void Scoped_registration_after_dispose_throws()
    {
        using var window = new MessageWindow();
        var registry = new HotkeyRegistry(window);
        registry.Dispose();

        Assert.Throws<ObjectDisposedException>(() => registry.TryRegisterScoped(
            new Hotkey(HotkeyModifiers.None, 0x1B, "关闭面板"),
            () => { }));
    }

    [Fact]
    public void Dispose_is_idempotent()
    {
        using var window = new MessageWindow();
        var registry = new HotkeyRegistry(window);
        registry.Dispose();
        registry.Dispose();
    }
}
