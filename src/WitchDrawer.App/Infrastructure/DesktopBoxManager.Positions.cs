using System.Globalization;
using System.Text.Json;
using System.Windows;
using WitchDrawer.App.Views;

namespace WitchDrawer.App.Infrastructure;

public sealed partial class DesktopBoxManager
{
    // 位置持久化、布局备份与重叠消解。

    public async Task SaveAllPositionsAsync()
    {
        var positions = _windows
            .Select(pair => new KeyValuePair<string, string>(
                BoxPositionSettingPrefix + pair.Key.ToString("N"),
                CaptureStoredPosition(pair.Value)))
            .ToArray();

        foreach (var (key, value) in positions)
        {
            await _drawerService.SetSettingAsync(key, value);
        }

        _logger.Info($"Recorded complete desktop layout for {positions.Length} boxes.");
    }

    public async Task<int> RecordLayoutBackupAsync(int slot)
    {
        var key = GetLayoutBackupSettingKey(slot);
        var positions = _windows
            .Select(pair => CaptureLayoutBackupPosition(pair.Key, pair.Value))
            .ToArray();
        var value = SerializeLayoutBackup(positions);

        await _drawerService.SetSettingAsync(key, value);
        _logger.Info($"Recorded layout backup slot {slot} with {positions.Length} boxes.");
        return positions.Length;
    }

    public async Task<bool> HasLayoutBackupAsync(
        int slot,
        StartupSettingsSnapshot? startupSnapshot = null)
    {
        var key = GetLayoutBackupSettingKey(slot);
        var value = startupSnapshot is not null
            ? startupSnapshot.Get(key)
            : await _drawerService.GetSettingAsync(key);
        var hasBackup = TryParseLayoutBackup(value, out _);
        if (!hasBackup && !string.IsNullOrWhiteSpace(value))
        {
            _logger.Info($"Layout backup slot {slot} contains invalid data and is treated as empty.");
        }

        return hasBackup;
    }

    public async Task<bool> DeleteLayoutBackupAsync(int slot)
    {
        var key = GetLayoutBackupSettingKey(slot);
        var deleted = await _drawerService.DeleteSettingAsync(key);
        _logger.Info(
            deleted
                ? $"Deleted layout backup slot {slot}."
                : $"Layout backup slot {slot} was already empty when deletion was requested.");
        return deleted;
    }

