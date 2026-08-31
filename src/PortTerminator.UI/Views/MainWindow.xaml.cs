using System.Windows;
using System.Windows.Input;

namespace PortTerminator.UI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = App.MainViewModel;
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            PortInputBox.Focus();
            PortInputBox.SelectAll();
            e.Handled = true;
        }
    }

    private void ConfirmOverlay_BackdropClick(object sender, MouseButtonEventArgs e)
    {
        App.MainViewModel.PortDetail.CancelConfirmCommand.Execute(null);
    }

    private void ConfirmDialog_StopPropagation(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    private void LogList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not System.Windows.Controls.ListBox listBox) return;

        var scrollViewer = FindVisualChild<System.Windows.Controls.ScrollViewer>(listBox);
        if (scrollViewer is null) return;

        scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta / 3.0);
        e.Handled = true;
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
                return match;

            var nested = FindVisualChild<T>(child);
            if (nested is not null)
                return nested;
        }

        return null;
    }
}
