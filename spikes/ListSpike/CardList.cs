using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ListSpike;

/// <summary>
/// Small visual-tree helpers so the spike can ask the tree itself what is
/// realized, rather than inferring it from counters that could drift.
/// </summary>
internal static class Tree
{
    internal static T? FindDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);

        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);

            if (child is T match)
            {
                return match;
            }

            if (FindDescendant<T>(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }

    internal static IEnumerable<T> Descendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);

        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);

            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in Descendants<T>(child))
            {
                yield return nested;
            }
        }
    }

    internal static T? FindAncestor<T>(DependencyObject node)
        where T : DependencyObject
    {
        for (var parent = VisualTreeHelper.GetParent(node);
             parent is not null;
             parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is T match)
            {
                return match;
            }
        }

        return null;
    }
}

/// <summary>
/// The row container. Exists to reset the hover tray at exactly the moment
/// recycling hands this container to another row.
/// </summary>
internal sealed class CardContainer : ListBoxItem
{
    /// <summary>Total containers ever built. With recycling this grows with viewport churn, not with item count.</summary>
    internal static int Created;

    public CardContainer()
    {
        Created++;

        // The recycling path in a nutshell: the same container instance gets a
        // new row's DataContext. This event is where that becomes visible.
        DataContextChanged += (_, _) => ResetTray();
    }

    public void ResetTray()
    {
        // Null on the very first realization (template not applied yet) —
        // nothing to reset in that case, the tray starts closed anyway.
        Tree.FindDescendant<TrayStrip>(this)?.Reset();
    }
}

/// <summary>
/// The virtualized card list. Overriding the container hooks is the canonical
/// place to learn that a container is being (re)used for a row.
/// </summary>
internal sealed class CardList : ListBox
{
    protected override DependencyObject GetContainerForItemOverride()
        => new CardContainer();

    protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
    {
        base.PrepareContainerForItemOverride(element, item);

        if (element is CardContainer card)
        {
            card.ResetTray();
        }
    }

    protected override void ClearContainerForItemOverride(DependencyObject element, object item)
    {
        if (element is CardContainer card)
        {
            card.ResetTray();
        }

        base.ClearContainerForItemOverride(element, item);
    }
}
