using System.Windows;
using WitchDrawer.App.Infrastructure;

namespace WitchDrawer.App.Tests;

/// <summary>
/// 锁定 <see cref="DesktopBoxManager.ResolveOverlapCascade"/> 的重叠消解规则：
/// 只有新放置/被钳制的盒子才参与消解；级联与钳制必须使用盒子所在显示器
/// 自己的工作区——用主屏工作区会把副屏盒子错搬回主屏（重启后位置漂移）。
/// </summary>
public sealed class DesktopBoxManagerOverlapTests
{
    private static readonly Rect PrimaryWorkArea = new(0, 0, 1920, 1040);

    /// <summary>主屏右侧的副屏工作区（虚拟坐标 X 从 1920 开始）。</summary>
    private static readonly Rect SecondaryWorkArea = new(1920, 0, 1920, 1040);

    [Fact]
    public void ResolveOverlapCascade_KeepsBoundsWithoutCollision()
    {
        var bounds = new Rect(300, 200, 240, 180);
        var placed = new[] { new Rect(0, 0, 200, 150) };

        var resolved = DesktopBoxManager.ResolveOverlapCascade(bounds, placed, PrimaryWorkArea);

        Assert.Equal(bounds, resolved);
    }

    [Fact]
    public void ResolveOverlapCascade_CascadesOverlapBelowObstacle()
    {
        var obstacle = new Rect(100, 100, 240, 180);
        var bounds = new Rect(120, 120, 240, 180);

        var resolved = DesktopBoxManager.ResolveOverlapCascade(
            bounds,
            new[] { obstacle },
            PrimaryWorkArea);

        Assert.Equal(obstacle.Bottom + 12, resolved.Top);
        Assert.Equal(bounds.Left, resolved.Left);
    }

    [Fact]
    public void ResolveOverlapCascade_WrapsRightWhenNoRoomBelow()
    {
        // 障碍物纵贯整个工作区：下方没有空间，必须绕到其右侧并回到工作区顶。
        var obstacle = new Rect(100, 0, 240, 1040);
        var bounds = new Rect(120, 500, 200, 150);

        var resolved = DesktopBoxManager.ResolveOverlapCascade(
            bounds,
            new[] { obstacle },
            PrimaryWorkArea);

        Assert.Equal(obstacle.Right + 12, resolved.Left);
        Assert.Equal(PrimaryWorkArea.Top, resolved.Top);
    }

    [Fact]
    public void ResolveOverlapCascade_SecondaryMonitorBoxIsNotDraggedBackToPrimary()
    {
        // 副屏上的盒子（X=2100，相对副屏工作区完全合法）：不重叠时一丝不动。
        // 若误用主屏工作区（Right=1920），它会被钳回主屏右缘——这正是重启后
        // 盒子"没有回到原来地方"的回归来源。
        var bounds = new Rect(2100, 300, 240, 180);

        var resolved = DesktopBoxManager.ResolveOverlapCascade(
            bounds,
            Array.Empty<Rect>(),
            SecondaryWorkArea);

        Assert.Equal(bounds, resolved);
    }

    [Fact]
    public void ResolveOverlapCascade_ClampsIntoItsOwnWorkArea()
    {
        // 盒子底边越出自己工作区的下边：钳回界内，但仍留在同一台显示器上。
        var bounds = new Rect(2100, 900, 240, 180);

        var resolved = DesktopBoxManager.ResolveOverlapCascade(
            bounds,
            Array.Empty<Rect>(),
            SecondaryWorkArea);

        Assert.Equal(SecondaryWorkArea.Bottom - bounds.Height, resolved.Top);
        Assert.Equal(bounds.Left, resolved.Left);
    }
}
