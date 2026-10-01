using WitchDrawer.Native.Windows;

namespace WitchDrawer.App.Tests;

public sealed class DesktopIconVisibilityTests
{
    [Theory]
    [InlineData(850, false)]
    [InlineData(150, true)]
    public void ApplyToggle_DelayedCommandDoesNotQueueAnotherToggle(int delay, bool registryChangesFirst)
    {
        var visible = true;
        var registryHidden = false;
        var elapsed = 0;
        var pending = new Queue<int>();
        var posts = 0;
        void Advance(int milliseconds)
        {
            elapsed += milliseconds;
            if (registryChangesFirst && elapsed >= 50) registryHidden = true;
            while (pending.TryPeek(out var due) && due <= elapsed)
            {
                pending.Dequeue();
                visible = !visible;
                registryHidden = !visible;
            }
        }
        var hidden = DesktopIconVisibility.ApplyToggle(
            () => new(new nint(7), new nint(9), new nint(42)),
            _ => visible, () => registryHidden,
            _ => { posts++; pending.Enqueue(elapsed + delay); return true; },
            Advance, value => registryHidden = value,
            (_, _) => throw new InvalidOperationException("A pending command must not trigger fallback."),
            () => { });
        Advance(2000);
        Assert.Equal(1, posts);
        Assert.True(hidden);
        Assert.False(visible);
        Assert.Empty(pending);
    }

    [Fact]
    public void ApplyToggle_UsesShellCommandOnFirstAttemptWhenVisibilityChanges()
    {
        var listView = new nint(42);
        var progman = new nint(7);
        var defView = new nint(9);
        var visible = true; // 图标当前可见 → 目标：隐藏
        var registryHidden = false;
        var posts = new List<nint>();
        var registryWrites = new List<bool>();
        var legacyWrites = new List<(nint Window, bool Hidden)>();
        var notifications = 0;

        var result = DesktopIconVisibility.ApplyToggle(
            () => new DesktopIconVisibility.DesktopShellHandles(progman, defView, listView),
            _ => visible,
            () => registryHidden,
            window =>
            {
                posts.Add(window);
                visible = false;
                return true;
            },
            _ => { },
            value =>
            {
                registryWrites.Add(value);
                registryHidden = value;
            },
            (window, hidden) => legacyWrites.Add((window, hidden)),
            () => notifications++);

        Assert.True(result);
        Assert.Equal([progman], posts);
        Assert.Equal([true], registryWrites);
        Assert.Empty(legacyWrites);
        Assert.Equal(0, notifications);
    }

    [Fact]
    public void ApplyToggle_WaitsForVisibilityWhenRegistryAlreadyMatchesTarget()
    {
        var visible = true;
        var posts = 0;
        var polls = 0;

        var result = DesktopIconVisibility.ApplyToggle(
            () => new DesktopIconVisibility.DesktopShellHandles(
                new nint(7), new nint(9), new nint(42)),
            _ => visible,
            () => true,
            _ =>
            {
                posts++;
                return true;
            },
            _ =>
            {
                polls++;
                if (polls == 2)
                {
                    visible = false;
                }
            },
            _ => { },
            (_, _) => throw new InvalidOperationException("Native toggle succeeded; no fallback expected."),
            () => throw new InvalidOperationException("Native toggle succeeded; no fallback expected."));

        Assert.True(result);
        Assert.Equal(1, posts);
        Assert.Equal(2, polls);
    }

    [Fact]
    public void ApplyToggle_RegistryChangeWithoutVisibilityChangeDoesNotRetryOrFallBack()
    {
        // A registry change alone does not prove that Explorer finished the queued command.
        var listView = new nint(42);
        var visible = false; // 目标：显示
        var registryHidden = true;
        var posts = new List<nint>();
        var legacyCalls = 0;
        var notifications = 0;

        Assert.Throws<TimeoutException>(() => DesktopIconVisibility.ApplyToggle(
            () => new DesktopIconVisibility.DesktopShellHandles(new nint(7), new nint(9), listView),
            _ => visible,
            () => registryHidden,
            window =>
            {
                posts.Add(window);
                registryHidden = false;

                return true;
            },
            _ => { },
            value => registryHidden = value,
            (_, _) => legacyCalls++,
            () => notifications++));

        Assert.Equal([new nint(7)], posts);
        Assert.Equal(0, legacyCalls);
        Assert.Equal(0, notifications);
    }

