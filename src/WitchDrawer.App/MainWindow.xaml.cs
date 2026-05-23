using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.App.ViewModels;
using WitchDrawer.App.Views;
using WitchDrawer.Core.Logging;
using WitchDrawer.Native.HotKeys;

namespace WitchDrawer.App;

public partial class MainWindow : Window
{
    private const string InternalDrawerItemDragFormat = "WitchDrawer.DesktopBoxItem";
    private const int WmHotKey = 0x0312;
    private const int QuickPanelHotKeyId = 0x5744;

    private readonly QuickPanelWindow _quickPanel;
    private readonly IAppLogger _logger;
    private NativeHotKey? _hotKey;
    private HwndSource? _source;

    public event EventHandler? WindowHidden;
    public event EventHandler? WindowClosing;

    public MainWindow(MainViewModel viewModel, QuickPanelWindow quickPanel, IAppLogger logger)
    {
        DataContext = viewModel;
        _quickPanel = quickPanel;
        _logger = logger;
        InitializeComponent();
        Loaded += OnLoaded;
        AppThemeManager.ThemeChanged += OnThemeChanged;
    }

    private bool _forceClosing;

    public void MinimizeToTray()
    {
        Hide();
        WindowHidden?.Invoke(this, EventArgs.Empty);
    }

    public void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_forceClosing)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        MinimizeToTray();
    }

    public void ForceClose()
    {
        _forceClosing = true;
        Close();
    }

    public MainViewModel ViewModel => (MainViewModel)DataContext;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            _source = HwndSource.FromHwnd(handle);
            _source?.AddHook(WndProc);

            _hotKey = new NativeHotKey(handle, QuickPanelHotKeyId);
            _hotKey.Register(
                HotKeyModifiers.Control | HotKeyModifiers.Alt | HotKeyModifiers.NoRepeat,
                (uint)KeyInterop.VirtualKeyFromKey(Key.W));
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to register quick panel hotkey.");
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        Loaded -= OnLoaded;
        AppThemeManager.ThemeChanged -= OnThemeChanged;
        _source?.RemoveHook(WndProc);
        _hotKey?.Dispose();
        _quickPanel.ForceClose();
        WindowClosing?.Invoke(this, EventArgs.Empty);
        base.OnClosed(e);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AppThemeManager.ApplyToWindow(this);
        WindowMotion.PopIn(this, 0.985, 160);
    }

    private void OnThemeChanged(object? sender, AppTheme theme)
    {
        AppThemeManager.ApplyToWindow(this);
    }

    private void OnShellHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnMinimizeClicked(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private nint WndProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == WmHotKey && wParam.ToInt32() == QuickPanelHotKeyId)
        {
            handled = true;
            _ = Dispatcher.InvokeAsync(async () => await _quickPanel.ToggleAsync());
        }

        return nint.Zero;
    }

    private void OnPreviewDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(InternalDrawerItemDragFormat))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnFilesDropped(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(InternalDrawerItemDragFormat))
        {
            e.Handled = true;
            return;
        }

        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            await ViewModel.ImportPathsAsync(paths);
            var lastItem = ViewModel.Items.LastOrDefault();
            if (lastItem is not null)
            {
                MainItemsList.SelectedItem = lastItem;
                MainItemsList.Focus();
            }
        }
    }

    private async void OnItemsMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source)
        {
            var item = ItemsControl.ContainerFromElement((ItemsControl)sender, source) as FrameworkElement;
            if (item?.DataContext is DrawerItemViewModel drawerItem)
            {
                await ViewModel.OpenItemCommand.ExecuteAsync(drawerItem);
            }
        }
    }

    private async void OnMainItemsPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete || MainItemsList.SelectedItem is not DrawerItemViewModel item)
        {
            return;
        }

        e.Handled = true;
        await ViewModel.DeleteItemCommand.ExecuteAsync(item);
        MainItemsList.Focus();
    }

    private void OnCreateBoxClicked(object sender, RoutedEventArgs e)
    {
        CreateBoxPopup.IsOpen = true;
    }

    private void OnDeleteBoxClicked(object sender, RoutedEventArgs e)
    {
        DeleteConfirmPopup.IsOpen = true;
    }

    private void OnCancelDeleteBoxClicked(object sender, RoutedEventArgs e)
    {
        DeleteConfirmPopup.IsOpen = false;
    }

    private void OnConfirmDeleteBoxClicked(object sender, RoutedEventArgs e)
    {
        DeleteConfirmPopup.IsOpen = false;
        if (ViewModel.DeleteSelectedBoxCommand.CanExecute(null))
        {
            ViewModel.DeleteSelectedBoxCommand.Execute(null);
        }
    }

    private void OnRenameBoxClicked(object sender, RoutedEventArgs e)
    {
        RenameBoxPopup.IsOpen = true;
        TxtRenameBox.Text = ViewModel.SelectedBox?.Name ?? "";
        
        Dispatcher.InvokeAsync(() =>
        {
            TxtRenameBox.Focus();
            System.Windows.Input.Keyboard.Focus(TxtRenameBox);
            TxtRenameBox.SelectAll();
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void OnRenameBoxPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && sender is System.Windows.Controls.TextBox tb)
        {
            var caret = tb.CaretIndex;
            tb.Text = tb.Text.Insert(caret, " ");
            tb.CaretIndex = caret + 1;
            e.Handled = true;
        }
    }

    private void OnConfirmRenameBoxClicked(object sender, RoutedEventArgs e)
    {
        var newName = TxtRenameBox.Text ?? "";

        RenameBoxPopup.IsOpen = false;
        if (ViewModel.RenameSelectedBoxCommand.CanExecute(newName))
        {
            ViewModel.RenameSelectedBoxCommand.Execute(newName);
        }
    }

    private void OnRenameBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            OnConfirmRenameBoxClicked(sender, e);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            RenameBoxPopup.IsOpen = false;
        }
    }

    private void OnOpenProjectLinkClicked(object sender, RoutedEventArgs e)
    {
        OpenExternalUri("https://github.com/witchscottishfoldcat/WitchDrawer");
    }

    private void OnOpenEmailClicked(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        OpenExternalUri("mailto:witchscottishfoldcat@gmail.com");
    }

    private void OnOpenWebsiteClicked(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        OpenExternalUri("https://www.witchcat.cn");
    }

    private void OpenExternalUri(string uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = uri,
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            _logger.Error(exception, $"Failed to open external URI: {uri}");
        }
    }
}
