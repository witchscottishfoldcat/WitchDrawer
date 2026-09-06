using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using WitchDrawer.App.Controls;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Models;

namespace WitchDrawer.App.ViewModels;

public sealed class DrawerItemViewModel : ObservableObject, IVirtualizingCanvasItem
{
    private const int MaxIconLoadAttempts = 4;

    private ImageSource? _iconImage;
    private bool _hasIcon;
    private int _isIconLoadRequested;
    private int _isLoadingIcon;
    private int _requestedIconPixelSize;
    private int _loadedIconPixelSize;
    private int _gridColumn;
    private int _gridRow;
    private double _gridLeft;
    private double _gridTop;
    private bool _isDragSource;
    private double _tempOffsetX;
    private double _tempOffsetY;

    private readonly bool _isPixelated;
    private readonly IAppLogger? _logger;

    public DrawerItemViewModel(
        DrawerItem model,
        string? boxName = null,
        bool isPixelated = false,
        int iconPixelSize = 32,
        IAppLogger? logger = null)
    {
        Model = model;
        BoxName = boxName ?? string.Empty;
        _isPixelated = isPixelated;
        _logger = logger;
        _requestedIconPixelSize = NormalizeIconPixelSize(iconPixelSize);
        _gridColumn = Math.Max(0, model.GridColumn ?? 0);
        _gridRow = Math.Max(0, model.GridRow ?? 0);
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

    public bool IsPixelated => _isPixelated;

    public int GridColumn
    {
        get => _gridColumn;
        private set => SetProperty(ref _gridColumn, value);
    }

    public int GridRow
    {
        get => _gridRow;
        private set => SetProperty(ref _gridRow, value);
    }

    public double GridLeft
    {
        get => _gridLeft;
        private set => SetProperty(ref _gridLeft, value);
    }

    public double GridTop
    {
        get => _gridTop;
        private set => SetProperty(ref _gridTop, value);
    }

    public bool IsDragSource
    {
        get => _isDragSource;
        set => SetProperty(ref _isDragSource, value);
    }

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

    double IVirtualizingCanvasItem.VirtualizationLeft => GridLeft;

    double IVirtualizingCanvasItem.VirtualizationTop => GridTop;

    internal bool IsIconLoadRequested => Volatile.Read(ref _isIconLoadRequested) == 1;

    public void EnsureIconLoaded()
    {
        if (Interlocked.Exchange(ref _isIconLoadRequested, 1) == 0)
        {
            _ = LoadIconAsync();
        }
    }

    public void ReloadIconIfNeeded()
    {
        if (!HasIcon)
        {
            Volatile.Write(ref _isIconLoadRequested, 1);
            _ = LoadIconAsync();
        }
    }

    public void RequestIconSize(int iconPixelSize)
    {
        var normalizedSize = NormalizeIconPixelSize(iconPixelSize);
        var previousSize = Interlocked.Exchange(ref _requestedIconPixelSize, normalizedSize);
        if ((previousSize != normalizedSize || !HasIcon) && IsIconLoadRequested)
        {
            _ = LoadIconAsync();
        }
    }

    public void SetGridPosition(int column, int row, DesktopBoxLayoutSettings layoutSettings)
    {
        GridColumn = column;
        GridRow = row;
        UpdateCanvasPosition(layoutSettings);
    }

    public void SetTempOffset(double offsetX, double offsetY, DesktopBoxLayoutSettings layoutSettings)
    {
        _tempOffsetX = offsetX;
        _tempOffsetY = offsetY;
        UpdateCanvasPosition(layoutSettings);
    }

    public void UpdateCanvasPosition(DesktopBoxLayoutSettings layoutSettings)
    {
        GridLeft = GridColumn * layoutSettings.ItemSlotWidth + _tempOffsetX;
        GridTop = GridRow * layoutSettings.ItemSlotHeight + _tempOffsetY;
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
            Volatile.Write(ref _loadedIconPixelSize, Volatile.Read(ref _requestedIconPixelSize));
            Interlocked.Exchange(ref _isLoadingIcon, 0);
            return;
        }

        var attemptedSize = Volatile.Read(ref _requestedIconPixelSize);
        try
        {
            while (true)
            {
                var requestedSize = Volatile.Read(ref _requestedIconPixelSize);
                attemptedSize = requestedSize;
                var (icon, terminalException) = await LoadIconWithRetriesAsync(
                    path,
                    Model.ItemKind == ItemKind.Directory,
                    requestedSize).ConfigureAwait(false);

                if (requestedSize != Volatile.Read(ref _requestedIconPixelSize))
                {
                    continue;
                }

                await SetIconOnUiThreadAsync(icon);
                Volatile.Write(ref _loadedIconPixelSize, requestedSize);

                if (terminalException is not null)
                {
                    _logger?.Error(
                        terminalException,
                        $"Failed to load icon for drawer item {Id:D} at {requestedSize}px.");
                }

                return;
            }
        }
        catch (Exception exception)
        {
            if (attemptedSize == Volatile.Read(ref _requestedIconPixelSize))
            {
                try
                {
                    await SetIconOnUiThreadAsync(null);
                }
                catch
                {
                    // The WPF dispatcher can be unavailable while the app is shutting down.
                }
            }

            Volatile.Write(ref _loadedIconPixelSize, attemptedSize);
            _logger?.Error(
                exception,
                $"Unexpected icon loading failure for drawer item {Id:D} at {attemptedSize}px.");
        }
        finally
        {
            Interlocked.Exchange(ref _isLoadingIcon, 0);
            if (Volatile.Read(ref _requestedIconPixelSize) != Volatile.Read(ref _loadedIconPixelSize))
            {
                _ = LoadIconAsync();
            }
        }
    }

    private async Task<(ImageSource? Icon, Exception? TerminalException)> LoadIconWithRetriesAsync(
        string path,
        bool isDirectory,
        int requestedSize)
    {
        Exception? terminalException = null;

        for (var attempt = 1; attempt <= MaxIconLoadAttempts; attempt++)
        {
            try
            {
                var icon = await ShellIconProvider
                    .GetIconAsync(path, isDirectory, requestedSize)
                    .ConfigureAwait(false);
                terminalException = null;

                if (icon is not null || attempt == MaxIconLoadAttempts)
                {
                    return (icon, null);
                }
            }
            catch (Exception exception)
            {
                terminalException = exception;
                if (attempt == MaxIconLoadAttempts)
                {
                    break;
                }
            }

            if (requestedSize != Volatile.Read(ref _requestedIconPixelSize))
            {
                break;
            }

            await Task.Delay(150 * attempt).ConfigureAwait(false);
        }

        return (null, terminalException);
    }

    private async Task SetIconOnUiThreadAsync(ImageSource? icon)
    {
        var application = Application.Current;
        if (application is null || application.Dispatcher.CheckAccess())
        {
            IconImage = icon;
            return;
        }

        await application.Dispatcher.InvokeAsync(() => IconImage = icon);
    }

    private static int NormalizeIconPixelSize(int iconPixelSize)
    {
        return Math.Clamp(
            iconPixelSize,
            DpiAwareIconSize.MinimumSourcePixelSize,
            DpiAwareIconSize.MaximumSourcePixelSize);
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
