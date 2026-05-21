using System.IO;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.Core.Models;

namespace WitchDrawer.App.ViewModels;

public sealed class DrawerItemViewModel : ObservableObject
{
    private ImageSource? _iconImage;
    private bool _hasIcon;

    public DrawerItemViewModel(DrawerItem model, string? boxName = null)
    {
        Model = model;
        BoxName = boxName ?? string.Empty;
        _ = LoadIconAsync();
    }

    public DrawerItem Model { get; }

    public Guid Id => Model.Id;

    public string DisplayName => Model.DisplayName;

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

    private async Task LoadIconAsync()
    {
        var path = PathLabel;
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var isDirectory = Model.ItemKind == ItemKind.Directory;
            var icon = await Task.Run(() => ShellIconProvider.GetIcon(path, isDirectory, 32)).ConfigureAwait(false);

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                IconImage = icon;
            });
        }
        catch
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                IconImage = null;
            });
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
