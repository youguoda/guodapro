namespace Shiyu.Windows;

/// <summary>
/// Keeps exactly one Shiyu running per logged-in user.
///
/// Two instances is not a cosmetic problem: both would listen to the clipboard
/// and write the same history, each judging "is this the same as the previous
/// entry?" against a different view of it, and only whichever started first
/// would win the global hotkeys — leaving the other to report that some other
/// application had taken them, when that application is Shiyu itself.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    /// <summary>
    /// Posted by a second instance on its way out, so the one already running
    /// can tell the user it is there. Registered window messages are unique
    /// system-wide for a given string, so no other application can collide.
    /// </summary>
    private static readonly uint ActivateMessage =
        NativeMethods.RegisterWindowMessageW("ShiyuActivateExistingInstance");

    private readonly Mutex? _mutex;
    private MessageWindow? _window;
    private bool _disposed;

    /// <summary>Raised on the running instance when another one was started.</summary>
    public event Action? AnotherInstanceStarted;

    private SingleInstance(Mutex? mutex, bool isOnlyInstance)
    {
        _mutex = mutex;
        IsOnlyInstance = isOnlyInstance;
    }

    public bool IsOnlyInstance { get; }

    /// <summary>
    /// Claims the right to be the running instance.
    ///
    /// The name is scoped to the session rather than the machine, so two
    /// different users logged into the same computer each get their own Shiyu —
    /// they have separate histories and nothing to contend over.
    /// </summary>
    public static SingleInstance Acquire(string name)
    {
        // A named mutex settles a simultaneous start atomically — exactly one
        // caller is told it created the mutex — and the operating system
        // releases it if the owning process is killed, so a crash cannot leave
        // a stale lock that keeps Shiyu from ever starting again.
        var mutex = new Mutex(initiallyOwned: false, $"Local\\{name}", out var createdNew);

        if (createdNew)
        {
            return new SingleInstance(mutex, isOnlyInstance: true);
        }

        mutex.Dispose();
        return new SingleInstance(null, isOnlyInstance: false);
    }

    /// <summary>
    /// Tells the instance already running that the user tried to start another
    /// one. Broadcast rather than addressed, because the second instance has no
    /// way to know the first one's window handle.
    /// </summary>
    public void NotifyExistingInstance()
        => NativeMethods.PostMessageW(NativeMethods.HwndBroadcast, ActivateMessage, IntPtr.Zero, IntPtr.Zero);

    /// <summary>Starts listening for later instances announcing themselves.</summary>
    public void WatchForOtherInstances(MessageWindow window)
    {
        _window = window;
        _window.MessageReceived += OnMessage;
    }

    private void OnMessage(WindowMessage message)
    {
        if (message.Id == ActivateMessage)
        {
            message.Handle();
            AnotherInstanceStarted?.Invoke();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_window is not null)
        {
            _window.MessageReceived -= OnMessage;
            _window = null;
        }

        _mutex?.Dispose();
    }
}