    [Fact]
    public void ApplyToggle_FallsBackToDirectWriteOnlyWhenNoShellCommandWasQueued()
    {
        var listView = new nint(42);
        var visible = true;
        var registryHidden = false;
        var posts = new List<nint>();
        var registryWrites = new List<bool>();
        var legacyWrites = new List<(nint Window, bool Hidden)>();
        var notifications = 0;

        var result = DesktopIconVisibility.ApplyToggle(
            () => new DesktopIconVisibility.DesktopShellHandles(new nint(7), new nint(9), listView),
            _ => visible,
            () => registryHidden,
            window =>
            {
                // Posting failed, so there cannot be a delayed toggle in Explorer's queue.
                posts.Add(window);
                return false;
            },
            _ => { },
            value =>
            {
                registryWrites.Add(value);
                registryHidden = value;
            },
            (window, hidden) => legacyWrites.Add((window, hidden)),
            () => notifications++);

        Assert.True(result);
        Assert.Equal([new nint(7), new nint(9)], posts);
        Assert.Equal([(listView, true)], legacyWrites);
        Assert.Equal([true], registryWrites);
        Assert.Equal(1, notifications);
    }

    [Fact]
    public void ApplyToggle_FlipsRegistryValueWhenDesktopViewIsUnavailable()
    {
        var registryHidden = false;
        var registryWrites = new List<bool>();
        var posts = new List<nint>();
        var notifications = 0;

        var result = DesktopIconVisibility.ApplyToggle(
            () => new DesktopIconVisibility.DesktopShellHandles(new nint(7), new nint(9), nint.Zero),
            _ => throw new InvalidOperationException("Must not probe visibility without a desktop view."),
            () => registryHidden,
            window =>
            {
                posts.Add(window);
                return true;
            },
            _ => throw new InvalidOperationException("Must not wait without a desktop view."),
            value =>
            {
                registryWrites.Add(value);
                registryHidden = value;
            },
            (_, _) => throw new InvalidOperationException("Must not touch a missing desktop view."),
            () => notifications++);

        Assert.True(result);
        Assert.Empty(posts);
        Assert.Equal([true], registryWrites);
        Assert.Equal(1, notifications);
    }

    [Fact]
    public void ApplyToggle_SendsCommandToShellViewWhenHostWindowIsMissing()
    {
        var defView = new nint(9);
        var visible = true;
        var posts = new List<nint>();

        var result = DesktopIconVisibility.ApplyToggle(
            () => new DesktopIconVisibility.DesktopShellHandles(nint.Zero, defView, new nint(42)),
            _ => visible,
            () => false,
            window =>
            {
                posts.Add(window);
                visible = false;
                return true;
            },
            _ => { },
            _ => { },
            (_, _) => throw new InvalidOperationException("Native toggle succeeded; no fallback expected."),
            () => throw new InvalidOperationException("Native toggle succeeded; no fallback expected."));

        Assert.True(result);
        Assert.Equal([defView], posts);
    }

    [Fact]
    public async Task RunToggleHiddenAsync_DoesNotBlockCallerWhileNativeToggleIsPending()
    {
        using var toggleStarted = new ManualResetEventSlim();
        using var releaseToggle = new ManualResetEventSlim();

        var operation = DesktopIconVisibility.RunToggleHiddenAsync(
            () =>
            {
                toggleStarted.Set();
                Assert.True(releaseToggle.Wait(TimeSpan.FromSeconds(5)));
                return true;
            });

        Assert.True(toggleStarted.Wait(TimeSpan.FromSeconds(5)));
        Assert.False(operation.IsCompleted);

        releaseToggle.Set();
        Assert.True(await operation.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(-1, true)]
    [InlineData("1", false)]
    public void IsHiddenRegistryValue_RecognizesOnlyNonZeroRegistryNumbers(
        object? value,
        bool expected)
    {
        Assert.Equal(expected, DesktopIconVisibility.IsHiddenRegistryValue(value));
    }

    [Theory]
    [InlineData("Progman", true)]
    [InlineData("WorkerW", true)]
    [InlineData("SHELLDLL_DefView", true)]
    [InlineData("SysListView32", false)]
    [InlineData("WitchDrawer", false)]
    [InlineData(null, false)]
    public void IsDesktopHostClass_RecognizesOnlyDesktopHosts(string? className, bool expected)
    {
        Assert.Equal(expected, DesktopIconVisibility.IsDesktopHostClass(className));
    }
}
