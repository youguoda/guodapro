using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OverlaySpike;

/// <summary>Stands in for the application the overlay floats above.</summary>
public partial class UnderlayWindow : Window
{
    public UnderlayWindow()
    {
        InitializeComponent();

        for (var i = 1; i <= 12; i++)
        {
            Rows.Items.Add($"第 {i} 条记录");
        }

        Rows.SelectedIndex = 0;
    }

    /// <summary>Raised with the selected row's rectangle in screen coordinates.</summary>
    public Action<Rect>? RowChanged { get; set; }

    public Action<string>? Reported { get; set; }

    private void OnRowChanged(object sender, SelectionChangedEventArgs e)
    {
        Status.Text = $"选中「{Rows.SelectedItem}」· {DateTime.Now:HH:mm:ss}";
        Reported?.Invoke($"underlay row {Rows.SelectedIndex} selected");
        PublishSelectedRow();
    }

    /// <summary>Reports where the selected row sits, so the connector can follow it.</summary>
    public void PublishSelectedRow()
    {
        // Containers do not exist until the list has laid out, and the list has
        // not laid out merely because the window is Loaded. Forcing it here is
        // what makes the first connector draw in the right place rather than at
        // the canvas origin.
        Rows.UpdateLayout();

        if (Rows.ItemContainerGenerator.ContainerFromIndex(Rows.SelectedIndex) is not ListBoxItem row)
        {
            Reported?.Invoke("underlay row container not realised");
            return;
        }

        // Reported in physical pixels throughout: the overlay converts into its
        // own space with PointFromScreen, which is the only conversion that
        // survives monitors at different scale factors.
        var topLeft = row.PointToScreen(new Point(0, 0));
        var bottomRight = row.PointToScreen(new Point(row.ActualWidth, row.ActualHeight));

        RowChanged?.Invoke(new Rect(topLeft, bottomRight));
    }
}
