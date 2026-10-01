using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using WitchDrawer.Core.Models;

namespace WitchDrawer.App.Infrastructure;

/// <summary>Shared corner bindings avoid invalidating a window's resource tree while dragging.</summary>
public sealed class DesktopBoxCornerSettings : ObservableObject
{
    private double? _boxRadius;
    private double _iconScale = 1;

    public CornerRadius BoxWindowRadius => BoxRadius(20);
    public CornerRadius BoxPixelWindowRadius => BoxRadius(14);
    public CornerRadius BoxCollapsedWindowRadius => BoxRadius(24);
    public CornerRadius BoxSurfaceRadius => BoxRadius(18);
    public CornerRadius BoxPixelSurfaceRadius => BoxRadius(12);
    public CornerRadius BoxCollapsedSurfaceRadius => BoxRadius(22);
    public CornerRadius BoxPopupRadius => BoxRadius(24);
    public CornerRadius BoxPopupSurfaceRadius => BoxRadius(23, 1);
    public CornerRadius BoxPopupInnerRadius => BoxRadius(22, 2);
    public CornerRadius PixelItemRadius => new(6 * _iconScale);
    public CornerRadius PixelIconRadius => new(4 * _iconScale);
    public CornerRadius MappingItemRadius => new(4 * _iconScale);

    internal void Apply(ThemeCustomization customization)
    {
        if (_boxRadius != customization.BoxCornerRadius)
        {
            _boxRadius = customization.BoxCornerRadius;
            OnPropertyChanged(nameof(BoxWindowRadius));
            OnPropertyChanged(nameof(BoxPixelWindowRadius));
            OnPropertyChanged(nameof(BoxCollapsedWindowRadius));
            OnPropertyChanged(nameof(BoxSurfaceRadius));
            OnPropertyChanged(nameof(BoxPixelSurfaceRadius));
            OnPropertyChanged(nameof(BoxCollapsedSurfaceRadius));
            OnPropertyChanged(nameof(BoxPopupRadius));
            OnPropertyChanged(nameof(BoxPopupSurfaceRadius));
            OnPropertyChanged(nameof(BoxPopupInnerRadius));
        }

        var scale = customization.IconCornerScale ?? 1;
        if (_iconScale == scale) return;
        _iconScale = scale;
        OnPropertyChanged(nameof(PixelItemRadius));
        OnPropertyChanged(nameof(PixelIconRadius));
        OnPropertyChanged(nameof(MappingItemRadius));
    }

    private CornerRadius BoxRadius(double baseline, double inset = 0) =>
        new(_boxRadius is { } radius ? Math.Max(0, radius - inset) : baseline);
}
