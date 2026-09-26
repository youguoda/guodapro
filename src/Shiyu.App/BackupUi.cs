using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Shiyu.Core;

namespace Shiyu.App;

/// <summary>
/// The backup buttons' whole journey: file pickers, the password ask, the
/// progress window, and the plain-words result.
///
/// The password is handled as bytes from the first ask to the wipe inside
/// BackupArchive — it is never a string, because a managed string cannot be
/// cleared and "wiped after use" would be a claim the code cannot keep.
/// </summary>
public sealed class BackupUi(
    EntryStore store,
    string imagesDirectory,
    Func<string?> readSettingsJson,
    Action<string> restoreSettingsJson)
{
    public void Export(Window owner)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出备份",
            Filter = "拾语备份 (*.shiyubk)|*.shiyubk",
            FileName = $"拾语备份 {DateTime.Now:yyyy-MM-dd}.shiyubk",
        };
        if (dialog.ShowDialog(owner) != true)
        {
            return;
        }

        var password = AskPassword(owner, "为备份设置口令", withConfirm: true,
            accept: "加密导出", skip: "不加密导出");
        if (password.Cancelled)
        {
            return;
        }

        var settings = readSettingsJson();
        RunWithProgress(owner, "正在导出…", report =>
        {
            var counts = BackupArchive.Export(
                dialog.FileName, store, imagesDirectory, settings, password.Bytes, report);
            return $"已导出 {counts.Entries} 条、{counts.Images} 张原图，共 {counts.Bytes / 1024} KB。";
        });
    }

    public void Import(Window owner)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "导入备份",
            Filter = "拾语备份 (*.shiyubk)|*.shiyubk",
        };
        if (dialog.ShowDialog(owner) != true)
        {
            return;
        }

        var password = AskPassword(owner, "备份的口令", withConfirm: false,
            accept: "用口令导入", skip: "无口令导入");
        if (password.Cancelled)
        {
            return;
        }

        var overwrite = AskImportMode(owner);
        if (overwrite is null)
        {
            return;
        }

        RunWithProgress(owner, "正在导入…", report =>
        {
            var outcome = BackupArchive.Import(
                dialog.FileName, store, imagesDirectory, overwrite.Value,
                restoreSettingsJson, password.Bytes, report);
            return overwrite.Value
                ? $"覆盖完成：导入 {outcome.Added} 条、{outcome.ImagesRestored} 张原图、{outcome.GroupsCreated} 个分组。"
                : $"合并完成：新增 {outcome.Added} 条（{outcome.SkippedExisting} 条已有跳过），还原 {outcome.ImagesRestored} 张原图。";
        });
    }

    /// <summary>True = overwrite, false = merge, null = walked away.</summary>
    private static bool? AskImportMode(Window owner)
    {
        bool? answer = null;
        var done = false;

        var window = new Window
        {
            Title = "导入方式",
            Width = 360,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = owner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Background = (Brush)Application.Current.FindResource("Brush.Background"),
            FontFamily = (FontFamily)Application.Current.FindResource("Font.Ui"),
            FontSize = (double)Application.Current.FindResource("Size.Body"),
        };

        var root = new StackPanel { Margin = new Thickness(14) };

        var explain = new TextBlock
        {
            Text = "合并：保留现有内容，只补进不重复的（按内容判断，不看时间）。\n覆盖：清空当前历史，换成备份里的内容（设置一并恢复）。",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        };
        explain.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
        root.Children.Add(explain);

        var row = new StackPanel { Orientation = Orientation.Horizontal };

        var merge = new Button { Content = "合并导入", Padding = new Thickness(14, 5, 14, 5), Cursor = Cursors.Hand };
        merge.Click += (_, _) => { answer = false; done = true; window.Close(); };

        var replace = new Button
        {
            Content = "覆盖导入",
            Padding = new Thickness(14, 5, 14, 5),
            Margin = new Thickness(8, 0, 0, 0),
            Cursor = Cursors.Hand,
        };
        replace.SetResourceReference(Control.ForegroundProperty, "Brush.Danger");
        replace.Click += (_, _) => { answer = true; done = true; window.Close(); };

        row.Children.Add(merge);
        row.Children.Add(replace);
        root.Children.Add(row);
        window.Content = root;
        window.ShowDialog();

        return done ? answer : null;
    }

    private sealed record PasswordAnswer(bool Cancelled, byte[]? Bytes);

    /// <summary>
    /// The password ask. Bytes are lifted straight out of the SecureString as
    /// UTF-16; skip returns null bytes for an unsealed backup, and the confirm
    /// copy is cleared the moment it has been compared.
    /// </summary>
    private static PasswordAnswer AskPassword(
        Window owner, string title, bool withConfirm, string accept, string skip)
    {
        var window = new Window
        {
            Title = title,
            Width = 340,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = owner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Background = (Brush)Application.Current.FindResource("Brush.Background"),
            FontFamily = (FontFamily)Application.Current.FindResource("Font.Ui"),
            FontSize = (double)Application.Current.FindResource("Size.Body"),
        };

        var root = new StackPanel { Margin = new Thickness(14) };

        var box = new PasswordBox { Padding = new Thickness(4) };
        box.SetResourceReference(Control.BackgroundProperty, "Brush.SurfaceInput");
        root.Children.Add(box);

        PasswordBox? again = null;
        if (withConfirm)
        {
            again = new PasswordBox { Padding = new Thickness(4), Margin = new Thickness(0, 6, 0, 0) };
            again.SetResourceReference(Control.BackgroundProperty, "Brush.SurfaceInput");
            root.Children.Add(again);
        }

        var hint = new TextBlock
        {
            Text = "两次输入不一致。",
            Foreground = (Brush)Application.Current.FindResource("Brush.Danger"),
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 6, 0, 0),
        };
        if (withConfirm)
        {
            root.Children.Add(hint);
        }

        byte[]? answer = null;
        var decided = false;

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };

        var ok = new Button { Content = accept, Padding = new Thickness(12, 4, 12, 4), Cursor = Cursors.Hand };
        ok.Click += (_, _) =>
        {
            var first = BytesOf(box.SecurePassword);
            if (first is null)
            {
                hint.Text = "口令不能为空——不需要加密请点「" + skip + "」。";
                hint.Visibility = Visibility.Visible;
                return;
            }

            var second = withConfirm ? BytesOf(again!.SecurePassword) : null;
            var same = !withConfirm || second is not null && first.SequenceEqual(second);
            if (second is not null)
            {
                Array.Clear(second);
            }

            if (!same)
            {
                Array.Clear(first);
                hint.Text = "两次输入不一致。";
                hint.Visibility = Visibility.Visible;
                return;
            }

            answer = first;
            decided = true;
            window.Close();
        };

        var plain = new Button { Content = skip, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(8, 0, 0, 0), Cursor = Cursors.Hand };
        plain.Click += (_, _) =>
        {
            answer = null;
            decided = true;
            window.Close();
        };

        var cancel = new Button { Content = "取消", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(8, 0, 0, 0), Cursor = Cursors.Hand };
        cancel.Click += (_, _) => window.Close();

        row.Children.Add(ok);
        row.Children.Add(plain);
        row.Children.Add(cancel);
        root.Children.Add(row);
        window.Content = root;
        window.ShowDialog();

        return decided ? new PasswordAnswer(false, answer) : new PasswordAnswer(true, null);
    }

    private static byte[]? BytesOf(SecureString secret)
    {
        if (secret.Length == 0)
        {
            return null;
        }

        var pinned = Marshal.SecureStringToCoTaskMemUnicode(secret);
        try
        {
            var bytes = new byte[secret.Length * 2];
            Marshal.Copy(pinned, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            Marshal.ZeroFreeCoTaskMemUnicode(pinned);
        }
    }

    private static void RunWithProgress(Window owner, string title, Func<IProgress<string>, string> work)
    {
        var window = new Window
        {
            Title = title,
            Width = 340,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = owner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Background = (Brush)Application.Current.FindResource("Brush.Background"),
            FontFamily = (FontFamily)Application.Current.FindResource("Font.Ui"),
            FontSize = (double)Application.Current.FindResource("Size.Body"),
        };

        var root = new StackPanel { Margin = new Thickness(14) };
        var status = new TextBlock { Text = title, Margin = new Thickness(0, 0, 0, 8) };
        status.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
        var bar = new ProgressBar { IsIndeterminate = true, Height = 6, Minimum = 0, Maximum = 100 };
        root.Children.Add(status);
        root.Children.Add(bar);
        window.Content = root;

        var progress = new Progress<string>(line => status.Text = line);
        window.Loaded += async (_, _) =>
        {
            try
            {
                var summary = await Task.Run(() => work(progress));
                window.Close();
                MessageBox.Show(owner, summary, "备份", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (BackupException failure)
            {
                window.Close();
                MessageBox.Show(owner, failure.Message, "备份", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception)
            {
                window.Close();
                MessageBox.Show(owner, "备份操作失败了，现有数据没有被动过。", "备份", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };

        window.ShowDialog();
    }
}
