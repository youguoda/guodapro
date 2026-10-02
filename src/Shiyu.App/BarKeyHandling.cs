using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Shiyu.Core;

namespace Shiyu.App;

internal partial class BarWindow
{
    // --- keyboard model --------------------------------------------------------

    /// <summary>Rows as displayed: pinned first, then the rest. Number keys and navigation count these.</summary>
    private IEnumerable<BarCard> VisibleRows => _pinned.Concat(_cards);

    /// <summary>Focus sits in a text field: letters belong to it, arrows move its caret.</summary>
    private static bool IsTyping
        => Keyboard.FocusedElement is TextBox;

    /// <summary>
    /// There is always an active row while any row exists, so Enter and the
    /// letter actions always have something definite to act on.
    /// </summary>
    private void EnsureActiveItem()
    {
        if (_selected is { } card && (_cards.Contains(card) || _pinned.Contains(card)))
        {
            return;
        }

        Select(VisibleRows.FirstOrDefault());
    }

    private void Move(int delta)
    {
        var rows = VisibleRows.ToList();
        if (rows.Count == 0)
        {
            return;
        }

        var current = _selected is { } card ? rows.IndexOf(card) : -1;
        var next = Math.Clamp(current + delta, 0, rows.Count - 1);

        Select(rows[next]);
        Cards.ScrollIntoView(rows[next]);

        // Noted, not followed: the panel waits for the selection to settle so
        // it glides to a still target instead of chasing a scrolling one.
        RunPreviewCommand(_previewPolicy.SelectionMoved(rows[next].Id));
    }

    // --- key hints (ticket 14) -------------------------------------------------

    private bool _keyHintsOn;

    /// <summary>
    /// Hold Ctrl and every actionable icon swaps in place for the key that
    /// drives it — the whole keyboard model taught at the place it applies,
    /// for exactly as long as the user asks.
    /// </summary>
    private void SetKeyHints(bool on)
    {
        if (_keyHintsOn == on)
        {
            return;
        }

        _keyHintsOn = on;
        SearchKeyBadge.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        KindKeyBadge.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        TagKeyBadge.Visibility = on ? Visibility.Visible : Visibility.Collapsed;

        foreach (var container in RealizedContainers())
        {
            ApplyKeyHintsTo(
                Tree.FindDescendant<RowKeyBadge>(container),
                Tree.FindDescendant<ActionTray>(container));

            // Links and emails dress as clickable for exactly as long as the
            // modifier that opens them is held. On release the resource
            // reference is restored rather than a local colour set, so theme
            // changes keep reaching these texts.
            if (Tree.FindDescendant<EntryBodyText>(container) is { } body)
            {
                var clickable = on
                    && ((FrameworkElement)container).DataContext is BarCard { IsOpenable: true };

                if (clickable)
                {
                    body.Foreground = (Brush)FindResource("Brush.Accent");
                    body.TextDecorations = System.Windows.TextDecorations.Underline;
                }
                else
                {
                    body.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
                    body.TextDecorations = null;
                }
            }
        }
    }

    /// <summary>
    /// One row's hint state — also called for rows realized while Ctrl is
    /// already down, so recycled rows arrive pre-badged rather than blank.
    /// </summary>
    internal void ApplyKeyHintsTo(RowKeyBadge? badge, ActionTray? tray)
    {
        if (badge is not null)
        {
            badge.Visibility = _keyHintsOn ? Visibility.Visible : Visibility.Collapsed;
        }

        tray?.ShowHints(_keyHintsOn);
    }

    private IEnumerable<BarCardContainer> RealizedContainers()
    {
        foreach (var item in _pinned.Concat(_cards))
        {
            if (PinnedList.ItemContainerGenerator.ContainerFromItem(item) is BarCardContainer pinned)
            {
                yield return pinned;
            }

            if (Cards.ItemContainerGenerator.ContainerFromItem(item) is BarCardContainer rest)
            {
                yield return rest;
            }
        }
    }

    private void OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        // Releasing Space takes down only what Space opened — a hover preview
        // under the pointer keeps its own rules. The guard is loose on
        // purpose: focus may have wandered between press and release.
        if (e.Key is Key.Space)
        {
            RunPreviewCommand(_previewPolicy.SpaceUp());
        }

