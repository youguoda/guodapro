namespace Shiyu.Core;

public enum KeyFlow
{
    Pass,
    Swallow,
}

public enum SpecialKey
{
    LeftWin,
    RightWin,
    V,
    Other,
}

public enum KeyDirection
{
    Down,
    Up,
}

/// <summary>
/// Decides, key event by key event, what a Win+V takeover does with it.
///
/// The rules that must never slip, because each slip is felt system-wide:
/// Win+V (either Win) is swallowed and triggers; the V and the Win releases
/// that follow a takeover are swallowed too (releasing the Win later would
/// pop the Start menu open); every other key — Win alone, Win+anything-else,
/// plain typing — passes untouched.
/// </summary>
public sealed class WinVFilter
{
    private bool _winHeld;
    private bool _tookOver;

    /// <summary>True while the Win releases after a takeover still need swallowing.</summary>
    public bool TookOver => _tookOver;

    public KeyFlow Feed(SpecialKey key, KeyDirection direction)
    {
        switch (key)
        {
            case SpecialKey.LeftWin:
            case SpecialKey.RightWin:
                var winDown = direction == KeyDirection.Down;
                if (_winHeld && winDown)
                {
                    // Auto-repeat of the Win key itself: harmless to pass.
                    return KeyFlow.Pass;
                }

                _winHeld = winDown;
                if (!winDown && _tookOver)
                {
                    // The last act of a takeover: the Win release is eaten so
                    // the Start menu never learns the key was touched.
                    _tookOver = false;
                    return KeyFlow.Swallow;
                }

                return KeyFlow.Pass;

            case SpecialKey.V when direction == KeyDirection.Down && _winHeld:
                // First press takes over; repeats while Win is still held are
                // swallowed too — passing one through would hand the system a
                // Win+V and pop its clipboard panel over ours. They do not
                // re-trigger: keyboard auto-repeat would then toggle the bar
                // wildly for as long as the key is down.
                if (!_tookOver)
                {
                    _tookOver = true;
                    Triggered?.Invoke();
                }

                return KeyFlow.Swallow;

            case SpecialKey.V when direction == KeyDirection.Up && _tookOver:
                return KeyFlow.Swallow;

            default:
                return KeyFlow.Pass;
        }
    }

    /// <summary>Raised when Win+V is recognised, before the caller swallows it.</summary>
    public event Action? Triggered;
}
