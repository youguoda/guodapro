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
/// Win+V (either Win) is swallowed and triggers; the V releases that follow
/// a takeover are swallowed too; every other key — Win alone, Win+anything-
/// else, plain typing — passes untouched.
///
/// The Win RELEASE is deliberately passed, not swallowed. Swallowing it
/// leaves the system believing Win is held forever (a stuck modifier: the
/// next E opens Explorer, L locks the screen). The Start menu that a bare
/// Win release would pop is prevented another way: the caller injects a
/// harmless mask keystroke at takeover time, so the shell saw "some key
/// was pressed while Win was down" and opens nothing on the release.
/// </summary>
public sealed class WinVFilter
{
    private bool _winHeld;
    private bool _tookOver;

    /// <summary>True while the V releases after a takeover still need swallowing.</summary>
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
                // Eaten because the down was eaten: letting the up through
                // alone would type 'v' into whatever has focus. This is also
                // the takeover's natural end — later V presses (without Win)
                // are ordinary typing and must pass whole.
                _tookOver = false;
                return KeyFlow.Swallow;

            default:
                return KeyFlow.Pass;
        }
    }

    /// <summary>Raised when Win+V is recognised, before the caller swallows it.</summary>
    public event Action? Triggered;
}
