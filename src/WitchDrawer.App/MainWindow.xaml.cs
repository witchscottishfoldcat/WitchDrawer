using WitchDrawer.Core.Localization;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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

    private readonly Func<QuickPanelWindow> _quickPanelFactory;
    private QuickPanelWindow? _quickPanel;
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
    private bool _isEditorOpacityRefreshQueued;
    private readonly HashSet<int> _recordedLayoutBackupSlots = [];
    public event EventHandler? WindowHidden;
    public event EventHandler? WindowClosing;
    public event EventHandler? DesktopShellRestarted;

    public event EventHandler<int>? RecordLayoutBackupRequested;

    public event EventHandler<int>? RestoreLayoutBackupRequested;

    public event EventHandler<int>? DeleteLayoutBackupRequested;

    public event EventHandler<Guid>? RecallBoxToScreenCenterRequested;

    /// <summary>
    /// Raised when the user asks to reopen a desktop box window (e.g. by
    /// double-clicking its entry in the sidebar list). Carries the box id.
    /// </summary>
    public event EventHandler<Guid>? ReopenBoxRequested;

    internal MainWindow(
        MainViewModel viewModel,
        Func<QuickPanelWindow> quickPanelFactory,
        IAppLogger logger,
        QuickPanelHotKeySettingsStore hotKeySettings,
        QuickPanelHotKey quickPanelHotKey)
    {
        DataContext = viewModel;
        _quickPanelFactory = quickPanelFactory;
        _logger = logger;
        _hotKeySettings = hotKeySettings;
        _quickPanelHotKey = quickPanelHotKey;
        InitializeComponent();
        AboutPageView.Logger = _logger;
        BoxDisplaySettings.Logger = _logger;
        UpdateHotKeyUi(Strings.Get("ClickToChange"));
        Loaded += OnLoaded;
        DpiChanged += OnDpiChanged;
        AppThemeManager.ThemeChanged += OnThemeChanged;
        AppThemeManager.BoxOpacityChanged += OnBoxOpacityChanged;
        AppThemeManager.DesktopBoxAppearanceChanged += OnDesktopBoxAppearanceChanged;
        ViewModel.Settings.PropertyChanged += OnViewModelPropertyChanged;
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

    internal void SendBehindDesktop()
    {
        if (!IsVisible)
        {
            return;
        }

        DesktopToolWindow.SendToBottomWithoutActivation(
            new WindowInteropHelper(this).Handle);
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

    /// <summary>
    /// 快捷面板窗口延迟到首次热键触发时才构建：它的 BAML 初始化与样式解析
    /// 不属于启动关键路径，提前构建只会加长主窗口出现前的无响应时间。
    /// </summary>
    private QuickPanelWindow GetQuickPanel() => _quickPanel ??= _quickPanelFactory();

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
        AppThemeManager.BoxOpacityChanged -= OnBoxOpacityChanged;
        AppThemeManager.DesktopBoxAppearanceChanged -= OnDesktopBoxAppearanceChanged;
        CompositionTarget.Rendering -= OnEditorAppearanceFrame;
        ViewModel.Settings.PropertyChanged -= OnViewModelPropertyChanged;
        _source?.RemoveHook(WndProc);
        _hotKey?.Dispose();
        _quickPanel?.ForceClose();
        WindowClosing?.Invoke(this, EventArgs.Empty);
        base.OnClosed(e);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateIconDisplayMetrics(VisualTreeHelper.GetDpi(this));
        ApplyThemeAppearance();
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
        ApplyThemeAppearance();
    }

    private void OnBoxOpacityChanged(object? sender, ThemeBoxOpacityChangedEventArgs e)
    {
        if (e.Theme != AppThemeManager.CurrentTheme || !ViewModel.Settings.EditorFollowsBoxOpacity)
        {
            return;
        }

        QueueEditorOpacityRefresh();
    }

    private void OnDesktopBoxAppearanceChanged(object? sender, AppTheme theme)
    {
        if (theme == AppThemeManager.CurrentTheme && ViewModel.Settings.EditorFollowsBoxOpacity)
            QueueEditorOpacityRefresh();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName))
        {
            UpdateHotKeyUi(Strings.Get("ClickToChange"));
            for (var slot = 1; slot <= 3; slot++)
                SetLayoutBackupSlotState(slot, _recordedLayoutBackupSlots.Contains(slot));
        }
        if (e.PropertyName == nameof(SettingsViewModel.EditorFollowsBoxOpacity))
        {
            QueueEditorOpacityRefresh();
        }
    }

    private void QueueEditorOpacityRefresh()
    {
        if (_isEditorOpacityRefreshQueued)
        {
            return;
        }

        _isEditorOpacityRefreshQueued = true;
        CompositionTarget.Rendering += OnEditorAppearanceFrame;
    }

    private void OnEditorAppearanceFrame(object? sender, EventArgs e)
    {
        CompositionTarget.Rendering -= OnEditorAppearanceFrame;
        _isEditorOpacityRefreshQueued = false;
        RefreshEditorOpacityResources();
    }

    private void ApplyThemeAppearance()
    {
        CompositionTarget.Rendering -= OnEditorAppearanceFrame;
        _isEditorOpacityRefreshQueued = false;
        AppThemeManager.ApplyToWindow(this);
        RefreshEditorOpacityResources();
    }

    private void RefreshEditorOpacityResources()
    {
        if (ViewModel.Settings.EditorFollowsBoxOpacity)
        {
            AppThemeManager.ApplyEditorOpacityResources(Resources);
            return;
        }

        AppThemeManager.ClearEditorOpacityResources(Resources);
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
            UpdateHotKeyUi(Strings.Get("EnabledClickToChange"));
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
        QuickPanelHotKeyButton.Content = Strings.Get("PressANewShortcut");
        QuickPanelHotKeyStatusText.Text = Strings.Get("IncludeCtrlAltOrWinEscToCancel");
        QuickPanelHotKeyButton.Focus();
        Keyboard.Focus(QuickPanelHotKeyButton);
    }

    private async void OnChangeDataDirectoryClick(object sender, RoutedEventArgs e)
    {
        var viewModel = ViewModel;
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = Strings.Get("ChooseANewDataFolderUseAnEmptyFolder")
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var targetDirectory = dialog.FolderName;
        if (string.Equals(
                Path.GetFullPath(targetDirectory),
                Path.GetFullPath(viewModel.Maintenance.CurrentDataDirectory),
                StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(
                this,
                Strings.Get("TheSelectedFolderIsAlreadyTheCurrentDataFolder"),
                Strings.Get("DataStorage"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            this,
            Strings.Format("CopyDataFromNNNtoNNNRestart", viewModel.Maintenance.CurrentDataDirectory, targetDirectory),
            Strings.Get("MoveDataStorage"),
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.OK)
        {
            return;
        }

        try
        {
            await viewModel.Maintenance.MigrateDataDirectoryAsync(targetDirectory);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Data directory migration failed.");
            MessageBox.Show(
                this,
                Strings.Get("DataMigrationFailedN") + exception.Message,
                Strings.Get("DataStorage"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        MessageBox.Show(
            this,
            Strings.Get("DataMigrationIsCompleteClickOKToRestartWitchDrawer"),
            Strings.Get("MigrationComplete"),
            MessageBoxButton.OK,
            MessageBoxImage.Question);

        // 交给 App 统一编排：布置"等本进程退出后再启动"的辅助进程，然后走完整关闭流程。
        if (Application.Current is App app)
        {
            await app.RestartApplicationAsync();
            return;
        }

        _forceClosing = true;
        Application.Current.Shutdown();
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
            CancelHotKeyCapture(Strings.Get("ChangeCanceled"));
            return;
        }

        if (IsModifierKey(key))
        {
            QuickPanelHotKeyStatusText.Text = Strings.Get("PressANonModifierKey");
            return;
        }

        var modifiers = GetHotKeyModifiers(Keyboard.Modifiers);
        if ((modifiers & (HotKeyModifiers.Control | HotKeyModifiers.Alt | HotKeyModifiers.Win)) == 0)
        {
            QuickPanelHotKeyStatusText.Text = Strings.Get("HoldAtLeastCtrlAltOrWin");
            return;
        }

        var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
        var candidate = new QuickPanelHotKey(modifiers, virtualKey);
        if (!candidate.IsValid)
        {
            QuickPanelHotKeyStatusText.Text = Strings.Get("ThisKeyCannotBeUsedForAGlobalShortcut");
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
            CancelHotKeyCapture(Strings.Get("ChangeCanceled"));
        }
    }

    private async Task ApplyQuickPanelHotKeyAsync(QuickPanelHotKey candidate)
    {
        if (_hotKey is null)
        {
            UpdateHotKeyUi(Strings.Get("TheShortcutServiceIsNotReady"));
            return;
        }

        if (candidate == _quickPanelHotKey && _isHotKeyRegistered)
        {
            UpdateHotKeyUi(Strings.Get("ShortcutUnchanged"));
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
            UpdateHotKeyUi(Strings.Get("SavedAndApplied"));
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to save quick panel hotkey.");
            RestorePreviousHotKey(previous, previousWasRegistered);
            UpdateHotKeyUi(Strings.Get("CouldNotSaveRestoredThePreviousShortcut"));
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
            ? Strings.Get("ThisShortcutIsUsedByAnotherAppChooseAnother")
            : Strings.Get("CouldNotRegisterTheShortcutTryAnotherCombination");
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
            _ = Dispatcher.InvokeAsync(async () => await GetQuickPanel().ToggleAsync());
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

        if (!ViewModel.CanImportFiles)
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

        if (!ViewModel.CanImportFiles)
        {
            e.Handled = true;
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
        if (e.ChangedButton != MouseButton.Left) return;
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
            CloseBoxPopups();
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
        if (e.ChangedButton != MouseButton.Left) return;
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

    private void OnOpenBoxDisplaySettings(object sender, RoutedEventArgs e)
    {
        var open = !BoxDisplaySettingsPopup.IsOpen;
        CloseBoxPopups();
        if (open && BoxDisplaySettingsPopup.Child is Border surface)
        {
            var chromeWidth = surface.Padding.Left + surface.Padding.Right + surface.BorderThickness.Left + surface.BorderThickness.Right;
            var chromeHeight = surface.Padding.Top + surface.Padding.Bottom + surface.BorderThickness.Top + surface.BorderThickness.Bottom;
            var expanded = BoxDisplaySettings.MeasureExpandedSize(surface.Width - chromeWidth);
            BoxSettingsPopupPlacement.Configure(BoxDisplaySettingsPopup, BoxDisplaySettingsButton,
                new Size(surface.Width, expanded.Height + chromeHeight));
        }
        BoxDisplaySettingsPopup.IsOpen = open && ViewModel.SelectedBox is not null;
        e.Handled = true;
    }

    private void OnMainItemsViewportSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (MainItemsList is null) return;
        const double rowHeight = 48;
        // Fill the available space with complete rows, so the last filename
        // and its path are never cut in half by the viewport.
        MainItemsList.MaxHeight = e.NewSize.Height >= rowHeight
            ? Math.Floor(e.NewSize.Height / rowHeight) * rowHeight
            : double.PositiveInfinity;
    }

    private void CloseBoxPopups()
    {
        // SelectedItem can raise SelectionChanged before later XAML names
        // have been connected during InitializeComponent.
        if (!IsInitialized) return;
        BoxDisplaySettingsPopup.IsOpen = false;
        DrawerSortPopup.IsOpen = false;
        BoxActionsPopup.IsOpen = false;
        RenameBoxPopup.IsOpen = false;
        DeleteConfirmPopup.IsOpen = false;
        RecallBoxConfirmPopup.IsOpen = false;
    }

    private void OnBoxPopupKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        if (e.OriginalSource is ComboBox { IsDropDownOpen: true }) return;
        CloseBoxPopups();
        e.Handled = true;
    }

    private void OnOpenDrawerSortMenu(object sender, RoutedEventArgs e)
    {
        var open = !DrawerSortPopup.IsOpen;
        CloseBoxPopups();
        DrawerSortPopup.IsOpen = open;
        e.Handled = true;
    }

    private void OnDrawerSortOptionClicked(object sender, RoutedEventArgs e)
    {
        DrawerSortPopup.IsOpen = false;
    }

    private void OnOpenBoxActionsMenu(object sender, RoutedEventArgs e)
    {
        var open = !BoxActionsPopup.IsOpen;
        CloseBoxPopups();
        BoxActionsPopup.IsOpen = open;
        e.Handled = true;
    }

    private void OnBoxActionMenuItemClicked(object sender, RoutedEventArgs e)
    {
        BoxActionsPopup.IsOpen = false;
    }

    private void OnShowDesktopBoxClicked(object sender, RoutedEventArgs e)
    {
        BoxActionsPopup.IsOpen = false;
        if (ViewModel.SelectedBox is { } box)
        {
            ReopenBoxRequested?.Invoke(this, box.Id);
        }
    }

    private void OnRecordLayoutBackupClicked(object sender, RoutedEventArgs e)
    {
        if (!TryGetLayoutBackupSlot(sender, out var slot))
        {
            return;
        }

        if (_recordedLayoutBackupSlots.Contains(slot))
        {
            var result = System.Windows.MessageBox.Show(
                this,
                Strings.Format("BackupSlotAlreadyContainsALayoutNNOverwriteIt", slot),
                Strings.Get("OverwriteLayoutBackup"),
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.OK)
            {
                return;
            }
        }

        RecordLayoutBackupRequested?.Invoke(this, slot);
    }

    private void OnRestoreLayoutBackupClicked(object sender, RoutedEventArgs e)
    {
        if (!TryGetLayoutBackupSlot(sender, out var slot))
        {
            return;
        }

        if (!_recordedLayoutBackupSlots.Contains(slot))
        {
            return;
        }

        var result = System.Windows.MessageBox.Show(
            this,
            Strings.Format("RestoreBackupSlotNNExistingBoxesWillMoveTo", slot),
            Strings.Get("RestoreLayout"),
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);
        if (result == MessageBoxResult.OK)
        {
            RestoreLayoutBackupRequested?.Invoke(this, slot);
        }
    }

    private void OnDeleteLayoutBackupClicked(object sender, RoutedEventArgs e)
    {
        if (!TryGetLayoutBackupSlot(sender, out var slot)
            || !_recordedLayoutBackupSlots.Contains(slot))
        {
            return;
        }

        var result = System.Windows.MessageBox.Show(
            this,
            Strings.Format("DeleteBackupSlotNNTheSavedLayoutInThis", slot),
            Strings.Get("DeleteLayoutBackup"),
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        if (result == MessageBoxResult.OK)
        {
            DeleteLayoutBackupRequested?.Invoke(this, slot);
        }
    }

    internal void SetLayoutBackupSlotState(int slot, bool hasBackup)
    {
        var controls = slot switch
        {
            1 => (LayoutBackupSlot1Status, LayoutBackupSlot1RecordButton, LayoutBackupSlot1RestoreButton, LayoutBackupSlot1DeleteButton),
            2 => (LayoutBackupSlot2Status, LayoutBackupSlot2RecordButton, LayoutBackupSlot2RestoreButton, LayoutBackupSlot2DeleteButton),
            3 => (LayoutBackupSlot3Status, LayoutBackupSlot3RecordButton, LayoutBackupSlot3RestoreButton, LayoutBackupSlot3DeleteButton),
            _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, "Layout backup slot must be from 1 to 3.")
        };
        var presentation = GetLayoutBackupSlotPresentation(hasBackup);

        if (hasBackup)
        {
            _recordedLayoutBackupSlots.Add(slot);
        }
        else
        {
            _recordedLayoutBackupSlots.Remove(slot);
        }

        controls.Item1.Text = presentation.StatusText;
        controls.Item1.FontWeight = hasBackup ? FontWeights.SemiBold : FontWeights.Normal;
        controls.Item1.SetResourceReference(
            TextBlock.ForegroundProperty,
            hasBackup ? "AccentBrush" : "TextMutedBrush");
        controls.Item2.Content = presentation.RecordButtonText;
        controls.Item3.Visibility = presentation.CanRestore ? Visibility.Visible : Visibility.Collapsed;
        controls.Item4.Visibility = presentation.CanDelete ? Visibility.Visible : Visibility.Collapsed;
        System.Windows.Automation.AutomationProperties.SetName(
            controls.Item2,
            hasBackup
                ? Strings.Format("OverwriteTheLayoutInBackupSlot", slot)
                : Strings.Format("SaveTheCurrentLayoutToBackupSlot", slot));
    }

    internal static LayoutBackupSlotPresentation GetLayoutBackupSlotPresentation(bool hasBackup) =>
        hasBackup
            ? new LayoutBackupSlotPresentation(Strings.Get("Saved"), Strings.Get("Overwrite"), true, true)
            : new LayoutBackupSlotPresentation(Strings.Get("Empty"), Strings.Get("Save"), false, false);

    private static bool TryGetLayoutBackupSlot(object sender, out int slot)
    {
        slot = 0;
        return sender is FrameworkElement { Tag: string raw }
            && int.TryParse(raw, out slot)
            && slot is >= 1 and <= 3;
    }

    internal readonly record struct LayoutBackupSlotPresentation(
        string StatusText,
        string RecordButtonText,
        bool CanRestore,
        bool CanDelete);

    private void OnThemeTransparencyInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox input)
        {
            return;
        }

        var binding = input.GetBindingExpression(TextBox.TextProperty);
        binding?.UpdateSource();
        binding?.UpdateTarget();
        e.Handled = true;
    }

    private void OnThemeTransparencyInputLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox input)
        {
            return;
        }

        var binding = input.GetBindingExpression(TextBox.TextProperty);
        binding?.UpdateSource();
        binding?.UpdateTarget();
    }

    private void OnRecallBoxClicked(object sender, RoutedEventArgs e)
    {
        BoxActionsPopup.IsOpen = false;
        RecallBoxConfirmPopup.IsOpen = ViewModel.SelectedBox is not null;
    }

    private void OnCancelRecallBoxClicked(object sender, RoutedEventArgs e)
    {
        RecallBoxConfirmPopup.IsOpen = false;
    }

    private void OnConfirmRecallBoxClicked(object sender, RoutedEventArgs e)
    {
        RecallBoxConfirmPopup.IsOpen = false;
        if (ViewModel.SelectedBox is { } box)
        {
            RecallBoxToScreenCenterRequested?.Invoke(this, box.Id);
        }
    }

    private void OnMainWindowPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source) return;
        if (ReferenceEquals(source, BtnMoreBoxActions) || BtnMoreBoxActions.IsAncestorOf(source)
            || ReferenceEquals(source, BoxDisplaySettingsButton) || BoxDisplaySettingsButton.IsAncestorOf(source)
            || ReferenceEquals(source, DrawerSortMenuButton) || DrawerSortMenuButton.IsAncestorOf(source)) return;
        foreach (var popup in new[] { BoxDisplaySettingsPopup, BoxActionsPopup, DrawerSortPopup })
        {
            if (popup.IsOpen && IsWithinBoxPopup(popup, source)) return;
        }
        BoxActionsPopup.IsOpen = false;
        BoxDisplaySettingsPopup.IsOpen = false;
    }

    internal static bool IsWithinBoxPopup(Popup popup, DependencyObject source)
    {
        // ComboBox dropdowns have a separate visual root. Follow logical and
        // template ownership as well, so an option click can finish selection
        // before any outside-click handling closes the settings panel.
        for (DependencyObject? current = source; current is not null;)
        {
            if (ReferenceEquals(current, popup) || ReferenceEquals(current, popup.Child)) return true;
            current = LogicalTreeHelper.GetParent(current)
                ?? (current as FrameworkElement)?.TemplatedParent
                ?? (current is Visual ? VisualTreeHelper.GetParent(current) : null);
        }
        return false;
    }

    private void OnMainWindowDeactivated(object? sender, EventArgs e)
    {
        CloseBoxPopups();
    }

    private async void OnCreateDrawerBoxClicked(object sender, RoutedEventArgs e)
    {
        CreateBoxPopup.IsOpen = false;
        await ViewModel.CreateDrawerBoxCommand.ExecuteAsync(null);
    }

    private async void OnCreateTodoBoxClicked(object sender, RoutedEventArgs e)
    {
        CreateBoxPopup.IsOpen = false;
        await ViewModel.CreateTodoBoxCommand.ExecuteAsync(null);
    }

    private void OnDeleteBoxClicked(object sender, RoutedEventArgs e)
    {
        BoxActionsPopup.IsOpen = false;
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
        CloseBoxPopups();
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
}
