using System.Windows.Input;

namespace Shiyu.App;

/// <summary>
/// Every key the narrow bar answers to, in one place. The key handler and the
/// hold-Ctrl key badges both read these, so what the badges teach and what the
/// keys do cannot drift apart.
/// </summary>
internal static class BarKeys
{
    public const string Search = "F";

    public const string Copy = "C";

    public const string Open = "O";

    public const string Pin = "P";

    public const string Favorite = "S";

    public const string Note = "N";

    public const string Delete = "D";

    public const string TagCycle = "Tab";

    public const string TypePrevious = "←";

    public const string TypeNext = "→";

    /// <summary>Rows one to ten answer to 1-9 and 0.</summary>
    public static string? RowKey(int oneBased)
        => oneBased >= 1 && oneBased <= 10 ? (oneBased == 10 ? "0" : oneBased.ToString()) : null;

    /// <summary>The tray button's letter, when it has one; null keeps its glyph.</summary>
    public static string? TrayKey(string actionId) => actionId switch
    {
        "copy" => Copy,
        "open" => Open,
        "pin" => Pin,
        "favorite" => Favorite,
        "note" => Note,
        "delete" => Delete,
        _ => null,
    };

    /// <summary>The WPF key each letter constant names, for the handler's cases.</summary>
    public static Key KeyFor(string letter) => letter switch
    {
        Search => Key.F,
        Copy => Key.C,
        Open => Key.O,
        Pin => Key.P,
        Favorite => Key.S,
        Note => Key.N,
        Delete => Key.D,
        _ => Key.None,
    };
}
