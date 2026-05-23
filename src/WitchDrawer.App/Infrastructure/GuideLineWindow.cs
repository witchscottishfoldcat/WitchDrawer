using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace WitchDrawer.App.Infrastructure;

public sealed class GuideLineWindow : Window
{
    private readonly Line _line;

    public GuideLineWindow(bool isVertical)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        IsHitTestVisible = false;
        Focusable = false;
        ResizeMode = ResizeMode.NoResize;

        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        _line = new Line
        {
            StrokeThickness = 1.5,
            StrokeDashArray = new DoubleCollection(new double[] { 4, 3 }),
            SnapsToDevicePixels = true
        };

        // Dynamically reference active AccentBrush theme resource
        _line.SetResourceReference(Shape.StrokeProperty, "AccentBrush");

        var canvas = new Canvas();
        canvas.Children.Add(_line);
        Content = canvas;
    }

    public void UpdateLine(double x1, double y1, double x2, double y2)
    {
        _line.X1 = x1 - Left;
        _line.Y1 = y1 - Top;
        _line.X2 = x2 - Left;
        _line.Y2 = y2 - Top;
    }
}
