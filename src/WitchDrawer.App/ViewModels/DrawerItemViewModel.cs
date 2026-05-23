using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.Core.Models;

namespace WitchDrawer.App.ViewModels;

public sealed class DrawerItemViewModel : ObservableObject
{
    private const int MaxIconLoadAttempts = 4;

    private ImageSource? _iconImage;
    private bool _hasIcon;
    private int _isLoadingIcon;

    private readonly bool _isPixelated;

    public DrawerItemViewModel(DrawerItem model, string? boxName = null, bool isPixelated = false)
    {
        Model = model;
        BoxName = boxName ?? string.Empty;
        _isPixelated = isPixelated;
        _ = LoadIconAsync();
    }

    public DrawerItem Model { get; }

    public Guid Id => Model.Id;

    public string DisplayName
    {
        get
        {
            var name = Model.DisplayName;
            if (name.EndsWith(".lnk", System.StringComparison.OrdinalIgnoreCase))
            {
                return name[..^4];
            }
            return name;
        }
    }

    public string KindLabel => Model.ItemKind == ItemKind.Directory ? "文件夹" : "文件";

    public string KindBadge => Model.ItemKind == ItemKind.Directory ? "DIR" : "FILE";

    public string PathLabel => Model.EffectivePath ?? string.Empty;

    public string ShortPathLabel
    {
        get
        {
            var path = PathLabel;
            if (path.Length <= 48)
            {
                return path;
            }

            return "..." + path[^45..];
        }
    }

    public string BoxName { get; }

    public string FallbackIconText => Model.ItemKind == ItemKind.Directory ? "DIR" : GetFallbackExtension();

    public ImageSource? IconImage
    {
        get => _iconImage;
        private set
        {
            if (SetProperty(ref _iconImage, value))
            {
                HasIcon = value is not null;
            }
        }
    }

    public bool HasIcon
    {
        get => _hasIcon;
        private set => SetProperty(ref _hasIcon, value);
    }

    public void ReloadIconIfNeeded()
    {
        if (!HasIcon)
        {
            _ = LoadIconAsync();
        }
    }

    private async Task LoadIconAsync()
    {
        if (Interlocked.Exchange(ref _isLoadingIcon, 1) == 1)
        {
            return;
        }

        var path = PathLabel;
        if (string.IsNullOrWhiteSpace(path))
        {
            Interlocked.Exchange(ref _isLoadingIcon, 0);
            return;
        }

        try
        {
            for (var attempt = 1; attempt <= MaxIconLoadAttempts; attempt++)
            {
                try
                {
                    var isDirectory = Model.ItemKind == ItemKind.Directory;
                    var icon = await ShellIconProvider.GetIconAsync(path, isDirectory, 32).ConfigureAwait(false);

                    if (_isPixelated && icon is BitmapSource bitmapSource)
                    {
                        var scaleX = 16.0 / bitmapSource.PixelWidth;
                        var scaleY = 16.0 / bitmapSource.PixelHeight;
                        var scale = new ScaleTransform(scaleX, scaleY);
                        scale.Freeze();
                        var transformed = new TransformedBitmap(bitmapSource, scale);
                        transformed.Freeze();
                        icon = transformed;
                    }

                    if (icon is not null || attempt == MaxIconLoadAttempts)
                    {
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            IconImage = icon;
                        });
                        return;
                    }
                }
                catch when (attempt < MaxIconLoadAttempts)
                {
                }

                await Task.Delay(150 * attempt).ConfigureAwait(false);
            }
        }
        catch
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                IconImage = null;
            });
        }
        finally
        {
            Interlocked.Exchange(ref _isLoadingIcon, 0);
        }
    }

    private string GetFallbackExtension()
    {
        var extension = Path.GetExtension(DisplayName).TrimStart('.');
        if (string.IsNullOrWhiteSpace(extension))
        {
            return "FILE";
        }

        return extension.Length <= 4 ? extension.ToUpperInvariant() : extension[..4].ToUpperInvariant();
    }
}
