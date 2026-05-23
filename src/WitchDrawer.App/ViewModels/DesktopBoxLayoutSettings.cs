using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;

namespace WitchDrawer.App.ViewModels;

public sealed partial class DesktopBoxLayoutSettings : ObservableObject
{
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
    private string _currentPreset = "5x5";
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
        _currentPreset = preset;
        UpdateDimensions();

        if (_presetChangedCallback is not null)
        {
            await _presetChangedCallback(preset);
        }
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
    }
}
