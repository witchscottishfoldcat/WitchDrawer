using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WitchDrawer.PluginSdk;

namespace WitchDrawer.Clock;

public sealed class ClockPlugin : IWidgetPlugin
{
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
    private readonly TextBlock _time = Text(64, "TextPrimaryBrush", FontWeights.Light);
    private readonly TextBlock _seconds = Text(25, "AccentBrush", FontWeights.SemiBold);
    private readonly TextBlock _period = Text(11, "TextMutedBrush");
    private readonly TextBlock _weekday = Text(12, "TextMutedBrush");
    private readonly TextBlock _date = Text(12, "TextMutedBrush");
    private readonly TextBlock _elapsed = Text(11, "TextMutedBrush");
    private readonly Border _progress = new() { Height = 3, CornerRadius = new CornerRadius(1.5), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Grid _calendar = new() { Margin = new Thickness(0, 2, 0, 10) };
    private WidgetContext? _context;
    private bool _showSeconds = true;
    private bool _use24Hours = true;
    private bool _showDate = true;

    public FrameworkElement CreateView(WidgetContext context)
    {
        _context = context;
        _showSeconds = context.Settings.GetValueOrDefault("showSeconds") != "false";
        _use24Hours = context.Settings.GetValueOrDefault("use24Hours") != "false";
        _showDate = context.Settings.GetValueOrDefault("showDate") != "false";
        var panel = new StackPanel { Width = 280, Margin = new Thickness(4, 0, 4, 3) };
        _date.HorizontalAlignment = HorizontalAlignment.Right;
        _calendar.Children.Add(_weekday);
        _calendar.Children.Add(_date);

        var timeRow = new Grid { Height = 78, Margin = new Thickness(0, 0, 0, 4) };
        _time.Name = "ClockTime";
        _time.FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI");
        _time.LineHeight = 72;
        _time.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        _time.VerticalAlignment = VerticalAlignment.Top;
        timeRow.Children.Add(_time);
        var suffix = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 11, 0, 0) };
        _seconds.Name = "ClockSeconds";
        suffix.Children.Add(_seconds);
        suffix.Children.Add(_period);
        timeRow.Children.Add(suffix);
        panel.Children.Add(timeRow);
        panel.Children.Add(_calendar);

        var track = new Border { Height = 3, CornerRadius = new CornerRadius(1.5) };
        track.SetResourceReference(Border.BackgroundProperty, "BorderBrushSoft");
        _progress.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        track.Child = _progress;
        panel.Children.Add(track);
        _elapsed.Margin = new Thickness(0, 10, 0, 0);
        panel.Children.Add(_elapsed);
        Update();
        _timer.Tick += OnTick;
        _timer.Start();
        return panel;
    }

    private static TextBlock Text(double size, string brush, FontWeight? weight = null)
    {
        var text = new TextBlock { FontSize = size, FontWeight = weight ?? FontWeights.Normal };
        text.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return text;
    }

    private void OnTick(object? sender, EventArgs e) => Update();
    private void Update()
    {
        var now = DateTime.Now;
        var culture = _context?.Culture ?? CultureInfo.CurrentCulture;
        SetText(_time, now.ToString(_use24Hours ? "HH:mm" : "hh:mm", culture));
        SetText(_seconds, now.ToString(":ss", culture));
        _seconds.Visibility = _showSeconds ? Visibility.Visible : Visibility.Collapsed;
        SetText(_period, _use24Hours ? "" : now.ToString("tt", culture));
        _period.Visibility = _use24Hours ? Visibility.Collapsed : Visibility.Visible;
        SetText(_weekday, now.ToString("dddd", culture));
        SetText(_date, now.ToString("d", culture));
        _calendar.Visibility = _showDate ? Visibility.Visible : Visibility.Collapsed;
        var fraction = now.TimeOfDay.TotalMinutes / (24 * 60);
        // Minute precision avoids an extra layout change every second.
        var width = 280 * Math.Floor(now.TimeOfDay.TotalMinutes) / (24 * 60);
        if (_progress.Width != width) _progress.Width = width;
        var chinese = culture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        SetText(_elapsed, chinese ? $"今天已过 {fraction.ToString("P0", culture)}" : $"{fraction.ToString("P0", culture)} of the day");
    }

    private static void SetText(TextBlock control, string text) { if (control.Text != text) control.Text = text; }

    public FrameworkElement CreateSettingsView()
    {
        var chinese = (_context?.Culture ?? CultureInfo.CurrentUICulture).Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        var panel = new StackPanel { Margin = new Thickness(20) };
        var hours = new CheckBox { Content = chinese ? "24 小时制" : "24-hour time", IsChecked = _use24Hours, Margin = new Thickness(0, 0, 0, 12) };
        var seconds = new CheckBox { Content = chinese ? "显示秒数" : "Show seconds", IsChecked = _showSeconds };
        var date = new CheckBox { Content = chinese ? "显示星期和日期" : "Show weekday and date", IsChecked = _showDate, Margin = new Thickness(0, 12, 0, 0) };
        foreach (var control in new[] { hours, seconds, date }) control.SetResourceReference(Control.ForegroundProperty, "TextPrimaryBrush");
        hours.Click += async (_, _) => { _use24Hours = hours.IsChecked == true; await SaveAsync(); };
        seconds.Click += async (_, _) => { _showSeconds = seconds.IsChecked == true; await SaveAsync(); };
        date.Click += async (_, _) => { _showDate = date.IsChecked == true; await SaveAsync(); };
        panel.Children.Add(hours); panel.Children.Add(seconds); panel.Children.Add(date);
        return panel;
    }

    private async Task SaveAsync()
    {
        Update();
        try
        {
            if (_context is not null) await _context.SaveSettingsAsync(new Dictionary<string, string>
            { ["showSeconds"] = _showSeconds ? "true" : "false", ["use24Hours"] = _use24Hours ? "true" : "false", ["showDate"] = _showDate ? "true" : "false" });
        }
        catch (Exception exception) { _context?.ReportError(exception); }
    }

    public void Dispose() { _timer.Stop(); _timer.Tick -= OnTick; _context = null; }
}
