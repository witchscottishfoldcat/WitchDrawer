using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WitchDrawer.Native.Metrics;
using WitchDrawer.PluginSdk;

namespace WitchDrawer.Performance;

public sealed class PerformancePlugin : IWidgetPlugin
{
    private readonly TextBlock _cpu = Text(38, "TextPrimaryBrush", FontWeights.Light);
    private readonly TextBlock _memory = Text(38, "TextPrimaryBrush", FontWeights.Light);
    private readonly TextBlock _cpuDetail = Text(11, "TextMutedBrush");
    private readonly TextBlock _memoryDetail = Text(11, "TextMutedBrush");
    private readonly TextBlock _memoryLabel = Text(12, "TextMutedBrush", FontWeights.SemiBold);
    private readonly TextBlock _status = Text(11, "TextMutedBrush");
    private readonly TextBlock _range = Text(11, "TextMutedBrush");
    private readonly TrendGraph _cpuTrend = new();
    private readonly TrendGraph _memoryTrend = new();
    private readonly CancellationTokenSource _lifetime = new();
    private Task? _worker;
    private int _disposed;

    public FrameworkElement CreateView(WidgetContext context)
    {
        var panel = new StackPanel { Width = 280, Margin = new Thickness(4, 5, 4, 7) };
        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition());
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
        columns.ColumnDefinitions.Add(new ColumnDefinition());
        var separator = new Border { Margin = new Thickness(0, 2, 0, 2) };
        separator.SetResourceReference(Border.BackgroundProperty, "BorderBrushSoft");
        Grid.SetColumn(separator, 1);
        columns.Children.Add(separator);
        var cpuPanel = Metric(Text(12, "TextMutedBrush", FontWeights.SemiBold), _cpu, _cpuDetail, _cpuTrend);
        ((TextBlock)cpuPanel.Children[0]).Text = "CPU";
        cpuPanel.Margin = new Thickness(0, 0, 15, 0);
        var memoryPanel = Metric(_memoryLabel, _memory, _memoryDetail, _memoryTrend);
        memoryPanel.Margin = new Thickness(15, 0, 0, 0);
        Grid.SetColumn(memoryPanel, 2);
        columns.Children.Add(cpuPanel);
        columns.Children.Add(memoryPanel);
        panel.Children.Add(columns);
        var footer = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        _range.HorizontalAlignment = HorizontalAlignment.Right;
        footer.Children.Add(_status);
        footer.Children.Add(_range);
        panel.Children.Add(footer);
        _cpu.Name = "CpuValue";
        _memory.Name = "MemoryValue";
        _cpu.Text = _memory.Text = "—";
        Localize(context.Culture, warmingUp: true);
        var token = _lifetime.Token;
        // Construction, native calls and waiting all stay off the UI thread. Awaited dispatch
        // permits at most one queued update, even when the host UI is busy.
        _worker = Task.Run(() => CollectAsync(panel.Dispatcher, context, token));
        return panel;
    }

    private static StackPanel Metric(TextBlock label, TextBlock value, TextBlock detail, TrendGraph trend)
    {
        var panel = new StackPanel();
        value.Margin = new Thickness(0, 5, 0, 2);
        detail.Height = 29;
        detail.TextTrimming = TextTrimming.CharacterEllipsis;
        trend.Margin = new Thickness(0, 9, 0, 0);
        panel.Children.Add(label);
        panel.Children.Add(value);
        panel.Children.Add(detail);
        panel.Children.Add(trend);
        return panel;
    }

    private static TextBlock Text(double size, string brush, FontWeight? weight = null)
    {
        var text = new TextBlock { FontSize = size, FontWeight = weight ?? FontWeights.Normal };
        text.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return text;
    }

    private async Task CollectAsync(Dispatcher dispatcher, WidgetContext context, CancellationToken token)
    {
        try
        {
            using var reader = new WindowsSystemMetricsReader();
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
            var initial = true;
            do
            {
                token.ThrowIfCancellationRequested();
                var snapshot = reader.Read();
                var warmingUp = initial;
                await dispatcher.InvokeAsync(() =>
                {
                    if (Volatile.Read(ref _disposed) == 0) ApplySample(snapshot, context.Culture, warmingUp);
                }, DispatcherPriority.Background, token).Task.ConfigureAwait(false);
                initial = false;
            } while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (!token.IsCancellationRequested && !dispatcher.HasShutdownStarted)
            {
                try
                {
                    await dispatcher.InvokeAsync(() =>
                    {
                        if (Volatile.Read(ref _disposed) == 0) context.ReportError(exception);
                    }, DispatcherPriority.Background, token).Task.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            }
        }
        // Dispose cancels promptly; the worker owns native handle and CTS cleanup.
        finally { _lifetime.Dispose(); }
    }

    private void ApplySample(SystemMetricsSnapshot sample, CultureInfo culture, bool warmingUp)
    {
        _cpu.Text = sample.CpuPercent is { } cpu ? cpu.ToString("0", culture) + "%" : "—";
        _memory.Text = sample.MemoryPercent.ToString("0", culture) + "%";
        _cpuTrend.Add(sample.CpuPercent, sample.CapturedAtUtc);
        _memoryTrend.Add(sample.MemoryPercent, sample.CapturedAtUtc);
        var chinese = Localize(culture, warmingUp && sample.CpuPercent is null);
        _cpuDetail.Text = sample.CpuPercent is null
            ? warmingUp ? (chinese ? "正在采样…" : "Sampling…") : (chinese ? "CPU 数据不可用" : "CPU unavailable")
            : chinese ? "总处理器占用" : "Total processor load";
        _memoryDetail.Text = $"{(sample.UsedMemoryBytes / 1073741824.0).ToString("0.0", culture)} / {(sample.TotalMemoryBytes / 1073741824.0).ToString("0.0", culture)} GiB";
        _memoryDetail.ToolTip = chinese ? "已用 / 总物理内存" : "Used / total physical memory";
    }

    private bool Localize(CultureInfo culture, bool warmingUp)
    {
        var chinese = culture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        _memoryLabel.Text = chinese ? "内存" : "MEMORY";
        _status.Text = warmingUp ? (chinese ? "正在采样…" : "Sampling…") : (chinese ? "每 2 秒更新" : "Updates every 2s");
        _range.Text = chinese ? "最近 60 秒" : "Last 60s";
        return chinese;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (_worker is null) _lifetime.Dispose();
        else
        {
            try { _lifetime.Cancel(); }
            catch (ObjectDisposedException) { } // Collection may already have ended after a reported fault.
        }
    }
}
