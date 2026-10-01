using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WitchDrawer.Core.Models;

namespace WitchDrawer.App.ViewModels;

public sealed class ThemeColorOptionViewModel : ObservableObject
{
    private readonly Action<string?> _apply;
    private string _hex = "#FFFFFF";
    private string _error = "";
    private bool _isRefreshing;
    private bool _isCustom;
    private Brush _previewBrush = Brushes.White;

    public ThemeColorOptionViewModel(string key, string label, Action<string?> apply)
    {
        Key = key;
        Label = label;
        _apply = apply;
        ResetCommand = new RelayCommand(() => { Error = ""; _apply(null); });
        ChooseColorCommand = new RelayCommand<string>(value => { if (value is not null) Hex = value; });
    }

    public string Key { get; }
    public string Label { get; }
    public static IReadOnlyList<string> Swatches { get; } =
        ["#FFFFFF", "#2C2C2E", "#0071E3", "#34C759", "#FF9500", "#AF52DE"];
    public IRelayCommand ResetCommand { get; }
    public IRelayCommand<string> ChooseColorCommand { get; }
    public bool IsCustom { get => _isCustom; private set => SetProperty(ref _isCustom, value); }
    public string Error { get => _error; private set => SetProperty(ref _error, value); }
    public string Hex
    {
        get => _hex;
        set
        {
            if (_isRefreshing) { SetProperty(ref _hex, value); return; }
            if (ThemeCustomization.NormalizeColor(value) is not { } normalized)
            {
                Error = "请输入六位 HEX 颜色，如 #0071E3";
                OnPropertyChanged();
                return;
            }
            Error = "";
            if (normalized == _hex) return;
            _apply(normalized);
        }
    }

    public Brush PreviewBrush => _previewBrush;

    internal void Refresh(Color color, bool isCustom)
    {
        var hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        if (hex == _hex && isCustom == IsCustom) return;
        var colorChanged = hex != _hex;
        _isRefreshing = true;
        try
        {
            Hex = hex;
            IsCustom = isCustom;
            Error = "";
            if (colorChanged)
            {
                var brush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
                brush.Freeze();
                _previewBrush = brush;
                OnPropertyChanged(nameof(PreviewBrush));
            }
        }
        finally { _isRefreshing = false; }
    }
}
