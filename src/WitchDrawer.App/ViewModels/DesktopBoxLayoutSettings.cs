using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;

namespace WitchDrawer.App.ViewModels;

public sealed partial class DesktopBoxLayoutSettings : ObservableObject
{
    public const string DefaultPreset = "6x6";
    public const string DefaultDrawerPreset = "4x4";
    public const double DrawerSurfaceInset = 10;

    /// <summary>
    /// 图标项容器（DesktopBoxWindow.xaml 中 ListBoxItem 的 Root Border）的描边厚度。
    /// 图标框内容区不变式依赖该值，修改 XAML 描边厚度时必须同步。
    /// </summary>
    public const double ItemBorderThickness = 1.2;

    private const int MaximumGridViewportColumns = 12;
    private const int MaximumGridViewportRows = 8;

    private double _iconSize = 20;
    private double _iconFrameSize = 30;
    private double _itemSpacing = 1;
    private double _itemSlotWidth = 51;
    private double _itemSlotHeight = 44;
    private Thickness _itemPadding = new Thickness(2, 1, 2, 1);
    private double _iconFontSize = 9;
    private TextWrapping _iconTextWrapping = TextWrapping.NoWrap;
    private double _iconTextMaxHeight = 14;
    private bool _isFileNameVisible = true;
    private CornerRadius _itemCornerRadius = new CornerRadius(8);
    private CornerRadius _iconCornerRadius = new CornerRadius(6);
    private int _columns = 5;
    private string _currentPreset = DefaultPreset;
    private readonly bool _isDrawerMode;
    private Func<string, Task>? _presetChangedCallback;

    public double IconSize
    {
        get => _iconSize;
        set => SetProperty(ref _iconSize, value);
    }

    public double IconFrameSize
    {
        get => _iconFrameSize;
        set => SetProperty(ref _iconFrameSize, value);
    }

    public double ItemSpacing
    {
        get => _itemSpacing;
        set
        {
            if (SetProperty(ref _itemSpacing, value))
            {
                OnPropertyChanged(nameof(ItemMargin));
            }
        }
    }

    public double ItemSlotWidth
    {
        get => _itemSlotWidth;
        set => SetProperty(ref _itemSlotWidth, value);
    }

    public double ItemSlotHeight
    {
        get => _itemSlotHeight + (IsFileNameVisible ? IconTextMaxHeight : 0);
        set => SetProperty(ref _itemSlotHeight, value);
    }

    public Thickness ItemPadding
    {
        get => _itemPadding;
        set => SetProperty(ref _itemPadding, value);
    }

    public double IconFontSize
    {
        get => _iconFontSize;
        set => SetProperty(ref _iconFontSize, value);
    }

    public TextWrapping IconTextWrapping
    {
        get => _iconTextWrapping;
        set => SetProperty(ref _iconTextWrapping, value);
    }

    public double IconTextMaxHeight
    {
        get => _iconTextMaxHeight;
        set
        {
            if (SetProperty(ref _iconTextMaxHeight, value) && IsFileNameVisible)
            {
                OnPropertyChanged(nameof(ItemSlotHeight));
            }
        }
    }

    public bool IsFileNameVisible
    {
        get => _isFileNameVisible;
        set
        {
            if (SetProperty(ref _isFileNameVisible, value))
            {
                OnPropertyChanged(nameof(ItemSlotHeight));
            }
        }
    }

    public CornerRadius ItemCornerRadius
    {
        get => _itemCornerRadius;
        set => SetProperty(ref _itemCornerRadius, value);
    }

    public CornerRadius IconCornerRadius
    {
        get => _iconCornerRadius;
        set => SetProperty(ref _iconCornerRadius, value);
    }

    public int Columns
    {
        get => _columns;
        set => SetProperty(ref _columns, value);
    }

    public double FallbackIconFontSize => Math.Max(9, Math.Round(IconSize * 0.32, 1));

    public Thickness ItemMargin => new(ItemSpacing);

    public string CurrentPreset => _currentPreset;

    public bool IsDrawerMode => _isDrawerMode;

    public string CurrentSizeLabel => _currentPreset switch
    {
        "3x3" => "超",
        "4x4" => "大",
        "5x5" => "中",
        _ => "小"
    };

