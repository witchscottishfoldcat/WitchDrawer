using WitchDrawer.App.Localization;
using WitchDrawer.Core.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using WitchDrawer.Core.Logging;

namespace WitchDrawer.App.Infrastructure;

/// <summary>Coordinates busy/status state shared by page operations, without coupling their view models.</summary>
public sealed class UiOperationState(IAppLogger logger) : LocalizedObservableObject
{
    private bool _isBusy;
    private string _statusText = Strings.Get("Ready");
    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }
    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }

    protected override void OnLanguageChanged() => StatusText = Strings.Get("Ready");

    public async Task RunAsync(Func<Task> action)
    {
        if (IsBusy) return;
        try { IsBusy = true; await action(); }
        catch (Exception exception) { logger.Error(exception, "Operation failed."); StatusText = exception.Message; }
        finally { IsBusy = false; }
    }
}