    public async Task<LayoutBackupRestoreResult> RestoreLayoutBackupAsync(int slot)
    {
        var key = GetLayoutBackupSettingKey(slot);
        await _refreshGate.WaitAsync();
        try
        {
            if (_closing)
            {
                return new LayoutBackupRestoreResult(false, 0, 0);
            }

            var value = await _drawerService.GetSettingAsync(key);
            if (!TryParseLayoutBackup(value, out var positions))
            {
                _logger.Info($"Layout backup slot {slot} is empty or invalid; restore skipped.");
                return new LayoutBackupRestoreResult(false, 0, 0);
            }

            var restoredCount = 0;
            var missingCount = 0;
            _isAdjustingPosition = true;
            try
            {
                foreach (var position in positions)
                {
                    if (!_windows.TryGetValue(position.BoxId, out var window))
                    {
                        missingCount++;
                        continue;
                    }

                    if (position.IsPhysicalPixels)
                    {
                        window.RememberPreferredWindowOriginPixels(new Point(position.Left, position.Top));
                        window.MoveWindowOriginPixels(position.Left, position.Top);
                    }
                    else
                    {
                        // Version 1 backups stored WPF DIPs without monitor identity.
                        // Keep the legacy interpretation for one-time migration.
                        window.Left = position.Left;
                        window.Top = position.Top;
                        window.RememberCurrentDisplayPosition();
                    }
                    window.ResyncSizeToContent();
                    window.UpdateLayout();

                    var bounds = window.GetVisibleBoundsPixels();
                    var workArea = window.GetWorkAreaPixels();
                    if (bounds.Width > 0 && bounds.Height > 0 && !workArea.IsEmpty)
                    {
                        var origin = CalculateClampedVisibleOrigin(bounds, workArea);
                        window.MoveToVisibleOriginPixels(origin.X, origin.Y);
                    }

                    if (window.IsVisible)
                    {
                        window.QueueSendToBottom();
                    }

                    restoredCount++;
                }
            }
            finally
            {
                _isAdjustingPosition = false;
            }

            await SaveAllPositionsAsync();
            _logger.Info(
                $"Restored layout backup slot {slot}: restored={restoredCount}, missing={missingCount}.");
            return new LayoutBackupRestoreResult(true, restoredCount, missingCount);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public async Task SavePositionAsync(Guid boxId)
    {
        if (!_windows.TryGetValue(boxId, out var window))
        {
            return;
        }

        var key = BoxPositionSettingPrefix + boxId.ToString("N");
        var value = CaptureStoredPosition(window);
        await _drawerService.SetSettingAsync(key, value);
    }

    public async Task<bool> CenterBoxOnScreenAsync(Guid boxId)
    {
        if (_closing)
        {
            return false;
        }

        await _refreshGate.WaitAsync();
        try
        {
            if (_closing || !_windows.TryGetValue(boxId, out var window))
            {
                _logger.Info($"Skipped screen-center recall for missing desktop box {boxId:N}.");
                return false;
            }

            if (!window.IsVisible)
            {
                await window.ViewModel.LoadAsync();
                if (_closing)
                {
                    return false;
                }

                window.Show();
            }

            window.ResyncSizeToContent();
            window.UpdateLayout();
            var bounds = window.GetVisibleBoundsPixels();
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                _logger.Info($"Skipped screen-center recall for unmeasured desktop box {boxId:N}.");
                return false;
            }

            var workArea = DesktopBoxWindow.GetPrimaryWorkAreaPixels();
            if (workArea.IsEmpty)
            {
                _logger.Info($"Skipped screen-center recall because the primary work area is unavailable for {boxId:N}.");
                return false;
            }

            var origin = CalculateCenteredVisibleOrigin(bounds.Size, workArea);
            _isAdjustingPosition = true;
            try
            {
                window.MoveToVisibleOriginPixels(origin.X, origin.Y);
            }
            finally
            {
                _isAdjustingPosition = false;
            }

            window.QueueSendToBottom();
            window.RememberCurrentDisplayPosition();
            var key = BoxPositionSettingPrefix + boxId.ToString("N");
            var value = CaptureStoredPosition(window);
            await _drawerService.SetSettingAsync(key, value);
            _logger.Info($"Recalled desktop box {boxId:N} to screen center at {value}.");
            return true;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    /// <summary>
    /// 恢复窗口位置。返回 <see langword="true"/> 表示该窗口需要参与重叠消解：
    /// 没有存档位置的新盒子（按级联位落位，可能压住已有盒子），或存档位置被
    /// 工作区钳制过（分辨率/显示器变化导致多个盒子挤到同一边缘）。原样还原的
    /// 盒子返回 <see langword="false"/>——保持用户摆好的相对关系，哪怕彼此重叠。
    /// </summary>
    private async Task<bool> PlaceWindowAsync(DesktopBoxWindow window, Guid boxId, int fallbackIndex)
    {
        var savedPosition = await _drawerService.GetSettingAsync(BoxPositionSettingPrefix + boxId.ToString("N"));
        if (TryParseStoredPosition(savedPosition, out var left, out var top, out var isPhysicalPixels))
        {
            if (isPhysicalPixels)
            {
                // Create the HWND before restoring. Assigning physical coordinates
                // through WPF Left/Top would reinterpret them using the primary DPI.
                new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
                window.RememberPreferredWindowOriginPixels(new Point(left, top));
                window.MoveWindowOriginPixels(left, top);
            }
            else
            {
                // Old builds stored monitor-dependent WPF DIPs without a monitor id.
                // Interpret them once with the legacy behavior; the next save writes px:.
                window.Left = left;
                window.Top = top;
                new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
                window.RememberCurrentDisplayPosition();
            }

            // Do not clamp yet. Before Show + LoadAsync + the first stable
            // SizeToContent pass, DesiredSize can be wider than the final HWND
            // (notably for a collapsed drawer). Clamping that provisional size
            // permanently shifts a right-edge box left after every restart.
            return false;
        }

        // New windows still need a pre-show measurement for their fallback placement.
        window.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        PlaceNewWindow(window, fallbackIndex);
        return true;
    }

    private static bool ClampWindowToCurrentWorkArea(DesktopBoxWindow window)
    {
        // UpdateLayout has completed the stable SizeToContent pass. Use layout
        // dimensions here because the native HWND resize can lag the WPF layout event.
        var bounds = window.GetLayoutVisibleBoundsPixels();
        var workArea = window.GetWorkAreaPixels();
        if (bounds.IsEmpty || workArea.IsEmpty)
        {
            return false;
        }

        var origin = CalculateClampedVisibleOrigin(bounds, workArea);
        var wasClamped = Math.Abs(origin.X - bounds.Left) > 0.5
            || Math.Abs(origin.Y - bounds.Top) > 0.5;
        if (wasClamped)
        {
            window.MoveToVisibleOriginPixels(origin.X, origin.Y);
        }

        return wasClamped;
    }

    internal static string SerializePosition(double left, double top) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{left:R}{PositionSeparator}{top:R}");

    internal static string SerializePhysicalPosition(double leftPixels, double topPixels) =>
        PhysicalPositionPrefix + SerializePosition(leftPixels, topPixels);

    internal static bool TryParseStoredPosition(
        string? raw,
        out double left,
        out double top,
        out bool isPhysicalPixels)
    {
        isPhysicalPixels = raw?.StartsWith(PhysicalPositionPrefix, StringComparison.Ordinal) == true;
        var coordinates = isPhysicalPixels ? raw![PhysicalPositionPrefix.Length..] : raw;
        if (TryParsePosition(coordinates, out left, out top))
        {
            return true;
        }

        isPhysicalPixels = false;
        return false;
    }

    internal static bool TryParsePosition(string? raw, out double left, out double top)
    {
        left = 0;
        top = 0;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var parts = raw.Split(PositionSeparator, StringSplitOptions.TrimEntries);
        if (parts.Length != 2
            || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out left)
            || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out top))
        {
            left = 0;
            top = 0;
            return false;
        }

        return double.IsFinite(left) && double.IsFinite(top);
    }

    private static string CaptureStoredPosition(DesktopBoxWindow window)
    {
        if (window.PreferredWindowOriginPixels is Point preferred)
        {
            return SerializePhysicalPosition(preferred.X, preferred.Y);
        }
        if (window.TryGetWindowBoundsPixels(out var bounds))
        {
            return SerializePhysicalPosition(bounds.Left, bounds.Top);
        }

        // A visible desktop box normally always has an HWND. Retain the legacy
        // fallback so shutdown cannot lose a position if a handle is being rebuilt.
        return SerializePosition(window.Left, window.Top);
    }

    private static LayoutBackupPosition CaptureLayoutBackupPosition(
        Guid boxId,
        DesktopBoxWindow window)
    {
        if (window.PreferredWindowOriginPixels is Point preferred)
        {
            return new LayoutBackupPosition(boxId, preferred.X, preferred.Y, IsPhysicalPixels: true);
        }
        if (window.TryGetWindowBoundsPixels(out var bounds))
        {
            return new LayoutBackupPosition(boxId, bounds.Left, bounds.Top, IsPhysicalPixels: true);
        }

        return new LayoutBackupPosition(boxId, window.Left, window.Top);
    }

    internal static Point CalculateCenteredVisibleOrigin(Size visibleSize, Rect workArea)
    {
        if (!double.IsFinite(visibleSize.Width)
            || !double.IsFinite(visibleSize.Height)
            || visibleSize.Width < 0
            || visibleSize.Height < 0
            || workArea.IsEmpty
            || !double.IsFinite(workArea.Left)
            || !double.IsFinite(workArea.Top)
            || !double.IsFinite(workArea.Width)
            || !double.IsFinite(workArea.Height))
        {
            throw new ArgumentOutOfRangeException(
                nameof(workArea),
                "Visible size and work area must contain finite, non-negative dimensions.");
        }

        var centeredLeft = workArea.Left + ((workArea.Width - visibleSize.Width) / 2);
        var centeredTop = workArea.Top + ((workArea.Height - visibleSize.Height) / 2);
        var left = Math.Max(
            workArea.Left,
            Math.Min(centeredLeft, workArea.Right - visibleSize.Width));
        var top = Math.Max(
            workArea.Top,
            Math.Min(centeredTop, workArea.Bottom - visibleSize.Height));
        return new Point(left, top);
    }

    internal static Point CalculateClampedVisibleOrigin(Rect visibleBounds, Rect workArea)
    {
        var left = Math.Max(
            workArea.Left,
            Math.Min(visibleBounds.Left, workArea.Right - visibleBounds.Width));
        var top = Math.Max(
            workArea.Top,
            Math.Min(visibleBounds.Top, workArea.Bottom - visibleBounds.Height));
        return new Point(left, top);
    }

    internal static string GetLayoutBackupSettingKey(int slot)
    {
        if (slot is < 1 or > LayoutBackupSlotCount)
        {
            throw new ArgumentOutOfRangeException(nameof(slot), slot, "Layout backup slot must be from 1 to 3.");
        }

        return LayoutBackupSettingPrefix + slot.ToString(CultureInfo.InvariantCulture);
    }

    internal static string SerializeLayoutBackup(IEnumerable<LayoutBackupPosition> positions)
    {
        ArgumentNullException.ThrowIfNull(positions);
        var snapshot = positions.ToArray();
        if (!IsValidLayoutBackup(snapshot))
        {
            throw new ArgumentException("Layout backup contains invalid or duplicate box positions.", nameof(positions));
        }

        return JsonSerializer.Serialize(new LayoutBackupPayload(LayoutBackupVersion, snapshot));
    }

    internal static bool TryParseLayoutBackup(string? raw, out LayoutBackupPosition[] positions)
    {
        positions = [];
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<LayoutBackupPayload>(raw);
            if (payload is null
                || (payload.Version != LayoutBackupVersion
                    && payload.Version != LegacyLayoutBackupVersion)
                || !IsValidLayoutBackup(payload.Positions))
            {
                return false;
            }

            positions = payload.Positions;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    private static bool IsValidLayoutBackup(LayoutBackupPosition[]? positions)
    {
        if (positions is null || positions.Length > MaxLayoutBackupPositions)
        {
            return false;
        }

        var ids = new HashSet<Guid>();
        return positions.All(position =>
            position.BoxId != Guid.Empty
            && double.IsFinite(position.Left)
            && double.IsFinite(position.Top)
            && ids.Add(position.BoxId));
    }

    private static void PlaceNewWindow(Window window, int index)
    {
        const double margin = 18;
        const double gap = 12;
        const double topPadding = 84;

        var workArea = SystemParameters.WorkArea;
        var centerX = workArea.Left + (workArea.Width - window.DesiredSize.Width) / 2;
        var centerY = workArea.Top + (workArea.Height - window.DesiredSize.Height) / 2;

        var offset = index * (window.DesiredSize.Width + gap);
        window.Left = Math.Max(workArea.Left + margin, Math.Min(centerX + offset, workArea.Right - window.DesiredSize.Width - margin));
        window.Top = Math.Max(workArea.Top + margin, Math.Min(centerY + topPadding * 0.5, workArea.Bottom - window.DesiredSize.Height - margin));
    }

    /// <summary>
    /// Nudges apart windows that were placed without a usable saved position this
    /// refresh (new boxes, or saved positions clamped by a resolution/monitor
    /// change). Windows restored from an intact saved position keep their exact
    /// spot — the user's arrangement is authoritative even when boxes overlap —
    /// but still count as obstacles so movable windows cascade around them.
    /// </summary>
    private void ResolveWindowOverlaps()
    {
        var entries = _windows.Values
            .Where(window => window.IsVisible)
            .OfType<DesktopBoxWindow>()
            .Select(window => (Window: window, Bounds: window.GetVisibleBoundsPixels()))
            .Where(entry => entry.Bounds.Width > 0 && entry.Bounds.Height > 0)
            .ToArray();

        // Restored positions are authoritative obstacles regardless of database order.
        var placed = entries
            .Where(entry => !_overlapResolutionBoxIds.Contains(entry.Window.ViewModel.BoxId))
            .Select(entry => entry.Bounds)
            .ToList();

        foreach (var entry in entries.Where(entry => _overlapResolutionBoxIds.Contains(entry.Window.ViewModel.BoxId)))
        {
            var bounds = entry.Bounds;
            var workArea = entry.Window.GetWorkAreaPixels();
            if (workArea.IsEmpty)
            {
                // A transient monitor query failure must not turn Rect.Empty's
                // infinities into an invalid native window position.
                placed.Add(bounds);
                continue;
            }

            var resolved = ResolveOverlapCascade(
                bounds,
                placed,
                workArea);

            if (Math.Abs(bounds.Left - resolved.Left) > 0.5
                || Math.Abs(bounds.Top - resolved.Top) > 0.5)
            {
                entry.Window.MoveToVisibleOriginPixels(resolved.Left, resolved.Top);
            }

            placed.Add(resolved);
        }
    }

    /// <summary>
    /// Cascades <paramref name="bounds"/> below/right of every rect in
    /// <paramref name="placed"/> until no overlap remains, then clamps the result
    /// into <paramref name="workArea"/>. Pure math kept separate from the window
    /// loop above so the collision rules stay unit-testable.
    /// </summary>
    internal static Rect ResolveOverlapCascade(
        Rect bounds,
        IReadOnlyList<Rect> placed,
        Rect workArea)
    {
        const double cascadeStep = 12;

        var moved = true;
        var guard = 0;
        while (moved && guard++ < 200)
        {
            moved = false;
            foreach (var other in placed)
            {
                if (!bounds.IntersectsWith(other))
                {
                    continue;
                }

                var nextLeft = bounds.Left;
                var nextTop = other.Bottom + cascadeStep;
                if (nextTop + bounds.Height > workArea.Bottom)
                {
                    // No room below; wrap to the right of the blocking box and
                    // restart from the top so the cascade stays on screen.
                    nextLeft = other.Right + cascadeStep;
                    nextTop = workArea.Top;
                }

                bounds = new Rect(nextLeft, nextTop, bounds.Width, bounds.Height);
                moved = true;
            }
        }

        // Clamp back into the work area so a wrapped cascade never leaves the box
        // hanging off the right/bottom edge.
        var clampedLeft = Math.Max(workArea.Left, Math.Min(bounds.Left, workArea.Right - bounds.Width));
        var clampedTop = Math.Max(workArea.Top, Math.Min(bounds.Top, workArea.Bottom - bounds.Height));
        if (Math.Abs(clampedLeft - bounds.Left) > 0.5
            || Math.Abs(clampedTop - bounds.Top) > 0.5)
        {
            bounds = new Rect(clampedLeft, clampedTop, bounds.Width, bounds.Height);
        }

        return bounds;
    }
}
