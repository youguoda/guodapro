using System.Windows.Input;
using Shiyu.Core;

namespace Shiyu.App;

/// <summary>
/// The narrow bar's keyboard model. The letters now live in <see cref="KeyMap"/>
/// （键位即数据，§5.2）：this class answers two questions only — which row a
/// numbered key lands on, and the tray letter a hover action carries. What the
/// badges teach and what the keys do cannot drift apart, because both read the
/// same table the settings cheatsheet and the tray menu render from.
/// </summary>
internal static class BarKeys
{
    /// <summary>Rows one to ten answer to 1-9 and 0.</summary>
    public static string? RowKey(int oneBased)
        => oneBased >= 1 && oneBased <= 10 ? (oneBased == 10 ? "0" : oneBased.ToString()) : null;

    /// <summary>The tray button's letter, when it has one; null keeps its glyph.</summary>
    public static string? TrayKey(string actionId) => KeyMap.TrayKey(actionId);
}
