using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using WitchDrawer.Core.Models;

namespace WitchDrawer.App.ViewModels;

public sealed class TodoUndoViewModel : ObservableObject
{
    private DispatcherTimer? _timer;
    private TodoDeleteUndo? _pending;
    public event EventHandler? AvailabilityChanged;
    public TodoDeleteUndo? Pending => IsAvailable ? _pending : null;
    public bool IsAvailable => _pending is not null && _pending.ExpiresAt > DateTimeOffset.UtcNow;
    public void Offer(TodoDeleteUndo pending)
    {
        Clear(); _pending = pending;
        var remaining = pending.ExpiresAt - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero) { Clear(); return; }
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = remaining };
        _timer.Tick += OnExpired; _timer.Start(); NotifyAvailability();
    }
    public void Clear()
    {
        if (_timer is not null) { _timer.Stop(); _timer.Tick -= OnExpired; _timer = null; }
        _pending = null; NotifyAvailability();
    }
    private void OnExpired(object? sender, EventArgs e) => Clear();
    private void NotifyAvailability() { OnPropertyChanged(nameof(IsAvailable)); AvailabilityChanged?.Invoke(this, EventArgs.Empty); }
}
