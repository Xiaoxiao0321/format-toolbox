using System.Windows;

namespace FormatToolbox.App;

public partial class ConfirmationWindow : Window
{
    public ConfirmationWindow(string message, string title, bool showCancel = true)
    {
        InitializeComponent(); Title = title; MessageText.Text = message; WindowSizing.Attach(this);
        if (!showCancel)
        {
            CancelButton.Visibility = Visibility.Collapsed; ConfirmButton.Content = "关闭";
            PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) { e.Handled = true; Close(); } };
        }
    }
    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    public static bool Confirm(Window owner, string message, string title) => new ConfirmationWindow(message, title) { Owner = owner }.ShowDialog() == true;
    public static void ShowMessage(Window owner, string message, string title) => new ConfirmationWindow(message, title, showCancel: false) { Owner = owner }.ShowDialog();
}
