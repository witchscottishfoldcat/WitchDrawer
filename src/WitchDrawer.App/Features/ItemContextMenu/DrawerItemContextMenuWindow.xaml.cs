using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WitchDrawer.Native.Windows;

namespace WitchDrawer.App.Features.ItemContextMenu;

internal enum DrawerItemContextAction
{
    None,
    Open,
    RunAsAdministrator,
    Reveal,
    RemoveFromBox
}

public partial class DrawerItemContextMenuWindow : Window
{
    private readonly TaskCompletionSource<DrawerItemContextAction> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly int _screenX;
    private readonly int _screenY;
    private IDisposable? _outsideClickMonitor;
    private DrawerItemContextAction _selectedAction;

    internal DrawerItemContextMenuWindow(
        bool showRunAsAdministrator,
        bool isMappingBox,
        bool isPixelStyle,
        int screenX,
        int screenY)
    {
        _screenX = screenX;
        _screenY = screenY;
        InitializeComponent();

        SurfaceCornerRadius = isPixelStyle ? new CornerRadius(3) : new CornerRadius(13);
        ItemCornerRadius = isPixelStyle ? new CornerRadius(2) : new CornerRadius(8);
        FontFamily = isPixelStyle
            ? new FontFamily("Consolas, Microsoft YaHei UI")
            : new FontFamily("Segoe UI, Microsoft YaHei UI");

        RunAsAdministratorButton.Visibility =
            showRunAsAdministrator ? Visibility.Visible : Visibility.Collapsed;
        RemoveButton.Content = isMappingBox ? "移除引用" : "移出收纳盒";
        System.Windows.Automation.AutomationProperties.SetName(
            RemoveButton,
            isMappingBox ? "移除引用" : "移出收纳盒");

        Loaded += OnLoaded;
        SourceInitialized += OnSourceInitialized;
        StateChanged += OnStateChanged;
    }

    public static readonly DependencyProperty SurfaceCornerRadiusProperty =
        DependencyProperty.Register(
            nameof(SurfaceCornerRadius),
            typeof(CornerRadius),
            typeof(DrawerItemContextMenuWindow),
            new PropertyMetadata(new CornerRadius(13)));

    public static readonly DependencyProperty ItemCornerRadiusProperty =
        DependencyProperty.Register(
            nameof(ItemCornerRadius),
            typeof(CornerRadius),
            typeof(DrawerItemContextMenuWindow),
            new PropertyMetadata(new CornerRadius(8)));

    public CornerRadius SurfaceCornerRadius
    {
        get => (CornerRadius)GetValue(SurfaceCornerRadiusProperty);
        set => SetValue(SurfaceCornerRadiusProperty, value);
    }

    public CornerRadius ItemCornerRadius
    {
        get => (CornerRadius)GetValue(ItemCornerRadiusProperty);
        set => SetValue(ItemCornerRadiusProperty, value);
    }

    internal Task<DrawerItemContextAction> ShowForSelectionAsync()
    {
        Show();
        return _completion.Task;
    }

    protected override void OnClosed(EventArgs e)
    {
        _outsideClickMonitor?.Dispose();
        _outsideClickMonitor = null;
        Loaded -= OnLoaded;
        SourceInitialized -= OnSourceInitialized;
        StateChanged -= OnStateChanged;
        _completion.TrySetResult(_selectedAction);
        base.OnClosed(e);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            UpdateLayout();
            var handle = new WindowInteropHelper(this).Handle;
            var dpi = VisualTreeHelper.GetDpi(this);
            TransientMenuWindow.PositionWithoutActivation(
                handle,
                _screenX,
                _screenY,
                (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX),
                (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY));

            _outsideClickMonitor = TransientMenuWindow.DismissOnOutsideInput(
                handle,
                () =>
                {
                    _ = Dispatcher.BeginInvoke(
                        DispatcherPriority.Input,
                        () =>
                        {
                            if (IsVisible)
                            {
                                Close();
                            }
                        });
                });
            Opacity = 1;
        }
        catch (Exception exception)
        {
            _completion.TrySetException(exception);
            Close();
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        TransientMenuWindow.ConfigureNoActivate(new WindowInteropHelper(this).Handle);
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && IsVisible)
        {
            Close();
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        e.Handled = true;
        Close();
    }

    private void SelectAndClose(DrawerItemContextAction action)
    {
        _selectedAction = action;
        Close();
    }

    private void OnOpenClick(object sender, RoutedEventArgs e) =>
        SelectAndClose(DrawerItemContextAction.Open);

    private void OnRunAsAdministratorClick(object sender, RoutedEventArgs e) =>
        SelectAndClose(DrawerItemContextAction.RunAsAdministrator);

    private void OnRevealClick(object sender, RoutedEventArgs e) =>
        SelectAndClose(DrawerItemContextAction.Reveal);

    private void OnRemoveClick(object sender, RoutedEventArgs e) =>
        SelectAndClose(DrawerItemContextAction.RemoveFromBox);
}