    public bool IsExtraLargePreset => _currentPreset == "3x3";

    public bool IsLargePreset => _currentPreset == "4x4";

    public bool IsMediumPreset => _currentPreset == "5x5";

    public bool IsSmallPreset => _currentPreset == DefaultPreset;

    public bool IsCompactPreset => _currentPreset == "6x6";

    public double DrawerCoverCellSize => ItemSlotWidth;

    public double DrawerPrimaryIconFrameSize => IconFrameSize;

    public double DrawerPrimaryIconSize => IconSize;

    public double DrawerPreviewIconFrameSize => _currentPreset switch
    {
        "3x3" => 26,
        "4x4" => 19,
        "5x5" => 15,
        _ => 12
    };

    public double DrawerPreviewIconSize => _currentPreset switch
    {
        "3x3" => 22,
        "4x4" => 16,
        "5x5" => 12,
        _ => 10
    };

    public double DrawerPreviewGap => _currentPreset switch
    {
        "3x3" => 1,
        "4x4" => 1,
        "5x5" => 0.75,
        _ => 0.5
    };

    public double DrawerSurfacePadding => DrawerSurfaceInset;

    /// <summary>
    /// 视口除网格外需要预留的 chrome 宽度上限余量：在 <see cref="GridViewportFixedChromeInset"/>
    /// 的基础上再加少量 slack。MaxWidth/MaxHeight 必须包含这部分，否则满 12 列/8 行的盒子会被上限裁掉一圈。
    /// </summary>
    public const double GridViewportChromeInset = 10;

    /// <summary>
    /// 固定模式视口的精确 chrome 尺寸，与 DesktopBoxWindow.xaml 中 IconList 的
    /// Padding (2px × 2) 加 ListBox Border (1px × 2) 一一对应：改动 XAML 中任一数值时
    /// 必须同步更新此处，否则固定模式最右/最下列图标会被裁掉（与自适应模式失配）。
    /// </summary>
    public const double GridViewportFixedChromeInset = 6;

    public double GridViewportMaxWidth => (ItemSlotWidth * MaximumGridViewportColumns) + GridViewportChromeInset;

    public double GridViewportMaxHeight => (ItemSlotHeight * MaximumGridViewportRows) + GridViewportChromeInset;

    public Thickness DrawerHoverMargin => _currentPreset switch
    {
        "3x3" => new Thickness(5),
        "4x4" => new Thickness(2.5),
        "5x5" => new Thickness(2),
        _ => new Thickness(1.5)
    };

    // Mapping list mode uses the small preset as its visual baseline. Each larger
    // step grows by 15% so switching sizes does not make the horizontal box jump.
    private double MappingListScale => _currentPreset switch
    {
        "3x3" => 1.45,
        "4x4" => 1.30,
        "5x5" => 1.15,
        _ => 1.0
    };

    public double MappingListWidth => Math.Round(220 * MappingListScale, 1);

    public double MappingListRowHeight => Math.Round(24 * MappingListScale, 1);

    public double MappingListMinHeight => Math.Round(58 * MappingListScale, 1);

    public double MappingListMaxHeight => Math.Round(294 * MappingListScale, 1);

    public double MappingListIconSize => Math.Round(14 * MappingListScale, 1);

    public double MappingListIconFrameSize => MappingListIconSize + 2;

    public double MappingListIconColumnWidth => MappingListIconFrameSize + 4;

    public double MappingListFontSize => _currentPreset switch
    {
        "3x3" => 15.6,
        "4x4" => 14.6,
        "5x5" => 13.5,
        _ => 12.5
    };

    public double MappingListTitleFontSize => _currentPreset switch
    {
        "3x3" => 14.5,
        "4x4" => 14,
        "5x5" => 13.5,
        _ => 13
    };

    public double MappingListFallbackFontSize => Math.Max(7, Math.Round(MappingListIconSize * 0.38, 1));

    public Thickness MappingListItemPadding => _currentPreset switch
    {
        "3x3" => new Thickness(2.5, 2, 3.5, 2),
        "4x4" => new Thickness(2, 1.5, 3, 1.5),
        "5x5" => new Thickness(1.5, 1, 2.5, 1),
        _ => new Thickness(1, 0.5, 2, 0.5)
    };

