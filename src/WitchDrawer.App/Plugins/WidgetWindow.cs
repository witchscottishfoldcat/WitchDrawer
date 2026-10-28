using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.Core.Localization;
using WitchDrawer.Core.Plugins;
using WitchDrawer.Native.Windows;

namespace WitchDrawer.App.Plugins;

internal sealed partial class WidgetWindow : Window
{
    private bool _forceClosing;
    private HwndSource? _source;
    private DesktopToolWindow? _native;
    private DispatcherOperation? _appearanceRefresh;
    private bool _closed;
    private readonly Viewbox _scaledView;
    public event Action? CloseRequested;
    public event Action? LayoutChanged;
    public event Action? DesktopChanged;
    public nint NativeHandle => new WindowInteropHelper(this).Handle;

    public WidgetWindow(InstalledPlugin installed, FrameworkElement view)
    {
        Title = installed.Manifest.Name;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        SetResourceReference(ForegroundProperty, "TextPrimaryBrush");
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        MinWidth = 160;
        MinHeight = 92;
        MaxWidth = Math.Min(8192, Math.Max(800, SystemParameters.VirtualScreenWidth));
        MaxHeight = Math.Min(8192, Math.Max(600, SystemParameters.VirtualScreenHeight));
        if (installed.Width is { } width && installed.Height is { } height && double.IsFinite(width) && double.IsFinite(height))
        {
            SizeToContent = SizeToContent.Manual;
            Width = Math.Clamp(width, MinWidth, MaxWidth);
            Height = Math.Clamp(height, MinHeight, MaxHeight);
        }
        WindowStartupLocation = WindowStartupLocation.Manual;
        var area = SystemParameters.WorkArea;
        Left = installed.Left is { } left && left >= SystemParameters.VirtualScreenLeft && left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 40 ? left : area.Left + 40;
        Top = installed.Top is { } top && top >= SystemParameters.VirtualScreenTop && top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 40 ? top : area.Top + 40;
        var root = new Border { BorderThickness = new Thickness(1) };
        root.SetBinding(Border.CornerRadiusProperty, new Binding(nameof(DesktopBoxCornerSettings.BoxWindowRadius))
        {
            Source = AppThemeManager.DesktopCorners,
            Mode = BindingMode.OneWay
        });
        root.SetResourceReference(Border.BackgroundProperty, "GlassSurfaceBrush");
        root.SetResourceReference(Border.BorderBrushProperty, "DesktopBoxBorderBrush");
        var layout = new Grid();
        _scaledView = new Viewbox
        {
            Stretch = Stretch.Uniform,
            StretchDirection = SizeToContent == SizeToContent.Manual ? StretchDirection.Both : StretchDirection.DownOnly,
            VerticalAlignment = VerticalAlignment.Top, Child = view
        };
        layout.Children.Add(new Border { Padding = new Thickness(18, 10, 18, 12), Child = _scaledView });
        AddResizeHandles(layout);
        // Canvas chrome contributes no desired size: the plugin determines the entire layout.
        var chrome = new Canvas();
        var close = new Button { Content = "×", FontSize = 14, FontWeight = FontWeights.Light, Width = 18, Height = 18, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0), Cursor = Cursors.Hand, ToolTip = Strings.Get("PluginDisable"), Visibility = Visibility.Hidden };
        System.Windows.Automation.AutomationProperties.SetName(close, Strings.Get("PluginDisable"));
        close.SetResourceReference(Control.ForegroundProperty, "TextMutedBrush");
        // Quiet chrome keeps the widget content prominent; no shadow or frame animation.
        var closeBorder = new FrameworkElementFactory(typeof(Border));
        closeBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
        closeBorder.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        closeBorder.Name = "Chrome";
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        closeBorder.AppendChild(presenter);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = closeBorder };
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("HoverBrush"), "Chrome"));
        template.Triggers.Add(hover);
        close.Template = template;
        Canvas.SetRight(close, 10);
        Canvas.SetTop(close, 5);
        close.Click += (_, _) => CloseRequested?.Invoke();
        chrome.Children.Add(close);
        var grip = new System.Windows.Shapes.Path
        {
            Width = 8, Height = 8, Data = Geometry.Parse("M0,8 L8,0 M4,8 L8,4"), StrokeThickness = 1,
            Opacity = 0.5, IsHitTestVisible = false, Visibility = Visibility.Hidden
        };
        grip.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "TextMutedBrush");
        Canvas.SetRight(grip, 7); Canvas.SetBottom(grip, 7);
        chrome.Children.Add(grip);
        layout.Children.Add(chrome);
        root.MouseEnter += (_, _) => close.Visibility = grip.Visibility = Visibility.Visible;
        root.MouseLeave += (_, _) => close.Visibility = grip.Visibility = Visibility.Hidden;
        root.MouseLeftButtonDown += (_, e) =>
        {
            // Leave buttons, editors, links and scrolling inside third-party widgets interactive.
            for (var source = e.OriginalSource as DependencyObject; source is not null && !ReferenceEquals(source, root);)
            {
                if (source is Thumb or ButtonBase or TextBoxBase or PasswordBox or Selector or RangeBase or ScrollViewer or System.Windows.Documents.Hyperlink) return;
                source = source is Visual or System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
            }
            if (e.ButtonState != MouseButtonState.Pressed) return;
            try { DragMove(); LayoutChanged?.Invoke(); }
            catch (InvalidOperationException) { }
            e.Handled = true;
        };
        root.Child = layout;
        Content = root;
        AppThemeManager.ApplyDesktopBoxResources(Resources);
        AppThemeManager.ThemeChanged += OnThemeChanged;
        AppThemeManager.BoxOpacityChanged += OnBoxOpacityChanged;
        AppThemeManager.DesktopBoxAppearanceChanged += OnAppearanceChanged;
    }

    private void OnThemeChanged(object? sender, AppTheme theme) => QueueAppearanceRefresh();

    private void OnBoxOpacityChanged(object? sender, ThemeBoxOpacityChangedEventArgs e) => OnAppearanceChanged(sender, e.Theme);

    private void OnAppearanceChanged(object? sender, AppTheme theme)
    {
        if (theme == AppThemeManager.CurrentTheme) QueueAppearanceRefresh();
    }

    private void QueueAppearanceRefresh()
    {
        if (_closed || _appearanceRefresh is { Status: DispatcherOperationStatus.Pending }) return;
        // Coalesce slider edits, with no polling or per-frame work when the theme is idle.
        _appearanceRefresh = Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
        {
            _appearanceRefresh = null;
            if (!_closed) AppThemeManager.ApplyDesktopBoxResources(Resources);
        });
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _source = HwndSource.FromHwnd(NativeHandle);
        _source?.AddHook(Hook);
        _native = new DesktopToolWindow(NativeHandle);
        RecoverDesktop();
    }

    public void RecoverDesktop()
    {
        if (DesktopWindowLayer.IsEnabled) DesktopWindowLayer.Configure(NativeHandle);
        else { _native?.Configure(); _native?.RefreshDesktopHostForShowDesktop(); }
    }

    private nint Hook(nint window, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == DesktopToolWindow.TaskbarCreatedMessage) DesktopChanged?.Invoke();
        if (message == 0x0021) { handled = true; return 3; } // WM_MOUSEACTIVATE: input without foreground activation.
        return 0;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_forceClosing) { e.Cancel = true; CloseRequested?.Invoke(); }
        base.OnClosing(e);
    }
    public void ForceClose() { _forceClosing = true; Close(); }
    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        _resizing = false;
        AppThemeManager.ThemeChanged -= OnThemeChanged;
        AppThemeManager.BoxOpacityChanged -= OnBoxOpacityChanged;
        AppThemeManager.DesktopBoxAppearanceChanged -= OnAppearanceChanged;
        _appearanceRefresh?.Abort();
        _appearanceRefresh = null;
        _source?.RemoveHook(Hook);
        Content = null;
        base.OnClosed(e);
    }
}
