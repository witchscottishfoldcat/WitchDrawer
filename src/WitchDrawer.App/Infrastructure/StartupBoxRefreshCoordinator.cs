namespace WitchDrawer.App.Infrastructure;

/// <summary>
/// The initial MainViewModel load raises BoxesChanged for its own snapshot. Ignore
/// that event, but remember changes made while desktop windows are being created.
/// </summary>
internal sealed class StartupBoxRefreshCoordinator
{
    private bool _mainViewModelLoaded;
    private bool _initialRefreshComplete;
    private bool _refreshPending;

    public bool HasPendingRefresh => _refreshPending;
    public bool IsMainViewModelLoaded => _mainViewModelLoaded;

    public void MarkMainViewModelLoaded() => _mainViewModelLoaded = true;

    public bool ShouldRefreshNow()
    {
        if (!_mainViewModelLoaded)
        {
            return false;
        }

        if (!_initialRefreshComplete)
        {
            _refreshPending = true;
            return false;
        }

        return true;
    }

    public bool CompleteInitialRefresh()
    {
        _initialRefreshComplete = true;
        var refreshPending = _refreshPending;
        _refreshPending = false;
        return refreshPending;
    }
}
