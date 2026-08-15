using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using WitchDrawer.App.Messages;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;

namespace WitchDrawer.App.ViewModels;

/// <summary>
/// 「收纳盒配置」面板中盒子尺寸控制行的 ViewModel：跟随主窗口当前选中的普通收纳盒，
/// 在自适应与固定 m×n 格之间切换。固定尺寸的下限是内容实际撑开的格子范围，
/// 由桌面盒窗口通过 <see cref="BoxGridExtentChangedMessage"/> 实时上报。
/// </summary>
public sealed partial class BoxSizeSettingsViewModel : ObservableObject
{
    private readonly DrawerService _drawerService;
    private readonly IAppLogger _logger;
    private readonly Dictionary<Guid, (int Columns, int Rows)> _extents = [];
    private BoxViewModel? _selectedBox;
    private bool _isFixedMode;
    private int _fixedColumns = BoxSizeModeState.Adaptive.Columns;
    private int _fixedRows = BoxSizeModeState.Adaptive.Rows;

    // 目标盒每次切换时递增；加载完成时把 _appliedStateVersion 对齐到该版本。
    // 用途：1) 丢弃乱序完成的过期加载结果；2) 状态未就位前拒绝写入，
    // 防止把上一个盒子的固定尺寸套用到新选中的盒子上。
    private int _stateVersion;
    private int _appliedStateVersion = -1;

    public BoxSizeSettingsViewModel(DrawerService drawerService, IAppLogger logger)
    {
        _drawerService = drawerService;
        _logger = logger;

        WeakReferenceMessenger.Default.Register<BoxSizeSettingsViewModel, BoxGridExtentChangedMessage>(
            this,
            static (recipient, message) =>
                recipient.ApplyGridExtent(message.BoxId, message.Columns, message.Rows));
    }

    public BoxViewModel? SelectedBox => _selectedBox;

    public bool HasSelection => _selectedBox is not null;

