using System.Diagnostics;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.App.ViewModels;
using WitchDrawer.App.Views;
using WitchDrawer.Core.Logging;
using WitchDrawer.Native.HotKeys;
using WitchDrawer.Native.Windows;

namespace WitchDrawer.App;

public partial class MainWindow : Window
{
    private const string InternalDrawerItemDragFormat = "WitchDrawer.DesktopBoxItem";
    private const string BoxListDragFormat = "WitchDrawer.BoxListOrder";
    private const int WmHotKey = 0x0312;
    private const int QuickPanelHotKeyId = 0x5744;

    private readonly QuickPanelWindow _quickPanel;
    private readonly IAppLogger _logger;
    private readonly QuickPanelHotKeySettingsStore _hotKeySettings;
    private QuickPanelHotKey _quickPanelHotKey;
    private NativeHotKey? _hotKey;
    private bool _isHotKeyRegistered;
    private bool _isCapturingHotKey;
    private bool _isApplyingHotKey;
    private HwndSource? _source;
    private Point? _boxDragStart;
    private BoxViewModel? _boxDragSource;
    private ListBoxItem? _boxDropTarget;
    private bool _isBoxVisualStylePageOpen;
    private bool _isBoxVisualStyleTransitioning;
    public event EventHandler? WindowHidden;
    public event EventHandler? WindowClosing;
    public event EventHandler? DesktopShellRestarted;

    /// <summary>
    /// Raised when the user asks to reopen a desktop box window (e.g. by
    /// double-clicking its entry in the sidebar list). Carries the box id.
    /// </summary>
    public event EventHandler<Guid>? ReopenBoxRequested;

    internal MainWindow(
        MainViewModel viewModel,
        QuickPanelWindow quickPanel,
        IAppLogger logger,
        QuickPanelHotKeySettingsStore hotKeySettings,
        QuickPanelHotKey quickPanelHotKey)
    {
        DataContext = viewModel;
        _quickPanel = quickPanel;
        _logger = logger;
        _hotKeySettings = hotKeySettings;
        _quickPanelHotKey = quickPanelHotKey;
        InitializeComponent();
        UpdateHotKeyUi("点击按钮可修改");
        Loaded += OnLoaded;
        DpiChanged += OnDpiChanged;
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
            RegisterInitialHotKey();
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to register quick panel hotkey.");
            _isHotKeyRegistered = false;
            UpdateHotKeyUi(GetHotKeyErrorText(exception));
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        Loaded -= OnLoaded;
        DpiChanged -= OnDpiChanged;
        AppThemeManager.ThemeChanged -= OnThemeChanged;
        _source?.RemoveHook(WndProc);
        _hotKey?.Dispose();
        _quickPanel.ForceClose();
        WindowClosing?.Invoke(this, EventArgs.Empty);
        base.OnClosed(e);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateIconDisplayMetrics(VisualTreeHelper.GetDpi(this));
        AppThemeManager.ApplyToWindow(this);
        WindowMotion.PopIn(this, 0.985, 160);
    }

    private void OnDpiChanged(object sender, DpiChangedEventArgs e)
    {
        UpdateIconDisplayMetrics(e.NewDpi);
    }

    private void UpdateIconDisplayMetrics(DpiScale dpi)
    {
        ViewModel.UpdateIconDisplayMetrics(dpi.DpiScaleX, dpi.DpiScaleY);
    }

    private void OnThemeChanged(object? sender, AppTheme theme)
    {
        AppThemeManager.ApplyToWindow(this);
    }

    private void RegisterInitialHotKey()
    {
        if (_hotKey is null)
        {
            return;
        }

        try
        {
            _hotKey.Register(_quickPanelHotKey.RegistrationModifiers, _quickPanelHotKey.VirtualKey);
            _isHotKeyRegistered = true;
            UpdateHotKeyUi("已启用，点击按钮可修改");
        }
        catch (Exception exception)
        {
            _isHotKeyRegistered = false;
            _logger.Error(exception, "Failed to register configured quick panel hotkey.");
            UpdateHotKeyUi(GetHotKeyErrorText(exception));
        }
    }

    private void OnQuickPanelHotKeyButtonClick(object sender, RoutedEventArgs e)
    {
        if (_isApplyingHotKey)
        {
            return;
        }

        _isCapturingHotKey = true;
        QuickPanelHotKeyButton.Content = "请按新快捷键…";
        QuickPanelHotKeyStatusText.Text = "需包含 Ctrl、Alt 或 Win；Esc 取消";
        QuickPanelHotKeyButton.Focus();
        Keyboard.Focus(QuickPanelHotKeyButton);
    }

    private async void OnQuickPanelHotKeyPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_isCapturingHotKey)
        {
            return;
        }

        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            CancelHotKeyCapture("已取消修改");
            return;
        }

        if (IsModifierKey(key))
        {
            QuickPanelHotKeyStatusText.Text = "继续按下一个非修饰键";
            return;
        }

        var modifiers = GetHotKeyModifiers(Keyboard.Modifiers);
        if ((modifiers & (HotKeyModifiers.Control | HotKeyModifiers.Alt | HotKeyModifiers.Win)) == 0)
        {
            QuickPanelHotKeyStatusText.Text = "请至少按住 Ctrl、Alt 或 Win";
            return;
        }

        var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
        var candidate = new QuickPanelHotKey(modifiers, virtualKey);
        if (!candidate.IsValid)
        {
            QuickPanelHotKeyStatusText.Text = "这个按键不能用作全局快捷键";
            return;
        }

        _isCapturingHotKey = false;
        _isApplyingHotKey = true;
        QuickPanelHotKeyButton.IsEnabled = false;
        try
        {
            await ApplyQuickPanelHotKeyAsync(candidate);
        }
        finally
        {
            _isApplyingHotKey = false;
            QuickPanelHotKeyButton.IsEnabled = true;
        }
    }

    private void OnQuickPanelHotKeyLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_isCapturingHotKey)
        {
            CancelHotKeyCapture("已取消修改");
        }
    }

    private async Task ApplyQuickPanelHotKeyAsync(QuickPanelHotKey candidate)
    {
        if (_hotKey is null)
        {
            UpdateHotKeyUi("快捷键组件尚未初始化");
            return;
        }

        if (candidate == _quickPanelHotKey && _isHotKeyRegistered)
        {
            UpdateHotKeyUi("快捷键未更改");
            return;
        }

        var previous = _quickPanelHotKey;
        var previousWasRegistered = _isHotKeyRegistered;
        try
        {
            _hotKey.Register(candidate.RegistrationModifiers, candidate.VirtualKey);
            _isHotKeyRegistered = true;
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to register the requested quick panel hotkey.");
            RestorePreviousHotKey(previous, previousWasRegistered);
            UpdateHotKeyUi(GetHotKeyErrorText(exception));
            return;
        }

        try
        {
            await _hotKeySettings.SaveAsync(candidate);
            _quickPanelHotKey = candidate;
            UpdateHotKeyUi("已保存并立即生效");
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to save quick panel hotkey.");
            RestorePreviousHotKey(previous, previousWasRegistered);
            UpdateHotKeyUi("保存失败，已恢复原快捷键");
        }
    }

    private void RestorePreviousHotKey(QuickPanelHotKey previous, bool previousWasRegistered)
    {
        if (_hotKey is null)
        {
            _isHotKeyRegistered = false;
            return;
        }

        if (!previousWasRegistered)
        {
            _hotKey.Unregister();
            _isHotKeyRegistered = false;
            return;
        }

        try
        {
            _hotKey.Register(previous.RegistrationModifiers, previous.VirtualKey);
            _isHotKeyRegistered = true;
        }
        catch (Exception restoreException)
        {
            _isHotKeyRegistered = false;
            _logger.Error(restoreException, "Failed to restore previous quick panel hotkey.");
        }
    }

    private void CancelHotKeyCapture(string statusText)
    {
        _isCapturingHotKey = false;
        UpdateHotKeyUi(statusText);
    }

    private void UpdateHotKeyUi(string statusText)
    {
        QuickPanelHotKeyButton.Content = _quickPanelHotKey.DisplayText;
        QuickPanelHotKeyStatusText.Text = statusText;
    }

    private static HotKeyModifiers GetHotKeyModifiers(ModifierKeys modifiers)
    {
        var result = HotKeyModifiers.None;
        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            result |= HotKeyModifiers.Control;
        }

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            result |= HotKeyModifiers.Alt;
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            result |= HotKeyModifiers.Shift;
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            result |= HotKeyModifiers.Win;
        }

        return result;
    }

    private static bool IsModifierKey(Key key)
    {
        return key is Key.LeftCtrl
            or Key.RightCtrl
            or Key.LeftAlt
            or Key.RightAlt
            or Key.LeftShift
            or Key.RightShift
            or Key.LWin
            or Key.RWin;
    }

    private static string GetHotKeyErrorText(Exception exception)
    {
        return exception is Win32Exception { NativeErrorCode: 1409 }
            ? "快捷键已被其他程序占用，请换一个组合"
            : "快捷键注册失败，请换一个组合重试";
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
        if (message == DesktopToolWindow.TaskbarCreatedMessage)
        {
            _ = Dispatcher.BeginInvoke(
                new Action(() => DesktopShellRestarted?.Invoke(this, EventArgs.Empty)));
        }

        if (message == WmHotKey && wParam.ToInt32() == QuickPanelHotKeyId)
        {
            handled = true;
            _ = Dispatcher.InvokeAsync(async () => await _quickPanel.ToggleAsync());
        }

        return nint.Zero;
    }

    private void OnPreviewDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(BoxListDragFormat))
        {
            // Let the sidebar ListBox handle its own reorder drag event.
            e.Handled = false;
            return;
        }

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

    private void OnBoxesSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox listBox && listBox.SelectedItem is not null)
        {
            listBox.ScrollIntoView(listBox.SelectedItem);
            ShowPrimaryBoxControls();
            ShowSelectedBoxOverview();
        }
    }

    private void OnBoxesPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source
            || ItemsControl.ContainerFromElement(BoxesList, source) is not ListBoxItem)
        {
            return;
        }

        ShowSelectedBoxOverview();
    }

    private void ShowSelectedBoxOverview()
    {
        if (ViewModel.ShowDashboardCommand.CanExecute(null))
        {
            ViewModel.ShowDashboardCommand.Execute(null);
        }
    }

    private void OnBoxesPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _boxDragStart = e.GetPosition(BoxesList);
        _boxDragSource = e.OriginalSource is DependencyObject source
            ? (ItemsControl.ContainerFromElement(BoxesList, source) as ListBoxItem)?.DataContext as BoxViewModel
            : null;
    }

    private void OnBoxesPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed
            || _boxDragStart is null
            || _boxDragSource is null)
        {
            return;
        }

        var current = e.GetPosition(BoxesList);
        if (Math.Abs(current.X - _boxDragStart.Value.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - _boxDragStart.Value.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var data = new DataObject(BoxListDragFormat, _boxDragSource.Id.ToString("D"));
        try
        {
            e.Handled = true;
            DragDrop.DoDragDrop(BoxesList, data, DragDropEffects.Move);
        }
        finally
        {
            _boxDragStart = null;
            _boxDragSource = null;
            ClearBoxDropIndicator();
        }
    }

    private void OnBoxesDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(BoxListDragFormat)
            || !TryGetBoxDropTarget(e, out var target, out var insertAfter))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            ClearBoxDropIndicator();
            return;
        }

        if (!ReferenceEquals(_boxDropTarget, target)
            || !string.Equals(
                target.Tag as string,
                insertAfter ? "DropAfter" : "DropBefore",
                StringComparison.Ordinal))
        {
            ClearBoxDropIndicator();
            _boxDropTarget = target;
            target.Tag = insertAfter ? "DropAfter" : "DropBefore";
            BoxesList.ScrollIntoView(target.DataContext);
        }

        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private async void OnBoxesDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(BoxListDragFormat)
            || e.Data.GetData(BoxListDragFormat) is not string draggedIdText
            || !Guid.TryParse(draggedIdText, out var draggedId)
            || !TryGetBoxDropTarget(e, out var target, out var insertAfter)
            || target.DataContext is not BoxViewModel targetBox)
        {
            ClearBoxDropIndicator();
            return;
        }

        e.Effects = DragDropEffects.Move;
        e.Handled = true;
        ClearBoxDropIndicator();
        await ViewModel.ReorderBoxAsync(draggedId, targetBox.Id, insertAfter);
    }

    private bool TryGetBoxDropTarget(
        DragEventArgs e,
        out ListBoxItem target,
        out bool insertAfter)
    {
        var position = e.GetPosition(BoxesList);
        var hit = BoxesList.InputHitTest(position) as DependencyObject;
        var container = hit is null
            ? null
            : ItemsControl.ContainerFromElement(BoxesList, hit) as ListBoxItem;

        if (container is null && BoxesList.Items.Count > 0)
        {
            container = BoxesList.ItemContainerGenerator.ContainerFromIndex(
                BoxesList.Items.Count - 1) as ListBoxItem;
            insertAfter = true;
        }
        else
        {
            insertAfter = container is not null
                && e.GetPosition(container).Y >= container.ActualHeight / 2.0;
        }

        target = container!;
        return container is not null;
    }

    private void ClearBoxDropIndicator()
    {
        if (_boxDropTarget is not null)
        {
            _boxDropTarget.Tag = null;
            _boxDropTarget = null;
        }
    }

    private void OnBoxesMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Double-clicking a sidebar entry reopens (shows + focuses) the
        // corresponding desktop box window — the only way back from the
        // window's close (X) -> Hide() behavior short of restarting the app.
        if (e.OriginalSource is not DependencyObject source
            || sender is not ItemsControl items)
        {
            return;
        }

        var container = ItemsControl.ContainerFromElement(items, source) as FrameworkElement;
        if (container?.DataContext is BoxViewModel box)
        {
            ReopenBoxRequested?.Invoke(this, box.Id);
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

    private async void OnCreateNormalBoxClicked(object sender, RoutedEventArgs e)
    {
        CreateBoxPopup.IsOpen = false;
        await ViewModel.CreateNormalBoxCommand.ExecuteAsync(null);
    }

    private async void OnCreateMappingBoxClicked(object sender, RoutedEventArgs e)
    {
        CreateBoxPopup.IsOpen = false;
        await ViewModel.CreateMappingBoxCommand.ExecuteAsync(null);
    }

    private void OnOpenBoxVisualStylePage(object sender, RoutedEventArgs e)
    {
        if (_isBoxVisualStylePageOpen
            || _isBoxVisualStyleTransitioning
            || ViewModel.SelectedBox?.CanSelectVisualStyle != true)
        {
            return;
        }

        _isBoxVisualStylePageOpen = true;
        BoxControlsPrimaryPanel.IsHitTestVisible = false;
        BoxVisualStyleSecondaryPanel.IsHitTestVisible = true;
        VisualStateManager.GoToElementState(
            BoxControlsPageHost,
            "VisualStyleSelectionState",
            useTransitions: true);
    }

    private void OnCloseBoxVisualStylePage(object sender, RoutedEventArgs e)
    {
        ShowPrimaryBoxControls();
    }

    private async void OnBoxVisualStyleSelected(object sender, RoutedEventArgs e)
    {
        if (_isBoxVisualStyleTransitioning
            || sender is not Button
            {
                DataContext: BoxVisualStyleOption option,
                RenderTransform: ScaleTransform scaleTransform
            })
        {
            return;
        }

        _isBoxVisualStyleTransitioning = true;
        BoxVisualStyleSecondaryPanel.IsHitTestVisible = false;
        try
        {
            AnimateVisualStyleSelection(scaleTransform);
            await Task.Delay(170);
            await ViewModel.SetSelectedBoxVisualStyleCommand.ExecuteAsync(option);
            await Task.Delay(40);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to complete box visual style selection animation.");
        }
        finally
        {
            ShowPrimaryBoxControls();
            _isBoxVisualStyleTransitioning = false;
        }
    }

    private void ShowPrimaryBoxControls()
    {
        if (!_isBoxVisualStylePageOpen && !_isBoxVisualStyleTransitioning)
        {
            return;
        }

        _isBoxVisualStylePageOpen = false;
        BoxVisualStyleSecondaryPanel.IsHitTestVisible = false;
        BoxControlsPrimaryPanel.IsHitTestVisible = true;
        VisualStateManager.GoToElementState(
            BoxControlsPageHost,
            "PrimaryControlsState",
            useTransitions: true);
    }

    private static void AnimateVisualStyleSelection(ScaleTransform scaleTransform)
    {
        var easing = new BackEase
        {
            Amplitude = 0.3,
            EasingMode = EasingMode.EaseOut
        };
        var pulse = new DoubleAnimation(
            fromValue: 1,
            toValue: 1.07,
            duration: TimeSpan.FromMilliseconds(85))
        {
            AutoReverse = true,
            EasingFunction = easing
        };

        scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
        scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, pulse.Clone());
    }

    private async void OnCreateTodoBoxClicked(object sender, RoutedEventArgs e)
    {
        CreateBoxPopup.IsOpen = false;
        await ViewModel.CreateTodoBoxCommand.ExecuteAsync(null);
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