        if (e.Key is Key.LeftCtrl or Key.RightCtrl && _keyHintsOn
            && (Keyboard.Modifiers & ModifierKeys.Control) == 0)
        {
            SetKeyHints(false);
        }
    }

    /// <summary>
    /// Losing focus while Ctrl is still down — Ctrl+Tab, a notification
    /// stealing the click — means the release event never arrives. The
    /// badges come in now, or they stay forever.
    /// </summary>
    private void OnLostFocus(object sender, EventArgs e) => SetKeyHints(false);

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.LeftCtrl or Key.RightCtrl)
        {
            SetKeyHints(true);
            return;
        }
        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;

                // The menu peels off first: closing it must not cost the user
                // their place — the bar stays, the selection stays.
                if (_cardMenu is { IsOpen: true } || _cardSubMenu is { IsOpen: true })
                {
                    CloseCardMenu();
                    return;
                }

                // The tray's ⋯ popup is a layer the same way.
                if (_trayMorePopup is { IsOpen: true })
                {
                    _trayMorePopup.IsOpen = false;
                    return;
                }

                // The header's group menu is a layer the same way (票 39): it
                // goes before any filter the stack would clear underneath it.
                if (_groupMenu is { IsOpen: true })
                {
                    _groupMenu.IsOpen = false;
                    return;
                }

                StepEscape();
                break;

            case Key.F when Keyboard.Modifiers == ModifierKeys.Control:
                e.Handled = true;
                SearchBox.Focus();
                SearchBox.SelectAll();
                break;

            // Tab trades focus navigation for filter cycling: Ctrl+F is the
            // way back to the search box, so nothing is lost.
            case Key.Tab:
                e.Handled = true;
                CycleTag(+1);
                break;

            case Key.Left when !IsTyping:
                e.Handled = true;
                CycleKind(-1);
                break;

            case Key.Right when !IsTyping:
                e.Handled = true;
                CycleKind(+1);
                break;

            case Key.Up when Keyboard.FocusedElement is not ComboBox:
                e.Handled = true;
                Move(-1);
                break;

            case Key.Down when Keyboard.FocusedElement is not ComboBox:
                e.Handled = true;
                Move(+1);
                break;

            // Held Space previews the active card in full (ticket 17). The
            // search box is focused right after summoning, so the rule has to
            // tell an empty box from a query being typed: with nothing typed,
            // Space is free to mean "show me"; mid-query it stays a space.
            case Key.Space when Keyboard.Modifiers == ModifierKeys.None
                && (SearchBox.Text.Length == 0 || !IsTyping):
                e.Handled = true;
                if (_selected is { } card)
                {
                    RunPreviewCommand(_previewPolicy.SpaceDown(card.Id));
                }
                break;

            case Key.Enter:
                e.Handled = true;
                if (_selected is { } enter)
                {
                    // Modifier+Enter pastes as plain text; for stored text the
                    // two coincide until formatted entries exist (ticket 07).
                    PasteEntry(enter);
                }
                break;

            case Key.D1 or Key.NumPad1: NumberedRow(1, e); break;
            case Key.D2 or Key.NumPad2: NumberedRow(2, e); break;
            case Key.D3 or Key.NumPad3: NumberedRow(3, e); break;
            case Key.D4 or Key.NumPad4: NumberedRow(4, e); break;
            case Key.D5 or Key.NumPad5: NumberedRow(5, e); break;
            case Key.D6 or Key.NumPad6: NumberedRow(6, e); break;
            case Key.D7 or Key.NumPad7: NumberedRow(7, e); break;
            case Key.D8 or Key.NumPad8: NumberedRow(8, e); break;
            case Key.D9 or Key.NumPad9: NumberedRow(9, e); break;
            case Key.D0 or Key.NumPad0: NumberedRow(10, e); break;

            // Letter actions only outside text fields — inside one, letters
            // are the search the user is typing.
            case Key.C when !IsTyping && Keyboard.Modifiers == ModifierKeys.None:
                e.Handled = true;
                if (_selected is { } copy) ExecuteAction("copy", copy, feedback: null);
                break;

            case Key.O when !IsTyping && Keyboard.Modifiers == ModifierKeys.None:
                e.Handled = true;
                if (_selected is { } open) ExecuteAction("open", open, feedback: null);
                break;

            case Key.P when !IsTyping && Keyboard.Modifiers == ModifierKeys.None:
                e.Handled = true;
                if (_selected is { } pin) ExecuteAction("pin", pin, feedback: null);
                break;

            case Key.S when !IsTyping && Keyboard.Modifiers == ModifierKeys.None:
                e.Handled = true;
                if (_selected is { } favourite) ExecuteAction("favorite", favourite, feedback: null);
                break;

            case Key.N when !IsTyping && Keyboard.Modifiers == ModifierKeys.None:
                e.Handled = true;
                if (_selected is { } note) ExecuteAction("note", note, feedback: null);
                break;

            case Key.G when !IsTyping && Keyboard.Modifiers == ModifierKeys.None:
                e.Handled = true;
                if (_selected is { } group) ExecuteAction("group", group, feedback: null);
                break;

            case Key.D when !IsTyping && Keyboard.Modifiers == ModifierKeys.None:
                e.Handled = true;
                if (_selected is { } del) ExecuteAction("delete", del, feedback: null);
                break;

            // The undo window is narrow on purpose: it answers only while the
            // footer is still showing the deletion it would reverse.
            case Key.Z when !IsTyping && Keyboard.Modifiers == ModifierKeys.None && _undoPending is not null:
                e.Handled = true;
                RestoreUndo();
                break;
        }
    }

    private void NumberedRow(int oneBased, KeyEventArgs e)
    {
        var rows = VisibleRows.ToList();

        if (oneBased <= rows.Count)
        {
            e.Handled = true;
            PasteEntry(rows[oneBased - 1]);
        }
    }

    /// <summary>Escape peels the most recent layer; only an empty stack hides the window.</summary>
    private void StepEscape()
    {
        var hasQuery = SearchBox.Text.Length > 0;
        var hasSubtype = _subtypeIndex != 0;
        var hasTag = TagFilter.SelectedItem as string is { } tag && tag != AnyTag;
        var hasKind = _kindIndex != 0;
        var hasFavorite = FavoriteOnly.IsChecked == true;
        var hasGroup = _selectedGroup is not null;

        switch (BarKeyboard.NextEscape(
            previewOpen: _preview is { IsVisible: true },
            hasQuery,
            hasSubtype,
            hasTag,
            hasKind,
            hasFavorite,
            hasGroup))
        {
            case BarKeyboard.EscapeAction.ClosePreview:
                // The panel's level in the stack, ready since the keyboard
                // model was written; this is its wiring.
                RunPreviewCommand(_previewPolicy.Escape());
                break;

            case BarKeyboard.EscapeAction.ClearQuery:
                SearchBox.Clear();
                break;

            case BarKeyboard.EscapeAction.ClearSubtypeFilter:
                SetSubtype(0);
                ApplyFilter();
                break;

            case BarKeyboard.EscapeAction.ClearTagFilter:
                TagFilter.SelectedItem = AnyTag;
                break;

            case BarKeyboard.EscapeAction.ClearTypeFilter:
                SetKindIndex(0);
                ApplyFilter();
                break;

            case BarKeyboard.EscapeAction.ClearFavoriteFilter:
                FavoriteOnly.IsChecked = false;
                break;

            case BarKeyboard.EscapeAction.ClearGroupFilter:
                SelectGroup(null);
                break;

            case BarKeyboard.EscapeAction.HideWindow:
                Dismiss();
                break;
        }
    }

    private void CycleKind(int delta)
    {
        SetKindIndex(BarKeyboard.Cycle(_kindIndex, delta, _kindChips.Length));
        ApplyFilter();
    }

    private void CycleTag(int delta)
    {
        var index = TagFilter.Items.IndexOf(TagFilter.SelectedItem);
        var next = BarKeyboard.Cycle(index < 0 ? 0 : index, delta, TagFilter.Items.Count);
        TagFilter.SelectedIndex = next;
    }
}
