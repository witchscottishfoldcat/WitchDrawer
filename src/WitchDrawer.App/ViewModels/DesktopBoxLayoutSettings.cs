using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;

namespace WitchDrawer.App.ViewModels;

public sealed partial class DesktopBoxLayoutSettings : ObservableObject
{
    public const string DefaultPreset = "6x6";

    private double _iconSize = 20;
    private double _iconFrameSize = 30;
    private double _itemSpacing = 1;
    private double _itemSlotWidth = 51;
    private double _itemSlotHeight = 44;
    private Thickness _itemPadding = new Thickness(2, 1, 2, 1);
    private double _iconFontSize = 9;
    private TextWrapping _iconTextWrapping = TextWrapping.NoWrap;
    private double _iconTextMaxHeight = 14;
    private CornerRadius _itemCornerRadius = new CornerRadius(8);
    private CornerRadius _iconCornerRadius = new CornerRadius(6);
    private int _columns = 5;
    private string _currentPreset = DefaultPreset;
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
        get => _itemSlotHeight;
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
        set => SetProperty(ref _iconTextMaxHeight, value);
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

    public DesktopBoxLayoutSettings()
    {
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
        if (preset is not ("3x3" or "4x4" or "5x5" or "6x6")
            || string.Equals(_currentPreset, preset, StringComparison.Ordinal))
        {
            return false;
        }

        _currentPreset = preset;
        UpdateDimensions();
        OnPropertyChanged(nameof(CurrentPreset));
        OnPropertyChanged(nameof(CurrentSizeLabel));
        OnPropertyChanged(nameof(IsExtraLargePreset));
        OnPropertyChanged(nameof(IsLargePreset));
        OnPropertyChanged(nameof(IsMediumPreset));
        OnPropertyChanged(nameof(IsSmallPreset));
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
        return true;
    }

    private void UpdateDimensions()
    {
        switch (_currentPreset)
        {
            case "3x3":
                IconSize = 44;
                IconFrameSize = 60;
                ItemSpacing = 2;
                Columns = 3;
                ItemSlotWidth = 74;
                ItemSlotHeight = 74;
                ItemPadding = new Thickness(4);
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
                ItemPadding = new Thickness(3);
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
                ItemPadding = new Thickness(2);
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
