using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Shiyu.Core;

namespace Shiyu.App;

internal partial class BarWindow
{
    // --- filters ----------------------------------------------------------------

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        _searchDebounce.Stop();
        _searchDebounce.Start();

        // 有查询时搜索描边转 1 DIP Accent（§6.1 搜索框）；聚焦态的 Accent
        // 由 SearchBox 模板自己的焦点触发器负责。
        SearchBox.SetResourceReference(BorderBrushProperty,
            SearchBox.Text.Length > 0 ? "Brush.Accent" : "Brush.StrokeStrong");
    }

    private void OnFilterChanged(object sender, RoutedEventArgs e)
    {
        if (IsLoaded)
        {
            ApplyFilter();
        }
    }

    private void OnClearFilters(object sender, RoutedEventArgs e)
    {
        SearchBox.Clear();
        KindFilterChipClear();
        SetSubtype(0);
        FavoriteOnly.IsChecked = false;
        SetTag(AnyTag);
        SelectGroup(null, apply: false);
        RefreshTagChoices();
        ApplyFilter();
    }

    private ToggleButton[] _kindChips = [];

    /// <summary>Each chip's ←→ cap; shown on the checked chip only, while Ctrl is held.</summary>
    private ContentControl[] _kindCaps = [];

    private int _kindIndex;

    private void SetKindIndex(int index)
    {
        _kindIndex = Math.Clamp(index, 0, _kindChips.Length - 1);
        for (var i = 0; i < _kindChips.Length; i++)
        {
            _kindChips[i].IsChecked = i == _kindIndex;
        }

        UpdateKindCaps();
    }

    private void KindFilterChipClear() => SetKindIndex(0);

    /// <summary>Which entry of the subtype popup is active; 0 is "all".</summary>
    private int _subtypeIndex;

    private Popup? _subtypePopup;

    private void SetSubtype(int index)
    {
        _subtypeIndex = Math.Clamp(index, 0, 4);

        // The button speaks for a facet hidden inside it: accented while a
        // subtype is active, quiet otherwise（状态矩阵：漏斗 accent）。
        SubtypeButton.SetResourceReference(ForegroundProperty,
            _subtypeIndex > 0 ? "Brush.Accent" : "Brush.TextSecondary");
    }

    private void OnSubtypeButtonClicked(object sender, RoutedEventArgs e)
    {
        if (_subtypePopup is { IsOpen: true })
        {
            _subtypePopup.IsOpen = false;
            return;
        }

        var labels = new[] { "子类型（全部）", "链接", "邮箱", "颜色", "路径" };
        var host = new StackPanel { MinWidth = 132 };

        for (var index = 0; index < labels.Length; index++)
        {
            var captured = index;
            var item = new Button
            {
                Content = (_subtypeIndex == captured ? "✓  " : "     ") + labels[captured],
                Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(0, 0, 0, 1),
                Cursor = Cursors.Hand,
            };
            item.SetResourceReference(BackgroundProperty, "Brush.Surface");
            item.Click += (_, _) =>
            {
                _subtypePopup!.IsOpen = false;
                SetSubtype(captured);
                ApplyFilter();
            };
            host.Children.Add(item);
        }

        _subtypePopup = new Popup
        {
            Child = WithPopupFont(MenuSurface(host)),
            PlacementTarget = SubtypeButton,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
        };
        _subtypePopup.IsOpen = true;
    }

    /// <summary>Chip clicks land here; the tag carries the kind index.</summary>
    private void OnKindChipClicked(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: string tag } && int.TryParse(tag, out var index))
        {
            SetKindIndex(index);
            ApplyFilter();
        }
    }

    private void ApplyFilter()
    {
        _browser.Filter = new HistoryFilter
        {
            Query = SearchBox.Text,
            Tag = _selectedTag != AnyTag ? _selectedTag : null,
            Kind = _kindIndex switch
            {
                1 => EntryKind.Text,
                2 => EntryKind.Image,
                3 => EntryKind.Files,
                _ => null,
            },
            Subtype = _subtypeIndex switch
            {
                1 => EntrySubtype.Link,
                2 => EntrySubtype.Email,
                3 => EntrySubtype.Color,
                4 => EntrySubtype.LocalPath,
                _ => null,
            },
            Favorite = FavoriteOnly.IsChecked == true ? true : null,
            Group = _selectedGroup,
        };

        UpdateFilterChrome();
        Rebuild();
    }

    /// <summary>
    /// ✕ 只在有筛选时出现（§6.1）：常驻的"清除筛选"在没有东西可清时只是一
    /// 个会响的按钮。读全部筛选层，包括尚未落库的搜索框现值。
    /// </summary>
    private void UpdateFilterChrome()
    {
        var any = SearchBox.Text.Length > 0
            || _subtypeIndex != 0
            || _selectedTag != AnyTag
            || _kindIndex != 0
            || FavoriteOnly.IsChecked == true
            || _selectedGroup is not null;

        ClearFilters.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
    }

    // --- the tag filter（U-04：ghost 下拉替换 Aero2 ComboBox）---------------------
    //
    // 键盘（Tab 循环）与弹出列表读同一份 _tagChoices/_selectedTag；第 2 行
    // 里从此没有 ComboBox。

    private List<string> _tagChoices = [AnyTag];

    private string _selectedTag = AnyTag;

    private Popup? _tagPopup;

    private void SetTag(string tag)
    {
        _selectedTag = _tagChoices.Contains(tag) ? tag : AnyTag;
        UpdateTagFace();
    }

    /// <summary>The ghost button's face: quiet and narrow when off, accented with the name when filtering.</summary>
    private void UpdateTagFace()
    {
        var filtered = _selectedTag != AnyTag;
        TagName.Text = filtered ? _selectedTag : string.Empty;
        TagFilter.SetResourceReference(ForegroundProperty,
            filtered ? "Brush.Accent" : "Brush.TextSecondary");
    }

    private void OnTagFilterClicked(object sender, RoutedEventArgs e)
    {
        if (_tagPopup is { IsOpen: true })
        {
            _tagPopup.IsOpen = false;
            return;
        }

        var host = new StackPanel { MinWidth = 120 };

        foreach (var choice in _tagChoices)
        {
            var captured = choice;
            var item = new Button
            {
                Content = (_selectedTag == captured ? "✓  " : "     ") + captured,
                Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(0, 0, 0, 1),
                Cursor = Cursors.Hand,
            };
            item.SetResourceReference(BackgroundProperty, "Brush.Surface");
            item.Click += (_, _) =>
            {
                _tagPopup!.IsOpen = false;
                SetTag(captured);
                ApplyFilter();
            };
            host.Children.Add(item);
        }

        _tagPopup = new Popup
        {
            Child = WithPopupFont(MenuSurface(host)),
            PlacementTarget = TagFilter,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
        };
        _tagPopup.Opened += (_, _) => FlipIntoWorkArea(_tagPopup);
        _tagPopup.IsOpen = true;
    }

    private void RefreshTagChoices()
    {
        var chosen = _selectedTag;
        _tagChoices = [AnyTag, .. _store.AllTags()];
        _selectedTag = _tagChoices.Contains(chosen) ? chosen : AnyTag;
        UpdateTagFace();
    }

    /// <summary>Chips for each visible group, in row order; the overflow takes the rest.</summary>
    private readonly List<(EntryGroup Group, ToggleButton Chip)> _groupChips = [];

    private bool _fittingGroups;

    /// <summary>Rebuilds the switcher row after groups appear, change, or vanish.</summary>
    private void RefreshGroups()
    {
        var groups = _store.Groups();
        _groupsById.Clear();
        foreach (var group in groups)
        {
            _groupsById[group.Id] = group;
        }

        // A deleted group leaves its id selected on the floor; back to 全部.
        if (_selectedGroup is { } current && !_groupsById.ContainsKey(current))
        {
            _selectedGroup = null;
        }

        foreach (var (_, chip) in _groupChips)
        {
            GroupRow.Children.Remove(chip);
        }
        _groupChips.Clear();

        foreach (var group in groups.Where(group => !group.Hidden))
        {
            var chip = new ToggleButton
            {
                Content = $"{group.Icon ?? "组"} {group.Name}",
                Cursor = Cursors.Hand,

                // The chips are the drawer's visible state and nothing more
                // (票 39): switching here, managing in the header's ⋯ menu.
                ToolTip = $"只看「{group.Name}」——再点一次回到全部（增删分组在头部 ⋯ 菜单）",

                // Tabs, not boxes: groups share the kind tabs' underline
                // idiom so the two rows read as one family.
                Style = TryFindResource("KindTab") as Style ?? new Style(typeof(ToggleButton)),
            };
            chip.Click += OnGroupChipClicked;
            GroupRow.Children.Insert(GroupRow.Children.Count - 1, chip);
            _groupChips.Add((group, chip));
        }

        GroupHostBorder.Visibility = groups.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SyncGroupChipStates();
        FitGroupRow();
    }

    private void OnGroupChipClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton chip)
        {
            return;
        }

        // Clicking the chip that is already active un-drawers: back to 全部.
        var picked = _groupChips.FirstOrDefault(pair => pair.Chip == chip).Group?.Id;
        SelectGroup(chip.IsChecked == true ? picked : null);
    }

    private void OnGroupHostResized(object sender, SizeChangedEventArgs e) => FitGroupRow();

    /// <summary>
    /// Shows as many chips as the row has room for; the rest fold into the
    /// overflow button, which itself lights up when the active drawer is one
    /// of them — a selection the user cannot see is a selection they cannot
    /// trust.
    /// </summary>
    private void FitGroupRow()
    {
        if (_fittingGroups || GroupHostBorder.Visibility != Visibility.Visible)
        {
            return;
        }

        _fittingGroups = true;
        try
        {
            var available = GroupHost.ActualWidth;
            if (available <= 0)
            {
                return;
            }

            AllGroupChip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            available -= AllGroupChip.DesiredSize.Width + 6;

            GroupOverflow.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var overflowWidth = GroupOverflow.DesiredSize.Width + 6;

            var used = 0.0;
            var fitted = 0;
            foreach (var (_, chip) in _groupChips)
            {
                chip.Visibility = Visibility.Visible;
                chip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var width = chip.DesiredSize.Width + 6;

                // Room for the overflow button is reserved up front unless
                // this is the last chip: better to show one chip fewer than
                // to squeeze the ⋯ button out of the row.
                var reserve = fitted + 1 < _groupChips.Count ? overflowWidth : 0;

                if (used + width + reserve > available)
                {
                    break;
                }

                used += width;
                fitted++;
            }

            var overflowed = _groupChips.Skip(fitted).ToList();
            foreach (var (_, chip) in overflowed)
            {
                chip.Visibility = Visibility.Collapsed;
            }

            GroupOverflow.Visibility = overflowed.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            BuildOverflowChoices(overflowed.Select(pair => pair.Group).ToList());
            SyncGroupChipStates();
        }
        finally
        {
            _fittingGroups = false;
        }
    }

    private Popup? _overflowPopup;

    private void OnGroupOverflowClicked(object sender, RoutedEventArgs e)
    {
        if (_overflowPopup is null)
        {
            return;
        }

        _overflowPopup.IsOpen = !_overflowPopup.IsOpen;
    }

    private void BuildOverflowChoices(IReadOnlyList<EntryGroup> overflowed)
    {
        if (_overflowPopup is not null)
        {
            _overflowPopup.IsOpen = false;
        }

        var host = new StackPanel { MinWidth = 120 };

        foreach (var group in overflowed)
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
            item.Click += (_, _) =>
            {
                _overflowPopup!.IsOpen = false;
                SelectGroup(group.Id);
            };
            host.Children.Add(item);
        }

        // The chips row shows and switches, it no longer manages (票 39):
        // 管理分组… moved to the header's ⋯ menu, so the row has one job.

        _overflowPopup = new Popup
        {
            Child = WithPopupFont(host),
            PlacementTarget = GroupOverflow,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
        };

        // Rebinding after rebuild keeps the anchor when the row re-fits while open.
        _overflowPopup.IsOpen = false;
    }

    private void SyncGroupChipStates()
    {
        AllGroupChip.IsChecked = _selectedGroup is null;

        var selectedIsOverflowed = false;
        foreach (var (group, chip) in _groupChips)
        {
            var active = group.Id == _selectedGroup;
            chip.IsChecked = active;
            if (active && chip.Visibility != Visibility.Visible)
            {
                selectedIsOverflowed = true;
            }
        }

        // The overflow button speaks for the drawer hidden inside it: with
        // the tab idiom, "checked" IS the accent underline.
        GroupOverflow.IsChecked = selectedIsOverflowed;
    }
}
