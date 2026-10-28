using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using WitchDrawer.App.Plugins;
using WitchDrawer.Core.Localization;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Plugins;
using WitchDrawer.Native.Shell;

namespace WitchDrawer.App.ViewModels;

public sealed class PluginRowViewModel
{
    public PluginManifest Manifest { get; }
    public InstalledPlugin? Installed { get; }
    public PluginCatalogEntry? CatalogEntry { get; }
    public string Name => Manifest.Name;
    public string Description => Manifest.Description;
    public string Details => $"{Manifest.Author} · {Manifest.Version}";
    public string Capabilities => FormatCapabilities(Manifest);
    internal static string FormatCapabilities(PluginManifest manifest) => manifest.Permissions.Length == 0 ? Strings.Get("PluginNoCapabilities")
        : string.Join(" · ", manifest.Permissions.Select(p => Strings.Get(p switch
        {
            "network" => "PluginNetwork", "system-metrics" => "PluginMetrics", _ => "PluginFiles"
        })));
    public string State { get; }
    public IAsyncRelayCommand InstallCommand { get; }
    public IAsyncRelayCommand EnableCommand { get; }
    public IAsyncRelayCommand DisableCommand { get; }
    public IAsyncRelayCommand UninstallCommand { get; }
    public IRelayCommand SettingsCommand { get; }
    public IRelayCommand SourceCommand { get; }
    public IRelayCommand ScreenshotCommand { get; }

    internal PluginRowViewModel(PluginCatalogEntry? catalog, InstalledPlugin? installed, PluginsViewModel owner,
        PluginService service, PluginRuntime runtime)
    {
        CatalogEntry = catalog;
        Installed = installed;
        Manifest = installed?.Manifest ?? catalog!.Plugin;
        var update = catalog is not null && installed is not null && PluginValidation.ParseVersion(catalog.Plugin.Version) > PluginValidation.ParseVersion(installed.Manifest.Version);
        var running = runtime.IsRunning(Manifest.Id);
        State = runtime.GetError(Manifest.Id) ?? (!service.IsCompatible(Manifest) ? Strings.Get("PluginIncompatible")
            : running ? Strings.Get("PluginRunning") : installed is null ? Strings.Get("PluginNotInstalled")
            : update ? Strings.Format("PluginUpdateAvailable", catalog!.Plugin.Version) : Strings.Get("PluginDisabled"));
        InstallCommand = new AsyncRelayCommand(() => owner.InstallAsync(catalog!),
            () => !owner.IsBusy && catalog is not null && service.IsCompatible(catalog.Plugin) && (installed is null || update) && !running && installed?.Enabled != true);
        EnableCommand = new AsyncRelayCommand(() => owner.RunAsync(() => runtime.EnableAsync(Manifest.Id)),
            () => !owner.IsBusy && installed is not null && !running && service.IsCompatible(Manifest));
        DisableCommand = new AsyncRelayCommand(() => owner.RunAsync(() => runtime.DisableAsync(Manifest.Id)),
            () => !owner.IsBusy && installed is not null && (running || installed.Enabled));
        UninstallCommand = new AsyncRelayCommand(() => owner.UninstallAsync(Manifest.Id), () => !owner.IsBusy && installed is not null);
        SettingsCommand = new RelayCommand(() => runtime.ShowSettings(Manifest.Id, Application.Current.MainWindow), () => !owner.IsBusy && running);
        SourceCommand = new RelayCommand(() => owner.OpenUrl(Manifest.RepositoryUrl));
        ScreenshotCommand = new RelayCommand(() => owner.OpenUrl(catalog!.ScreenshotUrl!), () => catalog?.ScreenshotUrl is not null);
    }
}

public sealed class PluginsViewModel : ObservableObject, IDisposable
{
    private readonly PluginService _service;
    private readonly PluginRuntime _runtime;
    private readonly IAppLogger _logger;
    private PluginCatalog _catalog = new();
    private bool _isBusy;
    private bool _loaded;
    private string _status = "";
    public ObservableCollection<PluginRowViewModel> Items { get; } = [];
    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand ImportCommand { get; }
    public IRelayCommand SubmitCommand { get; }
    public IRelayCommand GuideCommand { get; }
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public bool IsEmpty => Items.Count == 0;

    internal PluginsViewModel(PluginService service, PluginRuntime runtime, IAppLogger logger)
    {
        _service = service; _runtime = runtime; _logger = logger;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        ImportCommand = new AsyncRelayCommand(ImportAsync, () => !IsBusy);
        SubmitCommand = new RelayCommand(() => OpenUrl(PluginService.SubmissionUrl));
        GuideCommand = new RelayCommand(() => OpenUrl("https://github.com/witchscottishfoldcat/WitchDrawer.Plugins"));
        runtime.Changed += OnRuntimeChanged;
        Strings.LanguageChanged += OnLanguageChanged;
    }

