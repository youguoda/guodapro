using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Shiyu.Core;

namespace Shiyu.App;

internal partial class BarWindow
{
    // --- groups -----------------------------------------------------------------

    /// <summary>The drawer currently open, or null when the bar shows every pile at once.</summary>
    private long? _selectedGroup;

    private readonly Dictionary<long, EntryGroup> _groupsById = [];

    private BarCard? _chooserCard;

    private Popup? _groupChooser;

    /// <summary>Carries a settings deep-link id up to whoever owns settings.</summary>
    public event Action<string>? DataSettingsRequested;

    /// <summary>Switches the drawer. The chips show it; the filter narrows to it.</summary>
    private void SelectGroup(long? id, bool apply = true)
    {
        _selectedGroup = id;
        SyncGroupChipStates();

        if (apply)
        {
            ApplyFilter();
        }
    }

    /// <summary>The 归组 tray action: file this card into a pile, or back out of one.</summary>
    private void OpenGroupChooser(BarCard card, Button? anchor)
    {
        _chooserCard = card;

        var host = new StackPanel { MinWidth = 150 };

        foreach (var group in _store.Groups())
        {
            var item = new Button
            {
                Content = $"{group.Icon ?? "组"} {group.Name}",
                Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(0, 0, 0, 2),
                Cursor = Cursors.Hand,
                Tag = group.Id,
            };
            item.SetResourceReference(BackgroundProperty, "Brush.Surface");
            item.Click += (_, _) => FileCardInto(card, group.Id);
            host.Children.Add(item);
        }

        var ungrouped = new Button
        {
            Content = "未分组",
            Padding = new Thickness(10, 5, 10, 5),
            Margin = new Thickness(0, 0, 0, 4),
            Cursor = Cursors.Hand,
        };
        ungrouped.SetResourceReference(BackgroundProperty, "Brush.Surface");
        ungrouped.Click += (_, _) => FileCardInto(card, null);
        host.Children.Add(ungrouped);

        var name = new TextBox { Margin = new Thickness(0, 0, 0, 2), Padding = new Thickness(6, 4, 6, 4) };
        name.SetResourceReference(BackgroundProperty, "Brush.SurfaceInput");
        host.Children.Add(name);

        var create = new Button
        {
            Content = "新建分组并归入",
            Padding = new Thickness(10, 5, 10, 5),
            Margin = new Thickness(0, 0, 0, 2),
            Cursor = Cursors.Hand,
        };
        create.SetResourceReference(BackgroundProperty, "Brush.Surface");
        create.Click += (_, _) =>
        {
            var label = name.Text.Trim();
            if (label.Length == 0)
            {
                return;
            }

            var icon = label.FirstOrDefault(char.IsLetterOrDigit).ToString();
            var id = _store.CreateGroup(label, icon);
            RefreshGroups();
            FileCardInto(card, id);
        };
        host.Children.Add(create);

        var manage = new Button { Content = "管理分组…", Padding = new Thickness(10, 5, 10, 5), Cursor = Cursors.Hand };
        manage.SetResourceReference(BackgroundProperty, "Brush.Surface");
        manage.Click += OnManageGroups;
        host.Children.Add(manage);

        _groupChooser = new Popup
        {
            Child = WithPopupFont(host),
            PlacementTarget = anchor,
            Placement = anchor is null ? PlacementMode.MousePoint : PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
        };
        _groupChooser.IsOpen = true;
    }

    private void FileCardInto(BarCard card, long? groupId)
    {
        _store.SetEntryGroup(card.Id, groupId);
        card.GroupBadge = groupId is { } id && _groupsById.TryGetValue(id, out var group)
            ? group.Name
            : null;

        if (_groupChooser is not null)
        {
            _groupChooser.IsOpen = false;
        }

        // A drawer the card just left should not keep showing it; one it
        // joined does not gain it until asked, which the switcher already is.
        if (_selectedGroup is { } selected && selected != groupId)
        {
            ApplyFilter();
        }
    }

    private void OnManageGroups(object sender, RoutedEventArgs e)
    {
        if (_overflowPopup is not null)
        {
            _overflowPopup.IsOpen = false;
        }

        if (_groupChooser is not null)
        {
            _groupChooser.IsOpen = false;
        }

        var manager = new GroupManagerWindow(_store) { Owner = this };
        manager.DataSettingsRequested += () => DataSettingsRequested?.Invoke("store.protect");
        manager.ShowDialog();
        RefreshGroups();
        ApplyFilter();
    }
}