    /// <summary>
    /// 跟随主窗口当前选中的收纳盒；不支持固定尺寸的盒型（映射/待办/抽屉）
    /// 或未选中时置空，设置行随之隐藏。
    /// </summary>
    public void SetTargetBox(BoxViewModel? box)
    {
        var target = box is { SupportsFixedSize: true } ? box : null;
        if (!SetProperty(ref _selectedBox, target, nameof(SelectedBox)))
        {
            return;
        }

        var version = ++_stateVersion;

        // 立即重置为自适应默认值：异步加载完成前面板绝不残留上一个盒子的状态，
        // 既不误显示，也不会在加载间隙把旧盒子的尺寸写入新盒子。
        IsFixedMode = false;
        FixedColumns = BoxSizeModeState.Adaptive.Columns;
        FixedRows = BoxSizeModeState.Adaptive.Rows;

        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedExtent));
        OnPropertyChanged(nameof(ExtentHint));
        OnPropertyChanged(nameof(ModeSummary));
        NotifyBoundsChanged();
        _ = LoadStateAsync(target, version);
    }

    public bool IsFixedMode
    {
        get => _isFixedMode;
        private set
        {
            if (SetProperty(ref _isFixedMode, value))
            {
                OnPropertyChanged(nameof(IsAdaptiveMode));
                NotifyBoundsChanged();
            }
        }
    }

    public bool IsAdaptiveMode => !_isFixedMode;

    public int FixedColumns
    {
        get => _fixedColumns;
        private set
        {
            if (SetProperty(ref _fixedColumns, value))
            {
                NotifyBoundsChanged();
            }
        }
    }

    public int FixedRows
    {
        get => _fixedRows;
        private set
        {
            if (SetProperty(ref _fixedRows, value))
            {
                NotifyBoundsChanged();
            }
        }
    }

    public (int Columns, int Rows) SelectedExtent =>
        _selectedBox is null ? (1, 1) : GetExtent(_selectedBox.Id);

    public string ExtentHint
    {
        get
        {
            var (columns, rows) = SelectedExtent;
            return $"当前内容占用 {columns} × {rows}，固定尺寸不能小于该范围";
        }
    }

    public string ModeSummary =>
        !HasSelection
            ? string.Empty
            : IsFixedMode
                ? $"固定 {FixedColumns} × {FixedRows} 格，达到容量后停止导入"
                : "窗口随内容自动撑开";

    public bool CanDecreaseColumns =>
        IsFixedMode && FixedColumns > Math.Max(SelectedExtent.Columns, BoxSizeModeState.MinCells);

    public bool CanIncreaseColumns => IsFixedMode && FixedColumns < BoxSizeModeState.MaxColumns;

    public bool CanDecreaseRows =>
        IsFixedMode && FixedRows > Math.Max(SelectedExtent.Rows, BoxSizeModeState.MinCells);

    public bool CanIncreaseRows => IsFixedMode && FixedRows < BoxSizeModeState.MaxRows;

    [RelayCommand]
    private async Task UseAdaptiveModeAsync()
    {
        if (!IsFixedMode)
        {
            return;
        }

        await ApplyStateAsync(BoxSizeModeState.Adaptive);
    }

    [RelayCommand]
    private async Task UseFixedModeAsync()
    {
        var (extentColumns, extentRows) = SelectedExtent;
        var state = new BoxSizeModeState(
            true,
            Math.Max(FixedColumns, extentColumns),
            Math.Max(FixedRows, extentRows));
        await ApplyStateAsync(state);
    }

    [RelayCommand(CanExecute = nameof(CanDecreaseColumns))]
    private async Task DecreaseColumnsAsync() => await ResizeFixedAsync(FixedColumns - 1, FixedRows);

    [RelayCommand(CanExecute = nameof(CanIncreaseColumns))]
    private async Task IncreaseColumnsAsync() => await ResizeFixedAsync(FixedColumns + 1, FixedRows);

    [RelayCommand(CanExecute = nameof(CanDecreaseRows))]
    private async Task DecreaseRowsAsync() => await ResizeFixedAsync(FixedColumns, FixedRows - 1);

    [RelayCommand(CanExecute = nameof(CanIncreaseRows))]
    private async Task IncreaseRowsAsync() => await ResizeFixedAsync(FixedColumns, FixedRows + 1);

    private async Task ResizeFixedAsync(int columns, int rows)
    {
        var state = new BoxSizeModeState(
            true,
            BoxSizeModeState.ClampColumns(columns),
            BoxSizeModeState.ClampRows(rows));
        await ApplyStateAsync(state);
    }

    private async Task ApplyStateAsync(BoxSizeModeState state)
    {
        // 捕获调用时刻的目标盒：await 期间选中可能已切换，持久化与广播必须落到原目标。
        var box = _selectedBox;
        if (box is null
            || _appliedStateVersion != _stateVersion
            || !state.FitsExtent(SelectedExtent.Columns, SelectedExtent.Rows))
        {
            return;
        }

        IsFixedMode = state.IsFixed;
        FixedColumns = state.Columns;
        FixedRows = state.Rows;

        OnPropertyChanged(nameof(ModeSummary));
        try
        {
            await _drawerService.SetSettingAsync(
                BoxViewModel.GetSizeModeSettingKey(box.Id),
                state.Serialize());
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to persist box size mode state.");
            return;
        }

        WeakReferenceMessenger.Default.Send(
            new BoxSizeModeChangedMessage(box.Id, state.IsFixed, state.Columns, state.Rows));
    }

    private async Task LoadStateAsync(BoxViewModel? box, int version)
    {
        if (box is null)
        {
            return;
        }

        string? saved;
        try
        {
            saved = await _drawerService.GetSettingAsync(BoxViewModel.GetSizeModeSettingKey(box.Id));
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to load box size mode state.");
            return;
        }

        // 加载期间目标已切换（或发起了更新的加载）：丢弃过期结果，防止乱序覆盖。
        if (version != _stateVersion || _selectedBox?.Id != box.Id)
        {
            return;
        }

        var state = BoxSizeModeState.Parse(saved);

        IsFixedMode = state.IsFixed;
        FixedColumns = state.Columns;
        FixedRows = state.Rows;
        _appliedStateVersion = version;

        OnPropertyChanged(nameof(ModeSummary));
        OnPropertyChanged(nameof(ExtentHint));
    }

    private void ApplyGridExtent(Guid boxId, int columns, int rows)
    {
        _extents[boxId] = (columns, rows);
        if (_selectedBox?.Id == boxId)
        {
            OnPropertyChanged(nameof(ExtentHint));
            OnPropertyChanged(nameof(SelectedExtent));
            NotifyBoundsChanged();
        }
    }

    private (int Columns, int Rows) GetExtent(Guid boxId) =>
        _extents.TryGetValue(boxId, out var extent) ? extent : (1, 1);

    private void NotifyBoundsChanged()
    {
        OnPropertyChanged(nameof(CanDecreaseColumns));
        OnPropertyChanged(nameof(CanIncreaseColumns));
        OnPropertyChanged(nameof(CanDecreaseRows));
        OnPropertyChanged(nameof(CanIncreaseRows));
        DecreaseColumnsCommand.NotifyCanExecuteChanged();
        IncreaseColumnsCommand.NotifyCanExecuteChanged();
        DecreaseRowsCommand.NotifyCanExecuteChanged();
        IncreaseRowsCommand.NotifyCanExecuteChanged();
    }
}
