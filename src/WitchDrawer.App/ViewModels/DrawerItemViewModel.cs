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
    private int _requestedIconPixelSize;
    private int _loadedIconPixelSize;
    private int _loadingIconPixelSize;
    private int _iconConsumers;
    private bool _hasPermanentIconDemand;
    private readonly object _iconSync = new();
    private CancellationTokenSource? _iconLoadCancellation;
    private readonly Func<string?, bool, int, CancellationToken, Task<ImageSource?>> _loadIcon;
    private int _gridColumn;
    private int _gridRow;
    private double _gridLeft;
    private double _gridTop;
    private bool _isDragSource;
    private double _tempOffsetX;
    private double _tempOffsetY;

    private bool _isPixelated;
    private string _boxName;
    private readonly IAppLogger? _logger;

    public DrawerItemViewModel(
        DrawerItem model,
        string? boxName = null,
        bool isPixelated = false,
        int iconPixelSize = 32,
        IAppLogger? logger = null)
        : this(model, boxName, isPixelated, iconPixelSize, logger, ShellIconProvider.GetIconAsync)
    {
    }

    internal DrawerItemViewModel(
        DrawerItem model,
        string? boxName,
        bool isPixelated,
        int iconPixelSize,
        IAppLogger? logger,
        Func<string?, bool, int, CancellationToken, Task<ImageSource?>> loadIcon)
    {
        Model = model;
        _boxName = boxName ?? string.Empty;
        _isPixelated = isPixelated;
        _logger = logger;
        _loadIcon = loadIcon;
        _requestedIconPixelSize = NormalizeIconPixelSize(iconPixelSize);
        _gridColumn = Math.Max(0, model.GridColumn ?? 0);
        _gridRow = Math.Max(0, model.GridRow ?? 0);
    }

    public DrawerItem Model { get; private set; }

    internal bool TryUpdateModel(DrawerItem model)
    {
        // Retain containers and icon demand for ordering/position changes. A different
        // file path or presentation needs a new instance, including a fresh icon load.
        if (Model.Id != model.Id || Model.BoxId != model.BoxId
            || Model.DisplayName != model.DisplayName || Model.ItemKind != model.ItemKind
            || Model.EffectivePath != model.EffectivePath)
        {
            return false;
        }

        if (Model != model)
        {
            Model = model;
            OnPropertyChanged(nameof(Model));
        }
        return true;
    }

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

    /// <summary>
    /// 供鼠标悬停提示展示的名称。精简模式显示文件名（快捷方式自动去掉 .lnk），完整模式显示完整路径。
    /// 仅用于展示，不参与打开/导出等需要真实路径的逻辑。
    /// </summary>
    public string HoverDisplayText =>
        Infrastructure.DesktopHoverDisplayMode.IsCompact ? CompactFileName : PathLabel;

    private string CompactFileName
    {
        get
        {
            // 目录路径常以分隔符结尾，GetFileName 会返回空串；先裁掉再取。
            var trimmed = PathLabel.TrimEnd('\\', '/');
            var name = System.IO.Path.GetFileName(trimmed);
            if (string.IsNullOrEmpty(name))
            {
                // 根路径等极端情况取不出名字时回退为完整路径，避免空提示。
                return PathLabel;
            }

            if (name.Length > 4 && name.EndsWith(".lnk", System.StringComparison.OrdinalIgnoreCase))
            {
                return name[..^4];
            }
            return name;
        }
    }

    public void RaiseHoverDisplayTextChanged() => OnPropertyChanged(nameof(HoverDisplayText));

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

    public string BoxName => _boxName;

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
        lock (_iconSync)
        {
            if (!_hasPermanentIconDemand)
            {
                _hasPermanentIconDemand = true;
                _iconConsumers++;
                Volatile.Write(ref _isIconLoadRequested, 1);
            }
        }

        StartIconLoadIfNeeded();
    }

    internal IDisposable AcquireIconDemand()
    {
        lock (_iconSync)
        {
            _iconConsumers++;
            Volatile.Write(ref _isIconLoadRequested, 1);
        }

        StartIconLoadIfNeeded();
        return new IconDemand(this);
    }

    private void ReleaseIconDemand()
    {
        lock (_iconSync)
        {
            _iconConsumers--;
            if (_iconConsumers == 0)
            {
                Volatile.Write(ref _isIconLoadRequested, 0);
                _iconLoadCancellation?.Cancel();
                _iconLoadCancellation = null;
            }
        }
    }

    public void ReloadIconIfNeeded()
    {
        if (!HasIcon && IsIconLoadRequested)
        {
            StartIconLoadIfNeeded();
        }
    }

    public void RequestIconSize(int iconPixelSize)
    {
        var normalizedSize = NormalizeIconPixelSize(iconPixelSize);
        var previousSize = Interlocked.Exchange(ref _requestedIconPixelSize, normalizedSize);
        if ((previousSize != normalizedSize || !HasIcon) && IsIconLoadRequested)
        {
            StartIconLoadIfNeeded();
        }
    }

    public void UpdateBoxPresentation(string boxName, bool isPixelated, int iconPixelSize)
    {
        SetProperty(ref _boxName, boxName, nameof(BoxName));
        SetProperty(ref _isPixelated, isPixelated, nameof(IsPixelated));
        RequestIconSize(iconPixelSize);
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

    private void StartIconLoadIfNeeded()
    {
        CancellationTokenSource cancellation;
        int requestedSize;
        lock (_iconSync)
        {
            if (_iconConsumers == 0)
            {
                return;
            }

            requestedSize = Volatile.Read(ref _requestedIconPixelSize);
            if (_iconLoadCancellation is not null && _loadingIconPixelSize == requestedSize)
            {
                return;
            }

            _iconLoadCancellation?.Cancel();
            _iconLoadCancellation = null;
            if (_loadedIconPixelSize == requestedSize && HasIcon)
            {
                return;
            }

            cancellation = new CancellationTokenSource();
            _iconLoadCancellation = cancellation;
            _loadingIconPixelSize = requestedSize;
        }

        _ = LoadIconAsync(requestedSize, cancellation, cancellation.Token);
    }

    private async Task LoadIconAsync(
        int requestedSize,
        CancellationTokenSource cancellation,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (icon, terminalException) = string.IsNullOrWhiteSpace(PathLabel)
                ? (null, (Exception?)null)
                : await LoadIconWithRetriesAsync(
                    PathLabel,
                    Model.ItemKind == ItemKind.Directory,
                    requestedSize,
                    cancellationToken).ConfigureAwait(false);

            await SetIconOnUiThreadAsync(icon, requestedSize, cancellation, cancellationToken);
            if (terminalException is not null && !cancellationToken.IsCancellationRequested)
            {
                _logger?.Error(
                    terminalException,
                    $"Failed to load icon for drawer item {Id:D} at {requestedSize}px.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // An unloaded/recycled consumer or a new icon size no longer needs this request.
        }
        catch (Exception exception)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                _logger?.Error(
                    exception,
                    $"Unexpected icon loading failure for drawer item {Id:D} at {requestedSize}px.");
            }
        }
        finally
        {
            lock (_iconSync)
            {
                if (ReferenceEquals(_iconLoadCancellation, cancellation))
                {
                    _iconLoadCancellation = null;
                }

                cancellation.Dispose();
            }
        }
    }

    private async Task<(ImageSource? Icon, Exception? TerminalException)> LoadIconWithRetriesAsync(
        string path,
        bool isDirectory,
        int requestedSize,
        CancellationToken cancellationToken)
    {
        Exception? terminalException = null;
        for (var attempt = 1; attempt <= MaxIconLoadAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var icon = await _loadIcon(path, isDirectory, requestedSize, cancellationToken)
                    .ConfigureAwait(false);
                terminalException = null;
                if (icon is not null || attempt == MaxIconLoadAttempts)
                {
                    return (icon, null);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                terminalException = exception;
                if (attempt == MaxIconLoadAttempts)
                {
                    break;
                }
            }

            await Task.Delay(150 * attempt, cancellationToken).ConfigureAwait(false);
        }

        return (null, terminalException);
    }

    private async Task SetIconOnUiThreadAsync(
        ImageSource? icon,
        int requestedSize,
        CancellationTokenSource cancellation,
        CancellationToken cancellationToken)
    {
        void ApplyIcon()
        {
            lock (_iconSync)
            {
                if (cancellationToken.IsCancellationRequested
                    || !ReferenceEquals(_iconLoadCancellation, cancellation)
                    || _iconConsumers == 0
                    || requestedSize != Volatile.Read(ref _requestedIconPixelSize))
                {
                    return;
                }

                IconImage = icon;
                _loadedIconPixelSize = requestedSize;
            }
        }

        var application = Application.Current;
        if (application is null || application.Dispatcher.CheckAccess())
        {
            ApplyIcon();
            return;
        }

        await application.Dispatcher.InvokeAsync(ApplyIcon);
    }

    private sealed class IconDemand(DrawerItemViewModel owner) : IDisposable
    {
        private DrawerItemViewModel? _owner = owner;

        public void Dispose()
        {
            Interlocked.Exchange(ref _owner, null)?.ReleaseIconDemand();
        }
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
