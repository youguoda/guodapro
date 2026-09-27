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

    // --- the header's group menu (票 39) -----------------------------------------
    //
    // 头部管分组本身，卡片管卡片的归属：this menu is the head's half — switch
    // drawers, delete piles, make new ones. Card-level "move out" stays on the
    // card (tray 组 popup / context submenu); it operates on the card, not the
    // group, so it does not live here.
    //
    // The confirm and the create flow REPLACE the menu's content in place: a
    // second window would steal focus, and with StaysOpen=false a popup that
    // loses focus closes — the question would answer itself by vanishing.

    private Popup? _groupMenu;

    private void OnGroupMenuClicked(object sender, RoutedEventArgs e)
    {
        if (_groupMenu is { IsOpen: true })
        {
            _groupMenu.IsOpen = false;
            return;
        }

        var host = new StackPanel { MinWidth = 196 };
        var surface = MenuSurface(host);

        var popup = new Popup
        {
            Child = WithPopupFont(surface),
            PlacementTarget = GroupMenuButton,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
        };
        popup.Opened += (_, _) => FlipIntoWorkArea(popup);
        _groupMenu = popup;

        FillGroupMenu(host, popup);
        popup.IsOpen = true;
    }

    private void FillGroupMenu(StackPanel host, Popup popup)
    {
        host.Children.Clear();

        // The chips-row mouth: only unhidden groups. Hidden piles live in the
        // manager alone.
        var groups = _store.Groups().Where(group => !group.Hidden).ToList();

        if (groups.Count == 0)
        {
            var none = new TextBlock
            {
                Text = "还没有分组。新建一个，或复制后在卡片上归组。",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(2, 4, 2, 6),
            };
            none.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
            host.Children.Add(none);
        }

        foreach (var group in groups)
        {
            host.Children.Add(GroupMenuRow(group, host, popup));
        }

        var mind = new TextBlock
        {
            Text = "头部管分组本身，卡片管卡片的归属。",
            Margin = new Thickness(2, 6, 2, 6),
        };
        mind.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextTertiary");
        host.Children.Add(mind);

        host.Children.Add(GroupMenuAction("管理分组…", () => { popup.IsOpen = false; OnManageGroups(this, new RoutedEventArgs()); }));
        host.Children.Add(GroupMenuAction("新建分组", () => FillCreateGroup(host, popup)));
    }

    /// <summary>
    /// One group row: the whole strip switches the drawer; the ✕ beside it
    /// (revealed on hover) offers the delete. Two buttons side by side, not
    /// nested — a click meant for one must never fire the other.
    /// </summary>
    private UIElement GroupMenuRow(EntryGroup group, StackPanel host, Popup popup)
    {
        var pick = new Button
        {
            Content = new TextBlock { Text = $"{group.Icon ?? "组"} {group.Name}" },
            Padding = new Thickness(10, 5, 10, 5),
            Cursor = Cursors.Hand,
        };
        pick.SetResourceReference(BackgroundProperty, "Brush.Surface");
        pick.Click += (_, _) =>
        {
            popup.IsOpen = false;
            SelectGroup(group.Id);
        };

        var remove = new Button
        {
            Content = "✕",
            Width = 22,
            Height = 26,
            Margin = new Thickness(2, 0, 0, 0),
            Padding = new Thickness(0),
            Cursor = Cursors.Hand,
            Visibility = Visibility.Collapsed,
            ToolTip = "删除分组（条目不删，回到未分组）",
        };
        remove.SetResourceReference(ForegroundProperty, "Brush.TextSecondary");
        remove.SetResourceReference(BackgroundProperty, "Brush.Surface");
        remove.Click += (_, _) => FillDeleteGroupConfirm(host, popup, group);

        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
        DockPanel.SetDock(remove, Dock.Right);
        row.Children.Add(remove);
        row.Children.Add(pick);

        // The ✕ appears with the pointer, exactly for as long as it stays.
        row.MouseEnter += (_, _) => remove.Visibility = Visibility.Visible;
        row.MouseLeave += (_, _) => remove.Visibility = Visibility.Collapsed;

        return row;
    }

    /// <summary>
    /// The delete question, in place. ANY non-empty group confirms (票 39 评审
    /// 定案) — the pile's size is read from the store, not guessed from which
    /// drawer happens to be open.
    /// </summary>
    private void FillDeleteGroupConfirm(StackPanel host, Popup popup, EntryGroup group)
    {
        var members = _store.GroupEntryCount(group.Id);

        host.Children.Clear();

        var ask = new TextBlock
        {
            Text = members > 0
                ? $"删除分组「{group.Name}」？其中 {members} 条条目不会删除，将回到未分组。"
                : $"删除空分组「{group.Name}」？",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2, 4, 2, 8),
        };
        host.Children.Add(ask);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };

        var cancel = GroupMenuAction("取消", () => FillGroupMenu(host, popup));
        cancel.Margin = new Thickness(0);
        var confirm = GroupMenuAction("确认删除", () =>
        {
            _store.DeleteGroup(group.Id);
            RefreshGroups();
            ApplyFilter();
            popup.IsOpen = false;
        });
        confirm.Margin = new Thickness(6, 0, 0, 0);
        confirm.SetResourceReference(ForegroundProperty, "Brush.Danger");

        buttons.Children.Add(cancel);
        buttons.Children.Add(confirm);
        host.Children.Add(buttons);
    }

    /// <summary>The create flow, in place: a name and a button.</summary>
    private void FillCreateGroup(StackPanel host, Popup popup)
    {
        host.Children.Clear();

        var label = new TextBlock
        {
            Text = "新建分组",
            Margin = new Thickness(2, 4, 2, 6),
        };
        host.Children.Add(label);

        var name = new TextBox { Margin = new Thickness(0, 0, 0, 6), Padding = new Thickness(6, 4, 6, 4) };
        name.SetResourceReference(BackgroundProperty, "Brush.SurfaceInput");
        host.Children.Add(name);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };

        var cancel = GroupMenuAction("取消", () => FillGroupMenu(host, popup));
        cancel.Margin = new Thickness(0);
        var create = GroupMenuAction("新建", () =>
        {
            var text = name.Text.Trim();
            if (text.Length == 0)
            {
                return;
            }

            var icon = text.FirstOrDefault(char.IsLetterOrDigit).ToString();
            _store.CreateGroup(text, icon);
            RefreshGroups();
            popup.IsOpen = false;
        });
        create.Margin = new Thickness(6, 0, 0, 0);

        buttons.Children.Add(cancel);
        buttons.Children.Add(create);
        host.Children.Add(buttons);

        name.Focus();
    }

    private static Button GroupMenuAction(string label, Action run)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(10, 5, 10, 5),
            Margin = new Thickness(0, 0, 0, 1),
            Cursor = Cursors.Hand,
        };
        button.SetResourceReference(BackgroundProperty, "Brush.Surface");
        button.Click += (_, _) => run();
        return button;
    }

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