    public Thickness MappingListPadding => _currentPreset switch
    {
        "3x3" => new Thickness(7, 3.5, 7, 3.5),
        "4x4" => new Thickness(6, 3, 6, 3),
        "5x5" => new Thickness(5, 2.5, 5, 2.5),
        _ => new Thickness(4, 2, 4, 2)
    };

    public Thickness MappingListMargin => _currentPreset switch
    {
        "3x3" => new Thickness(0, 1.5, 0, 7),
        "4x4" => new Thickness(0, 1, 0, 6),
        "5x5" => new Thickness(0, 0.5, 0, 5),
        _ => new Thickness(0, 0, 0, 4)
    };

    public Thickness MappingListItemMargin => _currentPreset switch
    {
        "3x3" => new Thickness(0, 1.25, 0, 1.25),
        "4x4" => new Thickness(0, 1, 0, 1),
        "5x5" => new Thickness(0, 0.75, 0, 0.75),
        _ => new Thickness(0, 0.5, 0, 0.5)
    };

    public Thickness MappingListWindowMargin => new(Math.Round(4 * MappingListScale, 1));

    public DesktopBoxLayoutSettings(bool isDrawerMode = false)
    {
        _isDrawerMode = isDrawerMode;
        _currentPreset = isDrawerMode ? DefaultDrawerPreset : DefaultPreset;
        UpdateDimensions();
    }

