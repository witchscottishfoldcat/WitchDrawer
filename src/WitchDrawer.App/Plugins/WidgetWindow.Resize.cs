using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using static WitchDrawer.Native.Windows.User32Interop;

namespace WitchDrawer.App.Plugins;

[Flags]
internal enum WidgetResizeEdges { Left = 1, Top = 2, Right = 4, Bottom = 8 }

internal sealed partial class WidgetWindow
{
    private Rect _resizeStart;
    private NativePoint _resizeStartCursor;
    private SizeToContent _resizeStartMode;
    private bool _resizing;
    private DpiScale _resizeStartDpi;

    private void AddResizeHandles(Grid layout)
    {
        Add(WidgetResizeEdges.Left, Cursors.SizeWE, HorizontalAlignment.Left, VerticalAlignment.Stretch, 6, double.NaN, new Thickness(0, 12, 0, 12));
        Add(WidgetResizeEdges.Right, Cursors.SizeWE, HorizontalAlignment.Right, VerticalAlignment.Stretch, 6, double.NaN, new Thickness(0, 26, 0, 12));
        Add(WidgetResizeEdges.Top, Cursors.SizeNS, HorizontalAlignment.Stretch, VerticalAlignment.Top, double.NaN, 6, new Thickness(12, 0, 28, 0));
        Add(WidgetResizeEdges.Bottom, Cursors.SizeNS, HorizontalAlignment.Stretch, VerticalAlignment.Bottom, double.NaN, 6, new Thickness(12, 0, 12, 0));
        Add(WidgetResizeEdges.Left | WidgetResizeEdges.Top, Cursors.SizeNWSE, HorizontalAlignment.Left, VerticalAlignment.Top, 12, 12);
        Add(WidgetResizeEdges.Right | WidgetResizeEdges.Top, Cursors.SizeNESW, HorizontalAlignment.Right, VerticalAlignment.Top, 14, 14);
        Add(WidgetResizeEdges.Left | WidgetResizeEdges.Bottom, Cursors.SizeNESW, HorizontalAlignment.Left, VerticalAlignment.Bottom, 12, 12);
        Add(WidgetResizeEdges.Right | WidgetResizeEdges.Bottom, Cursors.SizeNWSE, HorizontalAlignment.Right, VerticalAlignment.Bottom, 14, 14);

        void Add(WidgetResizeEdges edges, Cursor cursor, HorizontalAlignment horizontal, VerticalAlignment vertical,
            double width, double height, Thickness margin = default)
        {
            var surface = new FrameworkElementFactory(typeof(Border));
            surface.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            var thumb = new Thumb
            {
                Name = "Resize" + edges.ToString().Replace(", ", ""),
                Cursor = cursor, Width = width, Height = height, Margin = margin,
                HorizontalAlignment = horizontal, VerticalAlignment = vertical,
                MinWidth = 0, MinHeight = 0,
                Focusable = false, Template = new ControlTemplate(typeof(Thumb)) { VisualTree = surface }
            };
            thumb.DragStarted += (_, e) =>
            {
                if (!GetCursorPos(out _resizeStartCursor)) return;
                _resizeStart = new Rect(Left, Top, ActualWidth, ActualHeight);
                _resizeStartMode = SizeToContent;
                _resizeStartDpi = VisualTreeHelper.GetDpi(this);
                _resizing = true;
                SizeToContent = SizeToContent.Manual;
                _scaledView.StretchDirection = StretchDirection.Both;
                Width = _resizeStart.Width;
                Height = _resizeStart.Height;
                e.Handled = true;
            };
            thumb.DragDelta += (_, e) =>
            {
                if (!_resizing || !GetCursorPos(out var cursorPosition)) return;
                var bounds = ResizeBounds(_resizeStart, edges,
                    (cursorPosition.X - _resizeStartCursor.X) / _resizeStartDpi.DpiScaleX,
                    (cursorPosition.Y - _resizeStartCursor.Y) / _resizeStartDpi.DpiScaleY,
                    new Size(MinWidth, MinHeight), new Size(MaxWidth, MaxHeight));
                Width = bounds.Width; Height = bounds.Height;
                Left = bounds.Left; Top = bounds.Top;
                e.Handled = true;
            };
            thumb.DragCompleted += (_, e) =>
            {
                if (!_resizing) return;
                _resizing = false;
                if (e.Canceled)
                {
                    Left = _resizeStart.Left; Top = _resizeStart.Top;
                    Width = _resizeStart.Width; Height = _resizeStart.Height;
                    SizeToContent = _resizeStartMode;
                    _scaledView.StretchDirection = _resizeStartMode == SizeToContent.Manual ? StretchDirection.Both : StretchDirection.DownOnly;
                }
                else LayoutChanged?.Invoke(); // Persist once, never on each drag frame.
                e.Handled = true;
            };
            layout.Children.Add(thumb);
        }
    }

    internal static Rect ResizeBounds(Rect start, WidgetResizeEdges edges, double dx, double dy, Size minimum, Size maximum)
    {
        var left = edges.HasFlag(WidgetResizeEdges.Left);
        var top = edges.HasFlag(WidgetResizeEdges.Top);
        var width = edges.HasFlag(WidgetResizeEdges.Right) ? start.Width + dx : left ? start.Width - dx : start.Width;
        var height = edges.HasFlag(WidgetResizeEdges.Bottom) ? start.Height + dy : top ? start.Height - dy : start.Height;
        width = Math.Clamp(width, minimum.Width, maximum.Width);
        height = Math.Clamp(height, minimum.Height, maximum.Height);
        return new Rect(left ? start.Right - width : start.Left, top ? start.Bottom - height : start.Top, width, height);
    }
}
