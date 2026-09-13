using System.Windows;
using System.Windows.Interop;
using WinForms = System.Windows.Forms;

namespace FormatToolbox.App;

public static class WindowSizing
{
    public static void Attach(Window window) => window.SourceInitialized += (_, _) => FitToScreen(window);

    public static void FitToScreen(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var screen = WinForms.Screen.FromHandle(handle).WorkingArea;
        var transform = HwndSource.FromHwnd(handle)?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;
        Fit(window, Rect.Transform(new Rect(screen.X, screen.Y, screen.Width, screen.Height), transform));
    }

    public static void Fit(Window window, Rect workArea)
    {
        var width = Math.Max(1, workArea.Width - 24); var height = Math.Max(1, workArea.Height - 24);
        window.MinWidth = Math.Min(window.MinWidth, width); window.MinHeight = Math.Min(window.MinHeight, height);
        window.Width = Math.Min(window.Width, width); window.Height = Math.Min(window.Height, height);
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = workArea.Left + (workArea.Width - window.Width) / 2;
        window.Top = workArea.Top + (workArea.Height - window.Height) / 2;
    }
}
