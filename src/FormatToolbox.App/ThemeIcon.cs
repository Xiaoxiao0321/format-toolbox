using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;

namespace FormatToolbox.App;

// Geometry stays vector-based and inherits the button's state-dependent foreground.
public static class ThemeIcon
{
    public static readonly DependencyProperty DataProperty = DependencyProperty.RegisterAttached(
        "Data", typeof(Geometry), typeof(ThemeIcon), new PropertyMetadata(null));
    public static void SetData(DependencyObject element, Geometry value) => element.SetValue(DataProperty, value);
    public static Geometry GetData(DependencyObject element) => (Geometry)element.GetValue(DataProperty);
    public static readonly DependencyProperty BrushProperty = DependencyProperty.RegisterAttached(
        "Brush", typeof(Brush), typeof(ThemeIcon), new PropertyMetadata(null));
    public static void SetBrush(DependencyObject element, Brush value) => element.SetValue(BrushProperty, value);
    public static Brush GetBrush(DependencyObject element) => (Brush)element.GetValue(BrushProperty);
}
