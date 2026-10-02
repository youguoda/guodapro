namespace Shiyu.Core;

/// <summary>
/// Extracts an application's icon as PNG bytes. A port because everything
/// about "ask Windows for an icon" is operating-system work; a fake in tests
/// stands in for the found / not-found / broke cases.
/// </summary>
public interface ISourceIconProvider
{
    /// <summary>The icon's PNG bytes, or null when none could be found.</summary>
    byte[]? ExtractIconPng(string exePath);
}

/// <summary>
/// Keeps one cached icon row per source application.
///
/// A thousand entries from one application must share one icon, an application
/// with no findable icon must be asked exactly once, and nothing in here may
/// ever cost the entry that triggered it — icons are an enhancement, not a
/// dependency.
/// </summary>
public sealed class SourceIconCache
{
    private readonly EntryStore _store;
    private readonly ISourceIconProvider? _provider;

    public SourceIconCache(EntryStore store, ISourceIconProvider? provider)
    {
        _store = store;
        _provider = provider;
    }

    /// <summary>
    /// Ensures the application has a row in the icon store, extracting now if
    /// this is the first time it has been seen. Swallows everything: the copy
    /// that triggered this must become an entry regardless.
    /// </summary>
    public void Ensure(string? sourceApp, string? exePath)
    {
        try
        {
            if (_provider is null
                || string.IsNullOrEmpty(sourceApp)
                || string.IsNullOrEmpty(exePath)
                || _store.HasApplicationIcon(sourceApp))
            {
                return;
            }

            var icon = _provider.ExtractIconPng(exePath);

            // Null is stored as a row with no icon on purpose: a tombstone
            // saying "asked, nothing to find", so later copies do not each
            // retry a lookup that already failed. Insert-or-ignore so a rare
            // race between two first copies cannot overwrite a good icon
            // with a tombstone.
            _store.SaveApplicationIcon(sourceApp, icon);
        }
        catch (Exception)
        {
            // expected: 图标永远不值得条目——墓碑行照样会存下。
        }
    }
}