    private void OnRuntimeChanged()
    {
        if (_loaded && !IsBusy) Infrastructure.FireAndForget.Run(ReloadAsync(), _logger, "Plugin list refresh failed.");
    }
    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (Application.Current is { } app)
            app.Dispatcher.BeginInvoke(OnRuntimeChanged);
    }
    public void Dispose() { _runtime.Changed -= OnRuntimeChanged; Strings.LanguageChanged -= OnLanguageChanged; }

    public async Task LoadAsync()
    {
        if (_loaded) return;
        _loaded = true;
        await RunAsync(async () =>
        {
            await _runtime.EnsureInitializedAsync();
            await _service.CleanupUnusedAsync();
            var result = await _service.RefreshCatalogAsync();
            _catalog = result.Catalog;
            Status = Strings.Get(result.Offline ? "PluginOfflineCatalog" : "PluginCatalogUpdated");
        });
    }
    private Task RefreshAsync() => RunAsync(async () =>
    {
        var result = await _service.RefreshCatalogAsync();
        _catalog = result.Catalog;
        Status = Strings.Get(result.Offline ? "PluginOfflineCatalog" : "PluginCatalogUpdated");
    });

    internal async Task RunAsync(Func<Task> operation)
    {
        if (IsBusy) return;
        IsBusy = true;
        NotifyCommands();
        Status = Strings.Get("PluginWorking");
        try
        {
            await operation();
            if (Status == Strings.Get("PluginWorking")) Status = Strings.Get("PluginOperationCompleted");
            await ReloadAsync();
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Plugin management operation failed.");
            Status = Strings.Format("PluginOperationFailed", exception.GetBaseException().Message);
            // Preserve list access after a failed download/start; state remains owned by Core.
            try { await ReloadAsync(); } catch (Exception readException) { _logger.Error(readException, "Plugin state reload failed."); }
        }
        finally { IsBusy = false; NotifyCommands(); }
    }

    private async Task ReloadAsync()
    {
        var installed = await _service.GetInstalledAsync();
        Items.Clear();
        foreach (var id in _catalog.Plugins.Select(p => p.Plugin.Id).Concat(installed.Select(p => p.Manifest.Id)).Distinct().Order())
            Items.Add(new PluginRowViewModel(_catalog.Plugins.Find(p => p.Plugin.Id == id), installed.FirstOrDefault(p => p.Manifest.Id == id), this, _service, _runtime));
        OnPropertyChanged(nameof(IsEmpty));
        NotifyCommands();
    }

    private void NotifyCommands()
    {
        RefreshCommand.NotifyCanExecuteChanged(); ImportCommand.NotifyCanExecuteChanged();
        foreach (var row in Items)
        {
            row.InstallCommand.NotifyCanExecuteChanged(); row.EnableCommand.NotifyCanExecuteChanged();
            row.DisableCommand.NotifyCanExecuteChanged(); row.UninstallCommand.NotifyCanExecuteChanged(); row.SettingsCommand.NotifyCanExecuteChanged();
        }
    }

    internal Task InstallAsync(PluginCatalogEntry entry) => RunAsync(async () =>
    {
        if (!ConfirmTrust(entry.Plugin)) { Status = Strings.Get("PluginCancelled"); return; }
        await _service.InstallFromCatalogAsync(entry);
        Status = Strings.Get("PluginInstalledDisabled");
    });

    private Task ImportAsync() => RunAsync(async () =>
    {
        var dialog = new OpenFileDialog { Filter = "WitchDrawer plugin (*.zip)|*.zip", CheckFileExists = true };
        if (dialog.ShowDialog(Application.Current.MainWindow) != true) { Status = Strings.Get("PluginCancelled"); return; }
        var preview = await _service.PreviewPackageAsync(dialog.FileName);
        if (!ConfirmTrust(preview.Manifest)) { Status = Strings.Get("PluginCancelled"); return; }
        await _service.InstallLocalAsync(dialog.FileName, preview.Sha256);
        Status = Strings.Get("PluginInstalledDisabled");
    });

    private static bool ConfirmTrust(PluginManifest manifest) => MessageBox.Show(Application.Current.MainWindow,
        Strings.Format("PluginTrustMessage", manifest.Name, manifest.Author, manifest.RepositoryUrl,
            PluginRowViewModel.FormatCapabilities(manifest)),
        Strings.Get("PluginCenter"), MessageBoxButton.OKCancel, MessageBoxImage.Information) == MessageBoxResult.OK;

    internal Task UninstallAsync(string id) => RunAsync(async () =>
    {
        if (MessageBox.Show(Application.Current.MainWindow, Strings.Get("PluginConfirmUninstall"), Strings.Get("PluginCenter"),
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) { Status = Strings.Get("PluginCancelled"); return; }
        await _runtime.DisableAsync(id);
        await _service.UninstallAsync(id);
        Status = Strings.Get("PluginUninstalled");
    });

    internal async void OpenUrl(string url)
    {
        try { await ExternalLinkLauncher.OpenAsync(url); }
        catch (Exception exception) { _logger.Error(exception, "Failed to open plugin link."); Status = Strings.Format("PluginOperationFailed", exception.Message); }
    }
}
