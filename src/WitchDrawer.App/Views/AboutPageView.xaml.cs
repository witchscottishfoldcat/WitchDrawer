using WitchDrawer.Core.Localization;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WitchDrawer.App.ViewModels;
using WitchDrawer.Core.Logging;

namespace WitchDrawer.App.Views;

public partial class AboutPageView : UserControl
{
    internal const string SupportPageUri = "https://www.witchcat.cn/zh/support";

    /// <summary>
    /// Owned by the hosting window; assigned right after startup so failure logging
    /// keeps going through the same application logger. Static because the view is
    /// hosted by LazyPageHost and is not instantiated until first shown.
    /// </summary>
    internal static IAppLogger? Logger { get; set; }

    public AboutPageView()
    {
        InitializeComponent();
    }

    private MainViewModel ViewModel => (MainViewModel)DataContext;

    private void OnOpenProjectLinkClicked(object sender, RoutedEventArgs e)
    {
        OpenExternalUri("https://github.com/witchscottishfoldcat/WitchDrawer");
    }

    private void OnOpenEmailClicked(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        OpenExternalUri("mailto:witchscottishfoldcat@gmail.com");
    }

    private void OnOpenWebsiteClicked(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        OpenExternalUri("https://www.witchcat.cn");
    }

    private void OnOpenSupportLinkClicked(object sender, RoutedEventArgs e)
    {
        OpenExternalUri(SupportPageUri);
    }

    private async void OnExportDiagnosticLogsClick(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this);
        var privacyConfirmation = MessageBox.Show(
            owner,
            Strings.Get("TheDiagnosticPackageIncludesOnlyRecentLogsAndBasic"),
            Strings.Get("ExportDiagnosticLogs"),
            MessageBoxButton.OKCancel,
            MessageBoxImage.Information);
        if (privacyConfirmation != MessageBoxResult.OK)
        {
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = Strings.Get("SaveWitchDrawerDiagnosticLogs"),
            Filter = Strings.Get("ZIPArchiveZipZip"),
            DefaultExt = ".zip",
            AddExtension = true,
            FileName = $"WitchDrawer-Diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip"
        };
        if (dialog.ShowDialog(owner) != true)
        {
            return;
        }

        ExportDiagnosticLogsButton.IsEnabled = false;
        try
        {
            var result = await ViewModel.Maintenance.ExportDiagnosticLogsAsync(dialog.FileName);
            MessageBox.Show(
                owner,
                Strings.Format("DiagnosticLogsExportedNNLogFilesNSavedTo", result.LogFileCount, result.ArchivePath),
                Strings.Get("ExportComplete"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                owner,
                Strings.Get("DiagnosticLogExportFailedN") + exception.Message,
                Strings.Get("ExportFailed"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            ExportDiagnosticLogsButton.IsEnabled = true;
        }
    }

    private void OpenExternalUri(string uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = uri,
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            Logger?.Error(exception, $"Failed to open external URI: {uri}");
        }
    }
}