    public void SetPresetChangedCallback(Func<string, Task> callback)
    {
        _presetChangedCallback = callback;
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private async Task ApplyPresetAsync(string preset)
    {
        if (!ApplyPresetCore(preset))
        {
            return;
        }

        if (_presetChangedCallback is not null)
        {
            await _presetChangedCallback(preset);
        }
    }

    public void ApplyPresetWithoutCallback(string? preset)
    {
        ApplyPresetCore(preset);
    }

    private bool ApplyPresetCore(string? preset)
    {
        var isValidPreset = preset is "3x3" or "4x4" or "5x5" or "6x6";
        if (!isValidPreset
            || string.Equals(_currentPreset, preset, StringComparison.Ordinal))
        {
            return false;
        }

        _currentPreset = preset!;
        UpdateDimensions();
        OnPropertyChanged(nameof(CurrentPreset));
        OnPropertyChanged(nameof(CurrentSizeLabel));
        OnPropertyChanged(nameof(IsExtraLargePreset));
        OnPropertyChanged(nameof(IsLargePreset));
        OnPropertyChanged(nameof(IsMediumPreset));
        OnPropertyChanged(nameof(IsSmallPreset));
        OnPropertyChanged(nameof(IsCompactPreset));
        OnPropertyChanged(nameof(DrawerCoverCellSize));
        OnPropertyChanged(nameof(DrawerPrimaryIconFrameSize));
        OnPropertyChanged(nameof(DrawerPrimaryIconSize));
        OnPropertyChanged(nameof(DrawerPreviewIconFrameSize));
        OnPropertyChanged(nameof(DrawerPreviewIconSize));
        OnPropertyChanged(nameof(DrawerPreviewGap));
        OnPropertyChanged(nameof(DrawerSurfacePadding));
        OnPropertyChanged(nameof(GridViewportMaxWidth));
        OnPropertyChanged(nameof(GridViewportMaxHeight));
        OnPropertyChanged(nameof(DrawerHoverMargin));
        OnPropertyChanged(nameof(MappingListWidth));
        OnPropertyChanged(nameof(MappingListRowHeight));
        OnPropertyChanged(nameof(MappingListMinHeight));
        OnPropertyChanged(nameof(MappingListMaxHeight));
        OnPropertyChanged(nameof(MappingListIconSize));
        OnPropertyChanged(nameof(MappingListIconFrameSize));
        OnPropertyChanged(nameof(MappingListIconColumnWidth));
        OnPropertyChanged(nameof(MappingListFontSize));
        OnPropertyChanged(nameof(MappingListTitleFontSize));
        OnPropertyChanged(nameof(MappingListFallbackFontSize));
        OnPropertyChanged(nameof(MappingListItemPadding));
        OnPropertyChanged(nameof(MappingListPadding));
        OnPropertyChanged(nameof(MappingListMargin));
        OnPropertyChanged(nameof(MappingListItemMargin));
        OnPropertyChanged(nameof(MappingListWindowMargin));
        return true;
    }

    private void UpdateDimensions()
    {
        // 不变式：IconFrameSize 必须 ≤ 项内容区 = ItemSlot - 2×ItemMargin - 2×(项边框 1.2 + ItemPadding)。
        // 否则图标框溢出内容区，其 1px 描边会在右/下（水平居中+垂直顶对齐的溢出方向）被裁掉，
        // 表现为"图标框缺边"。调整本表时由 IconFrame_FitsInsideItemContentArea 测试把关。
        switch (_currentPreset)
        {
            case "3x3":
                IconSize = 44;
                IconFrameSize = 60;
                ItemSpacing = 2;
                Columns = 3;
                ItemSlotWidth = 74;
                ItemSlotHeight = 74;
                ItemPadding = new Thickness(3);
                IconFontSize = 11;
                IconTextWrapping = TextWrapping.Wrap;
                IconTextMaxHeight = 32;
                ItemCornerRadius = new CornerRadius(14);
                IconCornerRadius = new CornerRadius(12);
                break;
            case "4x4":
                IconSize = 34;
                IconFrameSize = 46;
                ItemSpacing = 1.5;
                Columns = 4;
                ItemSlotWidth = 55;
                ItemSlotHeight = 55;
                ItemPadding = new Thickness(1);
                IconFontSize = 10;
                IconTextWrapping = TextWrapping.NoWrap;
                IconTextMaxHeight = 16;
                ItemCornerRadius = new CornerRadius(12);
                IconCornerRadius = new CornerRadius(10);
                break;
            case "5x5":
                IconSize = 26;
                IconFrameSize = 36;
                ItemSpacing = 1;
                Columns = 5;
                ItemSlotWidth = 44;
                ItemSlotHeight = 44;
                ItemPadding = new Thickness(1);
                IconFontSize = 9;
                IconTextWrapping = TextWrapping.NoWrap;
                IconTextMaxHeight = 14;
                ItemCornerRadius = new CornerRadius(10);
                IconCornerRadius = new CornerRadius(8);
                break;
            case "6x6":
                IconSize = 20;
                IconFrameSize = 30;
                ItemSpacing = 0.5;
                Columns = 6;
                ItemSlotWidth = 37;
                ItemSlotHeight = 37;
                ItemPadding = new Thickness(1);
                IconFontSize = 8;
                IconTextWrapping = TextWrapping.NoWrap;
                IconTextMaxHeight = 12;
                ItemCornerRadius = new CornerRadius(8);
                IconCornerRadius = new CornerRadius(6);
                break;
        }
        OnPropertyChanged(nameof(FallbackIconFontSize));
        OnPropertyChanged(nameof(IconFrameSize));
        OnPropertyChanged(nameof(ItemMargin));
        OnPropertyChanged(nameof(GridViewportMaxWidth));
        OnPropertyChanged(nameof(GridViewportMaxHeight));
        OnPropertyChanged(nameof(IsCompactPreset));
        OnPropertyChanged(nameof(MappingListWidth));
        OnPropertyChanged(nameof(MappingListRowHeight));
        OnPropertyChanged(nameof(MappingListMinHeight));
        OnPropertyChanged(nameof(MappingListMaxHeight));
        OnPropertyChanged(nameof(MappingListIconSize));
        OnPropertyChanged(nameof(MappingListIconFrameSize));
        OnPropertyChanged(nameof(MappingListIconColumnWidth));
        OnPropertyChanged(nameof(MappingListFontSize));
        OnPropertyChanged(nameof(MappingListTitleFontSize));
        OnPropertyChanged(nameof(MappingListFallbackFontSize));
        OnPropertyChanged(nameof(MappingListItemPadding));
        OnPropertyChanged(nameof(MappingListPadding));
        OnPropertyChanged(nameof(MappingListMargin));
        OnPropertyChanged(nameof(MappingListItemMargin));
        OnPropertyChanged(nameof(MappingListWindowMargin));
    }
}
